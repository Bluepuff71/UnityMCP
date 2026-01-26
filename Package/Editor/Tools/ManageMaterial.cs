using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for managing materials: create, modify properties, and assign to renderers.
    /// </summary>
    public static class ManageMaterial
    {
        /// <summary>
        /// Common URP shader name mappings for convenience.
        /// </summary>
        private static readonly Dictionary<string, string> ShaderAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Standard", "Universal Render Pipeline/Lit" },
            { "URP/Lit", "Universal Render Pipeline/Lit" },
            { "URP/Unlit", "Universal Render Pipeline/Unlit" },
            { "URP/SimpleLit", "Universal Render Pipeline/Simple Lit" },
            { "URP/BakedLit", "Universal Render Pipeline/Baked Lit" },
            { "URP/Particles/Lit", "Universal Render Pipeline/Particles/Lit" },
            { "URP/Particles/Unlit", "Universal Render Pipeline/Particles/Unlit" }
        };

        /// <summary>
        /// Manages materials: create, modify properties, and assign to renderers.
        /// </summary>
        /// <param name="action">The action to perform: create, get_info, set_property, set_color, assign_to_renderer, set_renderer_color</param>
        /// <param name="materialPath">Path to material asset (e.g., "Assets/Materials/MyMat.mat")</param>
        /// <param name="shader">Shader name for create (default: Universal Render Pipeline/Lit or Standard)</param>
        /// <param name="property">Property name (e.g., "_Color", "_BaseColor", "_MainTex")</param>
        /// <param name="value">Value to set (color as hex/object, float, texture path, vector)</param>
        /// <param name="color">Color value as hex string "#RRGGBB" or object {r,g,b,a}</param>
        /// <param name="target">Target GameObject name or path for renderer operations</param>
        /// <param name="slot">Material slot index (default: 0)</param>
        /// <param name="mode">For set_renderer_color: "property_block", "shared", or "instance"</param>
        /// <returns>Result object indicating success or failure with appropriate data.</returns>
        [MCPTool("manage_material", "Manage materials: create, modify properties, assign to renderers", Category = "Asset")]
        public static object Execute(
            [MCPParam("action", "Action: create, get_info, set_property, set_color, assign_to_renderer, set_renderer_color", required: true)] string action,
            [MCPParam("material_path", "Path to material asset (e.g., Assets/Materials/MyMat.mat)")] string materialPath = null,
            [MCPParam("shader", "Shader name for create (e.g., Standard, URP/Lit, Universal Render Pipeline/Lit)")] string shader = null,
            [MCPParam("property", "Property name (e.g., _Color, _BaseColor, _MainTex)")] string property = null,
            [MCPParam("value", "Value to set (color as hex/object, float, texture path, vector as array)")] object value = null,
            [MCPParam("color", "Color value as hex string #RRGGBB or object {r,g,b,a}")] object color = null,
            [MCPParam("target", "Target GameObject name or path for renderer operations")] string target = null,
            [MCPParam("slot", "Material slot index (default: 0)")] int slot = 0,
            [MCPParam("mode", "For set_renderer_color: property_block, shared, or instance (default: property_block)")] string mode = "property_block")
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
                    "create" => HandleCreate(materialPath, shader),
                    "get_info" => HandleGetInfo(materialPath),
                    "set_property" => HandleSetProperty(materialPath, property, value),
                    "set_color" => HandleSetColor(materialPath, property, color),
                    "assign_to_renderer" => HandleAssignToRenderer(materialPath, target, slot),
                    "set_renderer_color" => HandleSetRendererColor(target, property, color, slot, mode),
                    _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: create, get_info, set_property, set_color, assign_to_renderer, set_renderer_color")
                };
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageMaterial] Error executing action '{action}': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action '{action}': {exception.Message}"
                };
            }
        }

        #region Action Handlers

        /// <summary>
        /// Creates a new material asset.
        /// </summary>
        private static object HandleCreate(string materialPath, string shaderName)
        {
            if (string.IsNullOrWhiteSpace(materialPath))
            {
                throw MCPException.InvalidParams("The 'material_path' parameter is required for create action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(materialPath);

            // Ensure .mat extension
            if (!normalizedPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = Path.ChangeExtension(normalizedPath, ".mat");
            }

            // Check if asset already exists
            if (AssetDatabase.LoadAssetAtPath<Material>(normalizedPath) != null)
            {
                return new
                {
                    success = false,
                    error = $"Material already exists at '{normalizedPath}'."
                };
            }

            // Ensure parent directory exists
            string parentDirectory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentDirectory) && !AssetDatabase.IsValidFolder(parentDirectory))
            {
                if (!PathUtilities.EnsureFolderExists(parentDirectory, out string folderError))
                {
                    return new { success = false, error = folderError };
                }
            }

            // Resolve shader
            Shader resolvedShader = ResolveShader(shaderName);
            if (resolvedShader == null)
            {
                return new
                {
                    success = false,
                    error = $"Shader not found: '{shaderName ?? "(default)"}'. Try using full shader path like 'Universal Render Pipeline/Lit'."
                };
            }

            try
            {
                Material material = new Material(resolvedShader);
                AssetDatabase.CreateAsset(material, normalizedPath);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Material created successfully at '{normalizedPath}'.",
                    material = BuildMaterialInfo(normalizedPath, material)
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error creating material: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Gets detailed information about a material including all shader properties.
        /// </summary>
        private static object HandleGetInfo(string materialPath)
        {
            if (string.IsNullOrWhiteSpace(materialPath))
            {
                throw MCPException.InvalidParams("The 'material_path' parameter is required for get_info action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(materialPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(normalizedPath);

            if (material == null)
            {
                return new
                {
                    success = false,
                    error = $"Material not found at '{normalizedPath}'."
                };
            }

            return new
            {
                success = true,
                material = BuildDetailedMaterialInfo(normalizedPath, material)
            };
        }

        /// <summary>
        /// Sets a material shader property (color, float, texture, vector).
        /// </summary>
        private static object HandleSetProperty(string materialPath, string propertyName, object value)
        {
            if (string.IsNullOrWhiteSpace(materialPath))
            {
                throw MCPException.InvalidParams("The 'material_path' parameter is required for set_property action.");
            }

            if (string.IsNullOrWhiteSpace(propertyName))
            {
                throw MCPException.InvalidParams("The 'property' parameter is required for set_property action.");
            }

            if (value == null)
            {
                throw MCPException.InvalidParams("The 'value' parameter is required for set_property action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(materialPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(normalizedPath);

            if (material == null)
            {
                return new
                {
                    success = false,
                    error = $"Material not found at '{normalizedPath}'."
                };
            }

            if (!material.HasProperty(propertyName))
            {
                return new
                {
                    success = false,
                    error = $"Material does not have property '{propertyName}'.",
                    availableProperties = GetPropertyNames(material)
                };
            }

            Undo.RecordObject(material, $"Set Material Property {propertyName}");

            // Determine property type and set value
            ShaderPropertyType propertyType = GetPropertyType(material.shader, propertyName);
            object previousValue = null;
            object newValue = null;

            try
            {
                switch (propertyType)
                {
                    case ShaderPropertyType.Color:
                        previousValue = material.GetColor(propertyName);
                        Color colorValue = ParseColor(value);
                        material.SetColor(propertyName, colorValue);
                        newValue = FormatColor(colorValue);
                        break;

                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        previousValue = material.GetFloat(propertyName);
                        float floatValue = Convert.ToSingle(value);
                        material.SetFloat(propertyName, floatValue);
                        newValue = floatValue;
                        break;

                    case ShaderPropertyType.Vector:
                        previousValue = material.GetVector(propertyName);
                        Vector4 vectorValue = ParseVector4(value);
                        material.SetVector(propertyName, vectorValue);
                        newValue = new { x = vectorValue.x, y = vectorValue.y, z = vectorValue.z, w = vectorValue.w };
                        break;

                    case ShaderPropertyType.Texture:
                        previousValue = material.GetTexture(propertyName)?.name ?? "(none)";
                        Texture texture = ResolveTexture(value.ToString());
                        material.SetTexture(propertyName, texture);
                        newValue = texture?.name ?? "(none)";
                        break;

                    case ShaderPropertyType.Int:
                        previousValue = material.GetInt(propertyName);
                        int intValue = Convert.ToInt32(value);
                        material.SetInt(propertyName, intValue);
                        newValue = intValue;
                        break;

                    default:
                        return new
                        {
                            success = false,
                            error = $"Unsupported property type '{propertyType}' for property '{propertyName}'."
                        };
                }

                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Property '{propertyName}' set successfully.",
                    property = propertyName,
                    propertyType = propertyType.ToString(),
                    previousValue,
                    newValue
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error setting property '{propertyName}': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Convenience action to set material color.
        /// </summary>
        private static object HandleSetColor(string materialPath, string propertyName, object colorValue)
        {
            if (string.IsNullOrWhiteSpace(materialPath))
            {
                throw MCPException.InvalidParams("The 'material_path' parameter is required for set_color action.");
            }

            if (colorValue == null)
            {
                throw MCPException.InvalidParams("The 'color' parameter is required for set_color action.");
            }

            // Default to common color property names
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                propertyName = "_BaseColor"; // URP default
            }

            string normalizedPath = PathUtilities.NormalizePath(materialPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(normalizedPath);

            if (material == null)
            {
                return new
                {
                    success = false,
                    error = $"Material not found at '{normalizedPath}'."
                };
            }

            // Try to find the color property - check common names if specified doesn't exist
            string actualPropertyName = propertyName;
            if (!material.HasProperty(propertyName))
            {
                string[] commonColorProperties = { "_BaseColor", "_Color", "_TintColor", "_EmissionColor" };
                actualPropertyName = commonColorProperties.FirstOrDefault(p => material.HasProperty(p));

                if (actualPropertyName == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Material does not have property '{propertyName}' or common color properties.",
                        availableProperties = GetPropertyNames(material)
                    };
                }
            }

            try
            {
                Color color = ParseColor(colorValue);
                Color previousColor = material.GetColor(actualPropertyName);

                Undo.RecordObject(material, $"Set Material Color {actualPropertyName}");
                material.SetColor(actualPropertyName, color);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Color set successfully on property '{actualPropertyName}'.",
                    property = actualPropertyName,
                    previousColor = FormatColor(previousColor),
                    newColor = FormatColor(color)
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error setting color: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Assigns a material to a renderer on a GameObject.
        /// </summary>
        private static object HandleAssignToRenderer(string materialPath, string targetPath, int slot)
        {
            if (string.IsNullOrWhiteSpace(materialPath))
            {
                throw MCPException.InvalidParams("The 'material_path' parameter is required for assign_to_renderer action.");
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw MCPException.InvalidParams("The 'target' parameter is required for assign_to_renderer action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(materialPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(normalizedPath);

            if (material == null)
            {
                return new
                {
                    success = false,
                    error = $"Material not found at '{normalizedPath}'."
                };
            }

            GameObject gameObject = FindGameObject(targetPath);
            if (gameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"GameObject not found: '{targetPath}'."
                };
            }

            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                return new
                {
                    success = false,
                    error = $"No Renderer component found on GameObject '{targetPath}'."
                };
            }

            Material[] sharedMaterials = renderer.sharedMaterials;
            if (slot < 0 || slot >= sharedMaterials.Length)
            {
                return new
                {
                    success = false,
                    error = $"Material slot {slot} is out of range. Renderer has {sharedMaterials.Length} material slot(s)."
                };
            }

            string previousMaterialName = sharedMaterials[slot]?.name ?? "(none)";

            Undo.RecordObject(renderer, "Assign Material");
            sharedMaterials[slot] = material;
            renderer.sharedMaterials = sharedMaterials;
            EditorUtility.SetDirty(renderer);

            return new
            {
                success = true,
                message = $"Material assigned to slot {slot} on '{gameObject.name}'.",
                gameObject = gameObject.name,
                slot,
                previousMaterial = previousMaterialName,
                newMaterial = material.name
            };
        }

        /// <summary>
        /// Sets color on a renderer via PropertyBlock, shared material, or instanced material.
        /// </summary>
        private static object HandleSetRendererColor(string targetPath, string propertyName, object colorValue, int slot, string mode)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw MCPException.InvalidParams("The 'target' parameter is required for set_renderer_color action.");
            }

            if (colorValue == null)
            {
                throw MCPException.InvalidParams("The 'color' parameter is required for set_renderer_color action.");
            }

            // Default to common color property names
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                propertyName = "_BaseColor";
            }

            GameObject gameObject = FindGameObject(targetPath);
            if (gameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"GameObject not found: '{targetPath}'."
                };
            }

            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                return new
                {
                    success = false,
                    error = $"No Renderer component found on GameObject '{targetPath}'."
                };
            }

            string normalizedMode = mode?.ToLowerInvariant().Trim() ?? "property_block";
            Color color = ParseColor(colorValue);

            try
            {
                switch (normalizedMode)
                {
                    case "property_block":
                        return SetColorViaPropertyBlock(renderer, propertyName, color, gameObject.name);

                    case "shared":
                        return SetColorViaSharedMaterial(renderer, propertyName, color, slot, gameObject.name);

                    case "instance":
                        return SetColorViaInstanceMaterial(renderer, propertyName, color, slot, gameObject.name);

                    default:
                        return new
                        {
                            success = false,
                            error = $"Unknown mode: '{mode}'. Valid modes: property_block, shared, instance."
                        };
                }
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error setting renderer color: {exception.Message}"
                };
            }
        }

        #endregion

        #region Renderer Color Modes

        /// <summary>
        /// Sets color via MaterialPropertyBlock (non-destructive, runtime only).
        /// </summary>
        private static object SetColorViaPropertyBlock(Renderer renderer, string propertyName, Color color, string gameObjectName)
        {
            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);

            Color previousColor = propertyBlock.GetColor(propertyName);
            propertyBlock.SetColor(propertyName, color);
            renderer.SetPropertyBlock(propertyBlock);

            return new
            {
                success = true,
                message = $"Color set via PropertyBlock on '{gameObjectName}'.",
                mode = "property_block",
                property = propertyName,
                previousColor = FormatColor(previousColor),
                newColor = FormatColor(color),
                note = "PropertyBlock changes are runtime-only and won't persist in edit mode."
            };
        }

        /// <summary>
        /// Sets color on the shared material (affects all objects using this material).
        /// </summary>
        private static object SetColorViaSharedMaterial(Renderer renderer, string propertyName, Color color, int slot, string gameObjectName)
        {
            Material[] sharedMaterials = renderer.sharedMaterials;
            if (slot < 0 || slot >= sharedMaterials.Length)
            {
                return new
                {
                    success = false,
                    error = $"Material slot {slot} is out of range. Renderer has {sharedMaterials.Length} material slot(s)."
                };
            }

            Material material = sharedMaterials[slot];
            if (material == null)
            {
                return new
                {
                    success = false,
                    error = $"No material in slot {slot}."
                };
            }

            if (!material.HasProperty(propertyName))
            {
                // Try common color properties
                string[] commonColorProperties = { "_BaseColor", "_Color", "_TintColor" };
                string actualProperty = commonColorProperties.FirstOrDefault(p => material.HasProperty(p));
                if (actualProperty != null)
                {
                    propertyName = actualProperty;
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Material does not have property '{propertyName}'."
                    };
                }
            }

            Color previousColor = material.GetColor(propertyName);

            Undo.RecordObject(material, "Set Shared Material Color");
            material.SetColor(propertyName, color);
            EditorUtility.SetDirty(material);

            return new
            {
                success = true,
                message = $"Color set on shared material '{material.name}'.",
                mode = "shared",
                property = propertyName,
                previousColor = FormatColor(previousColor),
                newColor = FormatColor(color),
                warning = "This affects ALL objects using this material."
            };
        }

        /// <summary>
        /// Sets color on an instanced material (affects only this renderer).
        /// </summary>
        private static object SetColorViaInstanceMaterial(Renderer renderer, string propertyName, Color color, int slot, string gameObjectName)
        {
            Material[] materials = renderer.materials; // Creates instances
            if (slot < 0 || slot >= materials.Length)
            {
                return new
                {
                    success = false,
                    error = $"Material slot {slot} is out of range. Renderer has {materials.Length} material slot(s)."
                };
            }

            Material material = materials[slot];
            if (material == null)
            {
                return new
                {
                    success = false,
                    error = $"No material in slot {slot}."
                };
            }

            if (!material.HasProperty(propertyName))
            {
                // Try common color properties
                string[] commonColorProperties = { "_BaseColor", "_Color", "_TintColor" };
                string actualProperty = commonColorProperties.FirstOrDefault(p => material.HasProperty(p));
                if (actualProperty != null)
                {
                    propertyName = actualProperty;
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Material does not have property '{propertyName}'."
                    };
                }
            }

            Color previousColor = material.GetColor(propertyName);

            Undo.RecordObject(renderer, "Set Instance Material Color");
            material.SetColor(propertyName, color);
            renderer.materials = materials;

            return new
            {
                success = true,
                message = $"Color set on instanced material for '{gameObjectName}'.",
                mode = "instance",
                property = propertyName,
                previousColor = FormatColor(previousColor),
                newColor = FormatColor(color),
                note = "An instance of the material was created for this renderer."
            };
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Resolves a shader by name, checking aliases first.
        /// </summary>
        private static Shader ResolveShader(string shaderName)
        {
            // Default shader
            if (string.IsNullOrWhiteSpace(shaderName))
            {
                // Try URP first, fall back to Standard
                Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
                if (urpLit != null)
                {
                    return urpLit;
                }
                return Shader.Find("Standard");
            }

            // Check aliases
            if (ShaderAliases.TryGetValue(shaderName, out string aliasedName))
            {
                Shader aliasedShader = Shader.Find(aliasedName);
                if (aliasedShader != null)
                {
                    return aliasedShader;
                }
            }

            // Direct lookup
            return Shader.Find(shaderName);
        }

        /// <summary>
        /// Parses a color from hex string or object.
        /// Supports: "#RRGGBB", "#RRGGBBAA", "RRGGBB", {r,g,b,a}
        /// </summary>
        private static Color ParseColor(object value)
        {
            if (value == null)
            {
                throw new ArgumentException("Color value cannot be null.");
            }

            // If it's already a Color, return it
            if (value is Color colorVal)
            {
                return colorVal;
            }

            // If it's a string, parse as hex
            if (value is string hexString)
            {
                return ParseHexColor(hexString);
            }

            // If it's a dictionary or object, parse r,g,b,a
            if (value is Dictionary<string, object> dict)
            {
                return ParseColorFromDictionary(dict);
            }

            // Try to convert from JSON-like structure
            string valueString = value.ToString();

            // Check if it looks like hex
            if (valueString.StartsWith("#") || (valueString.Length >= 6 && IsHexString(valueString)))
            {
                return ParseHexColor(valueString);
            }

            // Try parsing as JSON object
            try
            {
                if (valueString.Contains("{"))
                {
                    // Parse simple JSON object format
                    var colorDict = ParseSimpleColorJson(valueString);
                    if (colorDict != null)
                    {
                        return ParseColorFromDictionary(colorDict);
                    }
                }
            }
            catch
            {
                // Fall through to error
            }

            throw new ArgumentException($"Cannot parse color from value: {value}");
        }

        /// <summary>
        /// Parses a hex color string.
        /// </summary>
        private static Color ParseHexColor(string hexString)
        {
            // Remove leading #
            if (hexString.StartsWith("#"))
            {
                hexString = hexString.Substring(1);
            }

            if (hexString.Length == 6)
            {
                // RGB format
                if (ColorUtility.TryParseHtmlString("#" + hexString, out Color color))
                {
                    return color;
                }
            }
            else if (hexString.Length == 8)
            {
                // RGBA format
                if (ColorUtility.TryParseHtmlString("#" + hexString, out Color color))
                {
                    return color;
                }
            }

            throw new ArgumentException($"Invalid hex color format: {hexString}");
        }

        /// <summary>
        /// Parses color from a dictionary with r,g,b,a keys.
        /// </summary>
        private static Color ParseColorFromDictionary(Dictionary<string, object> dict)
        {
            float r = dict.TryGetValue("r", out object rVal) ? Convert.ToSingle(rVal) : 0f;
            float g = dict.TryGetValue("g", out object gVal) ? Convert.ToSingle(gVal) : 0f;
            float b = dict.TryGetValue("b", out object bVal) ? Convert.ToSingle(bVal) : 0f;
            float a = dict.TryGetValue("a", out object aVal) ? Convert.ToSingle(aVal) : 1f;

            // Normalize if values are in 0-255 range
            if (r > 1f || g > 1f || b > 1f)
            {
                r = r / UnityConstants.ColorByteMax;
                g = g / UnityConstants.ColorByteMax;
                b = b / UnityConstants.ColorByteMax;
                if (a > 1f)
                {
                    a = a / UnityConstants.ColorByteMax;
                }
            }

            return new Color(r, g, b, a);
        }

        /// <summary>
        /// Parses a simple JSON object for color values.
        /// </summary>
        private static Dictionary<string, object> ParseSimpleColorJson(string json)
        {
            var result = new Dictionary<string, object>();
            json = json.Trim().Trim('{', '}');
            var parts = json.Split(',');

            foreach (var part in parts)
            {
                var keyValue = part.Split(':');
                if (keyValue.Length == 2)
                {
                    string key = keyValue[0].Trim().Trim('"', '\'').ToLowerInvariant();
                    string valueStr = keyValue[1].Trim().Trim('"', '\'');

                    if (float.TryParse(valueStr, out float floatValue))
                    {
                        result[key] = floatValue;
                    }
                }
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Checks if a string contains only hex characters.
        /// </summary>
        private static bool IsHexString(string value)
        {
            foreach (char c in value)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Formats a color for output.
        /// </summary>
        private static object FormatColor(Color color)
        {
            return new
            {
                r = color.r,
                g = color.g,
                b = color.b,
                a = color.a,
                hex = ColorUtility.ToHtmlStringRGBA(color)
            };
        }

        /// <summary>
        /// Parses a Vector4 from various input formats.
        /// </summary>
        private static Vector4 ParseVector4(object value)
        {
            if (value is Vector4 v4)
            {
                return v4;
            }

            if (value is Dictionary<string, object> dict)
            {
                float x = dict.TryGetValue("x", out object xVal) ? Convert.ToSingle(xVal) : 0f;
                float y = dict.TryGetValue("y", out object yVal) ? Convert.ToSingle(yVal) : 0f;
                float z = dict.TryGetValue("z", out object zVal) ? Convert.ToSingle(zVal) : 0f;
                float w = dict.TryGetValue("w", out object wVal) ? Convert.ToSingle(wVal) : 0f;
                return new Vector4(x, y, z, w);
            }

            if (value is IList<object> list)
            {
                float x = list.Count > 0 ? Convert.ToSingle(list[0]) : 0f;
                float y = list.Count > 1 ? Convert.ToSingle(list[1]) : 0f;
                float z = list.Count > 2 ? Convert.ToSingle(list[2]) : 0f;
                float w = list.Count > 3 ? Convert.ToSingle(list[3]) : 0f;
                return new Vector4(x, y, z, w);
            }

            // Try parsing as JSON array
            string valueStr = value.ToString();
            if (valueStr.StartsWith("["))
            {
                valueStr = valueStr.Trim('[', ']');
                var parts = valueStr.Split(',');
                float x = parts.Length > 0 && float.TryParse(parts[0].Trim(), out float xv) ? xv : 0f;
                float y = parts.Length > 1 && float.TryParse(parts[1].Trim(), out float yv) ? yv : 0f;
                float z = parts.Length > 2 && float.TryParse(parts[2].Trim(), out float zv) ? zv : 0f;
                float w = parts.Length > 3 && float.TryParse(parts[3].Trim(), out float wv) ? wv : 0f;
                return new Vector4(x, y, z, w);
            }

            throw new ArgumentException($"Cannot parse Vector4 from value: {value}");
        }

        /// <summary>
        /// Resolves a texture from an asset path.
        /// </summary>
        private static Texture ResolveTexture(string texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath) || texturePath.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string normalizedPath = PathUtilities.NormalizePath(texturePath);
            Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(normalizedPath);

            if (texture == null)
            {
                throw new ArgumentException($"Texture not found at '{normalizedPath}'.");
            }

            return texture;
        }

        /// <summary>
        /// Gets the shader property type for a property name.
        /// </summary>
        private static ShaderPropertyType GetPropertyType(Shader shader, string propertyName)
        {
            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                if (shader.GetPropertyName(i) == propertyName)
                {
                    return shader.GetPropertyType(i);
                }
            }
            return ShaderPropertyType.Float; // Default fallback
        }

        /// <summary>
        /// Gets all property names for a material's shader.
        /// </summary>
        private static string[] GetPropertyNames(Material material)
        {
            Shader shader = material.shader;
            int propertyCount = shader.GetPropertyCount();
            string[] names = new string[propertyCount];

            for (int i = 0; i < propertyCount; i++)
            {
                names[i] = shader.GetPropertyName(i);
            }

            return names;
        }

        /// <summary>
        /// Builds basic material info.
        /// </summary>
        private static object BuildMaterialInfo(string path, Material material)
        {
            return new
            {
                path,
                name = material.name,
                shader = material.shader?.name ?? "(none)",
                guid = AssetDatabase.AssetPathToGUID(path)
            };
        }

        /// <summary>
        /// Builds detailed material info including all shader properties.
        /// </summary>
        private static object BuildDetailedMaterialInfo(string path, Material material)
        {
            Shader shader = material.shader;
            int propertyCount = shader.GetPropertyCount();
            var properties = new List<object>();

            for (int i = 0; i < propertyCount; i++)
            {
                string propertyName = shader.GetPropertyName(i);
                ShaderPropertyType propertyType = shader.GetPropertyType(i);
                string description = shader.GetPropertyDescription(i);

                object currentValue = null;
                try
                {
                    currentValue = propertyType switch
                    {
                        ShaderPropertyType.Color => FormatColor(material.GetColor(propertyName)),
                        ShaderPropertyType.Float or ShaderPropertyType.Range => material.GetFloat(propertyName),
                        ShaderPropertyType.Vector => FormatVector4(material.GetVector(propertyName)),
                        ShaderPropertyType.Texture => material.GetTexture(propertyName)?.name ?? "(none)",
                        ShaderPropertyType.Int => material.GetInt(propertyName),
                        _ => "(unknown)"
                    };
                }
                catch
                {
                    currentValue = "(error reading value)";
                }

                var propertyInfo = new Dictionary<string, object>
                {
                    { "name", propertyName },
                    { "type", propertyType.ToString() },
                    { "value", currentValue }
                };

                if (!string.IsNullOrEmpty(description))
                {
                    propertyInfo["description"] = description;
                }

                if (propertyType == ShaderPropertyType.Range)
                {
                    var rangeLimits = shader.GetPropertyRangeLimits(i);
                    propertyInfo["min"] = rangeLimits.x;
                    propertyInfo["max"] = rangeLimits.y;
                }

                properties.Add(propertyInfo);
            }

            // Get render queue
            int renderQueue = material.renderQueue;
            string renderQueueName = renderQueue switch
            {
                < 2000 => "Background",
                < 2500 => "Geometry",
                < 3000 => "AlphaTest",
                < 3500 => "Transparent",
                _ => "Overlay"
            };

            // Get enabled keywords
            string[] keywords = material.shaderKeywords;

            return new
            {
                path,
                name = material.name,
                guid = AssetDatabase.AssetPathToGUID(path),
                shader = shader.name,
                renderQueue,
                renderQueueName,
                enableInstancing = material.enableInstancing,
                doubleSidedGI = material.doubleSidedGI,
                keywords = keywords.Length > 0 ? keywords : null,
                propertyCount,
                properties
            };
        }

        /// <summary>
        /// Formats a Vector4 for output.
        /// </summary>
        private static object FormatVector4(Vector4 vector)
        {
            return new
            {
                x = vector.x,
                y = vector.y,
                z = vector.z,
                w = vector.w
            };
        }

        /// <summary>
        /// Finds a GameObject by name or path.
        /// </summary>
        private static GameObject FindGameObject(string nameOrPath)
        {
            // First try direct find by name
            GameObject gameObject = GameObject.Find(nameOrPath);
            if (gameObject != null)
            {
                return gameObject;
            }

            // Try finding by path (handles "/" in name)
            if (nameOrPath.Contains("/"))
            {
                gameObject = GameObject.Find(nameOrPath);
                if (gameObject != null)
                {
                    return gameObject;
                }
            }

            // Search all root objects
            foreach (GameObject rootObject in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (rootObject.name == nameOrPath)
                {
                    return rootObject;
                }

                // Search children
                Transform found = rootObject.transform.Find(nameOrPath);
                if (found != null)
                {
                    return found.gameObject;
                }

                // Recursive search by name only (not path)
                gameObject = FindChildRecursive(rootObject.transform, nameOrPath);
                if (gameObject != null)
                {
                    return gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively searches for a child with the given name.
        /// </summary>
        private static GameObject FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child.gameObject;
                }

                GameObject found = FindChildRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        #endregion
    }
}
