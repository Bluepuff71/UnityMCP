using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Manages Animator Controllers: full CRUD on controllers, layers, states, sub-state machines,
    /// transitions, parameters, StateMachineBehaviours, blend trees, and runtime control.
    /// </summary>
    [MCPTool("manage_animator",
        "Manage Animator Controllers: create, inspect, and modify layers, states, transitions, parameters, blend trees, behaviours, and runtime control",
        Category = "Animation")]
    public static class ManageAnimator
    {
        #region Constants

        private const int MaxAssetResults = 50;
        private const int MaxStatesPerStateMachine = 100;
        private const int MaxTransitionsPerState = 50;
        private const int MaxBlendTreeChildren = 50;
        private const int MaxBlendTreeDepth = 3;
        private const int DefaultPageSize = 50;
        private const int MaxStateMachineDepth = 10;

        #endregion

        #region Asset Actions

        [MCPAction("list", Description = "Find all AnimatorController assets in the project", ReadOnlyHint = true)]
        public static object List(
            [MCPParam("search_pattern", "Optional name filter for asset search")] string searchPattern = null)
        {
            try
            {
                var filter = string.IsNullOrEmpty(searchPattern)
                    ? "t:AnimatorController"
                    : $"t:AnimatorController {searchPattern}";

                var guids = AssetDatabase.FindAssets(filter);
                var controllers = new List<object>();

                for (int i = 0; i < Math.Min(guids.Length, MaxAssetResults); i++)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
                    if (controller == null) continue;

                    controllers.Add(new
                    {
                        name = controller.name,
                        assetPath,
                        layerCount = controller.layers.Length,
                        parameterCount = controller.parameters.Length
                    });
                }

                return new { success = true, controllers = controllers.ToArray(), totalFound = guids.Length };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error listing AnimatorControllers: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("create", Description = "Create a new AnimatorController asset")]
        public static object Create(
            [MCPParam("path", "Asset path (must end in .controller)", required: true)] string path = null,
            [MCPParam("layers", "Optional list of layer names to create")] List<string> layers = null,
            [MCPParam("parameters", "Optional list of {name, type, default_value?} objects")] List<Dictionary<string, object>> parameters = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (!path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                throw MCPException.InvalidParams("'path' must end with .controller");
            try
            {
                if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/"))
                    path = "Assets/" + path;

                CreateFolderRecursive(System.IO.Path.GetDirectoryName(path));

                var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                if (controller == null)
                    throw new MCPException($"Failed to create AnimatorController at '{path}'", MCPErrorCodes.InternalError);

                // Rename default layer and add extras if provided
                if (layers != null && layers.Count > 0)
                {
                    var layerArray = controller.layers;
                    if (layerArray.Length > 0)
                    {
                        layerArray[0].name = layers[0];
                        if (layerArray[0].stateMachine != null)
                            layerArray[0].stateMachine.name = layers[0];
                        controller.layers = layerArray;
                    }

                    for (int i = 1; i < layers.Count; i++)
                    {
                        var stateMachine = new AnimatorStateMachine
                        {
                            name = layers[i],
                            hideFlags = HideFlags.HideInHierarchy
                        };
                        AssetDatabase.AddObjectToAsset(stateMachine, controller);

                        var newLayer = new AnimatorControllerLayer
                        {
                            name = layers[i],
                            stateMachine = stateMachine,
                            defaultWeight = 1f
                        };

                        layerArray = controller.layers;
                        var expandedLayers = new AnimatorControllerLayer[layerArray.Length + 1];
                        Array.Copy(layerArray, expandedLayers, layerArray.Length);
                        expandedLayers[layerArray.Length] = newLayer;
                        controller.layers = expandedLayers;
                    }
                }

                // Add parameters if provided
                if (parameters != null)
                {
                    foreach (var paramDef in parameters)
                    {
                        if (!paramDef.TryGetValue("name", out var nameObj) || nameObj == null)
                            continue;
                        var paramName = nameObj.ToString();

                        var paramType = AnimatorControllerParameterType.Float;
                        if (paramDef.TryGetValue("type", out var typeObj) && typeObj != null)
                            paramType = ParseParameterType(typeObj.ToString());

                        controller.AddParameter(paramName, paramType);

                        if (paramDef.TryGetValue("default_value", out var defaultVal) && defaultVal != null)
                        {
                            var paramArray = controller.parameters;
                            for (int i = paramArray.Length - 1; i >= 0; i--)
                            {
                                if (paramArray[i].name == paramName)
                                {
                                    SetParameterDefaultValue(ref paramArray[i], defaultVal);
                                    break;
                                }
                            }
                            controller.parameters = paramArray;
                        }
                    }
                }

                SaveController(controller);
                return new { success = true, action = "create", name = controller.name, path };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error creating AnimatorController: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("inspect", Description = "Get detailed structure of an AnimatorController", ReadOnlyHint = true)]
        public static object Inspect(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("page_size", "Max layers to return per page", Minimum = 1, Maximum = 200)] int pageSize = DefaultPageSize,
            [MCPParam("cursor", "Layer index offset for pagination", Minimum = 0)] int cursor = 0)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            try
            {
                var controller = LoadController(path);
                return BuildControllerInfo(controller, AssetDatabase.GetAssetPath(controller), cursor, pageSize);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error inspecting AnimatorController: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("delete", Description = "Delete an AnimatorController asset", DestructiveHint = true)]
        public static object Delete(
            [MCPParam("path", "Asset path to delete", required: true)] string path = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            try
            {
                if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/"))
                    path = "Assets/" + path;

                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null)
                    throw MCPException.InvalidParams($"AnimatorController not found at '{path}'");

                if (!AssetDatabase.DeleteAsset(path))
                    throw new MCPException($"Failed to delete asset at '{path}'", MCPErrorCodes.InternalError);

                return new { success = true, action = "delete", path };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error deleting AnimatorController: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Layer Actions

        [MCPAction("add_layer", Description = "Add a layer to an AnimatorController")]
        public static object AddLayer(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name for the new layer", required: true)] string layerName = null,
            [MCPParam("weight", "Layer weight (0-1)", Minimum = 0, Maximum = 1)] double weight = 1.0,
            [MCPParam("blending_mode", "Blending mode", Enum = new[] { "Override", "Additive" })] string blendingMode = "Override",
            [MCPParam("ik_pass", "Enable IK pass")] bool ikPass = false)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            try
            {
                var controller = LoadController(path);

                // Check for duplicate layer name
                foreach (var existingLayer in controller.layers)
                {
                    if (existingLayer.name.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                        throw MCPException.InvalidParams($"Layer '{layerName}' already exists.");
                }

                var stateMachine = new AnimatorStateMachine
                {
                    name = layerName,
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(stateMachine, controller);

                var newLayer = new AnimatorControllerLayer
                {
                    name = layerName,
                    stateMachine = stateMachine,
                    defaultWeight = (float)weight,
                    blendingMode = ParseBlendingMode(blendingMode),
                    iKPass = ikPass
                };

                Undo.RecordObject(controller, "Add Animator Layer");
                var layerArray = controller.layers;
                var expandedLayers = new AnimatorControllerLayer[layerArray.Length + 1];
                Array.Copy(layerArray, expandedLayers, layerArray.Length);
                expandedLayers[layerArray.Length] = newLayer;
                controller.layers = expandedLayers;

                SaveController(controller);
                return new { success = true, action = "add_layer", layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding layer: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("remove_layer", Description = "Remove a layer from an AnimatorController", DestructiveHint = true)]
        public static object RemoveLayer(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer to remove", required: true)] string layerName = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            try
            {
                var controller = LoadController(path);
                var layerArray = controller.layers;

                if (layerArray.Length <= 1)
                    throw MCPException.InvalidParams("Cannot remove the last layer. A controller must have at least one layer.");

                int layerIndex = FindLayerIndex(controller, layerName);

                Undo.RecordObject(controller, "Remove Animator Layer");
                var newLayers = layerArray.Where((_, i) => i != layerIndex).ToArray();
                controller.layers = newLayers;

                SaveController(controller);
                return new { success = true, action = "remove_layer", layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error removing layer: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("modify_layer", Description = "Modify an AnimatorController layer's properties")]
        public static object ModifyLayer(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer to modify", required: true)] string layerName = null,
            [MCPParam("new_name", "New name for the layer")] string newName = null,
            [MCPParam("weight", "New layer weight (0-1)")] object weight = null,
            [MCPParam("blending_mode", "New blending mode", Enum = new[] { "Override", "Additive" })] string blendingMode = null,
            [MCPParam("ik_pass", "Enable/disable IK pass")] object ikPass = null,
            [MCPParam("synced_layer_index", "Synced layer index (-1 to unsync)")] object syncedLayerIndex = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);

                Undo.RecordObject(controller, "Modify Animator Layer");
                var layerArray = controller.layers;

                if (newName != null)
                {
                    layerArray[layerIndex].name = newName;
                    if (layerArray[layerIndex].stateMachine != null)
                        layerArray[layerIndex].stateMachine.name = newName;
                }
                if (weight != null)
                    layerArray[layerIndex].defaultWeight = Convert.ToSingle(weight);
                if (blendingMode != null)
                    layerArray[layerIndex].blendingMode = ParseBlendingMode(blendingMode);
                if (ikPass != null)
                    layerArray[layerIndex].iKPass = Convert.ToBoolean(ikPass);
                if (syncedLayerIndex != null)
                {
                    int syncIndex = Convert.ToInt32(syncedLayerIndex);
                    if (syncIndex >= 0)
                    {
                        if (syncIndex >= layerArray.Length)
                            throw MCPException.InvalidParams($"Synced layer index {syncIndex} is out of range (0-{layerArray.Length - 1}).");
                        if (syncIndex == layerIndex)
                            throw MCPException.InvalidParams("A layer cannot sync to itself.");
                        if (layerArray[syncIndex].syncedLayerIndex == layerIndex)
                            throw MCPException.InvalidParams("Circular sync reference detected.");
                    }
                    layerArray[layerIndex].syncedLayerIndex = syncIndex;
                }

                controller.layers = layerArray;
                SaveController(controller);
                return new { success = true, action = "modify_layer", layer = newName ?? layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error modifying layer: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region State Actions

        [MCPAction("add_state", Description = "Add a state to a layer's state machine")]
        public static object AddState(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("state_name", "Name for the new state", required: true)] string stateName = null,
            [MCPParam("state_machine_path", "Path to sub-state machine (e.g. 'Combat/Melee')")] string stateMachinePath = null,
            [MCPParam("motion_path", "Asset path to an AnimationClip")] string motionPath = null,
            [MCPParam("position", "Node position [x, y] in the Animator window")] object position = null,
            [MCPParam("is_default", "Set as the default state")] bool isDefault = false)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(stateName)) throw MCPException.InvalidParams("'state_name' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var stateMachine = ResolveStateMachine(layer, stateMachinePath);

                // Check for duplicate
                foreach (var childState in stateMachine.states)
                {
                    if (childState.state.name.Equals(stateName, StringComparison.OrdinalIgnoreCase))
                        throw MCPException.InvalidParams($"State '{stateName}' already exists in this state machine.");
                }

                var statePosition = ParseStatePosition(position);

                Undo.RecordObject(stateMachine, "Add Animator State");
                var state = stateMachine.AddState(stateName, statePosition);

                if (!string.IsNullOrEmpty(motionPath))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionPath);
                    if (clip == null)
                        throw MCPException.InvalidParams($"AnimationClip not found at '{motionPath}'");
                    state.motion = clip;
                }

                if (isDefault)
                    stateMachine.defaultState = state;

                SaveController(controller);
                return new { success = true, action = "add_state", state = stateName, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding state: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("remove_state", Description = "Remove a state from a layer's state machine", DestructiveHint = true)]
        public static object RemoveState(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("state_name", "Name of the state to remove", required: true)] string stateName = null,
            [MCPParam("state_machine_path", "Path to sub-state machine")] string stateMachinePath = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(stateName)) throw MCPException.InvalidParams("'state_name' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var stateMachine = ResolveStateMachine(layer, stateMachinePath);
                var state = FindState(stateMachine, stateName);

                Undo.RecordObject(stateMachine, "Remove Animator State");
                stateMachine.RemoveState(state);

                SaveController(controller);
                return new { success = true, action = "remove_state", state = stateName, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error removing state: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("modify_state", Description = "Modify a state's properties")]
        public static object ModifyState(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("state_name", "Name of the state to modify", required: true)] string stateName = null,
            [MCPParam("state_machine_path", "Path to sub-state machine")] string stateMachinePath = null,
            [MCPParam("new_name", "New state name")] string newName = null,
            [MCPParam("motion_path", "Asset path to an AnimationClip")] string motionPath = null,
            [MCPParam("speed", "Playback speed")] object speed = null,
            [MCPParam("speed_parameter", "Speed multiplier parameter name")] string speedParameter = null,
            [MCPParam("tag", "State tag")] string tag = null,
            [MCPParam("write_default_values", "Write default values on state exit")] object writeDefaultValues = null,
            [MCPParam("cycle_offset", "Cycle offset")] object cycleOffset = null,
            [MCPParam("mirror", "Mirror the animation")] object mirror = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(stateName)) throw MCPException.InvalidParams("'state_name' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var stateMachine = ResolveStateMachine(layer, stateMachinePath);
                var state = FindState(stateMachine, stateName);

                Undo.RecordObject(state, "Modify Animator State");

                if (newName != null) state.name = newName;
                if (motionPath != null)
                {
                    if (string.IsNullOrEmpty(motionPath))
                    {
                        state.motion = null;
                    }
                    else
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionPath);
                        if (clip == null)
                            throw MCPException.InvalidParams($"AnimationClip not found at '{motionPath}'");
                        state.motion = clip;
                    }
                }
                if (speed != null) state.speed = Convert.ToSingle(speed);
                if (speedParameter != null)
                {
                    state.speedParameterActive = !string.IsNullOrEmpty(speedParameter);
                    state.speedParameter = speedParameter;
                }
                if (tag != null) state.tag = tag;
                if (writeDefaultValues != null) state.writeDefaultValues = Convert.ToBoolean(writeDefaultValues);
                if (cycleOffset != null) state.cycleOffset = Convert.ToSingle(cycleOffset);
                if (mirror != null) state.mirror = Convert.ToBoolean(mirror);

                SaveController(controller);
                return new { success = true, action = "modify_state", state = newName ?? stateName, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error modifying state: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Sub-State Machine Actions

        [MCPAction("add_sub_state_machine", Description = "Add a sub-state machine to a layer")]
        public static object AddSubStateMachine(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("sub_state_machine_name", "Name for the new sub-state machine", required: true)] string subStateMachineName = null,
            [MCPParam("state_machine_path", "Path to parent sub-state machine for nesting")] string stateMachinePath = null,
            [MCPParam("position", "Node position [x, y]")] object position = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(subStateMachineName)) throw MCPException.InvalidParams("'sub_state_machine_name' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var parentStateMachine = ResolveStateMachine(layer, stateMachinePath);
                var statePosition = ParseStatePosition(position);

                Undo.RecordObject(parentStateMachine, "Add Sub-State Machine");
                parentStateMachine.AddStateMachine(subStateMachineName, statePosition);

                SaveController(controller);
                return new { success = true, action = "add_sub_state_machine", subStateMachine = subStateMachineName, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding sub-state machine: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("remove_sub_state_machine", Description = "Remove a sub-state machine", DestructiveHint = true)]
        public static object RemoveSubStateMachine(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("sub_state_machine_name", "Name of the sub-state machine to remove", required: true)] string subStateMachineName = null,
            [MCPParam("state_machine_path", "Path to parent sub-state machine")] string stateMachinePath = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(subStateMachineName)) throw MCPException.InvalidParams("'sub_state_machine_name' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var parentStateMachine = ResolveStateMachine(layer, stateMachinePath);
                var childStateMachine = FindSubStateMachine(parentStateMachine, subStateMachineName);

                Undo.RecordObject(parentStateMachine, "Remove Sub-State Machine");
                parentStateMachine.RemoveStateMachine(childStateMachine);

                SaveController(controller);
                return new { success = true, action = "remove_sub_state_machine", subStateMachine = subStateMachineName, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error removing sub-state machine: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("modify_sub_state_machine", Description = "Rename a sub-state machine")]
        public static object ModifySubStateMachine(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("sub_state_machine_name", "Current name of the sub-state machine", required: true)] string subStateMachineName = null,
            [MCPParam("state_machine_path", "Path to parent sub-state machine")] string stateMachinePath = null,
            [MCPParam("new_name", "New name for the sub-state machine", required: true)] string newName = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(subStateMachineName)) throw MCPException.InvalidParams("'sub_state_machine_name' is required.");
            if (string.IsNullOrEmpty(newName)) throw MCPException.InvalidParams("'new_name' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var parentStateMachine = ResolveStateMachine(layer, stateMachinePath);
                var childStateMachine = FindSubStateMachine(parentStateMachine, subStateMachineName);

                Undo.RecordObject(childStateMachine, "Rename Sub-State Machine");
                childStateMachine.name = newName;

                SaveController(controller);
                return new { success = true, action = "modify_sub_state_machine", subStateMachine = newName, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error modifying sub-state machine: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Transition Actions

        [MCPAction("add_transition", Description = "Add a transition between states")]
        public static object AddTransition(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("source_state", "Source state name, 'Any State', or 'Entry'", required: true)] string sourceState = null,
            [MCPParam("destination_state", "Destination state name or 'Exit'", required: true)] string destinationState = null,
            [MCPParam("state_machine_path", "Path to sub-state machine")] string stateMachinePath = null,
            [MCPParam("has_exit_time", "Whether transition has exit time")] bool hasExitTime = true,
            [MCPParam("exit_time", "Exit time (normalized)")] double exitTime = 0.75,
            [MCPParam("duration", "Transition duration (normalized)")] double duration = 0.25,
            [MCPParam("conditions", "List of {parameter, mode, threshold} condition objects")] List<Dictionary<string, object>> conditions = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(sourceState)) throw MCPException.InvalidParams("'source_state' is required.");
            if (string.IsNullOrEmpty(destinationState)) throw MCPException.InvalidParams("'destination_state' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var stateMachine = ResolveStateMachine(layer, stateMachinePath);

                if (sourceState.Equals("Entry", StringComparison.OrdinalIgnoreCase))
                {
                    // Entry transitions use AnimatorTransition (no timing)
                    var destState = FindState(stateMachine, destinationState);
                    var entryTransition = stateMachine.AddEntryTransition(destState);
                    if (conditions != null)
                        entryTransition.conditions = BuildConditionArray(conditions);
                }
                else if (sourceState.Equals("Any State", StringComparison.OrdinalIgnoreCase))
                {
                    AnimatorState destState = null;
                    if (!destinationState.Equals("Exit", StringComparison.OrdinalIgnoreCase))
                        destState = FindState(stateMachine, destinationState);
                    else
                        throw MCPException.InvalidParams("'Any State' cannot transition directly to 'Exit'.");

                    var transition = stateMachine.AddAnyStateTransition(destState);
                    ApplyTransitionTiming(transition, hasExitTime, exitTime, duration);
                    if (conditions != null)
                        transition.conditions = BuildConditionArray(conditions);
                }
                else
                {
                    // Normal state-to-state transition
                    var srcState = FindState(stateMachine, sourceState);
                    AnimatorStateTransition transition;

                    if (destinationState.Equals("Exit", StringComparison.OrdinalIgnoreCase))
                    {
                        transition = srcState.AddExitTransition();
                    }
                    else
                    {
                        var destState = FindState(stateMachine, destinationState);
                        transition = srcState.AddTransition(destState);
                    }

                    ApplyTransitionTiming(transition, hasExitTime, exitTime, duration);
                    if (conditions != null)
                        transition.conditions = BuildConditionArray(conditions);
                }

                SaveController(controller);
                return new { success = true, action = "add_transition", source = sourceState, destination = destinationState, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding transition: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("remove_transition", Description = "Remove a transition", DestructiveHint = true)]
        public static object RemoveTransition(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("source_state", "Source state name, 'Any State', or 'Entry'", required: true)] string sourceState = null,
            [MCPParam("destination_state", "Destination state name or 'Exit'", required: true)] string destinationState = null,
            [MCPParam("state_machine_path", "Path to sub-state machine")] string stateMachinePath = null,
            [MCPParam("index", "Transition index for disambiguation", Minimum = 0)] int index = 0)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(sourceState)) throw MCPException.InvalidParams("'source_state' is required.");
            if (string.IsNullOrEmpty(destinationState)) throw MCPException.InvalidParams("'destination_state' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var stateMachine = ResolveStateMachine(layer, stateMachinePath);

                Undo.RecordObject(stateMachine, "Remove Transition");

                if (sourceState.Equals("Entry", StringComparison.OrdinalIgnoreCase))
                {
                    var transition = FindEntryTransition(stateMachine, destinationState, index);
                    stateMachine.RemoveEntryTransition(transition);
                }
                else if (sourceState.Equals("Any State", StringComparison.OrdinalIgnoreCase))
                {
                    var transition = FindAnyStateTransition(stateMachine, destinationState, index);
                    stateMachine.RemoveAnyStateTransition(transition);
                }
                else
                {
                    var srcState = FindState(stateMachine, sourceState);
                    var transition = FindStateTransition(srcState, destinationState, index);
                    Undo.RecordObject(srcState, "Remove Transition");
                    srcState.RemoveTransition(transition);
                }

                SaveController(controller);
                return new { success = true, action = "remove_transition", source = sourceState, destination = destinationState, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error removing transition: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("modify_transition", Description = "Modify a transition's properties")]
        public static object ModifyTransition(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("source_state", "Source state name, 'Any State', or 'Entry'", required: true)] string sourceState = null,
            [MCPParam("destination_state", "Destination state name or 'Exit'", required: true)] string destinationState = null,
            [MCPParam("state_machine_path", "Path to sub-state machine")] string stateMachinePath = null,
            [MCPParam("index", "Transition index for disambiguation", Minimum = 0)] int index = 0,
            [MCPParam("has_exit_time", "Whether transition has exit time")] object hasExitTime = null,
            [MCPParam("exit_time", "Exit time (normalized)")] object exitTime = null,
            [MCPParam("duration", "Transition duration")] object duration = null,
            [MCPParam("offset", "Transition offset")] object offset = null,
            [MCPParam("has_fixed_duration", "Whether duration is fixed")] object hasFixedDuration = null,
            [MCPParam("interruption_source", "Interruption source", Enum = new[] { "None", "Source", "Destination", "SourceThenDestination", "DestinationThenSource" })] string interruptionSource = null,
            [MCPParam("ordered_interruption", "Ordered interruption")] object orderedInterruption = null,
            [MCPParam("can_transition_to_self", "Can transition to self")] object canTransitionToSelf = null,
            [MCPParam("conditions", "List of {parameter, mode, threshold} — replaces all conditions")] List<Dictionary<string, object>> conditions = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(sourceState)) throw MCPException.InvalidParams("'source_state' is required.");
            if (string.IsNullOrEmpty(destinationState)) throw MCPException.InvalidParams("'destination_state' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var stateMachine = ResolveStateMachine(layer, stateMachinePath);

                if (sourceState.Equals("Entry", StringComparison.OrdinalIgnoreCase))
                {
                    var transition = FindEntryTransition(stateMachine, destinationState, index);
                    Undo.RecordObject(transition, "Modify Entry Transition");
                    if (conditions != null)
                        transition.conditions = BuildConditionArray(conditions);
                }
                else if (sourceState.Equals("Any State", StringComparison.OrdinalIgnoreCase))
                {
                    var transition = FindAnyStateTransition(stateMachine, destinationState, index);
                    Undo.RecordObject(transition, "Modify Any State Transition");
                    ApplyTransitionModifications(transition, hasExitTime, exitTime, duration, offset, hasFixedDuration, interruptionSource, orderedInterruption, canTransitionToSelf);
                    if (conditions != null)
                        transition.conditions = BuildConditionArray(conditions);
                }
                else
                {
                    var srcState = FindState(stateMachine, sourceState);
                    var transition = FindStateTransition(srcState, destinationState, index);
                    Undo.RecordObject(transition, "Modify Transition");
                    ApplyTransitionModifications(transition, hasExitTime, exitTime, duration, offset, hasFixedDuration, interruptionSource, orderedInterruption, canTransitionToSelf);
                    if (conditions != null)
                        transition.conditions = BuildConditionArray(conditions);
                }

                SaveController(controller);
                return new { success = true, action = "modify_transition", source = sourceState, destination = destinationState, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error modifying transition: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Parameter Actions

        [MCPAction("add_parameter", Description = "Add a parameter to an AnimatorController")]
        public static object AddParameter(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("parameter_name", "Name for the new parameter", required: true)] string parameterName = null,
            [MCPParam("type", "Parameter type", required: true, Enum = new[] { "Float", "Int", "Bool", "Trigger" })] string type = null,
            [MCPParam("default_value", "Default value for the parameter")] object defaultValue = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(parameterName)) throw MCPException.InvalidParams("'parameter_name' is required.");
            if (string.IsNullOrEmpty(type)) throw MCPException.InvalidParams("'type' is required.");
            try
            {
                var controller = LoadController(path);

                // Check for duplicate
                foreach (var existingParam in controller.parameters)
                {
                    if (existingParam.name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                        throw MCPException.InvalidParams($"Parameter '{parameterName}' already exists.");
                }

                var paramType = ParseParameterType(type);
                controller.AddParameter(parameterName, paramType);

                if (defaultValue != null)
                {
                    var paramArray = controller.parameters;
                    for (int i = paramArray.Length - 1; i >= 0; i--)
                    {
                        if (paramArray[i].name == parameterName)
                        {
                            SetParameterDefaultValue(ref paramArray[i], defaultValue);
                            break;
                        }
                    }
                    controller.parameters = paramArray;
                }

                SaveController(controller);
                return new { success = true, action = "add_parameter", parameter = parameterName, type, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding parameter: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("remove_parameter", Description = "Remove a parameter from an AnimatorController", DestructiveHint = true)]
        public static object RemoveParameter(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("parameter_name", "Name of the parameter to remove", required: true)] string parameterName = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(parameterName)) throw MCPException.InvalidParams("'parameter_name' is required.");
            try
            {
                var controller = LoadController(path);
                int paramIndex = FindParameterIndex(controller, parameterName);

                Undo.RecordObject(controller, "Remove Animator Parameter");
                controller.RemoveParameter(paramIndex);

                SaveController(controller);
                return new { success = true, action = "remove_parameter", parameter = parameterName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error removing parameter: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("modify_parameter", Description = "Modify a parameter's name, default value, or type")]
        public static object ModifyParameter(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("parameter_name", "Name of the parameter to modify", required: true)] string parameterName = null,
            [MCPParam("new_name", "New parameter name")] string newName = null,
            [MCPParam("default_value", "New default value")] object defaultValue = null,
            [MCPParam("type", "New parameter type (warning: may break conditions)", Enum = new[] { "Float", "Int", "Bool", "Trigger" })] string type = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(parameterName)) throw MCPException.InvalidParams("'parameter_name' is required.");
            try
            {
                var controller = LoadController(path);
                int paramIndex = FindParameterIndex(controller, parameterName);

                object warning = null;

                if (type != null)
                {
                    // Type change: collect warnings, remove old, add new
                    var warnings = CollectConditionsReferencingParameter(controller, parameterName);
                    if (warnings.Count > 0)
                        warning = warnings.ToArray();

                    var newParamType = ParseParameterType(type);
                    var finalName = newName ?? parameterName;

                    Undo.RecordObject(controller, "Change Parameter Type");
                    controller.RemoveParameter(paramIndex);
                    controller.AddParameter(finalName, newParamType);

                    if (defaultValue != null)
                    {
                        var paramArray = controller.parameters;
                        for (int i = paramArray.Length - 1; i >= 0; i--)
                        {
                            if (paramArray[i].name == finalName)
                            {
                                SetParameterDefaultValue(ref paramArray[i], defaultValue);
                                break;
                            }
                        }
                        controller.parameters = paramArray;
                    }
                }
                else
                {
                    // Name/default change only
                    Undo.RecordObject(controller, "Modify Animator Parameter");
                    var paramArray = controller.parameters;

                    if (newName != null) paramArray[paramIndex].name = newName;
                    if (defaultValue != null) SetParameterDefaultValue(ref paramArray[paramIndex], defaultValue);

                    controller.parameters = paramArray;
                }

                SaveController(controller);

                var result = new Dictionary<string, object>
                {
                    { "success", true },
                    { "action", "modify_parameter" },
                    { "parameter", newName ?? parameterName },
                    { "path", AssetDatabase.GetAssetPath(controller) }
                };
                if (warning != null)
                    result["warning"] = warning;

                return result;
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error modifying parameter: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Behaviour Actions

        [MCPAction("add_behaviour", Description = "Add a StateMachineBehaviour to a state or state machine")]
        public static object AddBehaviour(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("state_name", "State to attach behaviour to (omit for state machine level)")] string stateName = null,
            [MCPParam("state_machine_path", "Path to sub-state machine")] string stateMachinePath = null,
            [MCPParam("behaviour_type", "Fully-qualified C# type name", required: true)] string behaviourType = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(behaviourType)) throw MCPException.InvalidParams("'behaviour_type' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var stateMachine = ResolveStateMachine(layer, stateMachinePath);

                var resolvedType = ResolveBehaviourType(behaviourType);

                if (!string.IsNullOrEmpty(stateName))
                {
                    var state = FindState(stateMachine, stateName);
                    Undo.RecordObject(state, "Add StateMachineBehaviour");
                    state.AddStateMachineBehaviour(resolvedType);
                }
                else
                {
                    Undo.RecordObject(stateMachine, "Add StateMachineBehaviour");
                    stateMachine.AddStateMachineBehaviour(resolvedType);
                }

                SaveController(controller);
                return new { success = true, action = "add_behaviour", behaviourType, state = stateName, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding behaviour: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("remove_behaviour", Description = "Remove a StateMachineBehaviour", DestructiveHint = true)]
        public static object RemoveBehaviour(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("state_name", "State to remove behaviour from (omit for state machine level)")] string stateName = null,
            [MCPParam("state_machine_path", "Path to sub-state machine")] string stateMachinePath = null,
            [MCPParam("behaviour_type", "Fully-qualified C# type name", required: true)] string behaviourType = null,
            [MCPParam("index", "Index for disambiguation when multiple of same type", Minimum = 0)] int index = 0)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(behaviourType)) throw MCPException.InvalidParams("'behaviour_type' is required.");
            try
            {
                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var stateMachine = ResolveStateMachine(layer, stateMachinePath);

                var resolvedType = ResolveBehaviourType(behaviourType);
                StateMachineBehaviour[] behaviours;
                UnityEngine.Object target;

                if (!string.IsNullOrEmpty(stateName))
                {
                    var state = FindState(stateMachine, stateName);
                    behaviours = state.behaviours;
                    target = state;
                }
                else
                {
                    behaviours = stateMachine.behaviours;
                    target = stateMachine;
                }

                var matching = behaviours.Where(b => resolvedType.IsInstanceOfType(b)).ToList();
                if (index >= matching.Count)
                    throw MCPException.InvalidParams($"Behaviour index {index} out of range. Found {matching.Count} behaviours of type '{behaviourType}'.");

                var behaviourToRemove = matching[index];
                Undo.RecordObject(target, "Remove StateMachineBehaviour");

                var newBehaviours = behaviours.Where(b => b != behaviourToRemove).ToArray();
                if (target is AnimatorState state2)
                    state2.behaviours = newBehaviours;
                else if (target is AnimatorStateMachine sm)
                    sm.behaviours = newBehaviours;

                UnityEngine.Object.DestroyImmediate(behaviourToRemove, true);

                SaveController(controller);
                return new { success = true, action = "remove_behaviour", behaviourType, state = stateName, layer = layerName, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error removing behaviour: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Blend Tree Actions

        [MCPAction("manage_blend_tree", Description = "Create or replace a blend tree on a state")]
        public static object ManageBlendTree(
            [MCPParam("path", "Asset path of the AnimatorController", required: true)] string path = null,
            [MCPParam("layer_name", "Name of the layer", required: true)] string layerName = null,
            [MCPParam("state_name", "Name of the state", required: true)] string stateName = null,
            [MCPParam("state_machine_path", "Path to sub-state machine")] string stateMachinePath = null,
            [MCPParam("blend_type", "Blend tree type", required: true, Enum = new[] { "Simple1D", "SimpleDirectional2D", "FreeformDirectional2D", "FreeformCartesian2D", "Direct" })] string blendType = null,
            [MCPParam("blend_parameter", "Primary blend parameter name", required: true)] string blendParameter = null,
            [MCPParam("blend_parameter_y", "Secondary blend parameter (required for 2D types)")] string blendParameterY = null,
            [MCPParam("children", "List of child motions: {motion_path?, blend_tree?, threshold?, position?, time_scale?, direct_blend_parameter?}", required: true)] List<Dictionary<string, object>> children = null,
            [MCPParam("use_automatic_thresholds", "Automatically compute thresholds")] object useAutomaticThresholds = null)
        {
            if (string.IsNullOrEmpty(path)) throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(layerName)) throw MCPException.InvalidParams("'layer_name' is required.");
            if (string.IsNullOrEmpty(stateName)) throw MCPException.InvalidParams("'state_name' is required.");
            if (string.IsNullOrEmpty(blendType)) throw MCPException.InvalidParams("'blend_type' is required.");
            if (string.IsNullOrEmpty(blendParameter)) throw MCPException.InvalidParams("'blend_parameter' is required.");
            if (children == null || children.Count == 0) throw MCPException.InvalidParams("'children' is required and must not be empty.");
            try
            {
                var parsedBlendType = ParseBlendTreeType(blendType);

                // Validate 2D types need blend_parameter_y
                bool is2DType = parsedBlendType == BlendTreeType.SimpleDirectional2D ||
                                parsedBlendType == BlendTreeType.FreeformDirectional2D ||
                                parsedBlendType == BlendTreeType.FreeformCartesian2D;
                if (is2DType && string.IsNullOrEmpty(blendParameterY))
                    throw MCPException.InvalidParams("'blend_parameter_y' is required for 2D blend types.");

                var controller = LoadController(path);
                int layerIndex = FindLayerIndex(controller, layerName);
                var layer = controller.layers[layerIndex];
                var stateMachine = ResolveStateMachine(layer, stateMachinePath);
                var state = FindState(stateMachine, stateName);

                // Destroy old blend tree if exists
                if (state.motion is BlendTree oldBlendTree)
                {
                    state.motion = null;
                    UnityEngine.Object.DestroyImmediate(oldBlendTree, true);
                }

                // Create new blend tree
                var blendTree = new BlendTree
                {
                    name = stateName + " BlendTree",
                    blendType = parsedBlendType,
                    blendParameter = blendParameter
                };
                if (!string.IsNullOrEmpty(blendParameterY))
                    blendTree.blendParameterY = blendParameterY;

                AssetDatabase.AddObjectToAsset(blendTree, controller);

                // Build children
                var childMotions = BuildBlendTreeChildren(children, controller, parsedBlendType, 0);
                blendTree.children = childMotions;

                // Set automatic thresholds AFTER children
                if (useAutomaticThresholds != null)
                    blendTree.useAutomaticThresholds = Convert.ToBoolean(useAutomaticThresholds);

                state.motion = blendTree;

                SaveController(controller);
                return new { success = true, action = "manage_blend_tree", state = stateName, layer = layerName, childCount = childMotions.Length, path = AssetDatabase.GetAssetPath(controller) };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error managing blend tree: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Runtime Actions

        [MCPAction("runtime_inspect", Description = "Inspect current animator state during Play mode", ReadOnlyHint = true)]
        public static object RuntimeInspect(
            [MCPParam("target", "GameObject name, path, or instance ID", required: true)] string target = null)
        {
            if (string.IsNullOrEmpty(target)) throw MCPException.InvalidParams("'target' is required.");
            if (!EditorApplication.isPlaying)
                throw MCPException.InvalidParams("runtime_inspect requires Play mode. Enter Play mode first.");
            try
            {
                var gameObject = FindGameObject(target);
                var animator = gameObject.GetComponent<Animator>();
                if (animator == null)
                    throw MCPException.InvalidParams($"No Animator component found on '{gameObject.name}'.");

                // Build state name lookup from controller
                var stateNameLookup = BuildStateNameLookup(animator);

                var layerInfoList = new List<object>();
                for (int i = 0; i < animator.layerCount; i++)
                {
                    var stateInfo = animator.GetCurrentAnimatorStateInfo(i);
                    var isInTransition = animator.IsInTransition(i);

                    string currentStateName = stateNameLookup.TryGetValue(stateInfo.shortNameHash, out var name) ? name : $"Unknown (hash: {stateInfo.shortNameHash})";
                    string nextStateName = null;

                    if (isInTransition)
                    {
                        var nextInfo = animator.GetNextAnimatorStateInfo(i);
                        nextStateName = stateNameLookup.TryGetValue(nextInfo.shortNameHash, out var nextName) ? nextName : $"Unknown (hash: {nextInfo.shortNameHash})";
                    }

                    layerInfoList.Add(new
                    {
                        name = animator.GetLayerName(i),
                        currentState = currentStateName,
                        normalizedTime = stateInfo.normalizedTime,
                        isInTransition,
                        nextState = nextStateName
                    });
                }

                var parameterInfoList = new List<object>();
                foreach (var param in animator.parameters)
                {
                    object currentValue = param.type switch
                    {
                        AnimatorControllerParameterType.Float => (object)animator.GetFloat(param.name),
                        AnimatorControllerParameterType.Int => animator.GetInteger(param.name),
                        AnimatorControllerParameterType.Bool => animator.GetBool(param.name),
                        AnimatorControllerParameterType.Trigger => animator.GetBool(param.name),
                        _ => null
                    };

                    parameterInfoList.Add(new
                    {
                        name = param.name,
                        type = param.type.ToString(),
                        value = currentValue
                    });
                }

                return new
                {
                    success = true,
                    target = gameObject.name,
                    isActiveAndEnabled = animator.isActiveAndEnabled,
                    layers = layerInfoList.ToArray(),
                    parameters = parameterInfoList.ToArray()
                };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error inspecting runtime animator: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        [MCPAction("runtime_control", Description = "Set animator parameter values during Play mode")]
        public static object RuntimeControl(
            [MCPParam("target", "GameObject name, path, or instance ID", required: true)] string target = null,
            [MCPParam("parameter_name", "Name of the parameter to set", required: true)] string parameterName = null,
            [MCPParam("value", "Value for Float/Int/Bool parameters")] object value = null,
            [MCPParam("trigger", "true to SetTrigger, false to ResetTrigger")] object trigger = null)
        {
            if (string.IsNullOrEmpty(target)) throw MCPException.InvalidParams("'target' is required.");
            if (string.IsNullOrEmpty(parameterName)) throw MCPException.InvalidParams("'parameter_name' is required.");
            if (!EditorApplication.isPlaying)
                throw MCPException.InvalidParams("runtime_control requires Play mode. Enter Play mode first.");
            try
            {
                var gameObject = FindGameObject(target);
                var animator = gameObject.GetComponent<Animator>();
                if (animator == null)
                    throw MCPException.InvalidParams($"No Animator component found on '{gameObject.name}'.");

                // Find parameter type
                AnimatorControllerParameter targetParam = null;
                foreach (var param in animator.parameters)
                {
                    if (param.name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetParam = param;
                        break;
                    }
                }
                if (targetParam == null)
                    throw MCPException.InvalidParams($"Parameter '{parameterName}' not found on animator.");

                object resultValue = null;

                switch (targetParam.type)
                {
                    case AnimatorControllerParameterType.Float:
                        if (value == null) throw MCPException.InvalidParams("'value' is required for Float parameters.");
                        var floatVal = Convert.ToSingle(value);
                        animator.SetFloat(parameterName, floatVal);
                        resultValue = floatVal;
                        break;
                    case AnimatorControllerParameterType.Int:
                        if (value == null) throw MCPException.InvalidParams("'value' is required for Int parameters.");
                        var intVal = Convert.ToInt32(value);
                        animator.SetInteger(parameterName, intVal);
                        resultValue = intVal;
                        break;
                    case AnimatorControllerParameterType.Bool:
                        if (value == null) throw MCPException.InvalidParams("'value' is required for Bool parameters.");
                        var boolVal = Convert.ToBoolean(value);
                        animator.SetBool(parameterName, boolVal);
                        resultValue = boolVal;
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        if (trigger == null) throw MCPException.InvalidParams("'trigger' is required for Trigger parameters.");
                        var triggerVal = Convert.ToBoolean(trigger);
                        if (triggerVal) animator.SetTrigger(parameterName);
                        else animator.ResetTrigger(parameterName);
                        resultValue = triggerVal;
                        break;
                }

                return new { success = true, target = gameObject.name, parameter = parameterName, type = targetParam.type.ToString(), value = resultValue };
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error controlling runtime animator: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Private Helpers

        private static AnimatorController LoadController(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("Asset path is required.");

            if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/"))
                path = "Assets/" + path;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

            if (controller == null)
            {
                // Try search by name as fallback
                var searchName = System.IO.Path.GetFileNameWithoutExtension(path);
                var guids = AssetDatabase.FindAssets($"t:AnimatorController {searchName}");
                if (guids.Length > 0)
                {
                    var foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(foundPath);
                }
            }

            if (controller == null)
                throw MCPException.InvalidParams($"AnimatorController not found at '{path}'. Provide the full asset path (e.g., Assets/Animations/Player.controller).");

            return controller;
        }

        private static int FindLayerIndex(AnimatorController controller, string layerName)
        {
            var layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].name.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            var available = string.Join(", ", layers.Select(l => $"'{l.name}'"));
            throw MCPException.InvalidParams($"Layer '{layerName}' not found. Available: {available}");
        }

        private static int FindParameterIndex(AnimatorController controller, string parameterName)
        {
            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            var available = string.Join(", ", parameters.Select(p => $"'{p.name}'"));
            throw MCPException.InvalidParams($"Parameter '{parameterName}' not found. Available: {available}");
        }

        private static AnimatorStateMachine ResolveStateMachine(AnimatorControllerLayer layer, string stateMachinePath)
        {
            var currentStateMachine = layer.stateMachine;
            if (string.IsNullOrEmpty(stateMachinePath))
                return currentStateMachine;

            var segments = stateMachinePath.Split('/');
            foreach (var segment in segments)
            {
                bool found = false;
                foreach (var childSm in currentStateMachine.stateMachines)
                {
                    if (childSm.stateMachine.name.Equals(segment, StringComparison.OrdinalIgnoreCase))
                    {
                        currentStateMachine = childSm.stateMachine;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    var available = string.Join(", ", currentStateMachine.stateMachines.Select(s => $"'{s.stateMachine.name}'"));
                    throw MCPException.InvalidParams($"Sub-state machine '{segment}' not found. Available: {(available.Length > 0 ? available : "none")}");
                }
            }
            return currentStateMachine;
        }

        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
        {
            foreach (var childState in stateMachine.states)
            {
                if (childState.state.name.Equals(stateName, StringComparison.OrdinalIgnoreCase))
                    return childState.state;
            }
            var available = string.Join(", ", stateMachine.states.Select(s => $"'{s.state.name}'"));
            throw MCPException.InvalidParams($"State '{stateName}' not found. Available: {(available.Length > 0 ? available : "none")}");
        }

        private static AnimatorStateMachine FindSubStateMachine(AnimatorStateMachine parentStateMachine, string name)
        {
            foreach (var childSm in parentStateMachine.stateMachines)
            {
                if (childSm.stateMachine.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return childSm.stateMachine;
            }
            var available = string.Join(", ", parentStateMachine.stateMachines.Select(s => $"'{s.stateMachine.name}'"));
            throw MCPException.InvalidParams($"Sub-state machine '{name}' not found. Available: {(available.Length > 0 ? available : "none")}");
        }

        private static AnimatorStateTransition FindStateTransition(AnimatorState sourceState, string destinationName, int index)
        {
            var matching = sourceState.transitions.Where(t =>
                (destinationName.Equals("Exit", StringComparison.OrdinalIgnoreCase) && t.isExit) ||
                (t.destinationState != null && t.destinationState.name.Equals(destinationName, StringComparison.OrdinalIgnoreCase)) ||
                (t.destinationStateMachine != null && t.destinationStateMachine.name.Equals(destinationName, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            if (matching.Count == 0)
                throw MCPException.InvalidParams($"No transition found from '{sourceState.name}' to '{destinationName}'.");
            if (index >= matching.Count)
                throw MCPException.InvalidParams($"Transition index {index} out of range. Found {matching.Count} transitions from '{sourceState.name}' to '{destinationName}'.");
            return matching[index];
        }

        private static AnimatorStateTransition FindAnyStateTransition(AnimatorStateMachine stateMachine, string destinationName, int index)
        {
            var matching = stateMachine.anyStateTransitions.Where(t =>
                (t.destinationState != null && t.destinationState.name.Equals(destinationName, StringComparison.OrdinalIgnoreCase)) ||
                (t.destinationStateMachine != null && t.destinationStateMachine.name.Equals(destinationName, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            if (matching.Count == 0)
                throw MCPException.InvalidParams($"No Any State transition found to '{destinationName}'.");
            if (index >= matching.Count)
                throw MCPException.InvalidParams($"Transition index {index} out of range. Found {matching.Count} Any State transitions to '{destinationName}'.");
            return matching[index];
        }

        private static AnimatorTransition FindEntryTransition(AnimatorStateMachine stateMachine, string destinationName, int index)
        {
            var matching = stateMachine.entryTransitions.Where(t =>
                (t.destinationState != null && t.destinationState.name.Equals(destinationName, StringComparison.OrdinalIgnoreCase)) ||
                (t.destinationStateMachine != null && t.destinationStateMachine.name.Equals(destinationName, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            if (matching.Count == 0)
                throw MCPException.InvalidParams($"No Entry transition found to '{destinationName}'.");
            if (index >= matching.Count)
                throw MCPException.InvalidParams($"Transition index {index} out of range. Found {matching.Count} Entry transitions to '{destinationName}'.");
            return matching[index];
        }

        private static void SaveController(AnimatorController controller)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static GameObject FindGameObject(string target)
        {
            if (string.IsNullOrEmpty(target))
                throw MCPException.InvalidParams("'target' is required.");

            if (int.TryParse(target, out int instanceId))
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId);
                if (obj is GameObject gameObject) return gameObject;
                if (obj is Component component) return component.gameObject;
            }

            if (target.Contains("/"))
            {
                var scene = SceneManager.GetActiveScene();
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name.Equals(target, StringComparison.OrdinalIgnoreCase)) return root;
                    if (target.StartsWith(root.name + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        var found = root.transform.Find(target.Substring(root.name.Length + 1));
                        if (found != null) return found.gameObject;
                    }
                }
            }

            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            foreach (var gameObject in allObjects)
            {
                if (gameObject.name.Equals(target, StringComparison.OrdinalIgnoreCase))
                    return gameObject;
            }

            throw MCPException.InvalidParams($"GameObject not found: '{target}'");
        }

        private static void SetParameterDefaultValue(ref AnimatorControllerParameter param, object value)
        {
            switch (param.type)
            {
                case AnimatorControllerParameterType.Float:
                    param.defaultFloat = Convert.ToSingle(value);
                    break;
                case AnimatorControllerParameterType.Int:
                    param.defaultInt = Convert.ToInt32(value);
                    break;
                case AnimatorControllerParameterType.Bool:
                    param.defaultBool = Convert.ToBoolean(value);
                    break;
                case AnimatorControllerParameterType.Trigger:
                    // Triggers don't have persistent defaults
                    break;
            }
        }

        private static List<string> CollectConditionsReferencingParameter(AnimatorController controller, string parameterName)
        {
            var warnings = new List<string>();
            foreach (var layer in controller.layers)
            {
                CollectConditionsInStateMachine(layer.stateMachine, layer.name, parameterName, warnings, 0);
            }
            return warnings;
        }

        private static void CollectConditionsInStateMachine(AnimatorStateMachine stateMachine, string layerName, string parameterName, List<string> warnings, int depth)
        {
            if (depth > MaxStateMachineDepth) return;

            foreach (var childState in stateMachine.states)
            {
                foreach (var transition in childState.state.transitions)
                {
                    foreach (var condition in transition.conditions)
                    {
                        if (condition.parameter.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                        {
                            var destName = transition.isExit ? "Exit" : (transition.destinationState?.name ?? "unknown");
                            warnings.Add($"Condition on transition '{childState.state.name}' -> '{destName}' in layer '{layerName}'");
                        }
                    }
                }
            }

            foreach (var anyTransition in stateMachine.anyStateTransitions)
            {
                foreach (var condition in anyTransition.conditions)
                {
                    if (condition.parameter.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                    {
                        var destName = anyTransition.destinationState?.name ?? "unknown";
                        warnings.Add($"Condition on Any State transition to '{destName}' in layer '{layerName}'");
                    }
                }
            }

            foreach (var childSm in stateMachine.stateMachines)
            {
                CollectConditionsInStateMachine(childSm.stateMachine, layerName, parameterName, warnings, depth + 1);
            }
        }

        private static Vector3 ParseStatePosition(object position)
        {
            if (position == null) return new Vector3(200, 0, 0);

            if (position is Newtonsoft.Json.Linq.JArray jArray)
            {
                float posX = jArray.Count > 0 ? (float)jArray[0] : 200f;
                float posY = jArray.Count > 1 ? (float)jArray[1] : 0f;
                return new Vector3(posX, posY, 0);
            }

            if (position is List<object> positionList)
            {
                float posX = positionList.Count > 0 ? Convert.ToSingle(positionList[0]) : 200f;
                float posY = positionList.Count > 1 ? Convert.ToSingle(positionList[1]) : 0f;
                return new Vector3(posX, posY, 0);
            }

            return new Vector3(200, 0, 0);
        }

        private static void ApplyTransitionTiming(AnimatorStateTransition transition, bool hasExitTime, double exitTime, double duration)
        {
            transition.hasExitTime = hasExitTime;
            transition.exitTime = (float)exitTime;
            transition.duration = (float)duration;
            transition.hasFixedDuration = true;
        }

        private static void ApplyTransitionModifications(AnimatorStateTransition transition,
            object hasExitTime, object exitTime, object duration, object offset,
            object hasFixedDuration, string interruptionSource, object orderedInterruption, object canTransitionToSelf)
        {
            if (hasExitTime != null) transition.hasExitTime = Convert.ToBoolean(hasExitTime);
            if (exitTime != null) transition.exitTime = Convert.ToSingle(exitTime);
            if (duration != null) transition.duration = Convert.ToSingle(duration);
            if (offset != null) transition.offset = Convert.ToSingle(offset);
            if (hasFixedDuration != null) transition.hasFixedDuration = Convert.ToBoolean(hasFixedDuration);
            if (interruptionSource != null) transition.interruptionSource = ParseInterruptionSource(interruptionSource);
            if (orderedInterruption != null) transition.orderedInterruption = Convert.ToBoolean(orderedInterruption);
            if (canTransitionToSelf != null) transition.canTransitionToSelf = Convert.ToBoolean(canTransitionToSelf);
        }

        private static AnimatorCondition[] BuildConditionArray(List<Dictionary<string, object>> conditions)
        {
            var conditionArray = new AnimatorCondition[conditions.Count];
            for (int i = 0; i < conditions.Count; i++)
            {
                var conditionDef = conditions[i];
                conditionArray[i] = new AnimatorCondition
                {
                    parameter = conditionDef.TryGetValue("parameter", out var paramObj) ? paramObj.ToString() : "",
                    mode = conditionDef.TryGetValue("mode", out var modeObj) ? ParseConditionMode(modeObj.ToString()) : AnimatorConditionMode.If,
                    threshold = conditionDef.TryGetValue("threshold", out var threshObj) ? Convert.ToSingle(threshObj) : 0f
                };
            }
            return conditionArray;
        }

        private static ChildMotion[] BuildBlendTreeChildren(List<Dictionary<string, object>> children, AnimatorController controller, BlendTreeType blendType, int depth)
        {
            if (depth > MaxBlendTreeDepth)
                throw MCPException.InvalidParams($"Blend tree nesting depth exceeds maximum of {MaxBlendTreeDepth}.");

            int count = Math.Min(children.Count, MaxBlendTreeChildren);
            var childMotions = new ChildMotion[count];

            for (int i = 0; i < count; i++)
            {
                var childDef = children[i];
                var childMotion = new ChildMotion();

                if (childDef.TryGetValue("motion_path", out var motionPathObj) && motionPathObj != null)
                {
                    var motionPath = motionPathObj.ToString();
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionPath);
                    if (clip == null)
                        throw MCPException.InvalidParams($"AnimationClip not found at '{motionPath}'");
                    childMotion.motion = clip;
                }
                else if (childDef.TryGetValue("blend_tree", out var blendTreeObj) && blendTreeObj is Dictionary<string, object> nestedDef)
                {
                    // Nested blend tree
                    var nestedTree = new BlendTree
                    {
                        name = $"Nested BlendTree {depth + 1}_{i}",
                        blendType = nestedDef.TryGetValue("blend_type", out var btObj) ? ParseBlendTreeType(btObj.ToString()) : BlendTreeType.Simple1D,
                        blendParameter = nestedDef.TryGetValue("blend_parameter", out var bpObj) ? bpObj.ToString() : ""
                    };
                    if (nestedDef.TryGetValue("blend_parameter_y", out var bpyObj) && bpyObj != null)
                        nestedTree.blendParameterY = bpyObj.ToString();

                    AssetDatabase.AddObjectToAsset(nestedTree, controller);

                    if (nestedDef.TryGetValue("children", out var nestedChildrenObj) && nestedChildrenObj is List<Dictionary<string, object>> nestedChildren)
                    {
                        nestedTree.children = BuildBlendTreeChildren(nestedChildren, controller, nestedTree.blendType, depth + 1);
                    }

                    if (nestedDef.TryGetValue("use_automatic_thresholds", out var autoThresh))
                        nestedTree.useAutomaticThresholds = Convert.ToBoolean(autoThresh);

                    childMotion.motion = nestedTree;
                }

                if (childDef.TryGetValue("threshold", out var thresholdObj))
                    childMotion.threshold = Convert.ToSingle(thresholdObj);
                if (childDef.TryGetValue("time_scale", out var timeScaleObj))
                    childMotion.timeScale = Convert.ToSingle(timeScaleObj);
                else
                    childMotion.timeScale = 1f;
                if (childDef.TryGetValue("position", out var posObj) && posObj is Newtonsoft.Json.Linq.JArray posArray)
                    childMotion.position = new Vector2((float)posArray[0], (float)posArray[1]);
                if (childDef.TryGetValue("direct_blend_parameter", out var directParamObj) && directParamObj != null)
                    childMotion.directBlendParameter = directParamObj.ToString();

                // Validate Direct type children have directBlendParameter
                if (blendType == BlendTreeType.Direct && string.IsNullOrEmpty(childMotion.directBlendParameter))
                    throw MCPException.InvalidParams($"Child {i} requires 'direct_blend_parameter' for Direct blend type.");

                childMotions[i] = childMotion;
            }

            return childMotions;
        }

        private static Type ResolveBehaviourType(string behaviourType)
        {
            // Try exact match first (fully-qualified name)
            Type resolvedType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolvedType = assembly.GetType(behaviourType);
                if (resolvedType != null) break;
            }

            // Fallback: search by short name across all StateMachineBehaviour subclasses
            if (resolvedType == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (typeof(StateMachineBehaviour).IsAssignableFrom(type) &&
                                (type.Name.Equals(behaviourType, StringComparison.OrdinalIgnoreCase) ||
                                 type.FullName.Equals(behaviourType, StringComparison.OrdinalIgnoreCase)))
                            {
                                resolvedType = type;
                                break;
                            }
                        }
                    }
                    catch (System.Reflection.ReflectionTypeLoadException) { }
                    if (resolvedType != null) break;
                }
            }

            if (resolvedType == null)
                throw MCPException.InvalidParams($"Type not found: '{behaviourType}'. Use the class name or fully-qualified type name (e.g., 'MyNamespace.MyBehaviour').");
            if (!typeof(StateMachineBehaviour).IsAssignableFrom(resolvedType))
                throw MCPException.InvalidParams($"'{behaviourType}' is not a StateMachineBehaviour subclass.");

            return resolvedType;
        }

        private static Dictionary<int, string> BuildStateNameLookup(Animator animator)
        {
            var lookup = new Dictionary<int, string>();
            var runtimeController = animator.runtimeAnimatorController;
            if (runtimeController is AnimatorController editorController)
            {
                foreach (var layer in editorController.layers)
                {
                    CollectStateNames(layer.stateMachine, lookup, 0);
                }
            }
            return lookup;
        }

        private static void CollectStateNames(AnimatorStateMachine stateMachine, Dictionary<int, string> lookup, int depth)
        {
            if (depth > MaxStateMachineDepth) return;
            foreach (var childState in stateMachine.states)
            {
                lookup[Animator.StringToHash(childState.state.name)] = childState.state.name;
            }
            foreach (var childSm in stateMachine.stateMachines)
            {
                CollectStateNames(childSm.stateMachine, lookup, depth + 1);
            }
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
                return;

            folderPath = folderPath.Replace("\\", "/");
            var parts = folderPath.Split('/');
            string currentPath = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                var nextPath = currentPath + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                    AssetDatabase.CreateFolder(currentPath, parts[i]);
                currentPath = nextPath;
            }
        }

        #region Parse Helpers

        private static AnimatorLayerBlendingMode ParseBlendingMode(string mode)
        {
            if (string.Equals(mode, "Override", StringComparison.OrdinalIgnoreCase)) return AnimatorLayerBlendingMode.Override;
            if (string.Equals(mode, "Additive", StringComparison.OrdinalIgnoreCase)) return AnimatorLayerBlendingMode.Additive;
            throw MCPException.InvalidParams($"Invalid blending mode '{mode}'. Use 'Override' or 'Additive'.");
        }

        private static AnimatorControllerParameterType ParseParameterType(string type)
        {
            if (string.Equals(type, "Float", StringComparison.OrdinalIgnoreCase)) return AnimatorControllerParameterType.Float;
            if (string.Equals(type, "Int", StringComparison.OrdinalIgnoreCase)) return AnimatorControllerParameterType.Int;
            if (string.Equals(type, "Bool", StringComparison.OrdinalIgnoreCase)) return AnimatorControllerParameterType.Bool;
            if (string.Equals(type, "Trigger", StringComparison.OrdinalIgnoreCase)) return AnimatorControllerParameterType.Trigger;
            throw MCPException.InvalidParams($"Invalid parameter type '{type}'. Use 'Float', 'Int', 'Bool', or 'Trigger'.");
        }

        private static AnimatorConditionMode ParseConditionMode(string mode)
        {
            if (string.Equals(mode, "If", StringComparison.OrdinalIgnoreCase)) return AnimatorConditionMode.If;
            if (string.Equals(mode, "IfNot", StringComparison.OrdinalIgnoreCase)) return AnimatorConditionMode.IfNot;
            if (string.Equals(mode, "Greater", StringComparison.OrdinalIgnoreCase)) return AnimatorConditionMode.Greater;
            if (string.Equals(mode, "Less", StringComparison.OrdinalIgnoreCase)) return AnimatorConditionMode.Less;
            if (string.Equals(mode, "Equals", StringComparison.OrdinalIgnoreCase)) return AnimatorConditionMode.Equals;
            if (string.Equals(mode, "NotEqual", StringComparison.OrdinalIgnoreCase)) return AnimatorConditionMode.NotEqual;
            throw MCPException.InvalidParams($"Invalid condition mode '{mode}'. Use 'If', 'IfNot', 'Greater', 'Less', 'Equals', or 'NotEqual'.");
        }

        private static BlendTreeType ParseBlendTreeType(string type)
        {
            if (string.Equals(type, "Simple1D", StringComparison.OrdinalIgnoreCase)) return BlendTreeType.Simple1D;
            if (string.Equals(type, "SimpleDirectional2D", StringComparison.OrdinalIgnoreCase)) return BlendTreeType.SimpleDirectional2D;
            if (string.Equals(type, "FreeformDirectional2D", StringComparison.OrdinalIgnoreCase)) return BlendTreeType.FreeformDirectional2D;
            if (string.Equals(type, "FreeformCartesian2D", StringComparison.OrdinalIgnoreCase)) return BlendTreeType.FreeformCartesian2D;
            if (string.Equals(type, "Direct", StringComparison.OrdinalIgnoreCase)) return BlendTreeType.Direct;
            throw MCPException.InvalidParams($"Invalid blend tree type '{type}'. Use 'Simple1D', 'SimpleDirectional2D', 'FreeformDirectional2D', 'FreeformCartesian2D', or 'Direct'.");
        }

        private static TransitionInterruptionSource ParseInterruptionSource(string source)
        {
            if (string.Equals(source, "None", StringComparison.OrdinalIgnoreCase)) return TransitionInterruptionSource.None;
            if (string.Equals(source, "Source", StringComparison.OrdinalIgnoreCase)) return TransitionInterruptionSource.Source;
            if (string.Equals(source, "Destination", StringComparison.OrdinalIgnoreCase)) return TransitionInterruptionSource.Destination;
            if (string.Equals(source, "SourceThenDestination", StringComparison.OrdinalIgnoreCase)) return TransitionInterruptionSource.SourceThenDestination;
            if (string.Equals(source, "DestinationThenSource", StringComparison.OrdinalIgnoreCase)) return TransitionInterruptionSource.DestinationThenSource;
            throw MCPException.InvalidParams($"Invalid interruption source '{source}'. Use 'None', 'Source', 'Destination', 'SourceThenDestination', or 'DestinationThenSource'.");
        }

        #endregion

        #region Serialization Helpers

        private static object BuildControllerInfo(AnimatorController controller, string assetPath, int cursor, int pageSize)
        {
            var allLayers = controller.layers;
            int totalLayers = allLayers.Length;
            int startIndex = Math.Min(cursor, totalLayers);
            int endIndex = Math.Min(startIndex + pageSize, totalLayers);

            var layers = new List<object>();
            for (int i = startIndex; i < endIndex; i++)
            {
                layers.Add(BuildLayerInfo(allLayers[i]));
            }

            var parameters = new List<object>();
            foreach (var parameter in controller.parameters)
            {
                parameters.Add(BuildParameterInfo(parameter));
            }

            return new
            {
                name = controller.name,
                assetPath,
                instanceId = controller.GetInstanceID(),
                totalLayers,
                cursor = startIndex,
                pageSize,
                layerCount = allLayers.Length,
                layers = layers.ToArray(),
                parameterCount = controller.parameters.Length,
                parameters = parameters.ToArray(),
                animationClips = GetAnimationClipsInfo(controller)
            };
        }

        private static object BuildLayerInfo(AnimatorControllerLayer layer)
        {
            return new
            {
                name = layer.name,
                defaultWeight = layer.defaultWeight,
                blendingMode = layer.blendingMode.ToString(),
                syncedLayerIndex = layer.syncedLayerIndex,
                iKPass = layer.iKPass,
                stateMachine = layer.stateMachine != null ? BuildStateMachineInfo(layer.stateMachine, 0) : null
            };
        }

        private static object BuildStateMachineInfo(AnimatorStateMachine stateMachine, int depth)
        {
            if (depth > MaxStateMachineDepth)
            {
                return new { name = stateMachine.name, truncated = true, reason = "Maximum depth reached" };
            }

            var states = new List<object>();
            int stateCount = 0;
            foreach (var childState in stateMachine.states)
            {
                if (stateCount >= MaxStatesPerStateMachine) break;
                states.Add(BuildStateInfo(childState));
                stateCount++;
            }

            var subStateMachines = new List<object>();
            foreach (var childSm in stateMachine.stateMachines)
            {
                subStateMachines.Add(new
                {
                    name = childSm.stateMachine.name,
                    position = new { x = childSm.position.x, y = childSm.position.y },
                    stateMachine = BuildStateMachineInfo(childSm.stateMachine, depth + 1)
                });
            }

            var anyStateTransitions = new List<object>();
            foreach (var transition in stateMachine.anyStateTransitions)
            {
                anyStateTransitions.Add(BuildTransitionInfo(transition));
            }

            var entryTransitions = new List<object>();
            foreach (var transition in stateMachine.entryTransitions)
            {
                entryTransitions.Add(new
                {
                    destinationState = transition.destinationState != null ? transition.destinationState.name : null,
                    destinationStateMachine = transition.destinationStateMachine != null ? transition.destinationStateMachine.name : null,
                    conditionCount = transition.conditions.Length,
                    conditions = transition.conditions.Select(c => new
                    {
                        parameter = c.parameter,
                        mode = c.mode.ToString(),
                        threshold = c.threshold
                    }).ToArray()
                });
            }

            return new
            {
                name = stateMachine.name,
                defaultState = stateMachine.defaultState != null ? stateMachine.defaultState.name : null,
                stateCount = stateMachine.states.Length,
                states = states.ToArray(),
                subStateMachineCount = stateMachine.stateMachines.Length,
                subStateMachines = subStateMachines.ToArray(),
                anyStateTransitions = anyStateTransitions.ToArray(),
                entryTransitions = entryTransitions.ToArray(),
                behaviours = stateMachine.behaviours.Select(b => new
                {
                    type = b.GetType().Name,
                    fullType = b.GetType().FullName
                }).ToArray()
            };
        }

        private static object BuildStateInfo(ChildAnimatorState childState)
        {
            var state = childState.state;
            var transitions = new List<object>();
            int transitionCount = 0;
            foreach (var transition in state.transitions)
            {
                if (transitionCount >= MaxTransitionsPerState) break;
                transitions.Add(BuildTransitionInfo(transition));
                transitionCount++;
            }

            return new
            {
                name = state.name,
                nameHash = state.nameHash,
                tag = state.tag,
                position = new { x = childState.position.x, y = childState.position.y },
                speed = state.speed,
                speedParameterActive = state.speedParameterActive,
                speedParameter = state.speedParameter,
                cycleOffset = state.cycleOffset,
                cycleOffsetParameterActive = state.cycleOffsetParameterActive,
                cycleOffsetParameter = state.cycleOffsetParameter,
                mirror = state.mirror,
                mirrorParameterActive = state.mirrorParameterActive,
                mirrorParameter = state.mirrorParameter,
                iKOnFeet = state.iKOnFeet,
                writeDefaultValues = state.writeDefaultValues,
                motion = GetMotionInfo(state.motion),
                transitionCount = state.transitions.Length,
                transitions = transitions.ToArray(),
                behaviours = state.behaviours.Select(b => new
                {
                    type = b.GetType().Name,
                    fullType = b.GetType().FullName
                }).ToArray()
            };
        }

        private static object BuildTransitionInfo(AnimatorStateTransition transition)
        {
            return new
            {
                name = transition.name,
                destinationState = transition.destinationState != null ? transition.destinationState.name : null,
                destinationStateMachine = transition.destinationStateMachine != null ? transition.destinationStateMachine.name : null,
                isExit = transition.isExit,
                mute = transition.mute,
                solo = transition.solo,
                hasExitTime = transition.hasExitTime,
                exitTime = transition.exitTime,
                hasFixedDuration = transition.hasFixedDuration,
                duration = transition.duration,
                offset = transition.offset,
                orderedInterruption = transition.orderedInterruption,
                interruptionSource = transition.interruptionSource.ToString(),
                canTransitionToSelf = transition.canTransitionToSelf,
                conditionCount = transition.conditions.Length,
                conditions = transition.conditions.Select(c => new
                {
                    parameter = c.parameter,
                    mode = c.mode.ToString(),
                    threshold = c.threshold
                }).ToArray()
            };
        }

        private static object BuildParameterInfo(AnimatorControllerParameter parameter)
        {
            object defaultValue = parameter.type switch
            {
                AnimatorControllerParameterType.Float => (object)parameter.defaultFloat,
                AnimatorControllerParameterType.Int => parameter.defaultInt,
                AnimatorControllerParameterType.Bool => parameter.defaultBool,
                AnimatorControllerParameterType.Trigger => false,
                _ => null
            };

            return new
            {
                name = parameter.name,
                nameHash = parameter.nameHash,
                type = parameter.type.ToString(),
                defaultValue
            };
        }

        private static object GetMotionInfo(Motion motion)
        {
            if (motion == null) return null;

            if (motion is AnimationClip clip)
            {
                return new
                {
                    type = "AnimationClip",
                    name = clip.name,
                    length = clip.length,
                    frameRate = clip.frameRate,
                    isLooping = clip.isLooping,
                    isHumanMotion = clip.isHumanMotion,
                    legacy = clip.legacy,
                    hasGenericRootTransform = clip.hasGenericRootTransform,
                    hasMotionCurves = clip.hasMotionCurves,
                    hasMotionFloatCurves = clip.hasMotionFloatCurves,
                    hasRootCurves = clip.hasRootCurves,
                    assetPath = AssetDatabase.GetAssetPath(clip)
                };
            }

            if (motion is BlendTree blendTree)
            {
                var children = new List<object>();
                int childCount = 0;
                foreach (var child in blendTree.children)
                {
                    if (childCount >= MaxBlendTreeChildren) break;
                    children.Add(new
                    {
                        motion = GetMotionInfo(child.motion),
                        threshold = child.threshold,
                        position = new { x = child.position.x, y = child.position.y },
                        timeScale = child.timeScale,
                        cycleOffset = child.cycleOffset,
                        directBlendParameter = child.directBlendParameter,
                        mirror = child.mirror
                    });
                    childCount++;
                }

                return new
                {
                    type = "BlendTree",
                    name = blendTree.name,
                    blendType = blendTree.blendType.ToString(),
                    blendParameter = blendTree.blendParameter,
                    blendParameterY = blendTree.blendParameterY,
                    minThreshold = blendTree.minThreshold,
                    maxThreshold = blendTree.maxThreshold,
                    useAutomaticThresholds = blendTree.useAutomaticThresholds,
                    childCount = blendTree.children.Length,
                    children = children.ToArray()
                };
            }

            return new { type = motion.GetType().Name, name = motion.name };
        }

        private static object GetAnimationClipsInfo(AnimatorController controller)
        {
            var clips = controller.animationClips;
            var clipInfoList = new List<object>();

            foreach (var clip in clips)
            {
                if (clip == null) continue;
                clipInfoList.Add(new
                {
                    name = clip.name,
                    length = clip.length,
                    frameRate = clip.frameRate,
                    isLooping = clip.isLooping,
                    assetPath = AssetDatabase.GetAssetPath(clip)
                });
            }

            return new { totalClips = clips.Length, clips = clipInfoList.ToArray() };
        }

        #endregion

        #endregion
    }
}
