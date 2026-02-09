using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for managing ScriptableObject assets: create, modify, get, and list.
    /// </summary>
    public static class ManageScriptableObject
    {
        /// <summary>
        /// Manages ScriptableObject assets: create, modify, get, or list.
        /// </summary>
        /// <param name="action">The action to perform: create, modify, get, list</param>
        /// <param name="typeName">Full or short type name for the ScriptableObject class</param>
        /// <param name="folderPath">Folder path for create (e.g., "Assets/Data")</param>
        /// <param name="assetName">Name for the asset file</param>
        /// <param name="assetPath">Path to existing asset for modify/get</param>
        /// <param name="overwrite">Whether to overwrite existing asset (default: false)</param>
        /// <param name="patches">Array of property patches for create/modify</param>
        /// <returns>Result object indicating success or failure with appropriate data.</returns>
        [MCPTool("manage_scriptable_object", "Manage ScriptableObjects: create, modify, get, list", Category = "Asset", DestructiveHint = true)]
        public static object Execute(
            [MCPParam("action", "Action: create, modify, get, list", required: true, Enum = new[] { "create", "modify", "get", "list" })] string action,
            [MCPParam("type_name", "ScriptableObject type name (full or short)")] string typeName = null,
            [MCPParam("folder_path", "Folder path for create (e.g., Assets/Data)")] string folderPath = null,
            [MCPParam("asset_name", "Asset file name (without .asset extension)")] string assetName = null,
            [MCPParam("asset_path", "Path to existing asset for modify/get")] string assetPath = null,
            [MCPParam("overwrite", "Overwrite existing asset (default: false)")] bool overwrite = false,
            [MCPParam("patches", "Property patches array: [{path, value, op}]")] List<object> patches = null)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw MCPException.InvalidParams("The 'action' parameter is required.");
            }

            string normalizedAction = action.Trim().ToLowerInvariant();

            try
            {
                return normalizedAction switch
                {
                    "create" => HandleCreate(typeName, folderPath, assetName, overwrite, patches),
                    "modify" => HandleModify(assetPath, patches),
                    "get" => HandleGet(assetPath),
                    "list" => HandleList(typeName, folderPath),
                    _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: create, modify, get, list")
                };
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageScriptableObject] Error executing action '{action}': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action '{action}': {exception.Message}"
                };
            }
        }

        #region Action Handlers

        /// <summary>
        /// Creates a new ScriptableObject asset.
        /// </summary>
        private static object HandleCreate(string typeName, string folderPath, string assetName, bool overwrite, List<object> patches)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw MCPException.InvalidParams("The 'type_name' parameter is required for create action.");
            }

            if (string.IsNullOrWhiteSpace(assetName))
            {
                throw MCPException.InvalidParams("The 'asset_name' parameter is required for create action.");
            }

            // Resolve the ScriptableObject type
            Type scriptableObjectType = ResolveScriptableObjectType(typeName);
            if (scriptableObjectType == null)
            {
                return new
                {
                    success = false,
                    error = $"ScriptableObject type '{typeName}' not found. Ensure the type exists and derives from ScriptableObject."
                };
            }

            // Build the asset path
            string normalizedFolderPath = PathUtilities.NormalizePath(folderPath ?? "Assets");
            string fileName = assetName.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                ? assetName
                : $"{assetName}.asset";
            string fullAssetPath = $"{normalizedFolderPath}/{fileName}";

            // Check if asset already exists
            var existingAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(fullAssetPath);
            if (existingAsset != null && !overwrite)
            {
                return new
                {
                    success = false,
                    error = $"Asset already exists at '{fullAssetPath}'. Set overwrite=true to replace it."
                };
            }

            // Ensure parent directory exists
            if (!AssetDatabase.IsValidFolder(normalizedFolderPath))
            {
                if (!PathUtilities.EnsureFolderExists(normalizedFolderPath, out string folderError))
                {
                    return new { success = false, error = folderError };
                }
            }

            try
            {
                // Create the ScriptableObject instance
                ScriptableObject scriptableObject = ScriptableObject.CreateInstance(scriptableObjectType);
                if (scriptableObject == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to create instance of '{typeName}'."
                    };
                }

                scriptableObject.name = Path.GetFileNameWithoutExtension(fileName);

                // Apply patches if provided
                List<object> patchResults = null;
                if (patches != null && patches.Count > 0)
                {
                    patchResults = ApplyPatches(scriptableObject, patches);
                }

                // Delete existing asset if overwriting
                if (existingAsset != null && overwrite)
                {
                    AssetDatabase.DeleteAsset(fullAssetPath);
                }

                // Create the asset
                AssetDatabase.CreateAsset(scriptableObject, fullAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                return new
                {
                    success = true,
                    message = $"ScriptableObject '{assetName}' created successfully.",
                    asset = BuildAssetInfo(fullAssetPath, scriptableObject),
                    patchResults = patchResults?.Count > 0 ? patchResults : null
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error creating ScriptableObject: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Modifies an existing ScriptableObject asset with property patches.
        /// </summary>
        private static object HandleModify(string assetPath, List<object> patches)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw MCPException.InvalidParams("The 'asset_path' parameter is required for modify action.");
            }

            if (patches == null || patches.Count == 0)
            {
                throw MCPException.InvalidParams("The 'patches' parameter is required for modify action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(assetPath);
            ScriptableObject scriptableObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(normalizedPath);

            if (scriptableObject == null)
            {
                return new
                {
                    success = false,
                    error = $"ScriptableObject not found at '{normalizedPath}'."
                };
            }

            try
            {
                Undo.RecordObject(scriptableObject, "Modify ScriptableObject");

                List<object> patchResults = ApplyPatches(scriptableObject, patches);

                EditorUtility.SetDirty(scriptableObject);
                AssetDatabase.SaveAssets();

                int successCount = patchResults.Count(r => r is Dictionary<string, object> dict &&
                    dict.TryGetValue("success", out object successValue) && (bool)successValue);
                int failCount = patchResults.Count - successCount;

                return new
                {
                    success = failCount == 0,
                    message = failCount == 0
                        ? $"Successfully applied {successCount} patch(es)."
                        : $"Applied {successCount} patch(es), {failCount} failed.",
                    asset = BuildAssetInfo(normalizedPath, scriptableObject),
                    patchResults
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error modifying ScriptableObject: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Gets detailed information about a ScriptableObject asset.
        /// </summary>
        private static object HandleGet(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw MCPException.InvalidParams("The 'asset_path' parameter is required for get action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(assetPath);
            ScriptableObject scriptableObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(normalizedPath);

            if (scriptableObject == null)
            {
                return new
                {
                    success = false,
                    error = $"ScriptableObject not found at '{normalizedPath}'."
                };
            }

            return new
            {
                success = true,
                asset = BuildDetailedAssetInfo(normalizedPath, scriptableObject)
            };
        }

        /// <summary>
        /// Lists ScriptableObject assets of a given type.
        /// </summary>
        private static object HandleList(string typeName, string folderPath)
        {
            string searchFilter;
            Type targetType = null;

            if (!string.IsNullOrWhiteSpace(typeName))
            {
                targetType = ResolveScriptableObjectType(typeName);
                if (targetType == null)
                {
                    return new
                    {
                        success = false,
                        error = $"ScriptableObject type '{typeName}' not found."
                    };
                }
                searchFilter = $"t:{targetType.Name}";
            }
            else
            {
                searchFilter = "t:ScriptableObject";
            }

            string[] searchFolders = null;
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                string normalizedFolderPath = PathUtilities.NormalizePath(folderPath);
                if (!AssetDatabase.IsValidFolder(normalizedFolderPath))
                {
                    return new
                    {
                        success = false,
                        error = $"Folder not found: '{normalizedFolderPath}'."
                    };
                }
                searchFolders = new[] { normalizedFolderPath };
            }

            string[] guids = searchFolders != null
                ? AssetDatabase.FindAssets(searchFilter, searchFolders)
                : AssetDatabase.FindAssets(searchFilter);

            var assets = new List<object>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

                if (asset == null)
                {
                    continue;
                }

                // If a specific type was requested, filter by exact type or derived types
                if (targetType != null && !targetType.IsInstanceOfType(asset))
                {
                    continue;
                }

                assets.Add(new
                {
                    path,
                    name = asset.name,
                    type = asset.GetType().Name,
                    fullTypeName = asset.GetType().FullName,
                    guid
                });
            }

            return new
            {
                success = true,
                count = assets.Count,
                typeName = targetType?.FullName ?? "ScriptableObject",
                folder = folderPath ?? "(all)",
                assets
            };
        }

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies a list of patches to a ScriptableObject using SerializedObject/SerializedProperty.
        /// </summary>
        private static List<object> ApplyPatches(ScriptableObject scriptableObject, List<object> patches)
        {
            var results = new List<object>();
            SerializedObject serializedObject = new SerializedObject(scriptableObject);

            foreach (object patchObject in patches)
            {
                try
                {
                    var patch = ConvertToPatchDictionary(patchObject);
                    if (patch == null)
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            { "success", false },
                            { "error", "Invalid patch format. Expected object with 'path' or 'property_path' and 'value'." }
                        });
                        continue;
                    }

                    // Get property path (support both 'path' and 'property_path')
                    string propertyPath = null;
                    if (patch.TryGetValue("path", out object pathValue))
                    {
                        propertyPath = pathValue?.ToString();
                    }
                    else if (patch.TryGetValue("property_path", out object propPathValue))
                    {
                        propertyPath = propPathValue?.ToString();
                    }

                    if (string.IsNullOrWhiteSpace(propertyPath))
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            { "success", false },
                            { "error", "Patch must have a 'path' or 'property_path' field." }
                        });
                        continue;
                    }

                    // Get operation (default: "set")
                    string operation = "set";
                    if (patch.TryGetValue("op", out object opValue))
                    {
                        operation = opValue?.ToString()?.ToLowerInvariant() ?? "set";
                    }

                    // Get value
                    patch.TryGetValue("value", out object value);

                    // Apply the patch
                    var patchResult = ApplySinglePatch(serializedObject, propertyPath, value, operation);
                    results.Add(patchResult);
                }
                catch (Exception exception)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error", $"Error processing patch: {exception.Message}" }
                    });
                }
            }

            serializedObject.ApplyModifiedProperties();
            return results;
        }

        /// <summary>
        /// Applies a single patch to a SerializedObject.
        /// </summary>
        private static Dictionary<string, object> ApplySinglePatch(
            SerializedObject serializedObject,
            string propertyPath,
            object value,
            string operation)
        {
            // Handle array element access with auto-grow
            SerializedProperty property = GetOrCreateProperty(serializedObject, propertyPath);

            if (property == null)
            {
                return new Dictionary<string, object>
                {
                    { "path", propertyPath },
                    { "success", false },
                    { "error", $"Property not found: '{propertyPath}'" }
                };
            }

            try
            {
                switch (operation)
                {
                    case "set":
                        SetPropertyValue(property, value);
                        break;

                    case "array_resize":
                        if (!property.isArray)
                        {
                            return new Dictionary<string, object>
                            {
                                { "path", propertyPath },
                                { "success", false },
                                { "error", $"Property '{propertyPath}' is not an array." }
                            };
                        }
                        int newSize = Convert.ToInt32(value);
                        property.arraySize = newSize;
                        break;

                    default:
                        return new Dictionary<string, object>
                        {
                            { "path", propertyPath },
                            { "success", false },
                            { "error", $"Unknown operation: '{operation}'. Valid operations: set, array_resize" }
                        };
                }

                return new Dictionary<string, object>
                {
                    { "path", propertyPath },
                    { "success", true },
                    { "operation", operation }
                };
            }
            catch (Exception exception)
            {
                return new Dictionary<string, object>
                {
                    { "path", propertyPath },
                    { "success", false },
                    { "error", $"Failed to apply patch: {exception.Message}" }
                };
            }
        }

        /// <summary>
        /// Gets a SerializedProperty by path, auto-growing arrays if needed.
        /// </summary>
        private static SerializedProperty GetOrCreateProperty(SerializedObject serializedObject, string propertyPath)
        {
            // Check for array element pattern like "myList.Array.data[5]"
            Match arrayMatch = Regex.Match(propertyPath, @"^(.+)\.Array\.data\[(\d+)\](.*)$");

            if (arrayMatch.Success)
            {
                string arrayPath = arrayMatch.Groups[1].Value;
                int elementIndex = int.Parse(arrayMatch.Groups[2].Value);
                string remainingPath = arrayMatch.Groups[3].Value;

                SerializedProperty arrayProperty = serializedObject.FindProperty(arrayPath);
                if (arrayProperty == null || !arrayProperty.isArray)
                {
                    return null;
                }

                // Auto-grow array if needed
                if (elementIndex >= arrayProperty.arraySize)
                {
                    arrayProperty.arraySize = elementIndex + 1;
                    serializedObject.ApplyModifiedProperties();
                }

                SerializedProperty elementProperty = arrayProperty.GetArrayElementAtIndex(elementIndex);

                // If there's a remaining path, navigate further
                if (!string.IsNullOrEmpty(remainingPath) && remainingPath.StartsWith("."))
                {
                    return elementProperty.FindPropertyRelative(remainingPath.Substring(1));
                }

                return elementProperty;
            }

            // Try direct property access
            return serializedObject.FindProperty(propertyPath);
        }

        /// <summary>
        /// Sets the value of a SerializedProperty based on its type.
        /// </summary>
        private static void SetPropertyValue(SerializedProperty property, object value)
        {
            if (value == null)
            {
                // Handle null for object references
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                {
                    property.objectReferenceValue = null;
                    return;
                }
                throw new ArgumentException("Cannot set null value for this property type.");
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.intValue = Convert.ToInt32(value);
                    break;

                case SerializedPropertyType.Float:
                    property.floatValue = Convert.ToSingle(value);
                    break;

                case SerializedPropertyType.Boolean:
                    property.boolValue = Convert.ToBoolean(value);
                    break;

                case SerializedPropertyType.String:
                    property.stringValue = value.ToString();
                    break;

                case SerializedPropertyType.Enum:
                    if (value is string enumString)
                    {
                        // Try to match by name
                        string[] enumNames = property.enumNames;
                        int enumIndex = Array.FindIndex(enumNames,
                            n => n.Equals(enumString, StringComparison.OrdinalIgnoreCase));
                        if (enumIndex >= 0)
                        {
                            property.enumValueIndex = enumIndex;
                        }
                        else
                        {
                            throw new ArgumentException($"Enum value '{enumString}' not found. Valid values: {string.Join(", ", enumNames)}");
                        }
                    }
                    else
                    {
                        property.enumValueIndex = Convert.ToInt32(value);
                    }
                    break;

                case SerializedPropertyType.Vector2:
                    property.vector2Value = ParseVector2(value);
                    break;

                case SerializedPropertyType.Vector3:
                    property.vector3Value = ParseVector3(value);
                    break;

                case SerializedPropertyType.Vector4:
                    property.vector4Value = ParseVector4(value);
                    break;

                case SerializedPropertyType.Vector2Int:
                    property.vector2IntValue = ParseVector2Int(value);
                    break;

                case SerializedPropertyType.Vector3Int:
                    property.vector3IntValue = ParseVector3Int(value);
                    break;

                case SerializedPropertyType.Quaternion:
                    property.quaternionValue = ParseQuaternion(value);
                    break;

                case SerializedPropertyType.Color:
                    property.colorValue = ParseColor(value);
                    break;

                case SerializedPropertyType.Rect:
                    property.rectValue = ParseRect(value);
                    break;

                case SerializedPropertyType.RectInt:
                    property.rectIntValue = ParseRectInt(value);
                    break;

                case SerializedPropertyType.Bounds:
                    property.boundsValue = ParseBounds(value);
                    break;

                case SerializedPropertyType.BoundsInt:
                    property.boundsIntValue = ParseBoundsInt(value);
                    break;

                case SerializedPropertyType.AnimationCurve:
                    property.animationCurveValue = ParseAnimationCurve(value);
                    break;

                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = ResolveObjectReference(value);
                    break;

                case SerializedPropertyType.LayerMask:
                    if (value is string layerName)
                    {
                        property.intValue = LayerMask.GetMask(layerName);
                    }
                    else
                    {
                        property.intValue = Convert.ToInt32(value);
                    }
                    break;

                case SerializedPropertyType.ArraySize:
                    property.intValue = Convert.ToInt32(value);
                    break;

                default:
                    throw new ArgumentException($"Unsupported property type: {property.propertyType}");
            }
        }

        #endregion

        #region Value Parsing

        /// <summary>
        /// Parses a Vector2 from various input formats.
        /// </summary>
        private static Vector2 ParseVector2(object value)
        {
            if (value is Vector2 vector2Value)
            {
                return vector2Value;
            }

            if (value is Dictionary<string, object> dict)
            {
                float x = dict.TryGetValue("x", out object xVal) ? Convert.ToSingle(xVal) : 0f;
                float y = dict.TryGetValue("y", out object yVal) ? Convert.ToSingle(yVal) : 0f;
                return new Vector2(x, y);
            }

            if (value is IList<object> list && list.Count >= 2)
            {
                return new Vector2(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]));
            }

            throw new ArgumentException($"Cannot parse Vector2 from: {value}");
        }

        /// <summary>
        /// Parses a Vector3 from various input formats.
        /// </summary>
        private static Vector3 ParseVector3(object value)
        {
            if (value is Vector3 vector3Value)
            {
                return vector3Value;
            }

            if (value is Dictionary<string, object> dict)
            {
                float x = dict.TryGetValue("x", out object xVal) ? Convert.ToSingle(xVal) : 0f;
                float y = dict.TryGetValue("y", out object yVal) ? Convert.ToSingle(yVal) : 0f;
                float z = dict.TryGetValue("z", out object zVal) ? Convert.ToSingle(zVal) : 0f;
                return new Vector3(x, y, z);
            }

            if (value is IList<object> list && list.Count >= 3)
            {
                return new Vector3(
                    Convert.ToSingle(list[0]),
                    Convert.ToSingle(list[1]),
                    Convert.ToSingle(list[2]));
            }

            throw new ArgumentException($"Cannot parse Vector3 from: {value}");
        }

        /// <summary>
        /// Parses a Vector4 from various input formats.
        /// </summary>
        private static Vector4 ParseVector4(object value)
        {
            if (value is Vector4 vector4Value)
            {
                return vector4Value;
            }

            if (value is Dictionary<string, object> dict)
            {
                float x = dict.TryGetValue("x", out object xVal) ? Convert.ToSingle(xVal) : 0f;
                float y = dict.TryGetValue("y", out object yVal) ? Convert.ToSingle(yVal) : 0f;
                float z = dict.TryGetValue("z", out object zVal) ? Convert.ToSingle(zVal) : 0f;
                float w = dict.TryGetValue("w", out object wVal) ? Convert.ToSingle(wVal) : 0f;
                return new Vector4(x, y, z, w);
            }

            if (value is IList<object> list && list.Count >= 4)
            {
                return new Vector4(
                    Convert.ToSingle(list[0]),
                    Convert.ToSingle(list[1]),
                    Convert.ToSingle(list[2]),
                    Convert.ToSingle(list[3]));
            }

            throw new ArgumentException($"Cannot parse Vector4 from: {value}");
        }

        /// <summary>
        /// Parses a Vector2Int from various input formats.
        /// </summary>
        private static Vector2Int ParseVector2Int(object value)
        {
            if (value is Vector2Int vector2IntValue)
            {
                return vector2IntValue;
            }

            if (value is Dictionary<string, object> dict)
            {
                int x = dict.TryGetValue("x", out object xVal) ? Convert.ToInt32(xVal) : 0;
                int y = dict.TryGetValue("y", out object yVal) ? Convert.ToInt32(yVal) : 0;
                return new Vector2Int(x, y);
            }

            if (value is IList<object> list && list.Count >= 2)
            {
                return new Vector2Int(Convert.ToInt32(list[0]), Convert.ToInt32(list[1]));
            }

            throw new ArgumentException($"Cannot parse Vector2Int from: {value}");
        }

        /// <summary>
        /// Parses a Vector3Int from various input formats.
        /// </summary>
        private static Vector3Int ParseVector3Int(object value)
        {
            if (value is Vector3Int vector3IntValue)
            {
                return vector3IntValue;
            }

            if (value is Dictionary<string, object> dict)
            {
                int x = dict.TryGetValue("x", out object xVal) ? Convert.ToInt32(xVal) : 0;
                int y = dict.TryGetValue("y", out object yVal) ? Convert.ToInt32(yVal) : 0;
                int z = dict.TryGetValue("z", out object zVal) ? Convert.ToInt32(zVal) : 0;
                return new Vector3Int(x, y, z);
            }

            if (value is IList<object> list && list.Count >= 3)
            {
                return new Vector3Int(
                    Convert.ToInt32(list[0]),
                    Convert.ToInt32(list[1]),
                    Convert.ToInt32(list[2]));
            }

            throw new ArgumentException($"Cannot parse Vector3Int from: {value}");
        }

        /// <summary>
        /// Parses a Quaternion from various input formats.
        /// </summary>
        private static Quaternion ParseQuaternion(object value)
        {
            if (value is Quaternion quaternionValue)
            {
                return quaternionValue;
            }

            if (value is Dictionary<string, object> dict)
            {
                // Check for euler angles format
                if (dict.ContainsKey("euler") || (dict.ContainsKey("x") && dict.ContainsKey("y") && dict.ContainsKey("z") && !dict.ContainsKey("w")))
                {
                    Vector3 euler;
                    if (dict.TryGetValue("euler", out object eulerValue))
                    {
                        euler = ParseVector3(eulerValue);
                    }
                    else
                    {
                        float x = dict.TryGetValue("x", out object xVal) ? Convert.ToSingle(xVal) : 0f;
                        float y = dict.TryGetValue("y", out object yVal) ? Convert.ToSingle(yVal) : 0f;
                        float z = dict.TryGetValue("z", out object zVal) ? Convert.ToSingle(zVal) : 0f;
                        euler = new Vector3(x, y, z);
                    }
                    return Quaternion.Euler(euler);
                }

                // Quaternion components format
                float qx = dict.TryGetValue("x", out object qxVal) ? Convert.ToSingle(qxVal) : 0f;
                float qy = dict.TryGetValue("y", out object qyVal) ? Convert.ToSingle(qyVal) : 0f;
                float qz = dict.TryGetValue("z", out object qzVal) ? Convert.ToSingle(qzVal) : 0f;
                float qw = dict.TryGetValue("w", out object qwVal) ? Convert.ToSingle(qwVal) : 1f;
                return new Quaternion(qx, qy, qz, qw);
            }

            // Array format as euler angles [x, y, z]
            if (value is IList<object> list && list.Count >= 3)
            {
                Vector3 euler = new Vector3(
                    Convert.ToSingle(list[0]),
                    Convert.ToSingle(list[1]),
                    Convert.ToSingle(list[2]));
                return Quaternion.Euler(euler);
            }

            throw new ArgumentException($"Cannot parse Quaternion from: {value}");
        }

        /// <summary>
        /// Parses a Color from various input formats.
        /// </summary>
        private static Color ParseColor(object value)
        {
            if (value is Color colorValue)
            {
                return colorValue;
            }

            // Handle hex string
            if (value is string hexString)
            {
                if (!hexString.StartsWith("#"))
                {
                    hexString = "#" + hexString;
                }
                if (ColorUtility.TryParseHtmlString(hexString, out Color parsedColor))
                {
                    return parsedColor;
                }
                throw new ArgumentException($"Invalid color hex string: {value}");
            }

            if (value is Dictionary<string, object> dict)
            {
                float r = dict.TryGetValue("r", out object rVal) ? Convert.ToSingle(rVal) : 0f;
                float g = dict.TryGetValue("g", out object gVal) ? Convert.ToSingle(gVal) : 0f;
                float b = dict.TryGetValue("b", out object bVal) ? Convert.ToSingle(bVal) : 0f;
                float a = dict.TryGetValue("a", out object aVal) ? Convert.ToSingle(aVal) : 1f;

                // Normalize if values are in 0-255 range
                if (r > 1f || g > 1f || b > 1f)
                {
                    r /= UnityConstants.ColorByteMax;
                    g /= UnityConstants.ColorByteMax;
                    b /= UnityConstants.ColorByteMax;
                    if (a > 1f) a /= UnityConstants.ColorByteMax;
                }

                return new Color(r, g, b, a);
            }

            if (value is IList<object> list && list.Count >= 3)
            {
                float r = Convert.ToSingle(list[0]);
                float g = Convert.ToSingle(list[1]);
                float b = Convert.ToSingle(list[2]);
                float a = list.Count >= 4 ? Convert.ToSingle(list[3]) : 1f;

                // Normalize if values are in 0-255 range
                if (r > 1f || g > 1f || b > 1f)
                {
                    r /= UnityConstants.ColorByteMax;
                    g /= UnityConstants.ColorByteMax;
                    b /= UnityConstants.ColorByteMax;
                    if (a > 1f) a /= UnityConstants.ColorByteMax;
                }

                return new Color(r, g, b, a);
            }

            throw new ArgumentException($"Cannot parse Color from: {value}");
        }

        /// <summary>
        /// Parses a Rect from various input formats.
        /// </summary>
        private static Rect ParseRect(object value)
        {
            if (value is Rect rectValue)
            {
                return rectValue;
            }

            if (value is Dictionary<string, object> dict)
            {
                float x = dict.TryGetValue("x", out object xVal) ? Convert.ToSingle(xVal) : 0f;
                float y = dict.TryGetValue("y", out object yVal) ? Convert.ToSingle(yVal) : 0f;
                float width = dict.TryGetValue("width", out object wVal) ? Convert.ToSingle(wVal) : 0f;
                float height = dict.TryGetValue("height", out object hVal) ? Convert.ToSingle(hVal) : 0f;
                return new Rect(x, y, width, height);
            }

            if (value is IList<object> list && list.Count >= 4)
            {
                return new Rect(
                    Convert.ToSingle(list[0]),
                    Convert.ToSingle(list[1]),
                    Convert.ToSingle(list[2]),
                    Convert.ToSingle(list[3]));
            }

            throw new ArgumentException($"Cannot parse Rect from: {value}");
        }

        /// <summary>
        /// Parses a RectInt from various input formats.
        /// </summary>
        private static RectInt ParseRectInt(object value)
        {
            if (value is RectInt rectIntValue)
            {
                return rectIntValue;
            }

            if (value is Dictionary<string, object> dict)
            {
                int x = dict.TryGetValue("x", out object xVal) ? Convert.ToInt32(xVal) : 0;
                int y = dict.TryGetValue("y", out object yVal) ? Convert.ToInt32(yVal) : 0;
                int width = dict.TryGetValue("width", out object wVal) ? Convert.ToInt32(wVal) : 0;
                int height = dict.TryGetValue("height", out object hVal) ? Convert.ToInt32(hVal) : 0;
                return new RectInt(x, y, width, height);
            }

            if (value is IList<object> list && list.Count >= 4)
            {
                return new RectInt(
                    Convert.ToInt32(list[0]),
                    Convert.ToInt32(list[1]),
                    Convert.ToInt32(list[2]),
                    Convert.ToInt32(list[3]));
            }

            throw new ArgumentException($"Cannot parse RectInt from: {value}");
        }

        /// <summary>
        /// Parses Bounds from various input formats.
        /// </summary>
        private static Bounds ParseBounds(object value)
        {
            if (value is Bounds boundsValue)
            {
                return boundsValue;
            }

            if (value is Dictionary<string, object> dict)
            {
                Vector3 center = Vector3.zero;
                Vector3 size = Vector3.zero;

                if (dict.TryGetValue("center", out object centerVal))
                {
                    center = ParseVector3(centerVal);
                }
                if (dict.TryGetValue("size", out object sizeVal))
                {
                    size = ParseVector3(sizeVal);
                }

                return new Bounds(center, size);
            }

            throw new ArgumentException($"Cannot parse Bounds from: {value}");
        }

        /// <summary>
        /// Parses BoundsInt from various input formats.
        /// </summary>
        private static BoundsInt ParseBoundsInt(object value)
        {
            if (value is BoundsInt boundsIntValue)
            {
                return boundsIntValue;
            }

            if (value is Dictionary<string, object> dict)
            {
                Vector3Int position = Vector3Int.zero;
                Vector3Int size = Vector3Int.zero;

                if (dict.TryGetValue("position", out object posVal))
                {
                    position = ParseVector3Int(posVal);
                }
                if (dict.TryGetValue("size", out object sizeVal))
                {
                    size = ParseVector3Int(sizeVal);
                }

                return new BoundsInt(position, size);
            }

            throw new ArgumentException($"Cannot parse BoundsInt from: {value}");
        }

        /// <summary>
        /// Parses an AnimationCurve from various input formats.
        /// </summary>
        private static AnimationCurve ParseAnimationCurve(object value)
        {
            if (value is AnimationCurve curveValue)
            {
                return curveValue;
            }

            if (value is Dictionary<string, object> dict)
            {
                // Handle preset curves
                if (dict.TryGetValue("preset", out object presetValue))
                {
                    string preset = presetValue.ToString().ToLowerInvariant();
                    return preset switch
                    {
                        "linear" => AnimationCurve.Linear(0, 0, 1, 1),
                        "ease_in" or "easein" => AnimationCurve.EaseInOut(0, 0, 1, 1),
                        "ease_out" or "easeout" => AnimationCurve.EaseInOut(0, 0, 1, 1),
                        "ease_in_out" or "easeinout" => AnimationCurve.EaseInOut(0, 0, 1, 1),
                        "constant" => AnimationCurve.Constant(0, 1, 1),
                        _ => AnimationCurve.Linear(0, 0, 1, 1)
                    };
                }

                // Handle keyframes
                if (dict.TryGetValue("keys", out object keysValue) && keysValue is IList<object> keysList)
                {
                    var keyframes = new List<Keyframe>();
                    foreach (object keyObj in keysList)
                    {
                        if (keyObj is Dictionary<string, object> keyDict)
                        {
                            float time = keyDict.TryGetValue("time", out object t) ? Convert.ToSingle(t) : 0f;
                            float keyValue = keyDict.TryGetValue("value", out object v) ? Convert.ToSingle(v) : 0f;
                            float inTangent = keyDict.TryGetValue("inTangent", out object inT) ? Convert.ToSingle(inT) : 0f;
                            float outTangent = keyDict.TryGetValue("outTangent", out object outT) ? Convert.ToSingle(outT) : 0f;
                            keyframes.Add(new Keyframe(time, keyValue, inTangent, outTangent));
                        }
                        else if (keyObj is IList<object> keyArray && keyArray.Count >= 2)
                        {
                            float time = Convert.ToSingle(keyArray[0]);
                            float keyValue = Convert.ToSingle(keyArray[1]);
                            keyframes.Add(new Keyframe(time, keyValue));
                        }
                    }
                    return new AnimationCurve(keyframes.ToArray());
                }
            }

            // Handle array of [time, value] pairs
            if (value is IList<object> list)
            {
                var keyframes = new List<Keyframe>();
                foreach (object item in list)
                {
                    if (item is IList<object> pair && pair.Count >= 2)
                    {
                        float time = Convert.ToSingle(pair[0]);
                        float keyValue = Convert.ToSingle(pair[1]);
                        keyframes.Add(new Keyframe(time, keyValue));
                    }
                }
                if (keyframes.Count > 0)
                {
                    return new AnimationCurve(keyframes.ToArray());
                }
            }

            throw new ArgumentException($"Cannot parse AnimationCurve from: {value}");
        }

        /// <summary>
        /// Resolves an object reference from an asset path or GUID.
        /// </summary>
        private static UnityEngine.Object ResolveObjectReference(object value)
        {
            if (value == null)
            {
                return null;
            }

            string assetPath = value.ToString();

            // Check if it's a GUID
            if (Guid.TryParse(assetPath, out _))
            {
                assetPath = AssetDatabase.GUIDToAssetPath(assetPath);
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            // Normalize the path
            assetPath = PathUtilities.NormalizePath(assetPath);

            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Resolves a ScriptableObject type by name.
        /// </summary>
        private static Type ResolveScriptableObjectType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            // Try exact match first (full name with assembly)
            Type type = Type.GetType(typeName);
            if (type != null && typeof(ScriptableObject).IsAssignableFrom(type))
            {
                return type;
            }

            // Search all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip system assemblies for performance
                string assemblyName = assembly.FullName;
                if (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("mscorlib", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("netstandard", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    foreach (Type assemblyType in assembly.GetTypes())
                    {
                        if (!typeof(ScriptableObject).IsAssignableFrom(assemblyType))
                        {
                            continue;
                        }

                        if (assemblyType.Name == typeName || assemblyType.FullName == typeName)
                        {
                            return assemblyType;
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // Some assemblies may have types that cannot be loaded
                    Debug.LogWarning($"[ManageScriptableObject] ReflectionTypeLoadException when scanning assembly '{assembly.GetName().Name}': {ex.Message}");
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// Converts an object to a patch dictionary.
        /// </summary>
        private static Dictionary<string, object> ConvertToPatchDictionary(object input)
        {
            if (input is Dictionary<string, object> dict)
            {
                return dict;
            }

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
        /// Builds basic asset information.
        /// </summary>
        private static object BuildAssetInfo(string path, ScriptableObject asset)
        {
            return new
            {
                path,
                name = asset.name,
                type = asset.GetType().Name,
                fullTypeName = asset.GetType().FullName,
                guid = AssetDatabase.AssetPathToGUID(path)
            };
        }

        /// <summary>
        /// Builds detailed asset information including all serialized properties.
        /// </summary>
        private static object BuildDetailedAssetInfo(string path, ScriptableObject asset)
        {
            SerializedObject serializedObject = new SerializedObject(asset);
            var properties = new List<object>();

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                // Skip the script reference
                if (iterator.propertyPath == "m_Script")
                {
                    continue;
                }

                properties.Add(BuildPropertyInfo(iterator));
            }

            return new
            {
                path,
                name = asset.name,
                type = asset.GetType().Name,
                fullTypeName = asset.GetType().FullName,
                guid = AssetDatabase.AssetPathToGUID(path),
                propertyCount = properties.Count,
                properties
            };
        }

        /// <summary>
        /// Builds property information from a SerializedProperty.
        /// </summary>
        private static object BuildPropertyInfo(SerializedProperty property)
        {
            var info = new Dictionary<string, object>
            {
                { "path", property.propertyPath },
                { "name", property.name },
                { "displayName", property.displayName },
                { "type", property.propertyType.ToString() }
            };

            // Add value based on property type
            try
            {
                object value = GetPropertyValue(property);
                if (value != null)
                {
                    info["value"] = value;
                }
            }
            catch
            {
                info["value"] = "(unable to read)";
            }

            // Add array info if applicable
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                info["isArray"] = true;
                info["arraySize"] = property.arraySize;
            }

            return info;
        }

        /// <summary>
        /// Gets the value of a SerializedProperty as an object.
        /// </summary>
        private static object GetPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue;

                case SerializedPropertyType.Float:
                    return property.floatValue;

                case SerializedPropertyType.Boolean:
                    return property.boolValue;

                case SerializedPropertyType.String:
                    return property.stringValue;

                case SerializedPropertyType.Enum:
                    return new
                    {
                        index = property.enumValueIndex,
                        name = property.enumNames.Length > property.enumValueIndex
                            ? property.enumNames[property.enumValueIndex]
                            : "(unknown)"
                    };

                case SerializedPropertyType.Vector2:
                    var v2 = property.vector2Value;
                    return new { x = v2.x, y = v2.y };

                case SerializedPropertyType.Vector3:
                    var v3 = property.vector3Value;
                    return new { x = v3.x, y = v3.y, z = v3.z };

                case SerializedPropertyType.Vector4:
                    var v4 = property.vector4Value;
                    return new { x = v4.x, y = v4.y, z = v4.z, w = v4.w };

                case SerializedPropertyType.Vector2Int:
                    var v2i = property.vector2IntValue;
                    return new { x = v2i.x, y = v2i.y };

                case SerializedPropertyType.Vector3Int:
                    var v3i = property.vector3IntValue;
                    return new { x = v3i.x, y = v3i.y, z = v3i.z };

                case SerializedPropertyType.Quaternion:
                    var q = property.quaternionValue;
                    var euler = q.eulerAngles;
                    return new
                    {
                        x = q.x, y = q.y, z = q.z, w = q.w,
                        euler = new { x = euler.x, y = euler.y, z = euler.z }
                    };

                case SerializedPropertyType.Color:
                    var c = property.colorValue;
                    return new
                    {
                        r = c.r, g = c.g, b = c.b, a = c.a,
                        hex = ColorUtility.ToHtmlStringRGBA(c)
                    };

                case SerializedPropertyType.Rect:
                    var r = property.rectValue;
                    return new { x = r.x, y = r.y, width = r.width, height = r.height };

                case SerializedPropertyType.RectInt:
                    var ri = property.rectIntValue;
                    return new { x = ri.x, y = ri.y, width = ri.width, height = ri.height };

                case SerializedPropertyType.Bounds:
                    var b = property.boundsValue;
                    return new
                    {
                        center = new { x = b.center.x, y = b.center.y, z = b.center.z },
                        size = new { x = b.size.x, y = b.size.y, z = b.size.z }
                    };

                case SerializedPropertyType.BoundsInt:
                    var bi = property.boundsIntValue;
                    return new
                    {
                        position = new { x = bi.position.x, y = bi.position.y, z = bi.position.z },
                        size = new { x = bi.size.x, y = bi.size.y, z = bi.size.z }
                    };

                case SerializedPropertyType.ObjectReference:
                    var obj = property.objectReferenceValue;
                    if (obj == null)
                    {
                        return null;
                    }
                    string assetPath = AssetDatabase.GetAssetPath(obj);
                    return new
                    {
                        name = obj.name,
                        type = obj.GetType().Name,
                        instanceId = obj.GetInstanceID(),
                        assetPath = string.IsNullOrEmpty(assetPath) ? null : assetPath
                    };

                case SerializedPropertyType.LayerMask:
                    return property.intValue;

                case SerializedPropertyType.AnimationCurve:
                    var curve = property.animationCurveValue;
                    if (curve == null || curve.keys.Length == 0)
                    {
                        return null;
                    }
                    var keys = curve.keys.Select(k => new
                    {
                        time = k.time,
                        value = k.value,
                        inTangent = k.inTangent,
                        outTangent = k.outTangent
                    }).ToArray();
                    return new { keyCount = keys.Length, keys };

                default:
                    if (property.isArray && property.propertyType != SerializedPropertyType.String)
                    {
                        return $"[Array of {property.arraySize} elements]";
                    }
                    return null;
            }
        }

        #endregion
    }
}
