using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Provides GameObject search functionality with multiple search methods and pagination.
    /// </summary>
    public static class FindGameObjects
    {
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 500;

        #region Main Tool Entry Point

        /// <summary>
        /// Finds GameObjects in the current scene using various search methods.
        /// Returns paginated instance IDs for lightweight results.
        /// </summary>
        [MCPTool("gameobject_find", "Finds GameObjects by name, tag, layer, component, path, or instance ID", Category = "GameObject")]
        public static object Find(
            [MCPParam("search_method", "Search method: by_name, by_tag, by_layer, by_component, by_path, by_id (default: by_name)")] string searchMethod = "by_name",
            [MCPParam("search_term", "The term to search for (name, tag, layer name, component type, path, or instance ID)", required: true)] string searchTerm = null,
            [MCPParam("include_inactive", "Whether to include inactive GameObjects (default: false)")] bool includeInactive = false,
            [MCPParam("page_size", "Number of results per page (default: 50, max: 500)")] int pageSize = DefaultPageSize,
            [MCPParam("cursor", "Starting index for pagination (default: 0)")] int cursor = 0)
        {
            if (string.IsNullOrEmpty(searchTerm))
            {
                throw MCPException.InvalidParams("'search_term' parameter is required.");
            }

            string normalizedMethod = (searchMethod ?? "by_name").ToLowerInvariant().Trim();

            // Normalize common variations
            if (normalizedMethod == "name") normalizedMethod = "by_name";
            else if (normalizedMethod == "tag") normalizedMethod = "by_tag";
            else if (normalizedMethod == "layer") normalizedMethod = "by_layer";
            else if (normalizedMethod == "component") normalizedMethod = "by_component";
            else if (normalizedMethod == "path") normalizedMethod = "by_path";
            else if (normalizedMethod == "id" || normalizedMethod == "instance_id" || normalizedMethod == "instanceid") normalizedMethod = "by_id";

            // Clamp pagination values
            int resolvedPageSize = Mathf.Clamp(pageSize, 1, MaxPageSize);
            int resolvedCursor = Mathf.Max(0, cursor);

            try
            {
                List<GameObject> results = normalizedMethod switch
                {
                    "by_id" => SearchById(searchTerm, includeInactive),
                    "by_name" => SearchByName(searchTerm, includeInactive),
                    "by_path" => SearchByPath(searchTerm, includeInactive),
                    "by_tag" => SearchByTag(searchTerm, includeInactive),
                    "by_layer" => SearchByLayer(searchTerm, includeInactive),
                    "by_component" => SearchByComponent(searchTerm, includeInactive),
                    _ => throw MCPException.InvalidParams($"Unknown search method: '{searchMethod}'. Valid methods: by_name, by_tag, by_layer, by_component, by_path, by_id")
                };

                return BuildPaginatedResponse(results, resolvedPageSize, resolvedCursor, normalizedMethod, searchTerm);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error searching GameObjects: {exception.Message}"
                };
            }
        }

        #endregion

        #region Search Methods

        /// <summary>
        /// Searches for a GameObject by instance ID.
        /// </summary>
        private static List<GameObject> SearchById(string searchTerm, bool includeInactive)
        {
            var results = new List<GameObject>();

            if (!int.TryParse(searchTerm, out int instanceId))
            {
                throw MCPException.InvalidParams($"Invalid instance ID: '{searchTerm}'. Must be an integer.");
            }

            var obj = EditorUtility.InstanceIDToObject(instanceId);

            if (obj is GameObject gameObject)
            {
                if (includeInactive || gameObject.activeInHierarchy)
                {
                    results.Add(gameObject);
                }
            }
            else if (obj is Component component)
            {
                if (includeInactive || component.gameObject.activeInHierarchy)
                {
                    results.Add(component.gameObject);
                }
            }

            return results;
        }

        /// <summary>
        /// Searches for GameObjects by name (case-insensitive).
        /// </summary>
        private static List<GameObject> SearchByName(string searchTerm, bool includeInactive)
        {
            var results = new List<GameObject>();
            var allObjects = GetAllSceneObjects(includeInactive);

            foreach (var gameObject in allObjects)
            {
                if (gameObject != null && gameObject.name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(gameObject);
                }
            }

            return results;
        }

        /// <summary>
        /// Searches for a GameObject by hierarchy path.
        /// </summary>
        private static List<GameObject> SearchByPath(string searchTerm, bool includeInactive)
        {
            var results = new List<GameObject>();
            Scene activeScene = GetActiveScene();
            var roots = activeScene.GetRootGameObjects();

            // Normalize the search path
            string normalizedSearchPath = searchTerm.Replace('\\', '/').Trim('/');

            foreach (var root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                if (!includeInactive && !root.activeInHierarchy)
                {
                    continue;
                }

                string rootPath = root.name;

                // Check if search path matches root exactly
                if (normalizedSearchPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(root);
                    continue;
                }

                // Check if search path starts with root name
                if (normalizedSearchPath.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = normalizedSearchPath.Substring(rootPath.Length + 1);
                    Transform found = root.transform.Find(relativePath);

                    if (found != null)
                    {
                        if (includeInactive || found.gameObject.activeInHierarchy)
                        {
                            results.Add(found.gameObject);
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Searches for GameObjects by tag.
        /// </summary>
        private static List<GameObject> SearchByTag(string searchTerm, bool includeInactive)
        {
            var results = new List<GameObject>();

            // Validate tag exists
            bool tagExists = false;
            try
            {
                foreach (string tag in UnityEditorInternal.InternalEditorUtility.tags)
                {
                    if (tag.Equals(searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        tagExists = true;
                        searchTerm = tag; // Use the correct case
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[FindGameObjects] Error checking tags: {exception.Message}");
            }

            if (!tagExists)
            {
                // Return empty results for non-existent tag rather than throwing
                return results;
            }

            if (includeInactive)
            {
                // Unity's FindGameObjectsWithTag doesn't find inactive objects
                var allObjects = GetAllSceneObjects(true);
                foreach (var gameObject in allObjects)
                {
                    if (gameObject != null)
                    {
                        try
                        {
                            if (gameObject.CompareTag(searchTerm))
                            {
                                results.Add(gameObject);
                            }
                        }
                        catch (UnityException ex)
                        {
                            // CompareTag throws if tag doesn't exist - expected behavior
                            Debug.LogWarning($"[FindGameObjects] UnityException when comparing tag: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                try
                {
                    var foundObjects = GameObject.FindGameObjectsWithTag(searchTerm);
                    results.AddRange(foundObjects.Where(go => go != null));
                }
                catch (UnityException ex)
                {
                    // Tag might not exist at runtime - return empty results
                    Debug.LogWarning($"[FindGameObjects] UnityException when finding objects by tag: {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Searches for GameObjects by layer name or number.
        /// </summary>
        private static List<GameObject> SearchByLayer(string searchTerm, bool includeInactive)
        {
            var results = new List<GameObject>();

            int layerId;

            // Try to parse as layer number first
            if (int.TryParse(searchTerm, out int parsedLayerId))
            {
                if (parsedLayerId < 0 || parsedLayerId > 31)
                {
                    throw MCPException.InvalidParams($"Layer number must be between 0 and 31. Got: {parsedLayerId}");
                }
                layerId = parsedLayerId;
            }
            else
            {
                // Try to find by layer name
                layerId = LayerMask.NameToLayer(searchTerm);
                if (layerId == -1)
                {
                    // Return empty results for non-existent layer rather than throwing
                    return results;
                }
            }

            var allObjects = GetAllSceneObjects(includeInactive);
            foreach (var gameObject in allObjects)
            {
                if (gameObject != null && gameObject.layer == layerId)
                {
                    results.Add(gameObject);
                }
            }

            return results;
        }

        /// <summary>
        /// Searches for GameObjects that have a specific component type.
        /// </summary>
        private static List<GameObject> SearchByComponent(string searchTerm, bool includeInactive)
        {
            var results = new List<GameObject>();

            Type componentType = ResolveComponentType(searchTerm);
            if (componentType == null)
            {
                // Return empty results for non-existent component type rather than throwing
                return results;
            }

            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                throw MCPException.InvalidParams($"Type '{searchTerm}' is not a Component type.");
            }

            if (includeInactive)
            {
                var allObjects = GetAllSceneObjects(true);
                foreach (var gameObject in allObjects)
                {
                    if (gameObject != null && gameObject.GetComponent(componentType) != null)
                    {
                        results.Add(gameObject);
                    }
                }
            }
            else
            {
                var foundComponents = UnityEngine.Object.FindObjectsOfType(componentType) as Component[];
                if (foundComponents != null)
                {
                    foreach (var component in foundComponents)
                    {
                        if (component != null && component.gameObject != null)
                        {
                            results.Add(component.gameObject);
                        }
                    }
                }
            }

            // Remove duplicates (a GameObject might have multiple components of the same type)
            return results.Distinct().ToList();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the active scene, handling prefab stage.
        /// </summary>
        private static Scene GetActiveScene()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                return prefabStage.scene;
            }
            return EditorSceneManager.GetActiveScene();
        }

        /// <summary>
        /// Gets all GameObjects in the active scene.
        /// </summary>
        private static IEnumerable<GameObject> GetAllSceneObjects(bool includeInactive)
        {
            Scene activeScene = GetActiveScene();
            var roots = activeScene.GetRootGameObjects();
            var allObjects = new List<GameObject>();

            foreach (var root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                if (includeInactive || root.activeInHierarchy)
                {
                    allObjects.Add(root);
                }

                var transforms = root.GetComponentsInChildren<Transform>(includeInactive);
                foreach (var transform in transforms)
                {
                    if (transform != null && transform.gameObject != null && transform.gameObject != root)
                    {
                        allObjects.Add(transform.gameObject);
                    }
                }
            }

            return allObjects;
        }

        /// <summary>
        /// Resolves a component type by name.
        /// </summary>
        private static Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            // Try exact match first
            Type type = Type.GetType(typeName);
            if (type != null)
            {
                return type;
            }

            // Try with UnityEngine namespace
            type = Type.GetType($"UnityEngine.{typeName}, UnityEngine");
            if (type != null)
            {
                return type;
            }

            // Try UnityEngine.UI
            type = Type.GetType($"UnityEngine.UI.{typeName}, UnityEngine.UI");
            if (type != null)
            {
                return type;
            }

            // Search all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }

                // Try with UnityEngine prefix
                type = assembly.GetType($"UnityEngine.{typeName}");
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds a paginated response with instance IDs.
        /// </summary>
        private static object BuildPaginatedResponse(List<GameObject> results, int pageSize, int cursor, string searchMethod, string searchTerm)
        {
            int totalCount = results.Count;

            // Adjust cursor if it exceeds total
            if (cursor > totalCount)
            {
                cursor = totalCount;
            }

            int endIndex = Mathf.Min(totalCount, cursor + pageSize);
            int actualPageSize = endIndex - cursor;

            var instanceIds = new List<int>(actualPageSize);
            for (int i = cursor; i < endIndex; i++)
            {
                var gameObject = results[i];
                if (gameObject != null)
                {
                    instanceIds.Add(gameObject.GetInstanceID());
                }
            }

            bool hasMore = endIndex < totalCount;
            int? nextCursor = hasMore ? endIndex : (int?)null;

            return new
            {
                success = true,
                searchMethod,
                searchTerm,
                instanceIDs = instanceIds,
                pageSize,
                cursor,
                nextCursor,
                totalCount,
                hasMore
            };
        }

        #endregion
    }
}
