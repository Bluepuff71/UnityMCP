using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tools for managing the Unity Editor selection state.
    /// </summary>
    public static class SelectionTools
    {
        #region Selection Get

        /// <summary>
        /// Gets the currently selected objects in the Unity Editor.
        /// </summary>
        /// <returns>Information about the current selection including count and object details.</returns>
        [MCPTool("selection_get", "Get currently selected objects in the Unity Editor", Category = "Editor", ReadOnlyHint = true)]
        public static object Get()
        {
            try
            {
                var selectedObjects = Selection.objects;
                var objectDataList = new List<object>();

                foreach (var selectedObject in selectedObjects)
                {
                    if (selectedObject == null)
                    {
                        continue;
                    }

                    objectDataList.Add(BuildSelectedObjectData(selectedObject));
                }

                // Determine the active object (primary selection)
                object activeObjectData = null;
                if (Selection.activeObject != null)
                {
                    activeObjectData = BuildSelectedObjectData(Selection.activeObject);
                }

                return new
                {
                    success = true,
                    count = Selection.count,
                    activeObject = activeObjectData,
                    objects = objectDataList
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SelectionTools] Error getting selection: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error getting selection: {exception.Message}"
                };
            }
        }

        #endregion

        #region Selection Set

        /// <summary>
        /// Sets the selection to specified objects by instance IDs or asset paths.
        /// </summary>
        /// <param name="instanceIds">Array of instance IDs to select.</param>
        /// <param name="paths">Array of asset paths to select.</param>
        /// <returns>Result indicating success or failure with selection details.</returns>
        [MCPTool("selection_set", "Set selection by instance IDs or asset paths", Category = "Editor", DestructiveHint = true)]
        public static object Set(
            [MCPParam("instance_ids", "Array of instance IDs to select")] List<object> instanceIds = null,
            [MCPParam("paths", "Array of asset paths to select")] List<object> paths = null)
        {
            try
            {
                var objectsToSelect = new List<UnityEngine.Object>();
                var failedIds = new List<int>();
                var failedPaths = new List<string>();

                // Process instance IDs
                if (instanceIds != null && instanceIds.Count > 0)
                {
                    foreach (var idValue in instanceIds)
                    {
                        if (!TryParseInstanceId(idValue, out int instanceId))
                        {
                            Debug.LogWarning($"[SelectionTools] Invalid instance ID format: {idValue}");
                            continue;
                        }

                        var resolvedObject = EditorUtility.InstanceIDToObject(instanceId);
                        if (resolvedObject != null)
                        {
                            objectsToSelect.Add(resolvedObject);
                        }
                        else
                        {
                            failedIds.Add(instanceId);
                        }
                    }
                }

                // Process asset paths
                if (paths != null && paths.Count > 0)
                {
                    foreach (var pathValue in paths)
                    {
                        string assetPath = pathValue?.ToString();
                        if (string.IsNullOrEmpty(assetPath))
                        {
                            continue;
                        }

                        var loadedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                        if (loadedAsset != null)
                        {
                            objectsToSelect.Add(loadedAsset);
                        }
                        else
                        {
                            failedPaths.Add(assetPath);
                        }
                    }
                }

                // Validate we have something to select
                if (objectsToSelect.Count == 0 && (instanceIds == null || instanceIds.Count == 0) && (paths == null || paths.Count == 0))
                {
                    // Clear selection if no IDs or paths provided
                    Selection.objects = Array.Empty<UnityEngine.Object>();
                    return new
                    {
                        success = true,
                        message = "Selection cleared.",
                        count = 0,
                        objects = Array.Empty<object>()
                    };
                }

                if (objectsToSelect.Count == 0)
                {
                    return new
                    {
                        success = false,
                        error = "No valid objects found for the provided instance IDs or paths.",
                        failedInstanceIds = failedIds.Count > 0 ? failedIds : null,
                        failedPaths = failedPaths.Count > 0 ? failedPaths : null
                    };
                }

                // Set the selection
                Selection.objects = objectsToSelect.ToArray();

                // Build response with selected object data
                var selectedObjectsData = objectsToSelect.Select(BuildSelectedObjectData).ToList();

                var response = new Dictionary<string, object>
                {
                    { "success", true },
                    { "message", $"Selected {objectsToSelect.Count} object(s)." },
                    { "count", objectsToSelect.Count },
                    { "objects", selectedObjectsData }
                };

                if (failedIds.Count > 0)
                {
                    response["failedInstanceIds"] = failedIds;
                }

                if (failedPaths.Count > 0)
                {
                    response["failedPaths"] = failedPaths;
                }

                return response;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SelectionTools] Error setting selection: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error setting selection: {exception.Message}"
                };
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Builds a data object representing a selected object's information.
        /// </summary>
        /// <param name="selectedObject">The Unity object to describe.</param>
        /// <returns>An anonymous object containing the object's details.</returns>
        private static object BuildSelectedObjectData(UnityEngine.Object selectedObject)
        {
            if (selectedObject == null)
            {
                return null;
            }

            var baseData = new Dictionary<string, object>
            {
                { "name", selectedObject.name },
                { "instanceId", selectedObject.GetInstanceID() },
                { "type", selectedObject.GetType().Name }
            };

            // Add additional info for GameObjects
            if (selectedObject is GameObject gameObject)
            {
                baseData["isGameObject"] = true;
                baseData["activeSelf"] = gameObject.activeSelf;
                baseData["activeInHierarchy"] = gameObject.activeInHierarchy;
                baseData["tag"] = gameObject.tag;
                baseData["layer"] = gameObject.layer;
                baseData["layerName"] = LayerMask.LayerToName(gameObject.layer);
                baseData["path"] = GetGameObjectPath(gameObject);

                // Add transform info
                baseData["transform"] = new
                {
                    localPosition = new[] { gameObject.transform.localPosition.x, gameObject.transform.localPosition.y, gameObject.transform.localPosition.z },
                    localRotation = new[] { gameObject.transform.localEulerAngles.x, gameObject.transform.localEulerAngles.y, gameObject.transform.localEulerAngles.z },
                    localScale = new[] { gameObject.transform.localScale.x, gameObject.transform.localScale.y, gameObject.transform.localScale.z }
                };

                // Get component types
                var componentTypes = new List<string>();
                var components = gameObject.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component != null)
                    {
                        componentTypes.Add(component.GetType().Name);
                    }
                }
                baseData["componentTypes"] = componentTypes;
            }
            else
            {
                baseData["isGameObject"] = false;

                // Add asset path if it's a project asset
                string assetPath = AssetDatabase.GetAssetPath(selectedObject);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    baseData["assetPath"] = assetPath;
                    baseData["isAsset"] = true;
                }
                else
                {
                    baseData["isAsset"] = false;
                }
            }

            return baseData;
        }

        /// <summary>
        /// Gets the full hierarchy path of a GameObject.
        /// </summary>
        /// <param name="gameObject">The GameObject to get the path for.</param>
        /// <returns>The full hierarchy path as a string.</returns>
        private static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            try
            {
                var pathSegments = new Stack<string>();
                Transform currentTransform = gameObject.transform;

                while (currentTransform != null)
                {
                    pathSegments.Push(currentTransform.name);
                    currentTransform = currentTransform.parent;
                }

                return string.Join("/", pathSegments);
            }
            catch
            {
                return gameObject.name;
            }
        }

        /// <summary>
        /// Attempts to parse an instance ID from various input formats.
        /// </summary>
        /// <param name="value">The value to parse.</param>
        /// <param name="instanceId">The parsed instance ID if successful.</param>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        private static bool TryParseInstanceId(object value, out int instanceId)
        {
            instanceId = 0;

            if (value == null)
            {
                return false;
            }

            // Handle direct int
            if (value is int intValue)
            {
                instanceId = intValue;
                return true;
            }

            // Handle long (JSON often deserializes integers as long)
            if (value is long longValue)
            {
                instanceId = (int)longValue;
                return true;
            }

            // Handle double (JSON sometimes deserializes as double)
            if (value is double doubleValue)
            {
                instanceId = (int)doubleValue;
                return true;
            }

            // Handle string representation
            if (value is string stringValue && int.TryParse(stringValue, out int parsedValue))
            {
                instanceId = parsedValue;
                return true;
            }

            return false;
        }

        #endregion
    }
}
