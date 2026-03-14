using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// Handles component operations on GameObjects including add, remove, and set_property actions.
    /// </summary>
    [MCPTool("manage_component", "Manages components: add, remove, set_property, or inspect on GameObjects. Use action='inspect' first to discover available properties before set_property. Requires a target instance ID from find_gameobject.", Category = "Component")]
    public static class ManageComponents
    {
        #region Actions

        /// <summary>
        /// Adds a component to a GameObject, optionally setting initial properties.
        /// </summary>
        [MCPAction("add", Description = "Add a component to a GameObject")]
        public static object Add(
            [MCPParam("target", "Instance ID (int) or name/path (string) to identify target GameObject", required: true)] string target,
            [MCPParam("component_type", "The component type name (e.g., 'Rigidbody', 'BoxCollider')", required: true)] string componentType,
            [MCPParam("properties", "Object mapping property names to values (for multiple properties)")] object properties = null,
            [MCPParam("search_method", "How to find the target: by_id, by_name, by_path (default: auto-detect)")] string searchMethod = null)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("Target parameter is required.");
            }

            if (string.IsNullOrEmpty(componentType))
            {
                throw MCPException.InvalidParams("Component_type parameter is required.");
            }

            try
            {
                return HandleAdd(target, componentType, properties, searchMethod);
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
                    error = $"Error executing action 'add': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Removes a component from a GameObject.
        /// </summary>
        [MCPAction("remove", Description = "Remove a component from a GameObject", DestructiveHint = true)]
        public static object Remove(
            [MCPParam("target", "Instance ID (int) or name/path (string) to identify target GameObject", required: true)] string target,
            [MCPParam("component_type", "The component type name (e.g., 'Rigidbody', 'BoxCollider')", required: true)] string componentType,
            [MCPParam("search_method", "How to find the target: by_id, by_name, by_path (default: auto-detect)")] string searchMethod = null)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("Target parameter is required.");
            }

            if (string.IsNullOrEmpty(componentType))
            {
                throw MCPException.InvalidParams("Component_type parameter is required.");
            }

            try
            {
                return HandleRemove(target, componentType, searchMethod);
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
                    error = $"Error executing action 'remove': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Sets one or more properties on a component.
        /// </summary>
        [MCPAction("set_property", Description = "Set properties on a component")]
        public static object SetProperty(
            [MCPParam("target", "Instance ID (int) or name/path (string) to identify target GameObject", required: true)] string target,
            [MCPParam("component_type", "The component type name (e.g., 'Rigidbody', 'BoxCollider')", required: true)] string componentType,
            [MCPParam("property", "Single property name to set (for set_property action)")] string property = null,
            [MCPParam("value", "Value for the single property (for set_property action)")] object value = null,
            [MCPParam("properties", "Object mapping property names to values (for multiple properties)")] object properties = null,
            [MCPParam("search_method", "How to find the target: by_id, by_name, by_path (default: auto-detect)")] string searchMethod = null)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("Target parameter is required.");
            }

            if (string.IsNullOrEmpty(componentType))
            {
                throw MCPException.InvalidParams("Component_type parameter is required.");
            }

            try
            {
                return HandleSetProperty(target, componentType, property, value, properties, searchMethod);
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
                    error = $"Error executing action 'set_property': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Inspects a component, listing all serialized properties and their values.
        /// </summary>
        [MCPAction("inspect", Description = "List all serialized properties on a component", ReadOnlyHint = true)]
        public static object Inspect(
            [MCPParam("target", "Instance ID (int) or name/path (string) to identify target GameObject", required: true)] string target,
            [MCPParam("component_type", "The component type name (e.g., 'Rigidbody', 'BoxCollider')", required: true)] string componentType,
            [MCPParam("search_method", "How to find the target: by_id, by_name, by_path (default: auto-detect)")] string searchMethod = null)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("Target parameter is required.");
            }

            if (string.IsNullOrEmpty(componentType))
            {
                throw MCPException.InvalidParams("Component_type parameter is required.");
            }

            try
            {
                return HandleInspect(target, componentType, searchMethod);
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
                    error = $"Error executing action 'inspect': {exception.Message}"
                };
            }
        }

        #endregion

        #region Action Handlers

        /// <summary>
        /// Handles the add action - adds a component to a GameObject.
        /// </summary>
        private static object HandleAdd(string target, string componentTypeName, object initialProperties, string searchMethod)
        {
            GameObject targetGameObject = FindGameObject(target, searchMethod);
            if (targetGameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"Target GameObject '{target}' not found."
                };
            }

            Type componentType = SerializedPropertyHelper.ResolveComponentType(componentTypeName);
            if (componentType == null)
            {
                return new
                {
                    success = false,
                    error = $"Component type '{componentTypeName}' not found."
                };
            }

            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                return new
                {
                    success = false,
                    error = $"Type '{componentTypeName}' is not a Component."
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
            var conflictResult = CheckPhysicsConflicts(targetGameObject, componentType, componentTypeName);
            if (conflictResult != null)
            {
                return conflictResult;
            }

            try
            {
                Component newComponent = Undo.AddComponent(targetGameObject, componentType);
                if (newComponent == null)
                {
                    // Undo.AddComponent may return null in Unity 6 even when the component was added
                    newComponent = targetGameObject.GetComponent(componentType);
                    if (newComponent == null)
                    {
                        return new
                        {
                            success = false,
                            error = $"Failed to add component '{componentTypeName}' to '{targetGameObject.name}'."
                        };
                    }
                }

                // Set initial properties if provided
                var propertyResults = new List<object>();
                if (initialProperties != null)
                {
                    var propertiesDict = ConvertToPropertiesDictionary(initialProperties);
                    if (propertiesDict != null && propertiesDict.Count > 0)
                    {
                        Undo.RecordObject(newComponent, $"Set initial properties on {componentTypeName}");
                        propertyResults = SetPropertiesOnComponent(newComponent, propertiesDict);
                    }
                }

                EditorUtility.SetDirty(targetGameObject);

                return new
                {
                    success = true,
                    message = $"Added component '{componentTypeName}' to '{targetGameObject.name}'.",
                    gameObject = targetGameObject.name,
                    instanceID = targetGameObject.GetInstanceID(),
                    componentType = componentTypeName,
                    componentInstanceID = newComponent.GetInstanceID(),
                    propertyResults = propertyResults.Count > 0 ? propertyResults : null
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error adding component '{componentTypeName}': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Handles the remove action - removes a component from a GameObject.
        /// </summary>
        private static object HandleRemove(string target, string componentTypeName, string searchMethod)
        {
            GameObject targetGameObject = FindGameObject(target, searchMethod);
            if (targetGameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"Target GameObject '{target}' not found."
                };
            }

            Type componentType = SerializedPropertyHelper.ResolveComponentType(componentTypeName);
            if (componentType == null)
            {
                return new
                {
                    success = false,
                    error = $"Component type '{componentTypeName}' not found."
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
                    error = $"Component '{componentTypeName}' not found on '{targetGameObject.name}'."
                };
            }

            try
            {
                int componentInstanceId = componentToRemove.GetInstanceID();
                Undo.DestroyObjectImmediate(componentToRemove);

                EditorUtility.SetDirty(targetGameObject);

                return new
                {
                    success = true,
                    message = $"Removed component '{componentTypeName}' from '{targetGameObject.name}'.",
                    gameObject = targetGameObject.name,
                    instanceID = targetGameObject.GetInstanceID(),
                    removedComponentType = componentTypeName,
                    removedComponentInstanceID = componentInstanceId
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error removing component '{componentTypeName}': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Handles the set_property action - sets properties on a component.
        /// </summary>
        private static object HandleSetProperty(
            string target,
            string componentTypeName,
            string singleProperty,
            object singleValue,
            object multipleProperties,
            string searchMethod)
        {
            GameObject targetGameObject = FindGameObject(target, searchMethod);
            if (targetGameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"Target GameObject '{target}' not found."
                };
            }

            Type componentType = SerializedPropertyHelper.ResolveComponentType(componentTypeName);
            if (componentType == null)
            {
                return new
                {
                    success = false,
                    error = $"Component type '{componentTypeName}' not found."
                };
            }

            Component component = targetGameObject.GetComponent(componentType);
            if (component == null)
            {
                return new
                {
                    success = false,
                    error = $"Component '{componentTypeName}' not found on '{targetGameObject.name}'."
                };
            }

            // Build properties dictionary from either single or multiple mode
            var propertiesToSet = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(singleProperty))
            {
                propertiesToSet[singleProperty] = singleValue;
            }

            if (multipleProperties != null)
            {
                var multiDict = ConvertToPropertiesDictionary(multipleProperties);
                if (multiDict != null)
                {
                    foreach (var kvp in multiDict)
                    {
                        propertiesToSet[kvp.Key] = kvp.Value;
                    }
                }
            }

            if (propertiesToSet.Count == 0)
            {
                return new
                {
                    success = false,
                    error = "No properties specified. Use 'property' + 'value' for single property or 'properties' for multiple."
                };
            }

            Undo.RecordObject(component, $"Set properties on {componentTypeName}");

            var results = SetPropertiesOnComponent(component, propertiesToSet);

            EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(targetGameObject);

            int successCount = results.Count(r => r is Dictionary<string, object> dict && dict.ContainsKey("success") && (bool)dict["success"]);
            int failCount = results.Count - successCount;

            string message = failCount == 0
                ? $"Successfully set {successCount} property(ies) on '{componentTypeName}'."
                : $"Set {successCount} property(ies), {failCount} failed on '{componentTypeName}'.";

            return new
            {
                success = failCount == 0,
                message,
                gameObject = targetGameObject.name,
                instanceID = targetGameObject.GetInstanceID(),
                componentType = componentTypeName,
                componentInstanceID = component.GetInstanceID(),
                propertyResults = results
            };
        }

        /// <summary>
        /// Handles the inspect action - lists all serialized properties on a component.
        /// </summary>
        private static object HandleInspect(string target, string componentTypeName, string searchMethod)
        {
            GameObject targetGameObject = FindGameObject(target, searchMethod);
            if (targetGameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"Target GameObject '{target}' not found."
                };
            }

            Type componentType = SerializedPropertyHelper.ResolveComponentType(componentTypeName);
            if (componentType == null)
            {
                return new
                {
                    success = false,
                    error = $"Component type '{componentTypeName}' not found."
                };
            }

            Component component = targetGameObject.GetComponent(componentType);
            if (component == null)
            {
                return new
                {
                    success = false,
                    error = $"Component '{componentTypeName}' not found on '{targetGameObject.name}'."
                };
            }

            try
            {
                var serializedObject = new SerializedObject(component);
                var properties = new List<Dictionary<string, object>>();

                // Iterate through all visible serialized properties
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;

                while (iterator.NextVisible(enterChildren))
                {
                    // Skip the script reference property (m_Script)
                    if (iterator.name == "m_Script")
                    {
                        enterChildren = false;
                        continue;
                    }

                    var propertyInfo = new Dictionary<string, object>
                    {
                        { "path", iterator.propertyPath },
                        { "type", iterator.type }
                    };

                    // Serialize the property value
                    object serializedValue = SerializedPropertyHelper.SerializePropertyValue(iterator);
                    propertyInfo["value"] = serializedValue;

                    // Add isObjectReference flag for object reference properties
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference ||
                        iterator.propertyType == SerializedPropertyType.ExposedReference)
                    {
                        propertyInfo["isObjectReference"] = true;
                    }

                    properties.Add(propertyInfo);

                    // Don't enter children - we handle them via SerializePropertyValue for nested types
                    enterChildren = false;
                }

                int totalProperties = properties.Count;
                bool truncated = totalProperties > 50;
                if (truncated)
                {
                    properties = properties.Take(50).ToList();
                }

                return new
                {
                    success = true,
                    component = componentTypeName,
                    gameObject = new
                    {
                        name = targetGameObject.name,
                        instanceId = targetGameObject.GetInstanceID()
                    },
                    totalProperties,
                    truncated,
                    note = truncated ? $"Showing 50 of {totalProperties} properties. Use set_property to access specific properties." : null,
                    properties
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error inspecting component '{componentTypeName}': {exception.Message}"
                };
            }
        }

        #endregion

        #region Helper Methods - Property Setting

        /// <summary>
        /// Sets multiple properties on a component. Tries SerializedProperty first (handles
        /// serialized paths like "m_Volume" from inspect), then falls back to reflection
        /// (handles public C# property names like "volume").
        /// </summary>
        private static List<object> SetPropertiesOnComponent(Component component, Dictionary<string, object> properties)
        {
            var results = new List<object>();
            Type componentType = component.GetType();
            var serializedObject = new SerializedObject(component);

            foreach (var kvp in properties)
            {
                string propertyName = kvp.Key;
                object propertyValue = kvp.Value;

                try
                {
                    // Try SerializedProperty first (handles serialized paths like "m_Volume")
                    SerializedProperty serializedProperty = serializedObject.FindProperty(propertyName);
                    if (serializedProperty != null)
                    {
                        if (SerializedPropertyHelper.SetSerializedPropertyValue(serializedProperty, propertyValue))
                        {
                            serializedObject.ApplyModifiedProperties();
                            results.Add(new Dictionary<string, object>
                            {
                                { "property", propertyName },
                                { "success", true },
                                { "memberType", "serializedProperty" }
                            });
                            continue;
                        }
                    }

                    // Fall back to reflection: try C# property
                    PropertyInfo propertyInfo = componentType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                    if (propertyInfo != null && propertyInfo.CanWrite)
                    {
                        object convertedValue = ConvertValueToType(propertyValue, propertyInfo.PropertyType);
                        propertyInfo.SetValue(component, convertedValue);
                        results.Add(new Dictionary<string, object>
                        {
                            { "property", propertyName },
                            { "success", true },
                            { "memberType", "property" }
                        });
                        continue;
                    }

                    // Fall back to reflection: try C# field
                    FieldInfo fieldInfo = componentType.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public);
                    if (fieldInfo != null && !fieldInfo.IsInitOnly)
                    {
                        object convertedValue = ConvertValueToType(propertyValue, fieldInfo.FieldType);
                        fieldInfo.SetValue(component, convertedValue);
                        results.Add(new Dictionary<string, object>
                        {
                            { "property", propertyName },
                            { "success", true },
                            { "memberType", "field" }
                        });
                        continue;
                    }

                    // Not found via any method
                    results.Add(new Dictionary<string, object>
                    {
                        { "property", propertyName },
                        { "success", false },
                        { "error", $"Property or field '{propertyName}' not found or is read-only on {componentType.Name}." }
                    });
                }
                catch (Exception exception)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "property", propertyName },
                        { "success", false },
                        { "error", $"Failed to set '{propertyName}': {exception.Message}" }
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Converts a value to the target type, handling common Unity types.
        /// Supports $ref syntax for object references (scene objects, assets, components).
        /// </summary>
        private static object ConvertValueToType(object value, Type targetType)
        {
            if (value == null)
            {
                return GetDefaultValue(targetType);
            }

            // Handle nullable types
            Type underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                targetType = underlyingType;
            }

            // Check for $ref syntax early (object references)
            if (SerializedPropertyHelper.IsObjectReference(value))
            {
                return SerializedPropertyHelper.ResolveObjectReference((Dictionary<string, object>)value, targetType);
            }

            // Handle arrays of references
            if (targetType.IsArray && value is List<object> arrayList)
            {
                Type elementType = targetType.GetElementType();
                // Check if any element uses $ref syntax
                bool hasReferences = arrayList.Any(item => SerializedPropertyHelper.IsObjectReference(item));
                if (hasReferences || typeof(UnityEngine.Object).IsAssignableFrom(elementType))
                {
                    var resultArray = Array.CreateInstance(elementType, arrayList.Count);
                    for (int i = 0; i < arrayList.Count; i++)
                    {
                        object element = arrayList[i];
                        object convertedElement;
                        if (SerializedPropertyHelper.IsObjectReference(element))
                        {
                            convertedElement = SerializedPropertyHelper.ResolveObjectReference((Dictionary<string, object>)element, elementType);
                        }
                        else
                        {
                            convertedElement = ConvertValueToType(element, elementType);
                        }
                        resultArray.SetValue(convertedElement, i);
                    }
                    return resultArray;
                }
            }

            // Direct assignment if types match
            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            // Vector3 conversion
            if (targetType == typeof(Vector3))
            {
                return SerializedPropertyHelper.ParseVector3(value) ?? Vector3.zero;
            }

            // Vector2 conversion
            if (targetType == typeof(Vector2))
            {
                return SerializedPropertyHelper.ParseVector2(value) ?? Vector2.zero;
            }

            // Color conversion
            if (targetType == typeof(Color))
            {
                return SerializedPropertyHelper.ParseColor(value) ?? Color.white;
            }

            // Quaternion conversion
            if (targetType == typeof(Quaternion))
            {
                var euler = SerializedPropertyHelper.ParseVector3(value);
                return euler.HasValue ? Quaternion.Euler(euler.Value) : Quaternion.identity;
            }

            // Boolean conversion
            if (targetType == typeof(bool))
            {
                if (value is bool boolValue)
                {
                    return boolValue;
                }
                if (value is string stringValue)
                {
                    return bool.Parse(stringValue);
                }
                return Convert.ToBoolean(value);
            }

            // Integer conversion
            if (targetType == typeof(int))
            {
                return Convert.ToInt32(value);
            }

            // Float conversion
            if (targetType == typeof(float))
            {
                return Convert.ToSingle(value);
            }

            // Double conversion
            if (targetType == typeof(double))
            {
                return Convert.ToDouble(value);
            }

            // String conversion
            if (targetType == typeof(string))
            {
                return value.ToString();
            }

            // Enum conversion
            if (targetType.IsEnum)
            {
                if (value is string enumString)
                {
                    return Enum.Parse(targetType, enumString, ignoreCase: true);
                }
                return Enum.ToObject(targetType, Convert.ToInt32(value));
            }

            // LayerMask conversion
            if (targetType == typeof(LayerMask))
            {
                if (value is string layerName)
                {
                    return (LayerMask)LayerMask.GetMask(layerName);
                }
                return (LayerMask)Convert.ToInt32(value);
            }

            // Fallback to Convert.ChangeType
            return Convert.ChangeType(value, targetType);
        }

        /// <summary>
        /// Converts input to a properties dictionary.
        /// </summary>
        private static Dictionary<string, object> ConvertToPropertiesDictionary(object input)
        {
            if (input == null)
            {
                return null;
            }

            if (input is Dictionary<string, object> dict)
            {
                return dict;
            }

            // Handle other dictionary types
            if (input is System.Collections.IDictionary iDict)
            {
                var result = new Dictionary<string, object>();
                foreach (var key in iDict.Keys)
                {
                    result[key.ToString()] = iDict[key];
                }
                return result;
            }

            return null;
        }

        /// <summary>
        /// Gets the default value for a type.
        /// </summary>
        private static object GetDefaultValue(Type type)
        {
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }
            return null;
        }

        #endregion

        #region Helper Methods - GameObject Finding

        /// <summary>
        /// Finds a GameObject by instance ID, name, or path based on the search method.
        /// </summary>
        private static GameObject FindGameObject(string target, string searchMethod, bool searchInactive = true)
        {
            if (string.IsNullOrEmpty(target))
            {
                return null;
            }

            string normalizedMethod = (searchMethod ?? "").ToLowerInvariant().Trim();

            // Auto-detect search method if not specified
            if (string.IsNullOrEmpty(normalizedMethod))
            {
                if (int.TryParse(target, out _))
                {
                    normalizedMethod = "by_id";
                }
                else if (target.Contains("/"))
                {
                    normalizedMethod = "by_path";
                }
                else
                {
                    normalizedMethod = "by_name";
                }
            }

            Scene activeScene = GetActiveScene();

            switch (normalizedMethod)
            {
                case "by_id":
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
                    return null;

                case "by_path":
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
                    return null;

                case "by_name":
                default:
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

        #region Helper Methods - Misc

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
        /// Checks for 2D/3D physics component conflicts.
        /// </summary>
        private static object CheckPhysicsConflicts(GameObject targetGameObject, Type componentType, string componentTypeName)
        {
            bool isAdding2D = typeof(Rigidbody2D).IsAssignableFrom(componentType) || typeof(Collider2D).IsAssignableFrom(componentType);
            bool isAdding3D = typeof(Rigidbody).IsAssignableFrom(componentType) || typeof(Collider).IsAssignableFrom(componentType);

            if (isAdding2D)
            {
                if (targetGameObject.GetComponent<Rigidbody>() != null || targetGameObject.GetComponent<Collider>() != null)
                {
                    return new
                    {
                        success = false,
                        error = $"Cannot add 2D physics component '{componentTypeName}' - GameObject has 3D physics components."
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
                        error = $"Cannot add 3D physics component '{componentTypeName}' - GameObject has 2D physics components."
                    };
                }
            }

            return null;
        }

        #endregion
    }
}
