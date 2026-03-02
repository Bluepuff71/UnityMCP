using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Services;
using UnityMCP.Editor.Utilities;

#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Handles scene management operations like loading, saving, creating, and querying hierarchy.
    /// </summary>
    public static class ManageScene
    {
        #region Scene Creation

        /// <summary>
        /// Creates a new empty scene at the specified path.
        /// </summary>
        [MCPTool("scene_create", "Creates a new empty scene at the specified path", Category = "Scene", DestructiveHint = true)]
        public static object CreateScene(
            [MCPParam("name", "Name of the scene (without .unity extension)", required: true)] string name,
            [MCPParam("path", "Directory path relative to Assets (default: Scenes)")] string path = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw MCPException.InvalidParams("Scene name is required.");
            }

            // Normalize and validate path
            string relativeDirectory = NormalizePath(path);
            if (string.IsNullOrEmpty(relativeDirectory))
            {
                relativeDirectory = "Scenes";
            }

            string sceneFileName = $"{name}.unity";
            string relativePath = Path.Combine("Assets", relativeDirectory, sceneFileName).Replace('\\', '/');
            string fullDirectoryPath = Path.Combine(Application.dataPath, relativeDirectory);
            string fullPath = Path.Combine(fullDirectoryPath, sceneFileName);

            // Check if scene already exists
            if (File.Exists(fullPath))
            {
                return new
                {
                    success = false,
                    error = $"Scene already exists at '{relativePath}'."
                };
            }

            // Ensure directory exists
            try
            {
                Directory.CreateDirectory(fullDirectoryPath);
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    error = $"Could not create directory '{fullDirectoryPath}': {ex.Message}"
                };
            }

            try
            {
                // Create a new empty scene
                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                // Save it to the specified path
                bool saved = EditorSceneManager.SaveScene(newScene, relativePath);

                if (saved)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    return new
                    {
                        success = true,
                        message = $"Scene '{name}' created successfully.",
                        path = relativePath
                    };
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to save new scene to '{relativePath}'."
                    };
                }
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    error = $"Error creating scene: {ex.Message}"
                };
            }
        }

        #endregion

        #region Scene Loading

        /// <summary>
        /// Loads a scene by path or build index.
        /// </summary>
        [MCPTool("scene_load", "Loads a scene by path (relative to Assets) or build index", Category = "Scene", DestructiveHint = true)]
        public static object LoadScene(
            [MCPParam("name", "Name of the scene (without .unity extension)")] string name = null,
            [MCPParam("path", "Directory path relative to Assets (used with name)")] string path = null,
            [MCPParam("build_index", "Build index of the scene to load")] int? buildIndex = null)
        {
            // Determine how to load the scene
            if (buildIndex.HasValue)
            {
                return LoadSceneByBuildIndex(buildIndex.Value);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                string relativeDirectory = NormalizePath(path);
                string sceneFileName = $"{name}.unity";
                string relativePath = string.IsNullOrEmpty(relativeDirectory)
                    ? Path.Combine("Assets", sceneFileName).Replace('\\', '/')
                    : Path.Combine("Assets", relativeDirectory, sceneFileName).Replace('\\', '/');

                return LoadSceneByPath(relativePath);
            }
            else
            {
                throw MCPException.InvalidParams("Either 'name' or 'build_index' must be provided.");
            }
        }

        private static object LoadSceneByPath(string relativePath)
        {
            // Convert relative path to absolute for file existence check
            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            string fullPath = Path.Combine(projectRoot, relativePath);

            if (!File.Exists(fullPath))
            {
                return new
                {
                    success = false,
                    error = $"Scene file not found at '{relativePath}'."
                };
            }

            // Check for unsaved changes
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                return new
                {
                    success = false,
                    error = "Current scene has unsaved changes. Please save or discard changes before loading a new scene."
                };
            }

            try
            {
                EditorSceneManager.OpenScene(relativePath, OpenSceneMode.Single);
                return new
                {
                    success = true,
                    message = $"Scene '{relativePath}' loaded successfully.",
                    path = relativePath,
                    name = Path.GetFileNameWithoutExtension(relativePath)
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    error = $"Error loading scene '{relativePath}': {ex.Message}"
                };
            }
        }

        private static object LoadSceneByBuildIndex(int buildIndex)
        {
            if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                return new
                {
                    success = false,
                    error = $"Invalid build index: {buildIndex}. Must be between 0 and {SceneManager.sceneCountInBuildSettings - 1}."
                };
            }

            // Check for unsaved changes
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                return new
                {
                    success = false,
                    error = "Current scene has unsaved changes. Please save or discard changes before loading a new scene."
                };
            }

            try
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                return new
                {
                    success = true,
                    message = $"Scene at build index {buildIndex} loaded successfully.",
                    path = scenePath,
                    name = Path.GetFileNameWithoutExtension(scenePath),
                    buildIndex
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    error = $"Error loading scene with build index {buildIndex}: {ex.Message}"
                };
            }
        }

        #endregion

        #region Scene Saving

        /// <summary>
        /// Saves the current scene, optionally to a new path.
        /// </summary>
        [MCPTool("scene_save", "Saves the current scene, optionally to a new path", Category = "Scene", DestructiveHint = true)]
        public static object SaveScene(
            [MCPParam("name", "Name for Save As (without .unity extension)")] string name = null,
            [MCPParam("path", "Directory path for Save As (relative to Assets)")] string path = null)
        {
            try
            {
                Scene currentScene = EditorSceneManager.GetActiveScene();
                if (!currentScene.IsValid())
                {
                    return new
                    {
                        success = false,
                        error = "No valid scene is currently active to save."
                    };
                }

                bool saved;
                string finalPath = currentScene.path;

                // Check if this is a Save As operation
                if (!string.IsNullOrEmpty(name))
                {
                    string relativeDirectory = NormalizePath(path);
                    if (string.IsNullOrEmpty(relativeDirectory))
                    {
                        relativeDirectory = "Scenes";
                    }

                    string sceneFileName = $"{name}.unity";
                    string relativePath = Path.Combine("Assets", relativeDirectory, sceneFileName).Replace('\\', '/');
                    string fullDirectoryPath = Path.Combine(Application.dataPath, relativeDirectory);

                    // Ensure directory exists
                    if (!Directory.Exists(fullDirectoryPath))
                    {
                        Directory.CreateDirectory(fullDirectoryPath);
                    }

                    saved = EditorSceneManager.SaveScene(currentScene, relativePath);
                    finalPath = relativePath;
                }
                else
                {
                    // Regular save
                    if (string.IsNullOrEmpty(currentScene.path))
                    {
                        return new
                        {
                            success = false,
                            error = "Cannot save an untitled scene without providing a 'name'. Use Save As functionality."
                        };
                    }
                    saved = EditorSceneManager.SaveScene(currentScene);
                }

                if (saved)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                    // Auto-checkpoint: fold tracked asset changes into current bucket
                    try
                    {
                        if (CheckpointManager.HasPendingTracks)
                        {
                            CheckpointManager.SaveCheckpoint();
                        }
                    }
                    catch (Exception checkpointException)
                    {
                        Debug.LogWarning($"[ManageScene] Auto-checkpoint failed: {checkpointException.Message}");
                    }

                    return new
                    {
                        success = true,
                        message = $"Scene '{currentScene.name}' saved successfully.",
                        path = finalPath,
                        name = currentScene.name
                    };
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to save scene '{currentScene.name}'."
                    };
                }
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    error = $"Error saving scene: {ex.Message}"
                };
            }
        }

        #endregion

        #region Scene Info

        /// <summary>
        /// Gets information about the currently active scene.
        /// </summary>
        [MCPTool("scene_get_active", "Gets information about the currently active scene", Category = "Scene", ReadOnlyHint = true)]
        public static object GetActiveScene()
        {
            try
            {
                // Check for prefab stage first
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                Scene activeScene;
                bool inPrefabMode = false;

                if (prefabStage != null)
                {
                    activeScene = prefabStage.scene;
                    inPrefabMode = true;
                }
                else
                {
                    activeScene = EditorSceneManager.GetActiveScene();
                }

                if (!activeScene.IsValid())
                {
                    return new
                    {
                        success = false,
                        error = "No active scene found."
                    };
                }

                return new
                {
                    success = true,
                    name = activeScene.name,
                    path = activeScene.path,
                    buildIndex = activeScene.buildIndex,
                    isDirty = activeScene.isDirty,
                    isLoaded = activeScene.isLoaded,
                    rootCount = activeScene.rootCount,
                    inPrefabMode
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    error = $"Error getting active scene info: {ex.Message}"
                };
            }
        }

        #endregion

        #region Scene Hierarchy

        /// <summary>
        /// Gets the hierarchy of GameObjects in the current scene.
        /// </summary>
        [MCPTool("scene_get_hierarchy", "Gets the hierarchy of GameObjects in the current scene", Category = "Scene", ReadOnlyHint = true)]
        public static object GetHierarchy(
            [MCPParam("parent", "Instance ID or name of parent GameObject to list children of (null for roots)")] string parent = null,
            [MCPParam("max_depth", "Maximum depth to traverse (default: 1, just immediate children)", Minimum = 1)] int maxDepth = 1,
            [MCPParam("include_transform", "Include transform data in results")] bool includeTransform = false,
            [MCPParam("page_size", "Maximum number of items to return (default: 50, max: 500)", Minimum = 1, Maximum = 500)] int pageSize = 50,
            [MCPParam("cursor", "Starting index for pagination (default: 0)", Minimum = 0)] int cursor = 0)
        {
            try
            {
                // Check for prefab stage first
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                Scene activeScene;

                if (prefabStage != null)
                {
                    activeScene = prefabStage.scene;
                }
                else
                {
                    activeScene = EditorSceneManager.GetActiveScene();
                }

                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return new
                    {
                        success = false,
                        error = "No valid and loaded scene is active to get hierarchy from."
                    };
                }

                // Clamp values to safe ranges
                int resolvedPageSize = Mathf.Clamp(pageSize, 1, 500);
                int resolvedCursor = Mathf.Max(0, cursor);
                int resolvedMaxDepth = Mathf.Clamp(maxDepth, 1, 10);

                List<GameObject> nodes;
                string scope;

                // Resolve parent if provided
                GameObject parentGameObject = null;
                if (!string.IsNullOrEmpty(parent))
                {
                    parentGameObject = ResolveGameObject(parent, activeScene);
                    if (parentGameObject == null)
                    {
                        return new
                        {
                            success = false,
                            error = $"Parent GameObject '{parent}' not found."
                        };
                    }
                }

                if (parentGameObject == null)
                {
                    // Get root objects
                    nodes = activeScene.GetRootGameObjects().Where(go => go != null).ToList();
                    scope = "roots";
                }
                else
                {
                    // Get children of parent
                    nodes = new List<GameObject>(parentGameObject.transform.childCount);
                    foreach (Transform child in parentGameObject.transform)
                    {
                        if (child != null)
                        {
                            nodes.Add(child.gameObject);
                        }
                    }
                    scope = "children";
                }

                int total = nodes.Count;
                if (resolvedCursor > total)
                {
                    resolvedCursor = total;
                }

                int endIndex = Mathf.Min(total, resolvedCursor + resolvedPageSize);
                var items = new List<object>(Mathf.Max(0, endIndex - resolvedCursor));

                for (int i = resolvedCursor; i < endIndex; i++)
                {
                    var gameObject = nodes[i];
                    if (gameObject != null)
                    {
                        items.Add(BuildGameObjectSummary(gameObject, includeTransform, resolvedMaxDepth, 0));
                    }
                }

                bool truncated = endIndex < total;
                int? nextCursor = truncated ? endIndex : (int?)null;

                return new
                {
                    success = true,
                    sceneName = activeScene.name,
                    scope,
                    cursor = resolvedCursor,
                    pageSize = resolvedPageSize,
                    nextCursor,
                    truncated,
                    total,
                    items
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    error = $"Error getting scene hierarchy: {ex.Message}"
                };
            }
        }

        #endregion

        #region Screenshot

        /// <summary>
        /// Captures a screenshot of the Game View or Scene View, with optional target framing and camera angle.
        /// </summary>
        [MCPTool("scene_screenshot", "Captures a screenshot of the Game View or Scene View with optional target framing and camera angle", Category = "Scene", ReadOnlyHint = true)]
        public static object CaptureScreenshot(
            [MCPParam("filename", "Filename for the screenshot (without extension)")] string filename = null,
            [MCPParam("super_size", "Multiplier for resolution (1-4, default: 1)", Minimum = 1, Maximum = 4)] int superSize = 1,
            [MCPParam("target", "GameObject name, path, or instance ID to frame in the shot (auto-positions Scene View camera)")] string target = null,
            [MCPParam("angle", "Camera angle for Scene View capture", Enum = new[] { "current", "top", "front", "right", "isometric" })] string angle = "current",
            [MCPParam("view", "Which view to capture: 'game' (Game View, default) or 'scene' (Scene View)", Enum = new[] { "scene", "game" })] string view = "game")
        {
            try
            {
                // Validate super size
                int resolvedSuperSize = Mathf.Clamp(superSize, 1, 4);

                // Normalize view and angle parameters
                string resolvedView = string.IsNullOrEmpty(view) ? "game" : view.ToLowerInvariant();
                string resolvedAngle = string.IsNullOrEmpty(angle) ? "current" : angle.ToLowerInvariant();

                // Validate view parameter
                if (resolvedView != "game" && resolvedView != "scene")
                {
                    return new
                    {
                        success = false,
                        error = $"Invalid view '{view}'. Must be 'game' or 'scene'."
                    };
                }

                // Validate angle parameter
                string[] validAngles = { "current", "top", "front", "right", "isometric" };
                if (Array.IndexOf(validAngles, resolvedAngle) < 0)
                {
                    return new
                    {
                        success = false,
                        error = $"Invalid angle '{angle}'. Must be one of: current, top, front, right, isometric."
                    };
                }

                // If target or non-current angle is specified, we need the Scene View
                bool needsSceneViewSetup = !string.IsNullOrEmpty(target) || resolvedAngle != "current";

                // Position the Scene View camera if needed
                if (needsSceneViewSetup)
                {
                    SceneView sceneView = SceneView.lastActiveSceneView;
                    if (sceneView == null)
                    {
                        return new
                        {
                            success = false,
                            error = "No active Scene View found. Open a Scene View window first."
                        };
                    }

                    // If target specified, resolve and frame it
                    if (!string.IsNullOrEmpty(target))
                    {
                        Scene activeScene = EditorSceneManager.GetActiveScene();
                        GameObject targetGameObject = ResolveGameObject(target, activeScene);
                        if (targetGameObject == null)
                        {
                            return new
                            {
                                success = false,
                                error = $"Target GameObject '{target}' not found."
                            };
                        }

                        // Set the angle before framing if not "current"
                        if (resolvedAngle != "current")
                        {
                            Vector3 lookAtPoint = targetGameObject.transform.position;
                            Quaternion angleRotation = GetAngleRotation(resolvedAngle);
                            sceneView.LookAt(lookAtPoint, angleRotation);
                        }

                        // Select and frame the target
                        Selection.activeGameObject = targetGameObject;
                        sceneView.FrameSelected();
                    }
                    else if (resolvedAngle != "current")
                    {
                        // No target but angle specified: rotate around current pivot
                        Quaternion angleRotation = GetAngleRotation(resolvedAngle);
                        sceneView.LookAt(sceneView.pivot, angleRotation);
                    }

                    // Force the Scene View to repaint so the camera is updated
                    sceneView.Repaint();
                }

                // Generate filename if not provided
                string screenshotFileName = string.IsNullOrEmpty(filename)
                    ? $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
                    : filename;

                // Ensure Screenshots folder exists
                string screenshotsFolder = Path.Combine(Application.dataPath, "Screenshots");
                if (!Directory.Exists(screenshotsFolder))
                {
                    Directory.CreateDirectory(screenshotsFolder);
                }

                // Generate unique filename
                string basePath = Path.Combine(screenshotsFolder, screenshotFileName);
                string finalPath = basePath + ".png";
                int fileCounter = 1;
                while (File.Exists(finalPath))
                {
                    finalPath = $"{basePath}_{fileCounter}.png";
                    fileCounter++;
                }

                // Calculate relative path
                string relativePath = "Assets/Screenshots/" + Path.GetFileName(finalPath);

                if (resolvedView == "scene")
                {
                    // Capture from Scene View camera
                    return CaptureSceneViewScreenshot(finalPath, relativePath, resolvedSuperSize, resolvedAngle);
                }
                else
                {
                    // Existing Game View capture behavior
                    return CaptureGameViewScreenshot(finalPath, relativePath, resolvedSuperSize);
                }
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    error = $"Error capturing screenshot: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Captures a screenshot from the Game View using ScreenCapture.
        /// </summary>
        private static object CaptureGameViewScreenshot(string fullPath, string relativePath, int superSize)
        {
            // Best effort: ensure Game View exists and repaints before capture
            if (!Application.isBatchMode)
            {
                EnsureGameView();
            }

            // Capture the screenshot
            ScreenCapture.CaptureScreenshot(fullPath, superSize);

            // Schedule asset import (screenshot capture is async in play mode)
            if (Application.isPlaying)
            {
                ScheduleAssetImport(relativePath, fullPath, 30.0);
            }
            else
            {
                // In edit mode, wait for the file to be written then import.
                // Do NOT use ForceSynchronousImport -- see CaptureSceneViewScreenshot comment.
                EditorApplication.delayCall += () =>
                {
                    if (File.Exists(fullPath))
                    {
                        AssetDatabase.ImportAsset(relativePath);
                    }
                };
            }

            return new
            {
                success = true,
                message = "Screenshot capture initiated.",
                path = relativePath,
                fullPath,
                superSize,
                view = "game",
                isAsync = Application.isPlaying
            };
        }

        /// <summary>
        /// Captures a screenshot from the Scene View by rendering its camera to a RenderTexture.
        /// </summary>
        private static object CaptureSceneViewScreenshot(string fullPath, string relativePath, int superSize, string angle)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return new
                {
                    success = false,
                    error = "No active Scene View found. Open a Scene View window first."
                };
            }

            Camera sceneCamera = sceneView.camera;
            if (sceneCamera == null)
            {
                return new
                {
                    success = false,
                    error = "Scene View camera is not available."
                };
            }

            // Calculate resolution based on Scene View size and super size multiplier
            int captureWidth = (int)sceneView.position.width * superSize;
            int captureHeight = (int)sceneView.position.height * superSize;

            if (captureWidth <= 0 || captureHeight <= 0)
            {
                return new
                {
                    success = false,
                    error = "Scene View has invalid dimensions. Ensure it is visible and has a non-zero size."
                };
            }

            RenderTexture renderTexture = null;
            RenderTexture previousTargetTexture = sceneCamera.targetTexture;
            RenderTexture previousActiveRenderTexture = RenderTexture.active;

            try
            {
                // Create a temporary RenderTexture for the capture
                renderTexture = new RenderTexture(captureWidth, captureHeight, 24);
                sceneCamera.targetTexture = renderTexture;
                sceneCamera.Render();

                // Read pixels from the RenderTexture into a Texture2D
                RenderTexture.active = renderTexture;
                Texture2D screenshotTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
                screenshotTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                screenshotTexture.Apply();

                // Encode to PNG and write to disk
                byte[] pngBytes = screenshotTexture.EncodeToPNG();
                File.WriteAllBytes(fullPath, pngBytes);

                // Clean up the temporary texture
                UnityEngine.Object.DestroyImmediate(screenshotTexture);

                // Import the asset so it appears in the Asset Database.
                // IMPORTANT: Do NOT use ForceSynchronousImport here. It processes the entire import
                // pipeline synchronously, which can pump EditorApplication.update and cause re-entrant
                // PollForRequests dispatch -- leading to duplicate screenshot files with parallel captures.
                EditorApplication.delayCall += () =>
                {
                    if (File.Exists(fullPath))
                    {
                        AssetDatabase.ImportAsset(relativePath);
                    }
                };

                return new
                {
                    success = true,
                    message = "Scene View screenshot captured.",
                    path = relativePath,
                    fullPath,
                    superSize,
                    view = "scene",
                    angle,
                    resolution = new { width = captureWidth, height = captureHeight }
                };
            }
            finally
            {
                // Restore camera and RenderTexture state
                sceneCamera.targetTexture = previousTargetTexture;
                RenderTexture.active = previousActiveRenderTexture;

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
            }
        }

        /// <summary>
        /// Returns the Quaternion rotation for a named camera angle.
        /// </summary>
        private static Quaternion GetAngleRotation(string angle)
        {
            switch (angle)
            {
                case "top":
                    return Quaternion.Euler(90, 0, 0);
                case "front":
                    return Quaternion.Euler(0, 0, 0);
                case "right":
                    return Quaternion.Euler(0, -90, 0);
                case "isometric":
                    return Quaternion.Euler(30, -45, 0);
                default:
                    return Quaternion.identity;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Normalizes a path by removing leading/trailing slashes and "Assets/" prefix.
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string normalized = path.Replace('\\', '/').Trim('/');

            // Remove "Assets/" prefix if present
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Assets/".Length).TrimStart('/');
            }

            return normalized;
        }

        /// <summary>
        /// Resolves a GameObject by instance ID or name.
        /// </summary>
        private static GameObject ResolveGameObject(string target, Scene activeScene)
        {
            if (string.IsNullOrEmpty(target))
            {
                return null;
            }

            // Try to parse as instance ID first
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
                    if (root == null) continue;

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

            // Try name-based lookup (first match)
            var allRoots = activeScene.GetRootGameObjects();
            foreach (var root in allRoots)
            {
                if (root == null) continue;

                if (root.name.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    return root;
                }

                var transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
                foreach (var transform in transforms)
                {
                    if (transform != null && transform.gameObject != null &&
                        transform.gameObject.name.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        return transform.gameObject;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Builds a summary object for a GameObject.
        /// </summary>
        private static object BuildGameObjectSummary(GameObject gameObject, bool includeTransform, int maxDepth, int currentDepth)
        {
            if (gameObject == null)
            {
                return null;
            }

            int childCount = gameObject.transform != null ? gameObject.transform.childCount : 0;

            // Get component type names
            var componentTypes = new List<string>();
            try
            {
                var components = gameObject.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component != null)
                    {
                        componentTypes.Add(component.GetType().Name);
                    }
                }
            }
            catch
            {
                // Ignore errors when getting components
            }

            var summary = new Dictionary<string, object>
            {
                { "name", gameObject.name },
                { "instanceID", gameObject.GetInstanceID() },
                { "activeSelf", gameObject.activeSelf },
                { "activeInHierarchy", gameObject.activeInHierarchy },
                { "tag", gameObject.tag },
                { "layer", gameObject.layer },
                { "isStatic", gameObject.isStatic },
                { "path", GetGameObjectPath(gameObject) },
                { "childCount", childCount },
                { "componentTypes", componentTypes }
            };

            if (includeTransform && gameObject.transform != null)
            {
                var transform = gameObject.transform;
                summary["transform"] = new Dictionary<string, object>
                {
                    { "localPosition", new[] { transform.localPosition.x, transform.localPosition.y, transform.localPosition.z } },
                    { "localRotation", new[] { transform.localEulerAngles.x, transform.localEulerAngles.y, transform.localEulerAngles.z } },
                    { "localScale", new[] { transform.localScale.x, transform.localScale.y, transform.localScale.z } }
                };
            }

            // Include children if depth allows
            if (currentDepth < maxDepth - 1 && childCount > 0)
            {
                var children = new List<object>();
                foreach (Transform child in gameObject.transform)
                {
                    if (child != null)
                    {
                        children.Add(BuildGameObjectSummary(child.gameObject, includeTransform, maxDepth, currentDepth + 1));
                    }
                }
                summary["children"] = children;
            }

            return summary;
        }

        /// <summary>
        /// Gets the full hierarchy path of a GameObject.
        /// </summary>
        private static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            try
            {
                var names = new Stack<string>();
                Transform transform = gameObject.transform;
                while (transform != null)
                {
                    names.Push(transform.name);
                    transform = transform.parent;
                }
                return string.Join("/", names);
            }
            catch
            {
                return gameObject.name;
            }
        }

        /// <summary>
        /// Ensures the Game View is open and repainted.
        /// </summary>
        private static void EnsureGameView()
        {
            try
            {
                // Try to open Game View via menu
                EditorApplication.ExecuteMenuItem("Window/General/Game");
            }
            catch
            {
                // Ignore if menu item fails
            }

            try
            {
                // Get and repaint Game View
                var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType != null)
                {
                    var window = EditorWindow.GetWindow(gameViewType);
                    window?.Repaint();
                }
            }
            catch
            {
                // Ignore if repaint fails
            }

            try
            {
                SceneView.RepaintAll();
            }
            catch
            {
                // Ignore
            }

            try
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch
            {
                // Ignore
            }
        }

        /// <summary>
        /// Schedules asset import when a file exists (for async screenshot capture).
        /// </summary>
        private static void ScheduleAssetImport(string assetsRelativePath, string fullPath, double timeoutSeconds)
        {
            double startTime = EditorApplication.timeSinceStartup;

            void CheckAndImport()
            {
                try
                {
                    if (File.Exists(fullPath))
                    {
                        AssetDatabase.ImportAsset(assetsRelativePath, ImportAssetOptions.ForceSynchronousImport);
                        EditorApplication.update -= CheckAndImport;
                        return;
                    }
                }
                catch
                {
                    // Ignore errors during check
                }

                if (EditorApplication.timeSinceStartup - startTime > timeoutSeconds)
                {
                    EditorApplication.update -= CheckAndImport;
                }
            }

            EditorApplication.update += CheckAndImport;
        }

        #endregion
    }
}
