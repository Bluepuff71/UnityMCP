using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Utilities
{
    /// <summary>
    /// Provides shared helpers for reading and writing SerializedProperty values.
    /// Extracted from ManageComponents so that ManageSettings, ManageInputActions,
    /// and other tools can reuse the same serialization logic.
    /// </summary>
    public static class SerializedPropertyHelper
    {
        #region Constants

        /// <summary>
        /// Maximum recursion depth for property serialization to prevent infinite loops.
        /// </summary>
        public const int MaxSerializationDepth = 10;

        /// <summary>
        /// Maximum number of array elements to serialize before truncation.
        /// </summary>
        public const int MaxSerializedArrayElements = 100;

        #endregion

        #region Serialization (Read)

        /// <summary>
        /// Serializes a SerializedProperty value to a JSON-friendly object.
        /// Handles primitives, Unity types, object references, and nested structures.
        /// </summary>
        /// <param name="property">The SerializedProperty to serialize.</param>
        /// <param name="depth">Current recursion depth.</param>
        /// <param name="maxDepth">Maximum recursion depth to prevent infinite loops.</param>
        /// <returns>A JSON-friendly object representation of the property value.</returns>
        public static object SerializePropertyValue(SerializedProperty property, int depth = 0, int maxDepth = MaxSerializationDepth)
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
        public static Dictionary<string, object> SerializeUnityObject(UnityEngine.Object unityObject)
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
        /// Serializes a generic (nested) property by iterating its children.
        /// </summary>
        private static object SerializeGenericProperty(SerializedProperty property, int depth, int maxDepth)
        {
            // Handle arrays specially
            if (property.isArray)
            {
                return SerializeArrayProperty(property, depth, maxDepth);
            }

            // Handle fixed-size buffers (e.g. m_LayerCollisionMatrix is uint[32])
            if (property.isFixedBuffer)
            {
                return SerializeFixedBufferProperty(property, depth, maxDepth);
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
        /// Serializes a fixed-size buffer property (e.g. uint[32] layer collision matrix).
        /// Uses GetFixedBufferElementAtIndex to read each element.
        /// </summary>
        private static object SerializeFixedBufferProperty(SerializedProperty property, int depth, int maxDepth)
        {
            int bufferSize = property.fixedBufferSize;
            bool truncated = bufferSize > MaxSerializedArrayElements;
            int elementsToSerialize = truncated ? MaxSerializedArrayElements : bufferSize;

            var elements = new List<object>();
            for (int i = 0; i < elementsToSerialize; i++)
            {
                var element = property.GetFixedBufferElementAtIndex(i);
                elements.Add(SerializePropertyValue(element, depth + 1, maxDepth));
            }

            var result = new Dictionary<string, object>
            {
                { "$isFixedBuffer", true },
                { "length", bufferSize },
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

        #region Deserialization (Write)

        /// <summary>
        /// Sets a SerializedProperty value from a generic object. Returns true on success.
        /// </summary>
        public static bool SetSerializedPropertyValue(SerializedProperty property, object value)
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
                        property.stringValue = value is Newtonsoft.Json.Linq.JValue jv
                            ? jv.Value?.ToString() ?? ""
                            : value?.ToString() ?? "";
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
                        var eulerAngles = ParseVector3(value);
                        if (eulerAngles.HasValue) { property.quaternionValue = Quaternion.Euler(eulerAngles.Value); return true; }
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
        /// Parses a Vector3 from various input formats.
        /// </summary>
        public static Vector3? ParseVector3(object input)
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
                Debug.LogWarning($"[SerializedPropertyHelper] Failed to parse Vector3: {exception.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a Vector2 from various input formats.
        /// </summary>
        public static Vector2? ParseVector2(object input)
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
                Debug.LogWarning($"[SerializedPropertyHelper] Failed to parse Vector2: {exception.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a Color from various input formats.
        /// </summary>
        public static Color? ParseColor(object input)
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
                Debug.LogWarning($"[SerializedPropertyHelper] Failed to parse Color: {exception.Message}");
            }

            return null;
        }

        #endregion

        #region Object Reference Helpers

        /// <summary>
        /// Checks if the value is an object reference using the $ref syntax.
        /// Object references are dictionaries containing a "$ref" key.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if the value is an object reference dictionary with a $ref key.</returns>
        public static bool IsObjectReference(object value)
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
        public static UnityEngine.Object ResolveObjectReference(Dictionary<string, object> refDict, Type expectedType)
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
                    string resolvedAssetPath = AssetDatabase.GetAssetPath(resolvedObject);
                    if (!string.IsNullOrEmpty(resolvedAssetPath) && resolvedAssetPath.EndsWith(".prefab"))
                    {
                        gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(resolvedAssetPath);
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

        #region Type Resolution

        /// <summary>
        /// Resolves a component type by name. Searches UnityEngine, UnityEngine.UI,
        /// and all loaded assemblies.
        /// </summary>
        public static Type ResolveComponentType(string typeName)
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
    }
}
