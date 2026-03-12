using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
#if UNITY_MCP_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityMCP.Editor;
using UnityMCP.Editor.Core;


#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Handles Canvas UI operations including create, configure, list, and delete.
    /// </summary>
    [MCPTool("manage_canvas", "Manages Canvas objects: create, configure, list, or delete Canvas UIs with CanvasScaler and EventSystem.", Category = "UI")]
    public static class CanvasTools
    {
        #region Action Methods

        /// <summary>
        /// Creates a new Canvas with CanvasScaler, GraphicRaycaster, and EventSystem.
        /// </summary>
        [MCPAction("create", Description = "Create a new Canvas with CanvasScaler, GraphicRaycaster, and EventSystem")]
        public static object Create(
            [MCPParam("name", "Name for the Canvas")] string name = null,
            [MCPParam("render_mode", "Render mode", Enum = new[] { "overlay", "camera", "world" })] string renderMode = null,
            [MCPParam("scaler_mode", "Canvas scaler mode", Enum = new[] { "constant_pixel_size", "scale_with_screen_size", "constant_physical_size" })] string scalerMode = null,
            [MCPParam("reference_resolution", "Reference resolution as [width, height] array for scale_with_screen_size mode")] object referenceResolution = null,
            [MCPParam("match_width_or_height", "Match width (0) or height (1) for scale_with_screen_size mode", Minimum = 0, Maximum = 1)] float? matchWidthOrHeight = null)
        {
            try
            {
                return HandleCreate(name, renderMode, scalerMode, referenceResolution, matchWidthOrHeight);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error creating Canvas: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Modifies settings on an existing Canvas.
        /// </summary>
        [MCPAction("configure", Description = "Modify existing Canvas settings")]
        public static object Configure(
            [MCPParam("target", "Instance ID or name/path of the Canvas GameObject", required: true)] string target = null,
            [MCPParam("render_mode", "Render mode", Enum = new[] { "overlay", "camera", "world" })] string renderMode = null,
            [MCPParam("sort_order", "Sort order for the Canvas")] int? sortOrder = null,
            [MCPParam("scaler_mode", "Canvas scaler mode", Enum = new[] { "constant_pixel_size", "scale_with_screen_size", "constant_physical_size" })] string scalerMode = null,
            [MCPParam("reference_resolution", "Reference resolution as [width, height] array for scale_with_screen_size mode")] object referenceResolution = null,
            [MCPParam("render_camera", "Instance ID or name/path of the render camera (for camera render mode)")] string renderCamera = null,
            [MCPParam("match_width_or_height", "Match width (0) or height (1) for scale_with_screen_size mode", Minimum = 0, Maximum = 1)] float? matchWidthOrHeight = null)
        {
            try
            {
                return HandleConfigure(target, renderMode, sortOrder, scalerMode, referenceResolution, renderCamera, matchWidthOrHeight);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error configuring Canvas: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Lists all Canvases in the scene with configuration summaries.
        /// </summary>
        [MCPAction("list", Description = "List all Canvases in scene with config summary", ReadOnlyHint = true)]
        public static object List()
        {
            try
            {
                return HandleList();
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error listing Canvases: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Removes a Canvas and optionally its EventSystem.
        /// </summary>
        [MCPAction("delete", Description = "Remove Canvas and optionally EventSystem", DestructiveHint = true)]
        public static object Delete(
            [MCPParam("target", "Instance ID or name/path of the Canvas GameObject", required: true)] string target = null,
            [MCPParam("include_event_system", "Also delete the EventSystem if no other Canvases remain")] bool? includeEventSystem = null)
        {
            try
            {
                return HandleDelete(target, includeEventSystem ?? false);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error deleting Canvas: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Handler Methods

        private static object HandleCreate(string name, string renderMode, string scalerMode, object referenceResolution, float? matchWidthOrHeight)
        {
            if (string.IsNullOrEmpty(name))
            {
                name = "Canvas";
            }

            // Parse render mode
            RenderMode parsedRenderMode = RenderMode.ScreenSpaceOverlay;
            if (!string.IsNullOrEmpty(renderMode))
            {
                parsedRenderMode = ParseRenderMode(renderMode);
            }

            // Create Canvas GameObject
            var canvasGO = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(canvasGO, $"Create Canvas '{name}'");

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = parsedRenderMode;

            // Add CanvasScaler and configure
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            if (!string.IsNullOrEmpty(scalerMode))
            {
                scaler.uiScaleMode = ParseScalerMode(scalerMode);
            }

            if (referenceResolution != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                Vector2? parsedRes = ParseVector2(referenceResolution);
                if (parsedRes.HasValue)
                {
                    scaler.referenceResolution = parsedRes.Value;
                }
            }

            if (matchWidthOrHeight.HasValue && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                scaler.matchWidthOrHeight = matchWidthOrHeight.Value;
            }

            // Add GraphicRaycaster
            canvasGO.AddComponent<GraphicRaycaster>();

            // Create EventSystem if one doesn't already exist
            bool createdEventSystem = false;
            var existingEventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (existingEventSystem == null)
            {
                var eventSystemGO = new GameObject("EventSystem");
                eventSystemGO.AddComponent<EventSystem>();
#if UNITY_MCP_INPUT_SYSTEM
                eventSystemGO.AddComponent<InputSystemUIInputModule>();
#else
                eventSystemGO.AddComponent<StandaloneInputModule>();
#endif
                Undo.RegisterCreatedObjectUndo(eventSystemGO, "Create EventSystem");
                createdEventSystem = true;
            }

            EditorUtility.SetDirty(canvasGO);
            Selection.activeGameObject = canvasGO;

            return new
            {
                success = true,
                message = createdEventSystem
                    ? $"Canvas '{name}' created with EventSystem."
                    : $"Canvas '{name}' created (EventSystem already exists).",
                instance_id = canvasGO.GetInstanceID(),
                name = canvasGO.name,
                render_mode = canvas.renderMode.ToString(),
                scaler_mode = scaler.uiScaleMode.ToString(),
                created_event_system = createdEventSystem
            };
        }

        private static object HandleConfigure(string target, string renderMode, int? sortOrder, string scalerMode, object referenceResolution, string renderCamera, float? matchWidthOrHeight)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for configure action.");
            }

            GameObject targetGO = FindGameObject(target);
            if (targetGO == null)
            {
                return new
                {
                    success = false,
                    error = $"Target GameObject '{target}' not found."
                };
            }

            var canvas = targetGO.GetComponent<Canvas>();
            if (canvas == null)
            {
                return new
                {
                    success = false,
                    error = $"GameObject '{target}' does not have a Canvas component."
                };
            }

            Undo.RecordObject(canvas, "Configure Canvas");

            var changes = new List<string>();

            // Configure render mode
            if (!string.IsNullOrEmpty(renderMode))
            {
                canvas.renderMode = ParseRenderMode(renderMode);
                changes.Add($"render_mode={canvas.renderMode}");
            }

            // Configure sort order
            if (sortOrder.HasValue)
            {
                canvas.sortingOrder = sortOrder.Value;
                changes.Add($"sort_order={sortOrder.Value}");
            }

            // Configure render camera
            if (!string.IsNullOrEmpty(renderCamera))
            {
                GameObject cameraGO = FindGameObject(renderCamera);
                if (cameraGO == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Render camera '{renderCamera}' not found."
                    };
                }

                var cam = cameraGO.GetComponent<Camera>();
                if (cam == null)
                {
                    return new
                    {
                        success = false,
                        error = $"GameObject '{renderCamera}' does not have a Camera component."
                    };
                }

                canvas.worldCamera = cam;
                changes.Add($"render_camera={cameraGO.name}");
            }

            // Configure CanvasScaler
            var scaler = targetGO.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                Undo.RecordObject(scaler, "Configure CanvasScaler");

                if (!string.IsNullOrEmpty(scalerMode))
                {
                    scaler.uiScaleMode = ParseScalerMode(scalerMode);
                    changes.Add($"scaler_mode={scaler.uiScaleMode}");
                }

                if (referenceResolution != null)
                {
                    Vector2? parsedRes = ParseVector2(referenceResolution);
                    if (parsedRes.HasValue)
                    {
                        scaler.referenceResolution = parsedRes.Value;
                        changes.Add($"reference_resolution={parsedRes.Value}");
                    }
                }

                if (matchWidthOrHeight.HasValue && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                {
                    scaler.matchWidthOrHeight = matchWidthOrHeight.Value;
                    changes.Add($"match_width_or_height={matchWidthOrHeight.Value}");
                }
            }

            EditorUtility.SetDirty(targetGO);

            return new
            {
                success = true,
                message = changes.Count > 0
                    ? $"Canvas '{targetGO.name}' configured: {string.Join(", ", changes)}."
                    : $"No changes applied to Canvas '{targetGO.name}'.",
                instance_id = targetGO.GetInstanceID(),
                name = targetGO.name,
                changes
            };
        }

        private static object HandleList()
        {
            var allCanvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
            var canvasList = new List<object>();

            foreach (var canvas in allCanvases)
            {
                if (canvas == null) continue;

                var scaler = canvas.GetComponent<CanvasScaler>();
                var go = canvas.gameObject;

                var info = new Dictionary<string, object>
                {
                    { "instance_id", go.GetInstanceID() },
                    { "name", go.name },
                    { "active", go.activeInHierarchy },
                    { "render_mode", canvas.renderMode.ToString() },
                    { "sort_order", canvas.sortingOrder }
                };

                if (canvas.worldCamera != null)
                {
                    info["render_camera"] = canvas.worldCamera.name;
                }

                if (scaler != null)
                {
                    info["scaler_mode"] = scaler.uiScaleMode.ToString();
                    if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                    {
                        info["reference_resolution"] = new { x = scaler.referenceResolution.x, y = scaler.referenceResolution.y };
                        info["match_width_or_height"] = scaler.matchWidthOrHeight;
                    }
                }

                canvasList.Add(info);
            }

            var hasEventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>() != null;

            return new
            {
                success = true,
                count = canvasList.Count,
                canvases = canvasList,
                has_event_system = hasEventSystem
            };
        }

        private static object HandleDelete(string target, bool includeEventSystem)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for delete action.");
            }

            GameObject targetGO = FindGameObject(target);
            if (targetGO == null)
            {
                return new
                {
                    success = false,
                    error = $"Target Canvas '{target}' not found."
                };
            }

            var canvas = targetGO.GetComponent<Canvas>();
            if (canvas == null)
            {
                return new
                {
                    success = false,
                    error = $"GameObject '{target}' does not have a Canvas component."
                };
            }

            string canvasName = targetGO.name;
            int canvasInstanceId = targetGO.GetInstanceID();
            Undo.DestroyObjectImmediate(targetGO);

            bool deletedEventSystem = false;
            if (includeEventSystem)
            {
                // Only delete EventSystem if no other Canvases remain
                var remainingCanvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
                if (remainingCanvases.Length == 0)
                {
                    var eventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
                    if (eventSystem != null)
                    {
                        Undo.DestroyObjectImmediate(eventSystem.gameObject);
                        deletedEventSystem = true;
                    }
                }
            }

            return new
            {
                success = true,
                message = deletedEventSystem
                    ? $"Canvas '{canvasName}' and EventSystem deleted."
                    : $"Canvas '{canvasName}' deleted.",
                deleted = new { name = canvasName, instance_id = canvasInstanceId },
                deleted_event_system = deletedEventSystem
            };
        }

        #endregion

        #region Helper Methods

        private static RenderMode ParseRenderMode(string renderMode)
        {
            switch (renderMode?.ToLowerInvariant())
            {
                case "overlay": return RenderMode.ScreenSpaceOverlay;
                case "camera": return RenderMode.ScreenSpaceCamera;
                case "world": return RenderMode.WorldSpace;
                default:
                    throw MCPException.InvalidParams($"Invalid render_mode: '{renderMode}'. Valid values: overlay, camera, world");
            }
        }

        private static CanvasScaler.ScaleMode ParseScalerMode(string scalerMode)
        {
            switch (scalerMode?.ToLowerInvariant())
            {
                case "constant_pixel_size": return CanvasScaler.ScaleMode.ConstantPixelSize;
                case "scale_with_screen_size": return CanvasScaler.ScaleMode.ScaleWithScreenSize;
                case "constant_physical_size": return CanvasScaler.ScaleMode.ConstantPhysicalSize;
                default:
                    throw MCPException.InvalidParams($"Invalid scaler_mode: '{scalerMode}'. Valid values: constant_pixel_size, scale_with_screen_size, constant_physical_size");
            }
        }

        private static Vector2? ParseVector2(object value)
        {
            if (value == null) return null;

            if (value is IList<object> list && list.Count >= 2)
            {
                return new Vector2(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]));
            }

            if (value is IDictionary<string, object> dict)
            {
                float x = dict.ContainsKey("x") ? Convert.ToSingle(dict["x"]) :
                           dict.ContainsKey("width") ? Convert.ToSingle(dict["width"]) : 0f;
                float y = dict.ContainsKey("y") ? Convert.ToSingle(dict["y"]) :
                           dict.ContainsKey("height") ? Convert.ToSingle(dict["height"]) : 0f;
                return new Vector2(x, y);
            }

            return null;
        }

        private static GameObject FindGameObject(string target, bool searchInactive = true)
        {
            if (string.IsNullOrEmpty(target))
            {
                return null;
            }

            Scene activeScene = GetActiveScene();

            // Try instance ID first
            if (int.TryParse(target, out int instanceId))
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId);
                if (obj is GameObject gameObject)
                {
                    return gameObject;
                }
                if (obj is Component component)
                {
                    return component.gameObject;
                }
            }

            // Try path-based lookup
            if (target.Contains("/"))
            {
                var roots = activeScene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    if (root == null)
                    {
                        continue;
                    }

                    string rootPath = root.name;
                    if (target.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return root;
                    }

                    if (target.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        var found = root.transform.Find(target.Substring(rootPath.Length + 1));
                        if (found != null)
                        {
                            return found.gameObject;
                        }
                    }
                }
            }

            // Try name-based lookup
            var allObjects = GetAllSceneObjects(searchInactive);
            foreach (var gameObject in allObjects)
            {
                if (gameObject != null && gameObject.name.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    return gameObject;
                }
            }

            return null;
        }

        private static Scene GetActiveScene()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                return prefabStage.scene;
            }
            return EditorSceneManager.GetActiveScene();
        }

        private static List<GameObject> GetAllSceneObjects(bool includeInactive)
        {
            var results = new List<GameObject>();
            var roots = GetActiveScene().GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root == null) continue;
                results.Add(root);
                foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactive))
                {
                    if (child.gameObject != root)
                    {
                        results.Add(child.gameObject);
                    }
                }
            }
            return results;
        }

        #endregion
    }
}
