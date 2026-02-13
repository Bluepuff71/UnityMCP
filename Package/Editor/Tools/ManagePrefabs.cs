using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Utilities;

#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Handles prefab stage operations including opening, closing, saving, and creating prefabs.
    /// </summary>
    public static class ManagePrefabs
    {
        #region Main Tool Entry Point

        /// <summary>
        /// Manages prefab stage operations: open_stage, close_stage, save_open_stage, create_from_gameobject.
        /// </summary>
        [MCPTool("prefab_manage", "Manages prefab operations: open_stage, close_stage, save_open_stage, create_from_gameobject", Category = "Asset", DestructiveHint = true)]
        public static object Manage(
            [MCPParam("action", "Action to perform: open_stage, close_stage, save_open_stage, create_from_gameobject", required: true, Enum = new[] { "open_stage", "close_stage", "save_open_stage", "create_from_gameobject" })] string action,
            [MCPParam("prefab_path", "Path to the prefab asset (for open_stage and create_from_gameobject)")] string prefabPath = null,
            [MCPParam("save_before_close", "Whether to save before closing the prefab stage (default: false)")] bool saveBeforeClose = false,
            [MCPParam("target", "Name or instance ID of the GameObject to create prefab from")] string target = null,
            [MCPParam("allow_overwrite", "Whether to overwrite existing prefab (default: false)")] bool allowOverwrite = false,
            [MCPParam("search_inactive", "Whether to search inactive GameObjects (default: false)")] bool searchInactive = false)
        {
            if (string.IsNullOrEmpty(action))
            {
                throw MCPException.InvalidParams("Action parameter is required.");
            }

            string normalizedAction = action.ToLowerInvariant().Trim().Replace("-", "_");

            try
            {
                return normalizedAction switch
                {
                    "open_stage" or "openstage" or "open" => HandleOpenStage(prefabPath),
                    "close_stage" or "closestage" or "close" => HandleCloseStage(saveBeforeClose),
                    "save_open_stage" or "saveopenstage" or "save" => HandleSaveOpenStage(),
                    "create_from_gameobject" or "createfromgameobject" or "create" => HandleCreateFromGameObject(target, prefabPath, allowOverwrite, searchInactive),
                    _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: open_stage, close_stage, save_open_stage, create_from_gameobject")
                };
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
                    error = $"Error executing action '{action}': {exception.Message}"
                };
            }
        }

        #endregion

        #region Action Handlers

        /// <summary>
        /// Opens a prefab in the Prefab Stage editor.
        /// </summary>
        private static object HandleOpenStage(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath))
            {
                throw MCPException.InvalidParams("'prefab_path' parameter is required for open_stage action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(prefabPath);

            // Ensure path has .prefab extension
            if (!normalizedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath += ".prefab";
            }

            // Check if prefab exists
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(normalizedPath);
            if (prefabAsset == null)
            {
                return new
                {
                    success = false,
                    error = $"Prefab not found at '{normalizedPath}'."
                };
            }

            try
            {
                // Open the prefab stage
                PrefabStage prefabStage = PrefabStageUtility.OpenPrefab(normalizedPath);

                if (prefabStage == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to open prefab stage for '{normalizedPath}'."
                    };
                }

                return new
                {
                    success = true,
                    message = $"Prefab stage opened for '{normalizedPath}'.",
                    stageInfo = BuildStageInfo(prefabStage)
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error opening prefab stage: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Closes the current prefab stage and returns to the main scene.
        /// </summary>
        private static object HandleCloseStage(bool saveBeforeClose)
        {
            PrefabStage currentStage = PrefabStageUtility.GetCurrentPrefabStage();

            if (currentStage == null)
            {
                return new
                {
                    success = false,
                    error = "No prefab stage is currently open."
                };
            }

            string assetPath = currentStage.assetPath;
            string prefabRootName = currentStage.prefabContentsRoot?.name ?? "Unknown";
            bool wasDirty = currentStage.scene.isDirty;

            try
            {
                // Save if requested and there are unsaved changes
                if (saveBeforeClose && wasDirty)
                {
                    bool saved = SavePrefabStage(currentStage);
                    if (!saved)
                    {
                        return new
                        {
                            success = false,
                            error = "Failed to save prefab stage before closing."
                        };
                    }
                }

                // Close the prefab stage by going back to the main stage
                StageUtility.GoToMainStage();

                return new
                {
                    success = true,
                    message = $"Prefab stage closed{(saveBeforeClose && wasDirty ? " (saved)" : "")}.",
                    closedPrefab = new
                    {
                        assetPath,
                        prefabRootName,
                        wasDirty,
                        savedBeforeClose = saveBeforeClose && wasDirty
                    },
                    stageInfo = BuildCurrentStageInfo()
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error closing prefab stage: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Saves the currently open prefab stage.
        /// </summary>
        private static object HandleSaveOpenStage()
        {
            PrefabStage currentStage = PrefabStageUtility.GetCurrentPrefabStage();

            if (currentStage == null)
            {
                return new
                {
                    success = false,
                    error = "No prefab stage is currently open."
                };
            }

            string assetPath = currentStage.assetPath;
            bool wasDirty = currentStage.scene.isDirty;

            if (!wasDirty)
            {
                return new
                {
                    success = true,
                    message = "Prefab stage has no unsaved changes.",
                    stageInfo = BuildStageInfo(currentStage)
                };
            }

            try
            {
                bool saved = SavePrefabStage(currentStage);

                if (saved)
                {
                    return new
                    {
                        success = true,
                        message = $"Prefab stage saved successfully.",
                        stageInfo = BuildStageInfo(currentStage)
                    };
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to save prefab stage for '{assetPath}'."
                    };
                }
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error saving prefab stage: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Creates a new prefab from a scene GameObject.
        /// </summary>
        private static object HandleCreateFromGameObject(string target, string prefabPath, bool allowOverwrite, bool searchInactive)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for create_from_gameobject action.");
            }

            if (string.IsNullOrEmpty(prefabPath))
            {
                throw MCPException.InvalidParams("'prefab_path' parameter is required for create_from_gameobject action.");
            }

            // Find the target GameObject
            GameObject targetGameObject = FindGameObject(target, searchInactive);
            if (targetGameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"Target GameObject '{target}' not found{(searchInactive ? " (including inactive)" : "")}."
                };
            }

            string normalizedPath = PathUtilities.NormalizePath(prefabPath);

            // Ensure path has .prefab extension
            if (!normalizedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath += ".prefab";
            }

            // Check if prefab already exists
            bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(normalizedPath) != null;
            if (prefabExists && !allowOverwrite)
            {
                return new
                {
                    success = false,
                    error = $"Prefab already exists at '{normalizedPath}'. Set 'allow_overwrite' to true to replace it."
                };
            }

            // Ensure parent directory exists
            string parentDirectory = System.IO.Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentDirectory) && !AssetDatabase.IsValidFolder(parentDirectory))
            {
                if (!PathUtilities.EnsureFolderExists(parentDirectory, out string folderError))
                {
                    return new { success = false, error = folderError };
                }
            }

            try
            {
                // Create or overwrite the prefab and connect the instance
                GameObject prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    targetGameObject,
                    normalizedPath,
                    InteractionMode.UserAction);

                if (prefabAsset == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to create prefab at '{normalizedPath}'."
                    };
                }

                // Mark the scene as dirty since we connected the GameObject to the prefab
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

                string guid = AssetDatabase.AssetPathToGUID(normalizedPath);

                return new
                {
                    success = true,
                    message = prefabExists
                        ? $"Prefab overwritten at '{normalizedPath}'."
                        : $"Prefab created at '{normalizedPath}'.",
                    prefab = new
                    {
                        path = normalizedPath,
                        guid,
                        name = prefabAsset.name,
                        sourceGameObject = targetGameObject.name,
                        sourceInstanceID = targetGameObject.GetInstanceID(),
                        wasOverwritten = prefabExists
                    },
                    stageInfo = BuildCurrentStageInfo()
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error creating prefab: {exception.Message}"
                };
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Saves the current prefab stage.
        /// </summary>
        private static bool SavePrefabStage(PrefabStage prefabStage)
        {
            if (prefabStage == null || prefabStage.prefabContentsRoot == null)
            {
                return false;
            }

            try
            {
                // Save the prefab asset
                PrefabUtility.SaveAsPrefabAsset(prefabStage.prefabContentsRoot, prefabStage.assetPath);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManagePrefabs] Failed to save prefab stage: {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Builds information about the current prefab stage.
        /// </summary>
        private static object BuildStageInfo(PrefabStage prefabStage)
        {
            if (prefabStage == null)
            {
                return BuildCurrentStageInfo();
            }

            return new
            {
                isOpen = true,
                assetPath = prefabStage.assetPath,
                prefabRootName = prefabStage.prefabContentsRoot?.name ?? "Unknown",
                isDirty = prefabStage.scene.isDirty
            };
        }

        /// <summary>
        /// Builds information about the current stage (prefab or main scene).
        /// </summary>
        private static object BuildCurrentStageInfo()
        {
            PrefabStage currentStage = PrefabStageUtility.GetCurrentPrefabStage();

            if (currentStage != null)
            {
                return BuildStageInfo(currentStage);
            }

            // We're in the main scene
            Scene activeScene = EditorSceneManager.GetActiveScene();
            return new
            {
                isOpen = false,
                assetPath = (string)null,
                prefabRootName = (string)null,
                isDirty = activeScene.isDirty,
                mainSceneName = activeScene.name,
                mainScenePath = activeScene.path
            };
        }

        /// <summary>
        /// Finds a GameObject by instance ID or name in the current scene.
        /// </summary>
        private static GameObject FindGameObject(string target, bool searchInactive)
        {
            if (string.IsNullOrEmpty(target))
            {
                return null;
            }

            // Check if we're in a prefab stage
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            Scene activeScene = prefabStage != null ? prefabStage.scene : EditorSceneManager.GetActiveScene();

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

            // Try path-based lookup if contains "/"
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
            var allRoots = activeScene.GetRootGameObjects();
            foreach (var root in allRoots)
            {
                if (root == null)
                {
                    continue;
                }

                if (root.name.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    if (searchInactive || root.activeInHierarchy)
                    {
                        return root;
                    }
                }

                var transforms = root.GetComponentsInChildren<Transform>(searchInactive);
                foreach (var transform in transforms)
                {
                    if (transform != null && transform.gameObject != null &&
                        transform.gameObject.name.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        if (searchInactive || transform.gameObject.activeInHierarchy)
                        {
                            return transform.gameObject;
                        }
                    }
                }
            }

            return null;
        }

        #endregion
    }
}
