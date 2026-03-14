#if UNITY_MCP_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Manages Input Action Assets: full CRUD on assets, maps, actions, bindings, and composites.
    /// </summary>
    [MCPTool("manage_input_actions", "Manage Input Action Assets: assets, maps, actions, bindings, composites", Category = "Input")]
    public static class ManageInputActions
    {
        #region Constants

        private const int MaxAssetResults = 50;
        private const int MaxActionsPerMap = 50;
        private const int MaxBindingsPerAction = 30;

        #endregion

        #region Action Methods — Asset Level

        /// <summary>
        /// Find all .inputactions files in the project.
        /// </summary>
        [MCPAction("list", Description = "Find all Input Action Assets in the project", ReadOnlyHint = true)]
        public static object List(
            [MCPParam("search_pattern", "Optional name filter for asset search")] string searchPattern = null)
        {
            try
            {
                return HandleList(searchPattern);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error listing Input Action Assets: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Create a new InputActionAsset.
        /// </summary>
        [MCPAction("create", Description = "Create a new Input Action Asset")]
        public static object Create(
            [MCPParam("path", "Asset path (must end in .inputactions)", required: true)] string path = null,
            [MCPParam("maps", "Optional list of action map names to create")] List<string> maps = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            try
            {
                return HandleCreate(path, maps);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error creating Input Action Asset: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Full structure dump of an InputActionAsset.
        /// </summary>
        [MCPAction("inspect", Description = "Inspect full structure of an Input Action Asset", ReadOnlyHint = true)]
        public static object Inspect(
            [MCPParam("path", "Asset path to inspect", required: true)] string path = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            try
            {
                return HandleInspect(path);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error inspecting Input Action Asset: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Delete an InputActionAsset.
        /// </summary>
        [MCPAction("delete", Description = "Delete an Input Action Asset", DestructiveHint = true)]
        public static object Delete(
            [MCPParam("path", "Asset path to delete", required: true)] string path = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            try
            {
                return HandleDelete(path);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error deleting Input Action Asset: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Action Methods — Map Level

        /// <summary>
        /// List action maps in an asset.
        /// </summary>
        [MCPAction("list_maps", Description = "List action maps in an Input Action Asset", ReadOnlyHint = true)]
        public static object ListMaps(
            [MCPParam("path", "Asset path", required: true)] string path = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            try
            {
                return HandleListMaps(path);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error listing action maps: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Add an action map to an asset.
        /// </summary>
        [MCPAction("add_map", Description = "Add an action map to an Input Action Asset")]
        public static object AddMap(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Name for the new action map", required: true)] string mapName = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            try
            {
                return HandleAddMap(path, mapName);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding action map: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Rename an action map.
        /// </summary>
        [MCPAction("rename_map", Description = "Rename an action map")]
        public static object RenameMap(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Current map name", required: true)] string mapName = null,
            [MCPParam("new_name", "New map name", required: true)] string newName = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            if (string.IsNullOrEmpty(newName))
                throw MCPException.InvalidParams("'new_name' is required.");
            try
            {
                return HandleRenameMap(path, mapName, newName);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error renaming action map: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Remove an action map from an asset.
        /// </summary>
        [MCPAction("remove_map", Description = "Remove an action map from an Input Action Asset", DestructiveHint = true)]
        public static object RemoveMap(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Name of the map to remove", required: true)] string mapName = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            try
            {
                return HandleRemoveMap(path, mapName);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error removing action map: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Action Methods — Action Level

        /// <summary>
        /// List actions in an action map.
        /// </summary>
        [MCPAction("list_actions", Description = "List actions in an action map", ReadOnlyHint = true)]
        public static object ListActions(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Action map name", required: true)] string mapName = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            try
            {
                return HandleListActions(path, mapName);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error listing actions: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Add an action to an action map.
        /// </summary>
        [MCPAction("add_action", Description = "Add an action to an action map")]
        public static object AddAction(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Action map name", required: true)] string mapName = null,
            [MCPParam("action_name", "Name for the new action", required: true)] string actionName = null,
            [MCPParam("action_type", "Action type", Enum = new[] { "value", "button", "passthrough" })] string actionType = null,
            [MCPParam("control_type", "Expected control type (e.g. Vector2, Button, Axis)")] string controlType = null,
            [MCPParam("binding", "Optional initial binding path (e.g. <Keyboard>/space)")] string binding = null,
            [MCPParam("interactions", "Interactions for the action (e.g. press, hold)")] string interactions = null,
            [MCPParam("processors", "Processors for the action (e.g. normalize, invert)")] string processors = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            if (string.IsNullOrEmpty(actionName))
                throw MCPException.InvalidParams("'action_name' is required.");
            try
            {
                return HandleAddAction(path, mapName, actionName, actionType, controlType, binding, interactions, processors);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding action: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Modify properties of an existing action.
        /// </summary>
        [MCPAction("modify_action", Description = "Modify properties of an existing action")]
        public static object ModifyAction(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Action map name", required: true)] string mapName = null,
            [MCPParam("action_name", "Name of the action to modify", required: true)] string actionName = null,
            [MCPParam("new_name", "New name for the action")] string newName = null,
            [MCPParam("action_type", "New action type", Enum = new[] { "value", "button", "passthrough" })] string actionType = null,
            [MCPParam("control_type", "New expected control type")] string controlType = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            if (string.IsNullOrEmpty(actionName))
                throw MCPException.InvalidParams("'action_name' is required.");
            try
            {
                return HandleModifyAction(path, mapName, actionName, newName, actionType, controlType);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error modifying action: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Remove an action from an action map.
        /// </summary>
        [MCPAction("remove_action", Description = "Remove an action from an action map", DestructiveHint = true)]
        public static object RemoveAction(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Action map name", required: true)] string mapName = null,
            [MCPParam("action_name", "Name of the action to remove", required: true)] string actionName = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            if (string.IsNullOrEmpty(actionName))
                throw MCPException.InvalidParams("'action_name' is required.");
            try
            {
                return HandleRemoveAction(path, mapName, actionName);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error removing action: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Action Methods — Binding Level

        /// <summary>
        /// List bindings for an action.
        /// </summary>
        [MCPAction("list_bindings", Description = "List bindings for an action", ReadOnlyHint = true)]
        public static object ListBindings(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Action map name", required: true)] string mapName = null,
            [MCPParam("action_name", "Action name", required: true)] string actionName = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            if (string.IsNullOrEmpty(actionName))
                throw MCPException.InvalidParams("'action_name' is required.");
            try
            {
                return HandleListBindings(path, mapName, actionName);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error listing bindings: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Add a binding to an action.
        /// </summary>
        [MCPAction("add_binding", Description = "Add a binding to an action")]
        public static object AddBinding(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Action map name", required: true)] string mapName = null,
            [MCPParam("action_name", "Action name", required: true)] string actionName = null,
            [MCPParam("binding_path", "Binding path (e.g. <Keyboard>/space)", required: true)] string bindingPath = null,
            [MCPParam("interactions", "Interactions for the binding")] string interactions = null,
            [MCPParam("processors", "Processors for the binding")] string processors = null,
            [MCPParam("groups", "Control scheme groups")] string groups = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            if (string.IsNullOrEmpty(actionName))
                throw MCPException.InvalidParams("'action_name' is required.");
            if (string.IsNullOrEmpty(bindingPath))
                throw MCPException.InvalidParams("'binding_path' is required.");
            try
            {
                return HandleAddBinding(path, mapName, actionName, bindingPath, interactions, processors, groups);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding binding: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Add a composite binding to an action.
        /// </summary>
        [MCPAction("add_composite", Description = "Add a composite binding to an action")]
        public static object AddComposite(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Action map name", required: true)] string mapName = null,
            [MCPParam("action_name", "Action name", required: true)] string actionName = null,
            [MCPParam("composite_type", "Composite type (e.g. 1DAxis, 2DVector, ButtonWithOneModifier, ButtonWithTwoModifiers)", required: true)] string compositeType = null,
            [MCPParam("parts", "Dictionary of part name to binding path (e.g. {\"up\":\"<Keyboard>/w\"})", required: true)] Dictionary<string, object> parts = null,
            [MCPParam("interactions", "Interactions for the composite")] string interactions = null,
            [MCPParam("processors", "Processors for the composite")] string processors = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            if (string.IsNullOrEmpty(actionName))
                throw MCPException.InvalidParams("'action_name' is required.");
            if (string.IsNullOrEmpty(compositeType))
                throw MCPException.InvalidParams("'composite_type' is required.");
            if (parts == null || parts.Count == 0)
                throw MCPException.InvalidParams("'parts' is required and must contain at least one entry.");
            try
            {
                return HandleAddComposite(path, mapName, actionName, compositeType, parts, interactions, processors);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding composite binding: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Modify an existing binding.
        /// </summary>
        [MCPAction("modify_binding", Description = "Modify an existing binding")]
        public static object ModifyBinding(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Action map name", required: true)] string mapName = null,
            [MCPParam("action_name", "Action name", required: true)] string actionName = null,
            [MCPParam("binding_index", "Index of the binding to modify", required: true)] int? bindingIndex = null,
            [MCPParam("binding_path", "New binding path")] string bindingPath = null,
            [MCPParam("interactions", "New interactions")] string interactions = null,
            [MCPParam("processors", "New processors")] string processors = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            if (string.IsNullOrEmpty(actionName))
                throw MCPException.InvalidParams("'action_name' is required.");
            if (bindingIndex == null)
                throw MCPException.InvalidParams("'binding_index' is required.");
            try
            {
                return HandleModifyBinding(path, mapName, actionName, bindingIndex.Value, bindingPath, interactions, processors);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error modifying binding: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Remove a binding from an action.
        /// </summary>
        [MCPAction("remove_binding", Description = "Remove a binding from an action", DestructiveHint = true)]
        public static object RemoveBinding(
            [MCPParam("path", "Asset path", required: true)] string path = null,
            [MCPParam("map_name", "Action map name", required: true)] string mapName = null,
            [MCPParam("action_name", "Action name", required: true)] string actionName = null,
            [MCPParam("binding_index", "Index of the binding to remove", required: true)] int? bindingIndex = null)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("'path' is required.");
            if (string.IsNullOrEmpty(mapName))
                throw MCPException.InvalidParams("'map_name' is required.");
            if (string.IsNullOrEmpty(actionName))
                throw MCPException.InvalidParams("'action_name' is required.");
            if (bindingIndex == null)
                throw MCPException.InvalidParams("'binding_index' is required.");
            try
            {
                return HandleRemoveBinding(path, mapName, actionName, bindingIndex.Value);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error removing binding: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Handler Methods — Asset Level

        private static object HandleList(string searchPattern)
        {
            var guids = AssetDatabase.FindAssets("t:InputActionAsset");
            var results = new List<object>();

            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null) continue;

                if (!string.IsNullOrEmpty(searchPattern) &&
                    asset.name.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                results.Add(SerializeAssetSummary(asset, assetPath, guid));

                if (results.Count >= MaxAssetResults) break;
            }

            return new
            {
                success = true,
                count = results.Count,
                assets = results
            };
        }

        private static object HandleCreate(string path, List<string> maps)
        {
            if (!path.EndsWith(".inputactions", StringComparison.OrdinalIgnoreCase))
                throw MCPException.InvalidParams("'path' must end with .inputactions");

            if (AssetDatabase.LoadAssetAtPath<InputActionAsset>(path) != null)
                throw MCPException.InvalidParams($"An asset already exists at '{path}'.");

            var asset = ScriptableObject.CreateInstance<InputActionAsset>();

            if (maps != null)
            {
                foreach (var mapName in maps)
                {
                    if (!string.IsNullOrEmpty(mapName))
                    {
                        asset.AddActionMap(mapName);
                    }
                }
            }

            // Ensure parent directory exists
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
            {
                CreateFolderRecursive(directory);
            }

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            string createdGuid = AssetDatabase.AssetPathToGUID(path);

            return new
            {
                success = true,
                message = $"Input Action Asset created at '{path}'.",
                path,
                guid = createdGuid,
                name = asset.name,
                map_count = asset.actionMaps.Count
            };
        }

        private static object HandleInspect(string path)
        {
            var asset = LoadInputActionAsset(path);
            var mapsData = new List<object>();

            foreach (var map in asset.actionMaps)
            {
                mapsData.Add(SerializeMap(map));
            }

            string guid = AssetDatabase.AssetPathToGUID(path);

            return new
            {
                success = true,
                name = asset.name,
                path,
                guid,
                map_count = asset.actionMaps.Count,
                maps = mapsData
            };
        }

        private static object HandleDelete(string path)
        {
            // Verify asset exists
            LoadInputActionAsset(path);

            bool deleted = AssetDatabase.DeleteAsset(path);
            if (!deleted)
                throw new MCPException($"Failed to delete asset at '{path}'.", MCPErrorCodes.InternalError);

            return new
            {
                success = true,
                message = $"Input Action Asset deleted at '{path}'."
            };
        }

        #endregion

        #region Handler Methods — Map Level

        private static object HandleListMaps(string path)
        {
            var asset = LoadInputActionAsset(path);
            var mapsList = new List<object>();

            foreach (var map in asset.actionMaps)
            {
                mapsList.Add(new
                {
                    name = map.name,
                    action_count = map.actions.Count,
                    binding_count = map.bindings.Count
                });
            }

            return new
            {
                success = true,
                asset_name = asset.name,
                count = mapsList.Count,
                maps = mapsList
            };
        }

        private static object HandleAddMap(string path, string mapName)
        {
            var asset = LoadInputActionAsset(path);

            if (asset.FindActionMap(mapName) != null)
                throw MCPException.InvalidParams($"Action map '{mapName}' already exists in this asset.");

            Undo.RecordObject(asset, $"Add Action Map '{mapName}'");
            asset.AddActionMap(mapName);
            SaveAsset(asset);

            return new
            {
                success = true,
                message = $"Action map '{mapName}' added.",
                asset_name = asset.name,
                map_count = asset.actionMaps.Count
            };
        }

        private static object HandleRenameMap(string path, string mapName, string newName)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);

            if (asset.FindActionMap(newName) != null)
                throw MCPException.InvalidParams($"Action map '{newName}' already exists in this asset.");

            Undo.RecordObject(asset, $"Rename Action Map '{mapName}' to '{newName}'");

            // Use SerializedObject to modify the map name
            var serializedObject = new SerializedObject(asset);
            var mapsArray = serializedObject.FindProperty("m_ActionMaps");

            for (int mapIndex = 0; mapIndex < mapsArray.arraySize; mapIndex++)
            {
                var mapElement = mapsArray.GetArrayElementAtIndex(mapIndex);
                var nameProperty = mapElement.FindPropertyRelative("m_Name");
                if (nameProperty.stringValue == mapName)
                {
                    nameProperty.stringValue = newName;
                    break;
                }
            }

            serializedObject.ApplyModifiedProperties();
            SaveAsset(asset);

            return new
            {
                success = true,
                message = $"Action map renamed from '{mapName}' to '{newName}'.",
                old_name = mapName,
                new_name = newName
            };
        }

        private static object HandleRemoveMap(string path, string mapName)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);

            Undo.RecordObject(asset, $"Remove Action Map '{mapName}'");
            asset.RemoveActionMap(map);
            SaveAsset(asset);

            return new
            {
                success = true,
                message = $"Action map '{mapName}' removed.",
                asset_name = asset.name,
                map_count = asset.actionMaps.Count
            };
        }

        #endregion

        #region Handler Methods — Action Level

        private static object HandleListActions(string path, string mapName)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);
            var actionsList = new List<object>();

            int actionCount = 0;
            foreach (var action in map.actions)
            {
                if (actionCount >= MaxActionsPerMap) break;
                actionsList.Add(SerializeAction(action));
                actionCount++;
            }

            return new
            {
                success = true,
                map_name = map.name,
                count = actionsList.Count,
                capped = map.actions.Count > MaxActionsPerMap,
                actions = actionsList
            };
        }

        private static object HandleAddAction(string path, string mapName, string actionName,
            string actionType, string controlType, string binding, string interactions, string processors)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);

            if (map.FindAction(actionName) != null)
                throw MCPException.InvalidParams($"Action '{actionName}' already exists in map '{mapName}'.");

            Undo.RecordObject(asset, $"Add Action '{actionName}'");

            InputActionType parsedType = InputActionType.Button;
            if (!string.IsNullOrEmpty(actionType))
            {
                parsedType = ParseActionType(actionType);
            }

            var newAction = map.AddAction(
                actionName,
                parsedType,
                binding,
                interactions,
                processors,
                null,
                controlType);

            SaveAsset(asset);

            return new
            {
                success = true,
                message = $"Action '{actionName}' added to map '{mapName}'.",
                action = SerializeAction(newAction)
            };
        }

        private static object HandleModifyAction(string path, string mapName, string actionName,
            string newName, string actionType, string controlType)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);
            var action = FindAction(map, actionName);

            if (!string.IsNullOrEmpty(newName) && newName != actionName && map.FindAction(newName) != null)
                throw MCPException.InvalidParams($"Action '{newName}' already exists in map '{mapName}'.");

            Undo.RecordObject(asset, $"Modify Action '{actionName}'");

            var serializedObject = new SerializedObject(asset);
            var mapsArray = serializedObject.FindProperty("m_ActionMaps");
            var changes = new List<string>();

            // Find the map in serialized data
            for (int mapIndex = 0; mapIndex < mapsArray.arraySize; mapIndex++)
            {
                var mapElement = mapsArray.GetArrayElementAtIndex(mapIndex);
                var mapNameProperty = mapElement.FindPropertyRelative("m_Name");
                if (mapNameProperty.stringValue != mapName) continue;

                var actionsArray = mapElement.FindPropertyRelative("m_Actions");
                for (int actionIndex = 0; actionIndex < actionsArray.arraySize; actionIndex++)
                {
                    var actionElement = actionsArray.GetArrayElementAtIndex(actionIndex);
                    var actionNameProperty = actionElement.FindPropertyRelative("m_Name");
                    if (actionNameProperty.stringValue != actionName) continue;

                    if (!string.IsNullOrEmpty(newName))
                    {
                        actionNameProperty.stringValue = newName;
                        changes.Add($"name={newName}");
                    }

                    if (!string.IsNullOrEmpty(actionType))
                    {
                        var parsedType = ParseActionType(actionType);
                        var typeProperty = actionElement.FindPropertyRelative("m_Type");
                        typeProperty.intValue = (int)parsedType;
                        changes.Add($"type={parsedType}");
                    }

                    if (!string.IsNullOrEmpty(controlType))
                    {
                        var controlTypeProperty = actionElement.FindPropertyRelative("m_ExpectedControlType");
                        controlTypeProperty.stringValue = controlType;
                        changes.Add($"control_type={controlType}");
                    }

                    break;
                }
                break;
            }

            serializedObject.ApplyModifiedProperties();
            SaveAsset(asset);

            return new
            {
                success = true,
                message = changes.Count > 0
                    ? $"Action '{actionName}' modified: {string.Join(", ", changes)}."
                    : $"No changes applied to action '{actionName}'.",
                changes
            };
        }

        private static object HandleRemoveAction(string path, string mapName, string actionName)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);
            var action = FindAction(map, actionName);

            Undo.RecordObject(asset, $"Remove Action '{actionName}'");
            asset.RemoveAction($"{mapName}/{actionName}");
            SaveAsset(asset);

            return new
            {
                success = true,
                message = $"Action '{actionName}' removed from map '{mapName}'.",
                map_name = mapName,
                action_count = map.actions.Count
            };
        }

        #endregion

        #region Handler Methods — Binding Level

        private static object HandleListBindings(string path, string mapName, string actionName)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);
            var action = FindAction(map, actionName);
            var bindingsList = new List<object>();

            int bindingCount = 0;
            foreach (var binding in action.bindings)
            {
                if (bindingCount >= MaxBindingsPerAction) break;
                bindingsList.Add(SerializeBinding(binding, bindingCount));
                bindingCount++;
            }

            return new
            {
                success = true,
                action_name = action.name,
                count = bindingsList.Count,
                capped = action.bindings.Count > MaxBindingsPerAction,
                bindings = bindingsList
            };
        }

        private static object HandleAddBinding(string path, string mapName, string actionName,
            string bindingPath, string interactions, string processors, string groups)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);
            var action = FindAction(map, actionName);

            Undo.RecordObject(asset, $"Add Binding to '{actionName}'");

            var bindingSyntax = action.AddBinding(bindingPath);
            if (!string.IsNullOrEmpty(interactions))
                bindingSyntax.WithInteractions(interactions);
            if (!string.IsNullOrEmpty(processors))
                bindingSyntax.WithProcessors(processors);
            if (!string.IsNullOrEmpty(groups))
                bindingSyntax.WithGroups(groups);

            SaveAsset(asset);

            return new
            {
                success = true,
                message = $"Binding '{bindingPath}' added to action '{actionName}'.",
                action_name = actionName,
                binding_count = action.bindings.Count
            };
        }

        private static object HandleAddComposite(string path, string mapName, string actionName,
            string compositeType, Dictionary<string, object> parts, string interactions, string processors)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);
            var action = FindAction(map, actionName);

            Undo.RecordObject(asset, $"Add Composite Binding to '{actionName}'");

            int compositeBindingIndex = action.bindings.Count;
            var compositeSyntax = action.AddCompositeBinding(compositeType);

            if (!string.IsNullOrEmpty(interactions))
                action.ChangeBinding(compositeBindingIndex).WithInteractions(interactions);
            if (!string.IsNullOrEmpty(processors))
                action.ChangeBinding(compositeBindingIndex).WithProcessors(processors);

            foreach (var part in parts)
            {
                string partName = part.Key;
                string partPath = part.Value?.ToString();
                if (!string.IsNullOrEmpty(partPath))
                {
                    compositeSyntax.With(partName, partPath);
                }
            }

            SaveAsset(asset);

            var partNames = parts.Keys.ToList();

            return new
            {
                success = true,
                message = $"Composite '{compositeType}' added to action '{actionName}' with parts: {string.Join(", ", partNames)}.",
                action_name = actionName,
                composite_type = compositeType,
                parts = partNames,
                binding_count = action.bindings.Count
            };
        }

        private static object HandleModifyBinding(string path, string mapName, string actionName,
            int bindingIndex, string bindingPath, string interactions, string processors)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);
            var action = FindAction(map, actionName);

            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                throw MCPException.InvalidParams($"'binding_index' {bindingIndex} is out of range (0..{action.bindings.Count - 1}).");

            Undo.RecordObject(asset, $"Modify Binding on '{actionName}'");

            var changes = new List<string>();
            var existingBinding = action.bindings[bindingIndex];

            string newPath = !string.IsNullOrEmpty(bindingPath) ? bindingPath : existingBinding.path;
            string newInteractions = interactions ?? existingBinding.interactions;
            string newProcessors = processors ?? existingBinding.processors;

            if (!string.IsNullOrEmpty(bindingPath))
                changes.Add($"path={bindingPath}");
            if (interactions != null)
                changes.Add($"interactions={interactions}");
            if (processors != null)
                changes.Add($"processors={processors}");

            action.ChangeBinding(bindingIndex).WithPath(newPath)
                .WithInteractions(newInteractions)
                .WithProcessors(newProcessors);

            SaveAsset(asset);

            return new
            {
                success = true,
                message = changes.Count > 0
                    ? $"Binding [{bindingIndex}] on '{actionName}' modified: {string.Join(", ", changes)}."
                    : $"No changes applied to binding [{bindingIndex}] on '{actionName}'.",
                changes
            };
        }

        private static object HandleRemoveBinding(string path, string mapName, string actionName, int bindingIndex)
        {
            var asset = LoadInputActionAsset(path);
            var map = FindMap(asset, mapName);
            var action = FindAction(map, actionName);

            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                throw MCPException.InvalidParams($"'binding_index' {bindingIndex} is out of range (0..{action.bindings.Count - 1}).");

            Undo.RecordObject(asset, $"Remove Binding from '{actionName}'");

            var removedBinding = action.bindings[bindingIndex];
            string removedPath = removedBinding.path;

            action.ChangeBinding(bindingIndex).Erase();
            SaveAsset(asset);

            return new
            {
                success = true,
                message = $"Binding [{bindingIndex}] ('{removedPath}') removed from action '{actionName}'.",
                action_name = actionName,
                binding_count = action.bindings.Count
            };
        }

        #endregion

        #region Private Helpers

        private static InputActionAsset LoadInputActionAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw MCPException.InvalidParams("Asset path is required.");

            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (asset == null)
                throw MCPException.InvalidParams($"No InputActionAsset found at '{path}'.");

            return asset;
        }

        private static InputActionMap FindMap(InputActionAsset asset, string mapName)
        {
            var map = asset.FindActionMap(mapName);
            if (map == null)
                throw MCPException.InvalidParams($"Action map '{mapName}' not found in asset '{asset.name}'.");
            return map;
        }

        private static InputAction FindAction(InputActionMap map, string actionName)
        {
            var action = map.FindAction(actionName);
            if (action == null)
                throw MCPException.InvalidParams($"Action '{actionName}' not found in map '{map.name}'.");
            return action;
        }

        private static InputActionType ParseActionType(string typeString)
        {
            switch (typeString?.ToLowerInvariant())
            {
                case "value": return InputActionType.Value;
                case "button": return InputActionType.Button;
                case "passthrough": return InputActionType.PassThrough;
                default:
                    throw MCPException.InvalidParams($"Invalid action_type: '{typeString}'. Valid values: value, button, passthrough");
            }
        }

        private static void SaveAsset(InputActionAsset asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static object SerializeAssetSummary(InputActionAsset asset, string assetPath, string guid)
        {
            return new
            {
                path = assetPath,
                guid,
                name = asset.name,
                map_count = asset.actionMaps.Count
            };
        }

        private static object SerializeMap(InputActionMap map)
        {
            var actionsList = new List<object>();
            int actionCount = 0;

            foreach (var action in map.actions)
            {
                if (actionCount >= MaxActionsPerMap) break;
                actionsList.Add(SerializeAction(action));
                actionCount++;
            }

            return new
            {
                name = map.name,
                action_count = map.actions.Count,
                capped = map.actions.Count > MaxActionsPerMap,
                actions = actionsList
            };
        }

        private static object SerializeAction(InputAction action)
        {
            var bindingsList = new List<object>();
            int bindingCount = 0;

            foreach (var binding in action.bindings)
            {
                if (bindingCount >= MaxBindingsPerAction) break;
                bindingsList.Add(SerializeBinding(binding, bindingCount));
                bindingCount++;
            }

            return new
            {
                name = action.name,
                type = action.type.ToString(),
                control_type = action.expectedControlType,
                binding_count = action.bindings.Count,
                capped = action.bindings.Count > MaxBindingsPerAction,
                bindings = bindingsList
            };
        }

        private static object SerializeBinding(InputBinding binding, int index)
        {
            var result = new Dictionary<string, object>
            {
                { "index", index },
                { "path", binding.path },
                { "is_composite", binding.isComposite },
                { "is_part_of_composite", binding.isPartOfComposite }
            };

            if (!string.IsNullOrEmpty(binding.name))
                result["name"] = binding.name;
            if (!string.IsNullOrEmpty(binding.interactions))
                result["interactions"] = binding.interactions;
            if (!string.IsNullOrEmpty(binding.processors))
                result["processors"] = binding.processors;
            if (!string.IsNullOrEmpty(binding.groups))
                result["groups"] = binding.groups;

            return result;
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parentFolder = System.IO.Path.GetDirectoryName(folderPath);
            if (!string.IsNullOrEmpty(parentFolder) && !AssetDatabase.IsValidFolder(parentFolder))
            {
                CreateFolderRecursive(parentFolder);
            }

            string folderName = System.IO.Path.GetFileName(folderPath);
            AssetDatabase.CreateFolder(parentFolder, folderName);
        }

        #endregion
    }
}
#endif
