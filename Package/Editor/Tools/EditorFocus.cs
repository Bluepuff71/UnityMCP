using System;
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
    /// Programmatically frames and selects objects in the Scene View.
    /// </summary>
    public static class EditorFocus
    {
        #region Focus Tool

        /// <summary>
        /// Focuses the Scene View on a specified GameObject, selecting it and optionally locking the view.
        /// </summary>
        [MCPTool("focus_editor", "Frame and select a GameObject in the Scene View", Category = "Editor", ReadOnlyHint = false)]
        public static object FocusOnTarget(
            [MCPParam("target", "GameObject name, path, or instance ID to focus on", required: true)] string target,
            [MCPParam("lock", "Lock the Scene View to follow this object (keeps it selected)")] bool lockView = false)
        {
            try
            {
                if (string.IsNullOrEmpty(target))
                {
                    throw MCPException.InvalidParams("Target is required.");
                }

                // Resolve the target GameObject
                Scene activeScene = EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return new
                    {
                        success = false,
                        error = "No valid and loaded scene is active."
                    };
                }

                GameObject targetGameObject = ResolveGameObject(target, activeScene);
                if (targetGameObject == null)
                {
                    return new
                    {
                        success = false,
                        error = $"GameObject '{target}' not found."
                    };
                }

                // Get the Scene View
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    return new
                    {
                        success = false,
                        error = "No active Scene View found. Open a Scene View window first."
                    };
                }

                // Select the target and frame it in the Scene View
                Selection.activeGameObject = targetGameObject;
                sceneView.FrameSelected(lockView);
                sceneView.Repaint();

                // Build the response
                string gameObjectPath = GetGameObjectPath(targetGameObject);

                return new
                {
                    success = true,
                    message = lockView
                        ? $"Focused and locked Scene View on '{targetGameObject.name}'."
                        : $"Focused Scene View on '{targetGameObject.name}'.",
                    target = new
                    {
                        name = targetGameObject.name,
                        instanceID = targetGameObject.GetInstanceID(),
                        path = gameObjectPath
                    },
                    locked = lockView
                };
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    error = $"Error focusing on target: {ex.Message}"
                };
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Resolves a GameObject by instance ID, path, or name.
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
                var resolvedObject = EditorUtility.InstanceIDToObject(instanceId);
                if (resolvedObject is GameObject gameObject)
                {
                    return gameObject;
                }
                if (resolvedObject is Component component)
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
                var names = new System.Collections.Generic.Stack<string>();
                Transform currentTransform = gameObject.transform;
                while (currentTransform != null)
                {
                    names.Push(currentTransform.name);
                    currentTransform = currentTransform.parent;
                }
                return string.Join("/", names);
            }
            catch
            {
                return gameObject.name;
            }
        }

        #endregion
    }
}
