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

#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Handles component operations on GameObjects including add, remove, and set_property actions.
    /// </summary>
    public static class ManageComponents
    {
        #region Constants

        /// <summary>
        /// Maximum recursion depth for property serialization to prevent infinite loops.
        /// </summary>
        private const int MaxSerializationDepth = 10;

        /// <summary>
        /// Maximum number of array elements to serialize before truncation.
        /// </summary>
        private const int MaxSerializedArrayElements = 100;

        #endregion

        #region Main Tool Entry Point

        /// <summary>
        /// Manages components on GameObjects with add, remove, set_property, and inspect actions.
        /// </summary>
        [MCPTool("component_manage", "Manages components: add, remove, set_property, or inspect on GameObjects", Category = "Component", DestructiveHint = true)]
        public static object Manage(
            [MCPParam("action", "Action to perform: add, remove, set_property, inspect", required: true, Enum = new[] { "add", "remove", "set_property", "inspect" })] string action,
            [MCPParam("target", "Instance ID (int) or name/path (string) to identify target GameObject", required: true)] string target,
            [MCPParam("component_type", "The component type name (e.g., 'Rigidbody', 'BoxCollider')", required: true)] string componentType,
            [MCPParam("property", "Single property name to set (for set_property action)")] string property = null,
            [MCPParam("value", "Value for the single property (for set_property action)")] object value = null,
            [MCPParam("properties", "Object mapping property names to values (for multiple properties)")] object properties = null,
            [MCPParam("search_method", "How to find the target: by_id, by_name, by_path (default: auto-detect)")] string searchMethod = null)
        {
            if (string.IsNullOrEmpty(action))
            {
                throw MCPException.InvalidParams("Action parameter is required.");
            }

            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("Target parameter is required.");
            }

            if (string.IsNullOrEmpty(componentType))
            {
                throw MCPException.InvalidParams("Component_type parameter is required.");
            }

            string normalizedAction = action.ToLowerInvariant();

            try
            {
                return normalizedAction switch
                {
                    "add" => HandleAdd(target, componentType, properties, searchMethod),
                    "remove" => HandleRemove(target, componentType, searchMethod),
                    "set_property" => HandleSetProperty(target, componentType, property, value, properties, searchMethod),
                    "inspect" => HandleInspect(target, componentType, searchMethod),
                    _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: add, remove, set_property, inspect")
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

            Type componentType = ResolveComponentType(componentTypeName);
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

            Type componentType = ResolveComponentType(componentTypeName);
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

            Type componentType = ResolveComponentType(componentTypeName);
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

            Type componentType = ResolveComponentType(componentTypeName);
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
                    object serializedValue = SerializePropertyValue(iterator);
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
                        if (SetSerializedPropertyValue(serializedProperty, propertyValue))
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
        /// Sets a SerializedProperty value from a generic object. Returns true on success.
        /// </summary>
        private static bool SetSerializedPropertyValue(SerializedProperty property, object value)
        {
            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        property.intValue = Convert.ToInt32(value);
                        return true;

                    case SerializedPropertyType.Float:
                        property.floatValue = Convert.ToSingle(value);
                        return true;

                    case SerializedPropertyType.Boolean:
                        property.boolValue = Convert.ToBoolean(value);
                        return true;

                    case SerializedPropertyType.String:
                        property.stringValue = value?.ToString() ?? "";
                        return true;

                    case SerializedPropertyType.Enum:
                        if (value is string enumString)
                        {
                            int enumIndex = Array.FindIndex(property.enumNames,
                                n => n.Equals(enumString, StringComparison.OrdinalIgnoreCase));
                            if (enumIndex >= 0)
                            {
                                property.enumValueIndex = enumIndex;
                                return true;
                            }
                        }
                        property.enumValueIndex = Convert.ToInt32(value);
                        return true;

                    case SerializedPropertyType.Vector2:
                        var vector2 = ParseVector2(value);
                        if (vector2.HasValue) { property.vector2Value = vector2.Value; return true; }
                        return false;

                    case SerializedPropertyType.Vector3:
                        var vector3 = ParseVector3(value);
                        if (vector3.HasValue) { property.vector3Value = vector3.Value; return true; }
                        return false;

                    case SerializedPropertyType.Vector2Int:
                        var v2i = ParseVector2(value);
                        if (v2i.HasValue) { property.vector2IntValue = new Vector2Int((int)v2i.Value.x, (int)v2i.Value.y); return true; }
                        return false;

                    case SerializedPropertyType.Vector3Int:
                        var v3i = ParseVector3(value);
                        if (v3i.HasValue) { property.vector3IntValue = new Vector3Int((int)v3i.Value.x, (int)v3i.Value.y, (int)v3i.Value.z); return true; }
                        return false;

                    case SerializedPropertyType.Color:
                        var color = ParseColor(value);
                        if (color.HasValue) { property.colorValue = color.Value; return true; }
                        return false;

                    case SerializedPropertyType.Quaternion:
                        var euler = ParseVector3(value);
                        if (euler.HasValue) { property.quaternionValue = Quaternion.Euler(euler.Value); return true; }
                        return false;

                    case SerializedPropertyType.LayerMask:
                        if (value is string layerName)
                        {
                            property.intValue = LayerMask.GetMask(layerName);
                        }
                        else
                        {
                            property.intValue = Convert.ToInt32(value);
                        }
                        return true;

                    case SerializedPropertyType.ObjectReference:
                        if (value == null)
                        {
                            property.objectReferenceValue = null;
                            return true;
                        }
                        if (IsObjectReference(value))
                        {
                            var resolved = ResolveObjectReference((Dictionary<string, object>)value, typeof(UnityEngine.Object));
                            property.objectReferenceValue = resolved;
                            return true;
                        }
                        return false;

                    default:
                        // Unsupported type - fall back to reflection
                        return false;
                }
            }
            catch
            {
                return false;
            }
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
            if (IsObjectReference(value))
            {
                return ResolveObjectReference((Dictionary<string, object>)value, targetType);
            }

            // Handle arrays of references
            if (targetType.IsArray && value is List<object> arrayList)
            {
                Type elementType = targetType.GetElementType();
                // Check if any element uses $ref syntax
                bool hasReferences = arrayList.Any(item => IsObjectReference(item));
                if (hasReferences || typeof(UnityEngine.Object).IsAssignableFrom(elementType))
                {
                    var resultArray = Array.CreateInstance(elementType, arrayList.Count);
                    for (int i = 0; i < arrayList.Count; i++)
                    {
                        object element = arrayList[i];
                        object convertedElement;
                        if (IsObjectReference(element))
                        {
                            convertedElement = ResolveObjectReference((Dictionary<string, object>)element, elementType);
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
                return ParseVector3(value) ?? Vector3.zero;
            }

            // Vector2 conversion
            if (targetType == typeof(Vector2))
            {
                return ParseVector2(value) ?? Vector2.zero;
            }

            // Color conversion
            if (targetType == typeof(Color))
            {
                return ParseColor(value) ?? Color.white;
            }

            // Quaternion conversion
            if (targetType == typeof(Quaternion))
            {
                var euler = ParseVector3(value);
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
        /// Parses a Vector3 from various input formats.
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
                Debug.LogWarning($"[ManageComponents] Failed to parse Vector3: {exception.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a Vector2 from various input formats.
        /// </summary>
        private static Vector2? ParseVector2(object input)
        {
            if (input == null)
            {
                return null;
            }

            try
            {
                // Handle List<object> (from JSON array)
                if (input is List<object> list && list.Count >= 2)
                {
                    return new Vector2(
                        Convert.ToSingle(list[0]),
                        Convert.ToSingle(list[1])
                    );
                }

                // Handle Dictionary<string, object> (from JSON object)
                if (input is Dictionary<string, object> dict)
                {
                    if (dict.TryGetValue("x", out object xValue) &&
                        dict.TryGetValue("y", out object yValue))
                    {
                        return new Vector2(
                            Convert.ToSingle(xValue),
                            Convert.ToSingle(yValue)
                        );
                    }
                }

                // Handle array types
                if (input is object[] array && array.Length >= 2)
                {
                    return new Vector2(
                        Convert.ToSingle(array[0]),
                        Convert.ToSingle(array[1])
                    );
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageComponents] Failed to parse Vector2: {exception.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a Color from various input formats.
        /// </summary>
        private static Color? ParseColor(object input)
        {
            if (input == null)
            {
                return null;
            }

            try
            {
                // Handle List<object> (from JSON array [r,g,b] or [r,g,b,a])
                if (input is List<object> list && list.Count >= 3)
                {
                    float red = Convert.ToSingle(list[0]);
                    float green = Convert.ToSingle(list[1]);
                    float blue = Convert.ToSingle(list[2]);
                    float alpha = list.Count >= 4 ? Convert.ToSingle(list[3]) : 1f;
                    return new Color(red, green, blue, alpha);
                }

                // Handle Dictionary<string, object> (from JSON object {r,g,b,a})
                if (input is Dictionary<string, object> dict)
                {
                    if (dict.TryGetValue("r", out object rValue) &&
                        dict.TryGetValue("g", out object gValue) &&
                        dict.TryGetValue("b", out object bValue))
                    {
                        float red = Convert.ToSingle(rValue);
                        float green = Convert.ToSingle(gValue);
                        float blue = Convert.ToSingle(bValue);
                        float alpha = dict.TryGetValue("a", out object aValue) ? Convert.ToSingle(aValue) : 1f;
                        return new Color(red, green, blue, alpha);
                    }
                }

                // Handle array types
                if (input is object[] array && array.Length >= 3)
                {
                    float red = Convert.ToSingle(array[0]);
                    float green = Convert.ToSingle(array[1]);
                    float blue = Convert.ToSingle(array[2]);
                    float alpha = array.Length >= 4 ? Convert.ToSingle(array[3]) : 1f;
                    return new Color(red, green, blue, alpha);
                }

                // Handle string color names or hex
                if (input is string colorString)
                {
                    if (ColorUtility.TryParseHtmlString(colorString, out Color parsedColor))
                    {
                        return parsedColor;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageComponents] Failed to parse Color: {exception.Message}");
            }

            return null;
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

        /// <summary>
        /// Checks if the value is an object reference using the $ref syntax.
        /// Object references are dictionaries containing a "$ref" key.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if the value is an object reference dictionary with a $ref key.</returns>
        private static bool IsObjectReference(object value)
        {
            if (value is Dictionary<string, object> dict)
            {
                return dict.ContainsKey("$ref");
            }
            return false;
        }

        /// <summary>
        /// Resolves an object reference from a $ref dictionary to a Unity Object.
        /// Supports instance IDs (integers) and asset paths (strings starting with "Assets/").
        /// Optionally retrieves a specific component using the $component key.
        /// </summary>
        /// <param name="refDict">The reference dictionary containing $ref and optional $component.</param>
        /// <param name="expectedType">The expected type of the resolved object.</param>
        /// <returns>The resolved Unity Object, or throws an exception with details on failure.</returns>
        private static UnityEngine.Object ResolveObjectReference(Dictionary<string, object> refDict, Type expectedType)
        {
            if (!refDict.TryGetValue("$ref", out object refValue))
            {
                throw new ArgumentException("Object reference dictionary must contain a '$ref' key.");
            }

            UnityEngine.Object resolvedObject = null;
            string refDescription = "";

            // Resolve by instance ID (integer)
            if (refValue is int instanceId)
            {
                resolvedObject = EditorUtility.InstanceIDToObject(instanceId);
                refDescription = $"instance {instanceId}";
                if (resolvedObject == null)
                {
                    throw new ArgumentException($"No object found with instance ID {instanceId}.");
                }
            }
            else if (refValue is long longId)
            {
                // Handle JSON deserialization which may produce long instead of int
                int intId = (int)longId;
                resolvedObject = EditorUtility.InstanceIDToObject(intId);
                refDescription = $"instance {intId}";
                if (resolvedObject == null)
                {
                    throw new ArgumentException($"No object found with instance ID {intId}.");
                }
            }
            // Resolve by asset path (string starting with "Assets/")
            else if (refValue is string assetPath)
            {
                if (!assetPath.StartsWith("Assets/"))
                {
                    throw new ArgumentException($"Asset path must start with 'Assets/', got: '{assetPath}'.");
                }
                resolvedObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                refDescription = $"asset '{assetPath}'";
                if (resolvedObject == null)
                {
                    throw new ArgumentException($"No asset found at path '{assetPath}'.");
                }
            }
            else
            {
                throw new ArgumentException($"$ref value must be an integer (instance ID) or string (asset path), got: {refValue?.GetType().Name ?? "null"}.");
            }

            // Handle $component to get a specific component from the resolved object
            if (refDict.TryGetValue("$component", out object componentValue) && componentValue is string componentTypeName)
            {
                GameObject gameObject = null;

                // If the resolved object is a GameObject, use it directly
                if (resolvedObject is GameObject go)
                {
                    gameObject = go;
                }
                // If the resolved object is a Component, get its GameObject
                else if (resolvedObject is Component comp)
                {
                    gameObject = comp.gameObject;
                }
                // If the resolved object is a prefab asset, get its root GameObject
                else
                {
                    // Try to get a GameObject from a prefab asset
                    string assetPath = AssetDatabase.GetAssetPath(resolvedObject);
                    if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".prefab"))
                    {
                        gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    }
                }

                if (gameObject == null)
                {
                    throw new ArgumentException($"Cannot get component '{componentTypeName}' from {refDescription}: resolved object is not a GameObject or Component.");
                }

                Type componentType = ResolveComponentType(componentTypeName);
                if (componentType == null)
                {
                    throw new ArgumentException($"Component type '{componentTypeName}' not found.");
                }

                Component foundComponent = gameObject.GetComponent(componentType);
                if (foundComponent == null)
                {
                    throw new ArgumentException($"Component '{componentTypeName}' not found on {refDescription} (GameObject: '{gameObject.name}').");
                }

                resolvedObject = foundComponent;
                refDescription = $"{componentTypeName} on {refDescription}";
            }

            // Validate the resolved object is assignable to the expected type
            if (!expectedType.IsAssignableFrom(resolvedObject.GetType()))
            {
                throw new ArgumentException($"Cannot assign {resolvedObject.GetType().Name} ({refDescription}) to property expecting {expectedType.Name}.");
            }

            return resolvedObject;
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

        #region Helper Methods - Property Serialization

        /// <summary>
        /// Serializes a SerializedProperty value to a JSON-friendly object.
        /// Handles primitives, Unity types, object references, and nested structures.
        /// </summary>
        /// <param name="property">The SerializedProperty to serialize.</param>
        /// <param name="depth">Current recursion depth.</param>
        /// <param name="maxDepth">Maximum recursion depth to prevent infinite loops.</param>
        /// <returns>A JSON-friendly object representation of the property value.</returns>
        private static object SerializePropertyValue(SerializedProperty property, int depth = 0, int maxDepth = MaxSerializationDepth)
        {
            if (property == null)
            {
                return null;
            }

            // Prevent infinite recursion
            if (depth > maxDepth)
            {
                return new Dictionary<string, object>
                {
                    { "$truncated", true },
                    { "$reason", "Max depth exceeded" }
                };
            }

            switch (property.propertyType)
            {
                // Primitive types
                case SerializedPropertyType.Integer:
                    return property.intValue;

                case SerializedPropertyType.Float:
                    return property.floatValue;

                case SerializedPropertyType.Boolean:
                    return property.boolValue;

                case SerializedPropertyType.String:
                    return property.stringValue;

                case SerializedPropertyType.Character:
                    return property.intValue > 0 ? ((char)property.intValue).ToString() : "";

                // Enum type
                case SerializedPropertyType.Enum:
                    return new Dictionary<string, object>
                    {
                        { "index", property.enumValueIndex },
                        { "value", property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
                            ? property.enumNames[property.enumValueIndex]
                            : property.enumValueIndex.ToString() },
                        { "options", property.enumNames }
                    };

                // Unity vector types
                case SerializedPropertyType.Vector2:
                    return new Dictionary<string, object>
                    {
                        { "x", property.vector2Value.x },
                        { "y", property.vector2Value.y }
                    };

                case SerializedPropertyType.Vector3:
                    return new Dictionary<string, object>
                    {
                        { "x", property.vector3Value.x },
                        { "y", property.vector3Value.y },
                        { "z", property.vector3Value.z }
                    };

                case SerializedPropertyType.Vector4:
                    return new Dictionary<string, object>
                    {
                        { "x", property.vector4Value.x },
                        { "y", property.vector4Value.y },
                        { "z", property.vector4Value.z },
                        { "w", property.vector4Value.w }
                    };

                case SerializedPropertyType.Vector2Int:
                    return new Dictionary<string, object>
                    {
                        { "x", property.vector2IntValue.x },
                        { "y", property.vector2IntValue.y }
                    };

                case SerializedPropertyType.Vector3Int:
                    return new Dictionary<string, object>
                    {
                        { "x", property.vector3IntValue.x },
                        { "y", property.vector3IntValue.y },
                        { "z", property.vector3IntValue.z }
                    };

                // Quaternion - expose as euler angles for readability
                case SerializedPropertyType.Quaternion:
                    var euler = property.quaternionValue.eulerAngles;
                    return new Dictionary<string, object>
                    {
                        { "x", property.quaternionValue.x },
                        { "y", property.quaternionValue.y },
                        { "z", property.quaternionValue.z },
                        { "w", property.quaternionValue.w },
                        { "eulerAngles", new Dictionary<string, object>
                            {
                                { "x", euler.x },
                                { "y", euler.y },
                                { "z", euler.z }
                            }
                        }
                    };

                // Color
                case SerializedPropertyType.Color:
                    return new Dictionary<string, object>
                    {
                        { "r", property.colorValue.r },
                        { "g", property.colorValue.g },
                        { "b", property.colorValue.b },
                        { "a", property.colorValue.a },
                        { "hex", ColorUtility.ToHtmlStringRGBA(property.colorValue) }
                    };

                // Rect types
                case SerializedPropertyType.Rect:
                    return new Dictionary<string, object>
                    {
                        { "x", property.rectValue.x },
                        { "y", property.rectValue.y },
                        { "width", property.rectValue.width },
                        { "height", property.rectValue.height }
                    };

                case SerializedPropertyType.RectInt:
                    return new Dictionary<string, object>
                    {
                        { "x", property.rectIntValue.x },
                        { "y", property.rectIntValue.y },
                        { "width", property.rectIntValue.width },
                        { "height", property.rectIntValue.height }
                    };

                // Bounds types
                case SerializedPropertyType.Bounds:
                    return new Dictionary<string, object>
                    {
                        { "center", new Dictionary<string, object>
                            {
                                { "x", property.boundsValue.center.x },
                                { "y", property.boundsValue.center.y },
                                { "z", property.boundsValue.center.z }
                            }
                        },
                        { "size", new Dictionary<string, object>
                            {
                                { "x", property.boundsValue.size.x },
                                { "y", property.boundsValue.size.y },
                                { "z", property.boundsValue.size.z }
                            }
                        }
                    };

                case SerializedPropertyType.BoundsInt:
                    return new Dictionary<string, object>
                    {
                        { "position", new Dictionary<string, object>
                            {
                                { "x", property.boundsIntValue.position.x },
                                { "y", property.boundsIntValue.position.y },
                                { "z", property.boundsIntValue.position.z }
                            }
                        },
                        { "size", new Dictionary<string, object>
                            {
                                { "x", property.boundsIntValue.size.x },
                                { "y", property.boundsIntValue.size.y },
                                { "z", property.boundsIntValue.size.z }
                            }
                        }
                    };

                // LayerMask
                case SerializedPropertyType.LayerMask:
                    int layerMaskValue = property.intValue;
                    var layerNames = new List<string>();
                    for (int i = 0; i < 32; i++)
                    {
                        if ((layerMaskValue & (1 << i)) != 0)
                        {
                            string layerName = LayerMask.LayerToName(i);
                            if (!string.IsNullOrEmpty(layerName))
                            {
                                layerNames.Add(layerName);
                            }
                        }
                    }
                    return new Dictionary<string, object>
                    {
                        { "value", layerMaskValue },
                        { "layers", layerNames }
                    };

                // AnimationCurve
                case SerializedPropertyType.AnimationCurve:
                    var curve = property.animationCurveValue;
                    var keyframes = new List<Dictionary<string, object>>();
                    if (curve != null)
                    {
                        foreach (var key in curve.keys)
                        {
                            keyframes.Add(new Dictionary<string, object>
                            {
                                { "time", key.time },
                                { "value", key.value },
                                { "inTangent", key.inTangent },
                                { "outTangent", key.outTangent }
                            });
                        }
                    }
                    return new Dictionary<string, object>
                    {
                        { "keyCount", curve?.length ?? 0 },
                        { "keys", keyframes }
                    };

                // Gradient (stored as generic, requires special handling)
                case SerializedPropertyType.Gradient:
                    // Gradients can't be directly accessed via SerializedProperty
                    // Return a placeholder indicating the type
                    return new Dictionary<string, object>
                    {
                        { "$type", "Gradient" },
                        { "$note", "Gradient values require reflection to access" }
                    };

                // Object reference - the key type for this task
                case SerializedPropertyType.ObjectReference:
                    return SerializeObjectReference(property);

                // Exposed reference (similar to object reference)
                case SerializedPropertyType.ExposedReference:
                    var exposedRef = property.exposedReferenceValue;
                    if (exposedRef == null)
                    {
                        return null;
                    }
                    return SerializeUnityObject(exposedRef);

                // Array size (special property for arrays)
                case SerializedPropertyType.ArraySize:
                    return property.intValue;

                // Fixed buffer size
                case SerializedPropertyType.FixedBufferSize:
                    return property.fixedBufferSize;

                // Generic - nested object/struct, need to iterate children
                case SerializedPropertyType.Generic:
                    return SerializeGenericProperty(property, depth, maxDepth);

                // Managed reference (Unity 2019.3+)
                case SerializedPropertyType.ManagedReference:
                    return SerializeManagedReference(property, depth, maxDepth);

                // Hash128
                case SerializedPropertyType.Hash128:
                    return property.hash128Value.ToString();

                default:
                    return new Dictionary<string, object>
                    {
                        { "$type", property.propertyType.ToString() },
                        { "$unsupported", true }
                    };
            }
        }

        /// <summary>
        /// Serializes a Unity Object reference to a JSON-friendly format.
        /// Returns null for null references, or a reference dictionary for non-null objects.
        /// The isObjectReference flag is added at the property level by the inspect handler.
        /// </summary>
        private static object SerializeObjectReference(SerializedProperty property)
        {
            var objectRef = property.objectReferenceValue;
            if (objectRef == null)
            {
                return null;
            }

            return SerializeUnityObject(objectRef);
        }

        /// <summary>
        /// Serializes a Unity Object to a compact reference format with $ref (instance ID),
        /// $name, and $type for identification without additional lookups.
        /// </summary>
        private static Dictionary<string, object> SerializeUnityObject(UnityEngine.Object unityObject)
        {
            if (unityObject == null)
            {
                return null;
            }

            return new Dictionary<string, object>
            {
                { "$ref", unityObject.GetInstanceID() },
                { "$name", unityObject.name },
                { "$type", unityObject.GetType().Name }
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

        /// <summary>
        /// Serializes a generic (nested) property by iterating its children.
        /// </summary>
        private static object SerializeGenericProperty(SerializedProperty property, int depth, int maxDepth)
        {
            // Handle arrays specially
            if (property.isArray)
            {
                return SerializeArrayProperty(property, depth, maxDepth);
            }

            // For non-array generics (structs/nested objects), iterate children
            var result = new Dictionary<string, object>
            {
                { "$type", property.type }
            };

            var iterator = property.Copy();
            var endProperty = property.GetEndProperty();

            // Enter the first child
            if (!iterator.NextVisible(true))
            {
                return result;
            }

            // Iterate through all visible children
            do
            {
                // Check if we've passed the end of this property's children
                if (SerializedProperty.EqualContents(iterator, endProperty))
                {
                    break;
                }

                string childName = iterator.name;
                result[childName] = SerializePropertyValue(iterator, depth + 1, maxDepth);
            }
            while (iterator.NextVisible(false));

            return result;
        }

        /// <summary>
        /// Serializes an array property.
        /// </summary>
        private static object SerializeArrayProperty(SerializedProperty property, int depth, int maxDepth)
        {
            int arraySize = property.arraySize;

            // For very large arrays, truncate and indicate
            bool truncated = arraySize > MaxSerializedArrayElements;
            int elementsToSerialize = truncated ? MaxSerializedArrayElements : arraySize;

            var elements = new List<object>();
            for (int i = 0; i < elementsToSerialize; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                elements.Add(SerializePropertyValue(element, depth + 1, maxDepth));
            }

            var result = new Dictionary<string, object>
            {
                { "$isArray", true },
                { "length", arraySize },
                { "elements", elements }
            };

            if (truncated)
            {
                result["$truncated"] = true;
                result["$truncatedAt"] = MaxSerializedArrayElements;
            }

            return result;
        }

        /// <summary>
        /// Serializes a managed reference property (Unity 2019.3+).
        /// </summary>
        private static object SerializeManagedReference(SerializedProperty property, int depth, int maxDepth)
        {
            // Get the managed reference type info
            string typeName = property.managedReferenceFullTypename;

            if (string.IsNullOrEmpty(typeName))
            {
                return null; // Null managed reference
            }

            var result = new Dictionary<string, object>
            {
                { "$managedReferenceType", typeName }
            };

            // Iterate children like a generic property
            var iterator = property.Copy();
            var endProperty = property.GetEndProperty();

            if (!iterator.NextVisible(true))
            {
                return result;
            }

            do
            {
                if (SerializedProperty.EqualContents(iterator, endProperty))
                {
                    break;
                }

                string childName = iterator.name;
                result[childName] = SerializePropertyValue(iterator, depth + 1, maxDepth);
            }
            while (iterator.NextVisible(false));

            return result;
        }

        #endregion

        #region Helper Methods - Component Type Resolution

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
