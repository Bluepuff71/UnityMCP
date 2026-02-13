using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Handles GameObject manipulation operations including create, modify, delete, duplicate, and move_relative.
    /// </summary>
    public static class ManageGameObject
    {
        private const string UntaggedTag = "Untagged";

        #region Main Tool Entry Point

        /// <summary>
        /// Manages GameObjects in the scene with create, modify, delete, duplicate, and move_relative actions.
        /// </summary>
        [MCPTool("gameobject_manage", "Manages GameObjects: create, modify, delete, duplicate, or move_relative", Category = "GameObject", DestructiveHint = true)]
        public static object Manage(
            [MCPParam("action", "Action to perform: create, modify, delete, duplicate, move_relative", required: true, Enum = new[] { "create", "modify", "delete", "duplicate", "move_relative" })] string action,
            [MCPParam("target", "Instance ID (int) or name/path (string) to identify target GameObject")] string target = null,
            [MCPParam("name", "Name for new object (create) or rename target (modify)")] string name = null,
            [MCPParam("parent", "Instance ID or name/path of parent GameObject")] string parent = null,
            [MCPParam("position", "Local position as [x,y,z] array or {x,y,z} object")] object position = null,
            [MCPParam("rotation", "Local euler angles as [x,y,z] array or {x,y,z} object")] object rotation = null,
            [MCPParam("scale", "Local scale as [x,y,z] array or {x,y,z} object")] object scale = null,
            [MCPParam("setActive", "Activate or deactivate the GameObject")] bool? setActive = null,
            [MCPParam("tag", "Tag to assign to the GameObject")] string tag = null,
            [MCPParam("layer", "Layer name to assign to the GameObject")] string layer = null,
            [MCPParam("primitiveType", "Primitive type for create: Cube, Sphere, Capsule, Cylinder, Plane, Quad")] string primitiveType = null,
            [MCPParam("prefabPath", "Path to prefab asset for instantiation")] string prefabPath = null,
            [MCPParam("componentsToAdd", "Array of component type names to add")] List<object> componentsToAdd = null,
            [MCPParam("componentsToRemove", "Array of component type names to remove")] List<string> componentsToRemove = null,
            [MCPParam("new_name", "New name for duplicated object")] string newName = null,
            [MCPParam("offset", "Position offset as [x,y,z] for duplicate or move_relative")] object offset = null,
            [MCPParam("reference_object", "Reference object for move_relative action")] string referenceObject = null,
            [MCPParam("direction", "Direction for move_relative: left, right, up, down, forward, back")] string direction = null,
            [MCPParam("distance", "Distance for move_relative")] float distance = 1f,
            [MCPParam("world_space", "Use world space for move_relative (default: true)")] bool worldSpace = true)
        {
            if (string.IsNullOrEmpty(action))
            {
                throw MCPException.InvalidParams("Action parameter is required.");
            }

            string normalizedAction = action.ToLowerInvariant();

            // Usability improvement: alias 'name' to 'target' for modification actions
            if (string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(name) && normalizedAction != "create")
            {
                target = name;
            }

            try
            {
                return normalizedAction switch
                {
                    "create" => HandleCreate(name, parent, position, rotation, scale, tag, layer, primitiveType, prefabPath, componentsToAdd),
                    "modify" => HandleModify(target, name, parent, position, rotation, scale, setActive, tag, layer, componentsToAdd, componentsToRemove),
                    "delete" => HandleDelete(target),
                    "duplicate" => HandleDuplicate(target, newName, position, offset, parent),
                    "move_relative" => HandleMoveRelative(target, referenceObject, direction, distance, offset, worldSpace),
                    _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: create, modify, delete, duplicate, move_relative")
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
        /// Handles the create action - creates a new GameObject.
        /// </summary>
        private static object HandleCreate(
            string name,
            string parentTarget,
            object position,
            object rotation,
            object scale,
            string tag,
            string layer,
            string primitiveType,
            string prefabPath,
            List<object> componentsToAdd)
        {
            GameObject newGameObject = null;
            bool createdNewObject = false;

            // Try prefab instantiation first
            if (!string.IsNullOrEmpty(prefabPath))
            {
                string resolvedPath = ResolvePrefabPath(prefabPath);
                if (resolvedPath == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Prefab not found: '{prefabPath}'"
                    };
                }

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(resolvedPath);
                if (prefabAsset == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Asset at path '{resolvedPath}' is not a valid GameObject."
                    };
                }

                try
                {
                    newGameObject = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
                    if (newGameObject == null)
                    {
                        return new
                        {
                            success = false,
                            error = $"Failed to instantiate prefab at '{resolvedPath}'."
                        };
                    }

                    if (!string.IsNullOrEmpty(name))
                    {
                        newGameObject.name = name;
                    }

                    Undo.RegisterCreatedObjectUndo(newGameObject, $"Instantiate Prefab '{prefabAsset.name}'");
                }
                catch (Exception exception)
                {
                    return new
                    {
                        success = false,
                        error = $"Error instantiating prefab '{resolvedPath}': {exception.Message}"
                    };
                }
            }

            // Fallback: create primitive or empty GameObject
            if (newGameObject == null)
            {
                if (string.IsNullOrEmpty(name))
                {
                    throw MCPException.InvalidParams("'name' parameter is required for create action when not instantiating a prefab.");
                }

                if (!string.IsNullOrEmpty(primitiveType))
                {
                    if (!Enum.TryParse(primitiveType, true, out PrimitiveType parsedType))
                    {
                        string validTypes = string.Join(", ", Enum.GetNames(typeof(PrimitiveType)));
                        throw MCPException.InvalidParams($"Invalid primitive type: '{primitiveType}'. Valid types: {validTypes}");
                    }

                    newGameObject = ObjectFactory.CreatePrimitive(parsedType);
                    newGameObject.name = name;
                }
                else
                {
                    newGameObject = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(newGameObject, $"Create GameObject '{name}'");
                }

                createdNewObject = true;
            }

            Undo.RecordObject(newGameObject.transform, "Set GameObject Transform");
            Undo.RecordObject(newGameObject, "Set GameObject Properties");

            // Set parent
            if (!string.IsNullOrEmpty(parentTarget))
            {
                GameObject parentGameObject = FindGameObject(parentTarget);
                if (parentGameObject == null)
                {
                    UnityEngine.Object.DestroyImmediate(newGameObject);
                    return new
                    {
                        success = false,
                        error = $"Parent '{parentTarget}' not found."
                    };
                }
                newGameObject.transform.SetParent(parentGameObject.transform, true);
            }

            // Set transform
            Vector3? parsedPosition = ParseVector3(position);
            Vector3? parsedRotation = ParseVector3(rotation);
            Vector3? parsedScale = ParseVector3(scale);

            if (parsedPosition.HasValue)
            {
                newGameObject.transform.localPosition = parsedPosition.Value;
            }
            if (parsedRotation.HasValue)
            {
                newGameObject.transform.localEulerAngles = parsedRotation.Value;
            }
            if (parsedScale.HasValue)
            {
                newGameObject.transform.localScale = parsedScale.Value;
            }

            // Set tag
            if (!string.IsNullOrEmpty(tag))
            {
                if (!SetTag(newGameObject, tag, out string tagError))
                {
                    UnityEngine.Object.DestroyImmediate(newGameObject);
                    return new
                    {
                        success = false,
                        error = tagError
                    };
                }
            }

            // Set layer
            if (!string.IsNullOrEmpty(layer))
            {
                if (!SetLayer(newGameObject, layer, out string layerError))
                {
                    Debug.LogWarning($"[ManageGameObject] {layerError}");
                }
            }

            // Add components
            if (componentsToAdd != null)
            {
                foreach (var componentSpec in componentsToAdd)
                {
                    string typeName = ExtractComponentTypeName(componentSpec);
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var addResult = AddComponent(newGameObject, typeName);
                        if (addResult != null)
                        {
                            UnityEngine.Object.DestroyImmediate(newGameObject);
                            return addResult;
                        }
                    }
                }
            }

            EditorUtility.SetDirty(newGameObject);
            EditorUtility.SetDirty(newGameObject.transform);
            Selection.activeGameObject = newGameObject;

            string successMessage = createdNewObject
                ? $"GameObject '{newGameObject.name}' created successfully."
                : $"Prefab instantiated as '{newGameObject.name}'.";

            return new
            {
                success = true,
                message = successMessage,
                gameObject = BuildGameObjectData(newGameObject)
            };
        }

        /// <summary>
        /// Handles the modify action - modifies an existing GameObject.
        /// </summary>
        private static object HandleModify(
            string target,
            string name,
            string parentTarget,
            object position,
            object rotation,
            object scale,
            bool? setActive,
            string tag,
            string layer,
            List<object> componentsToAdd,
            List<string> componentsToRemove)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for modify action.");
            }

            // When setActive=true, search for inactive objects
            bool searchInactive = setActive == true;
            GameObject targetGameObject = FindGameObject(target, searchInactive);

            if (targetGameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"Target GameObject '{target}' not found."
                };
            }

            Undo.RecordObject(targetGameObject.transform, "Modify GameObject Transform");
            Undo.RecordObject(targetGameObject, "Modify GameObject Properties");

            bool modified = false;

            // Rename
            if (!string.IsNullOrEmpty(name) && targetGameObject.name != name)
            {
                targetGameObject.name = name;
                modified = true;
            }

            // Set parent
            if (!string.IsNullOrEmpty(parentTarget))
            {
                GameObject newParent = FindGameObject(parentTarget);
                if (newParent == null)
                {
                    return new
                    {
                        success = false,
                        error = $"New parent '{parentTarget}' not found."
                    };
                }

                if (newParent.transform.IsChildOf(targetGameObject.transform))
                {
                    return new
                    {
                        success = false,
                        error = $"Cannot parent '{targetGameObject.name}' to '{newParent.name}' - would create hierarchy loop."
                    };
                }

                if (targetGameObject.transform.parent != newParent.transform)
                {
                    targetGameObject.transform.SetParent(newParent.transform, true);
                    modified = true;
                }
            }

            // Set active
            if (setActive.HasValue && targetGameObject.activeSelf != setActive.Value)
            {
                targetGameObject.SetActive(setActive.Value);
                modified = true;
            }

            // Set tag
            if (!string.IsNullOrEmpty(tag))
            {
                if (!SetTag(targetGameObject, tag, out string tagError))
                {
                    return new
                    {
                        success = false,
                        error = tagError
                    };
                }
                modified = true;
            }

            // Set layer
            if (!string.IsNullOrEmpty(layer))
            {
                if (!SetLayer(targetGameObject, layer, out string layerError))
                {
                    return new
                    {
                        success = false,
                        error = layerError
                    };
                }
                modified = true;
            }

            // Set transform
            Vector3? parsedPosition = ParseVector3(position);
            Vector3? parsedRotation = ParseVector3(rotation);
            Vector3? parsedScale = ParseVector3(scale);

            if (parsedPosition.HasValue && targetGameObject.transform.localPosition != parsedPosition.Value)
            {
                targetGameObject.transform.localPosition = parsedPosition.Value;
                modified = true;
            }
            if (parsedRotation.HasValue && targetGameObject.transform.localEulerAngles != parsedRotation.Value)
            {
                targetGameObject.transform.localEulerAngles = parsedRotation.Value;
                modified = true;
            }
            if (parsedScale.HasValue && targetGameObject.transform.localScale != parsedScale.Value)
            {
                targetGameObject.transform.localScale = parsedScale.Value;
                modified = true;
            }

            // Remove components
            if (componentsToRemove != null)
            {
                foreach (string typeName in componentsToRemove)
                {
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var removeResult = RemoveComponent(targetGameObject, typeName);
                        if (removeResult != null)
                        {
                            return removeResult;
                        }
                        modified = true;
                    }
                }
            }

            // Add components
            if (componentsToAdd != null)
            {
                foreach (var componentSpec in componentsToAdd)
                {
                    string typeName = ExtractComponentTypeName(componentSpec);
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var addResult = AddComponent(targetGameObject, typeName);
                        if (addResult != null)
                        {
                            return addResult;
                        }
                        modified = true;
                    }
                }
            }

            if (!modified)
            {
                return new
                {
                    success = true,
                    message = $"No modifications applied to GameObject '{targetGameObject.name}'.",
                    gameObject = BuildGameObjectData(targetGameObject)
                };
            }

            EditorUtility.SetDirty(targetGameObject);
            EditorUtility.SetDirty(targetGameObject.transform);

            return new
            {
                success = true,
                message = $"GameObject '{targetGameObject.name}' modified successfully.",
                gameObject = BuildGameObjectData(targetGameObject)
            };
        }

        /// <summary>
        /// Handles the delete action - deletes GameObjects.
        /// </summary>
        private static object HandleDelete(string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for delete action.");
            }

            List<GameObject> targets = FindGameObjects(target, searchInactive: true);

            if (targets.Count == 0)
            {
                return new
                {
                    success = false,
                    error = $"Target GameObject(s) '{target}' not found."
                };
            }

            var deletedObjects = new List<object>();
            foreach (var targetGameObject in targets)
            {
                if (targetGameObject != null)
                {
                    string gameObjectName = targetGameObject.name;
                    int instanceId = targetGameObject.GetInstanceID();
                    Undo.DestroyObjectImmediate(targetGameObject);
                    deletedObjects.Add(new { name = gameObjectName, instanceID = instanceId });
                }
            }

            if (deletedObjects.Count == 0)
            {
                return new
                {
                    success = false,
                    error = "Failed to delete target GameObject(s)."
                };
            }

            string message = deletedObjects.Count == 1
                ? $"GameObject deleted successfully."
                : $"{deletedObjects.Count} GameObjects deleted successfully.";

            return new
            {
                success = true,
                message,
                deleted = deletedObjects
            };
        }

        /// <summary>
        /// Handles the duplicate action - duplicates a GameObject.
        /// </summary>
        private static object HandleDuplicate(
            string target,
            string newName,
            object position,
            object offset,
            string parentTarget)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for duplicate action.");
            }

            GameObject sourceGameObject = FindGameObject(target);
            if (sourceGameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"Target GameObject '{target}' not found."
                };
            }

            GameObject duplicatedGameObject = UnityEngine.Object.Instantiate(sourceGameObject);
            Undo.RegisterCreatedObjectUndo(duplicatedGameObject, $"Duplicate {sourceGameObject.name}");

            // Set name
            if (!string.IsNullOrEmpty(newName))
            {
                duplicatedGameObject.name = newName;
            }
            else
            {
                duplicatedGameObject.name = sourceGameObject.name.Replace("(Clone)", "").Trim() + "_Copy";
            }

            // Set position
            Vector3? parsedPosition = ParseVector3(position);
            Vector3? parsedOffset = ParseVector3(offset);

            if (parsedPosition.HasValue)
            {
                duplicatedGameObject.transform.position = parsedPosition.Value;
            }
            else if (parsedOffset.HasValue)
            {
                duplicatedGameObject.transform.position = sourceGameObject.transform.position + parsedOffset.Value;
            }

            // Set parent
            if (!string.IsNullOrEmpty(parentTarget))
            {
                GameObject newParent = FindGameObject(parentTarget);
                if (newParent != null)
                {
                    duplicatedGameObject.transform.SetParent(newParent.transform, true);
                }
                else
                {
                    Debug.LogWarning($"[ManageGameObject] Parent '{parentTarget}' not found. Object will remain at root level.");
                }
            }
            else
            {
                duplicatedGameObject.transform.SetParent(sourceGameObject.transform.parent, true);
            }

            EditorUtility.SetDirty(duplicatedGameObject);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = duplicatedGameObject;

            return new
            {
                success = true,
                message = $"Duplicated '{sourceGameObject.name}' as '{duplicatedGameObject.name}'.",
                originalName = sourceGameObject.name,
                originalId = sourceGameObject.GetInstanceID(),
                duplicatedObject = BuildGameObjectData(duplicatedGameObject)
            };
        }

        /// <summary>
        /// Handles the move_relative action - moves a GameObject relative to a reference object.
        /// </summary>
        private static object HandleMoveRelative(
            string target,
            string referenceTarget,
            string direction,
            float distance,
            object offset,
            bool worldSpace)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for move_relative action.");
            }

            if (string.IsNullOrEmpty(referenceTarget))
            {
                throw MCPException.InvalidParams("'reference_object' parameter is required for move_relative action.");
            }

            GameObject targetGameObject = FindGameObject(target);
            if (targetGameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"Target GameObject '{target}' not found."
                };
            }

            GameObject referenceGameObject = FindGameObject(referenceTarget);
            if (referenceGameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"Reference object '{referenceTarget}' not found."
                };
            }

            Vector3? customOffset = ParseVector3(offset);

            if (!customOffset.HasValue && string.IsNullOrEmpty(direction))
            {
                throw MCPException.InvalidParams("Either 'direction' or 'offset' parameter is required for move_relative action.");
            }

            Undo.RecordObject(targetGameObject.transform, $"Move {targetGameObject.name} relative to {referenceGameObject.name}");

            Vector3 newPosition;

            if (customOffset.HasValue)
            {
                if (worldSpace)
                {
                    newPosition = referenceGameObject.transform.position + customOffset.Value;
                }
                else
                {
                    newPosition = referenceGameObject.transform.TransformPoint(customOffset.Value);
                }
            }
            else
            {
                Vector3 directionVector = GetDirectionVector(direction, referenceGameObject.transform, worldSpace);
                newPosition = referenceGameObject.transform.position + directionVector * distance;
            }

            targetGameObject.transform.position = newPosition;

            EditorUtility.SetDirty(targetGameObject);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            return new
            {
                success = true,
                message = $"Moved '{targetGameObject.name}' relative to '{referenceGameObject.name}'.",
                movedObject = targetGameObject.name,
                referenceObject = referenceGameObject.name,
                newPosition = new[] { newPosition.x, newPosition.y, newPosition.z },
                direction,
                distance,
                gameObject = BuildGameObjectData(targetGameObject)
            };
        }

        #endregion

        #region Helper Methods - GameObject Finding

        /// <summary>
        /// Finds a single GameObject by instance ID, name, or path.
        /// </summary>
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

        /// <summary>
        /// Finds multiple GameObjects matching the target.
        /// </summary>
        private static List<GameObject> FindGameObjects(string target, bool searchInactive = true)
        {
            var results = new List<GameObject>();

            if (string.IsNullOrEmpty(target))
            {
                return results;
            }

            // Try instance ID first (single result)
            if (int.TryParse(target, out int instanceId))
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId);
                if (obj is GameObject gameObject)
                {
                    results.Add(gameObject);
                }
                else if (obj is Component component)
                {
                    results.Add(component.gameObject);
                }
                return results;
            }

            // Try path-based lookup (single result)
            if (target.Contains("/"))
            {
                Scene activeScene = GetActiveScene();
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
                        results.Add(root);
                        return results;
                    }

                    if (target.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        var found = root.transform.Find(target.Substring(rootPath.Length + 1));
                        if (found != null)
                        {
                            results.Add(found.gameObject);
                            return results;
                        }
                    }
                }
            }

            // Find all matching by name
            var allObjects = GetAllSceneObjects(searchInactive);
            foreach (var gameObject in allObjects)
            {
                if (gameObject != null && gameObject.name.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(gameObject);
                }
            }

            return results;
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

        #endregion

        #region Helper Methods - Vector Parsing

        /// <summary>
        /// Parses a Vector3 from various input formats.
        /// Supports: [x,y,z] array, {x,y,z} object, List&lt;object&gt;, Dictionary&lt;string,object&gt;
        /// </summary>
        private static Vector3? ParseVector3(object input)
        {
            if (input == null)
            {
                return null;
            }

            try
            {
                // Handle List<object> (from JSON array)
                if (input is List<object> list && list.Count >= 3)
                {
                    return new Vector3(
                        Convert.ToSingle(list[0]),
                        Convert.ToSingle(list[1]),
                        Convert.ToSingle(list[2])
                    );
                }

                // Handle Dictionary<string, object> (from JSON object)
                if (input is Dictionary<string, object> dict)
                {
                    if (dict.TryGetValue("x", out object xValue) &&
                        dict.TryGetValue("y", out object yValue) &&
                        dict.TryGetValue("z", out object zValue))
                    {
                        return new Vector3(
                            Convert.ToSingle(xValue),
                            Convert.ToSingle(yValue),
                            Convert.ToSingle(zValue)
                        );
                    }
                }

                // Handle array types
                if (input is object[] array && array.Length >= 3)
                {
                    return new Vector3(
                        Convert.ToSingle(array[0]),
                        Convert.ToSingle(array[1]),
                        Convert.ToSingle(array[2])
                    );
                }

                // Handle double[] or float[]
                if (input is double[] doubleArray && doubleArray.Length >= 3)
                {
                    return new Vector3(
                        (float)doubleArray[0],
                        (float)doubleArray[1],
                        (float)doubleArray[2]
                    );
                }

                if (input is float[] floatArray && floatArray.Length >= 3)
                {
                    return new Vector3(floatArray[0], floatArray[1], floatArray[2]);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageGameObject] Failed to parse Vector3: {exception.Message}");
            }

            return null;
        }

        #endregion

        #region Helper Methods - Component Management

        /// <summary>
        /// Extracts the component type name from various input formats.
        /// </summary>
        private static string ExtractComponentTypeName(object componentSpec)
        {
            if (componentSpec is string typeName)
            {
                return typeName;
            }

            if (componentSpec is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("typeName", out object typeNameValue))
                {
                    return typeNameValue?.ToString();
                }
                if (dict.TryGetValue("type", out object typeValue))
                {
                    return typeValue?.ToString();
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a component to a GameObject.
        /// </summary>
        private static object AddComponent(GameObject targetGameObject, string typeName)
        {
            Type componentType = ResolveComponentType(typeName);
            if (componentType == null)
            {
                return new
                {
                    success = false,
                    error = $"Component type '{typeName}' not found."
                };
            }

            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                return new
                {
                    success = false,
                    error = $"Type '{typeName}' is not a Component."
                };
            }

            if (componentType == typeof(Transform))
            {
                return new
                {
                    success = false,
                    error = "Cannot add another Transform component."
                };
            }

            // Check for 2D/3D physics conflicts
            bool isAdding2D = typeof(Rigidbody2D).IsAssignableFrom(componentType) || typeof(Collider2D).IsAssignableFrom(componentType);
            bool isAdding3D = typeof(Rigidbody).IsAssignableFrom(componentType) || typeof(Collider).IsAssignableFrom(componentType);

            if (isAdding2D)
            {
                if (targetGameObject.GetComponent<Rigidbody>() != null || targetGameObject.GetComponent<Collider>() != null)
                {
                    return new
                    {
                        success = false,
                        error = $"Cannot add 2D physics component '{typeName}' - GameObject has 3D physics components."
                    };
                }
            }
            else if (isAdding3D)
            {
                if (targetGameObject.GetComponent<Rigidbody2D>() != null || targetGameObject.GetComponent<Collider2D>() != null)
                {
                    return new
                    {
                        success = false,
                        error = $"Cannot add 3D physics component '{typeName}' - GameObject has 2D physics components."
                    };
                }
            }

            try
            {
                Component newComponent = Undo.AddComponent(targetGameObject, componentType);
                if (newComponent == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to add component '{typeName}' to '{targetGameObject.name}'."
                    };
                }

                return null; // Success
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error adding component '{typeName}': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Removes a component from a GameObject.
        /// </summary>
        private static object RemoveComponent(GameObject targetGameObject, string typeName)
        {
            Type componentType = ResolveComponentType(typeName);
            if (componentType == null)
            {
                return new
                {
                    success = false,
                    error = $"Component type '{typeName}' not found."
                };
            }

            if (componentType == typeof(Transform))
            {
                return new
                {
                    success = false,
                    error = "Cannot remove the Transform component."
                };
            }

            Component componentToRemove = targetGameObject.GetComponent(componentType);
            if (componentToRemove == null)
            {
                return new
                {
                    success = false,
                    error = $"Component '{typeName}' not found on '{targetGameObject.name}'."
                };
            }

            try
            {
                Undo.DestroyObjectImmediate(componentToRemove);
                return null; // Success
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error removing component '{typeName}': {exception.Message}"
                };
            }
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

        #endregion

        #region Helper Methods - Tag and Layer

        /// <summary>
        /// Sets the tag on a GameObject, creating it if necessary.
        /// </summary>
        private static bool SetTag(GameObject gameObject, string tag, out string errorMessage)
        {
            errorMessage = null;
            string tagToSet = string.IsNullOrEmpty(tag) ? UntaggedTag : tag;

            if (tagToSet != UntaggedTag && !InternalEditorUtility.tags.Contains(tagToSet))
            {
                try
                {
                    InternalEditorUtility.AddTag(tagToSet);
                }
                catch (Exception exception)
                {
                    errorMessage = $"Failed to create tag '{tagToSet}': {exception.Message}";
                    return false;
                }
            }

            try
            {
                gameObject.tag = tagToSet;
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = $"Failed to set tag '{tagToSet}': {exception.Message}";
                return false;
            }
        }

        /// <summary>
        /// Sets the layer on a GameObject.
        /// </summary>
        private static bool SetLayer(GameObject gameObject, string layer, out string errorMessage)
        {
            errorMessage = null;
            int layerId = LayerMask.NameToLayer(layer);

            if (layerId == -1)
            {
                errorMessage = $"Layer '{layer}' not found.";
                return false;
            }

            gameObject.layer = layerId;
            return true;
        }

        #endregion

        #region Helper Methods - Prefab Resolution

        /// <summary>
        /// Model file extensions that can be instantiated as GameObjects.
        /// </summary>
        private static readonly HashSet<string> s_modelExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".fbx", ".obj", ".blend", ".dae", ".3ds", ".max", ".ma", ".mb", ".gltf", ".glb"
        };

        /// <summary>
        /// Resolves a prefab or model path, handling name-only and partial paths.
        /// Supports both .prefab files and model files (.fbx, .obj, etc.).
        /// </summary>
        private static string ResolvePrefabPath(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath))
            {
                return null;
            }

            string extension = System.IO.Path.GetExtension(prefabPath);

            // Check if it's a model file extension - these are valid as-is
            if (s_modelExtensions.Contains(extension))
            {
                // Model file with full path - check if it exists
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                {
                    return prefabPath;
                }
                return null;
            }

            // If it's just a name without path, search for the prefab
            if (!prefabPath.Contains("/") && (string.IsNullOrEmpty(extension) || extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase)))
            {
                string prefabName = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                string[] guids = AssetDatabase.FindAssets($"t:Prefab {prefabName}");

                if (guids.Length == 0)
                {
                    return null;
                }
                if (guids.Length == 1)
                {
                    return AssetDatabase.GUIDToAssetPath(guids[0]);
                }

                // Multiple matches - try exact name match
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (fileName.Equals(prefabName, StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }

                // Return first match
                return AssetDatabase.GUIDToAssetPath(guids[0]);
            }

            // Append .prefab extension if missing (only for non-model files)
            if (!extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                prefabPath += ".prefab";
            }

            // Check if the asset exists at the given path
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                return prefabPath;
            }

            return null;
        }

        #endregion

        #region Helper Methods - Direction Vectors

        /// <summary>
        /// Gets a direction vector based on a direction string.
        /// </summary>
        private static Vector3 GetDirectionVector(string direction, Transform referenceTransform, bool useWorldSpace)
        {
            string normalizedDirection = direction?.ToLowerInvariant() ?? "forward";

            if (useWorldSpace)
            {
                return normalizedDirection switch
                {
                    "right" => Vector3.right,
                    "left" => Vector3.left,
                    "up" => Vector3.up,
                    "down" => Vector3.down,
                    "forward" or "front" => Vector3.forward,
                    "back" or "backward" or "behind" => Vector3.back,
                    _ => LogUnknownDirection(normalizedDirection, Vector3.forward)
                };
            }

            return normalizedDirection switch
            {
                "right" => referenceTransform.right,
                "left" => -referenceTransform.right,
                "up" => referenceTransform.up,
                "down" => -referenceTransform.up,
                "forward" or "front" => referenceTransform.forward,
                "back" or "backward" or "behind" => -referenceTransform.forward,
                _ => LogUnknownDirection(normalizedDirection, referenceTransform.forward)
            };
        }

        /// <summary>
        /// Logs a warning for unknown direction and returns the fallback.
        /// </summary>
        private static Vector3 LogUnknownDirection(string direction, Vector3 fallback)
        {
            Debug.LogWarning($"[ManageGameObject] Unknown direction '{direction}', defaulting to forward.");
            return fallback;
        }

        #endregion

        #region Helper Methods - GameObject Data Building

        /// <summary>
        /// Builds a data object representing a GameObject's state.
        /// </summary>
        private static object BuildGameObjectData(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

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
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageGameObject] Error getting components: {exception.Message}");
            }

            return new
            {
                name = gameObject.name,
                instanceID = gameObject.GetInstanceID(),
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                tag = gameObject.tag,
                layer = gameObject.layer,
                layerName = LayerMask.LayerToName(gameObject.layer),
                path = GetGameObjectPath(gameObject),
                transform = new
                {
                    localPosition = new[] { gameObject.transform.localPosition.x, gameObject.transform.localPosition.y, gameObject.transform.localPosition.z },
                    localRotation = new[] { gameObject.transform.localEulerAngles.x, gameObject.transform.localEulerAngles.y, gameObject.transform.localEulerAngles.z },
                    localScale = new[] { gameObject.transform.localScale.x, gameObject.transform.localScale.y, gameObject.transform.localScale.z }
                },
                componentTypes
            };
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

        #endregion
    }
}
