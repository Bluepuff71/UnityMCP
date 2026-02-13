using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for managing shaders: get info, list, find, and manage shader keywords.
    /// </summary>
    public static class ManageShader
    {
        /// <summary>
        /// Manages shaders: get info, list, find, and manage shader keywords.
        /// </summary>
        /// <param name="action">The action to perform: get, list, find, get_keywords, set_keywords</param>
        /// <param name="shaderPath">Asset path to shader file</param>
        /// <param name="shaderName">Shader name (e.g., "Standard", "URP/Lit")</param>
        /// <param name="folderPath">Folder to search in for list/find</param>
        /// <param name="searchPattern">Pattern to search for (find action)</param>
        /// <param name="searchType">Type of search: name, property, keyword</param>
        /// <param name="keywords">Array of keyword names to enable/disable</param>
        /// <param name="enable">Whether to enable (true) or disable (false) keywords</param>
        /// <param name="materialPath">Material path for per-material keywords</param>
        /// <returns>Result object indicating success or failure with appropriate data.</returns>
        [MCPTool("manage_shader", "Manage shaders: get info, list, find, manage keywords", Category = "Asset", DestructiveHint = true)]
        public static object Execute(
            [MCPParam("action", "Action: get, list, find, get_keywords, set_keywords", required: true, Enum = new[] { "get", "list", "find", "get_keywords", "set_keywords" })] string action,
            [MCPParam("shader_path", "Asset path to shader file")] string shaderPath = null,
            [MCPParam("shader_name", "Shader name (e.g., Standard, URP/Lit)")] string shaderName = null,
            [MCPParam("folder_path", "Folder to search in for list/find")] string folderPath = null,
            [MCPParam("search_pattern", "Pattern to search for (find action)")] string searchPattern = null,
            [MCPParam("search_type", "Type of search: name, property, keyword")] string searchType = "name",
            [MCPParam("keywords", "Array of keyword names to enable/disable")] List<object> keywords = null,
            [MCPParam("enable", "Enable (true) or disable (false) keywords")] bool enable = true,
            [MCPParam("material_path", "Material path for per-material keywords")] string materialPath = null)
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
                    "get" => HandleGet(shaderPath, shaderName),
                    "list" => HandleList(folderPath),
                    "find" => HandleFind(searchPattern, searchType, folderPath),
                    "get_keywords" => HandleGetKeywords(shaderPath, shaderName, materialPath),
                    "set_keywords" => HandleSetKeywords(keywords, enable, materialPath),
                    _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: get, list, find, get_keywords, set_keywords")
                };
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageShader] Error executing action '{action}': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action '{action}': {exception.Message}"
                };
            }
        }

        #region Action Handlers

        /// <summary>
        /// Gets detailed information about a shader.
        /// </summary>
        private static object HandleGet(string shaderPath, string shaderName)
        {
            Shader shader = ResolveShader(shaderPath, shaderName);
            if (shader == null)
            {
                return new
                {
                    success = false,
                    error = $"Shader not found. Specify shader_path or shader_name."
                };
            }

            return new
            {
                success = true,
                shader = BuildDetailedShaderInfo(shader)
            };
        }

        /// <summary>
        /// Lists shaders in the project or a specific folder.
        /// </summary>
        private static object HandleList(string folderPath)
        {
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

            // Find all shader assets
            string[] guids = searchFolders != null
                ? AssetDatabase.FindAssets("t:Shader", searchFolders)
                : AssetDatabase.FindAssets("t:Shader");

            var shaders = new List<object>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);

                if (shader == null)
                {
                    continue;
                }

                shaders.Add(new
                {
                    path,
                    name = shader.name,
                    guid,
                    propertyCount = shader.GetPropertyCount(),
                    passCount = shader.passCount,
                    isSupported = shader.isSupported
                });
            }

            // Also include built-in shaders that are commonly used
            var builtInShaders = new List<object>();
            string[] commonBuiltInShaders = new[]
            {
                "Standard",
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Particles/Lit",
                "Universal Render Pipeline/Particles/Unlit",
                "Sprites/Default",
                "UI/Default",
                "Hidden/InternalErrorShader"
            };

            foreach (string builtInName in commonBuiltInShaders)
            {
                Shader builtIn = Shader.Find(builtInName);
                if (builtIn != null)
                {
                    builtInShaders.Add(new
                    {
                        name = builtIn.name,
                        isBuiltIn = true,
                        propertyCount = builtIn.GetPropertyCount(),
                        passCount = builtIn.passCount,
                        isSupported = builtIn.isSupported
                    });
                }
            }

            return new
            {
                success = true,
                folder = folderPath ?? "(all)",
                projectShaderCount = shaders.Count,
                projectShaders = shaders,
                builtInShaderCount = builtInShaders.Count,
                builtInShaders
            };
        }

        /// <summary>
        /// Finds shaders by name pattern, property, or keyword.
        /// </summary>
        private static object HandleFind(string searchPattern, string searchType, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(searchPattern))
            {
                throw MCPException.InvalidParams("The 'search_pattern' parameter is required for find action.");
            }

            string normalizedSearchType = searchType?.ToLowerInvariant().Trim() ?? "name";

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

            // Find all shader assets
            string[] guids = searchFolders != null
                ? AssetDatabase.FindAssets("t:Shader", searchFolders)
                : AssetDatabase.FindAssets("t:Shader");

            var matches = new List<object>();
            Regex patternRegex = null;

            try
            {
                patternRegex = new Regex(searchPattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                // If not a valid regex, treat as simple contains match
                patternRegex = null;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);

                if (shader == null)
                {
                    continue;
                }

                bool isMatch = false;
                List<string> matchReasons = new List<string>();

                switch (normalizedSearchType)
                {
                    case "name":
                        isMatch = MatchesPattern(shader.name, searchPattern, patternRegex);
                        if (isMatch)
                        {
                            matchReasons.Add($"Name matches: {shader.name}");
                        }
                        break;

                    case "property":
                        for (int i = 0; i < shader.GetPropertyCount(); i++)
                        {
                            string propertyName = shader.GetPropertyName(i);
                            string propertyDescription = shader.GetPropertyDescription(i);
                            if (MatchesPattern(propertyName, searchPattern, patternRegex) ||
                                MatchesPattern(propertyDescription, searchPattern, patternRegex))
                            {
                                isMatch = true;
                                matchReasons.Add($"Property: {propertyName}");
                            }
                        }
                        break;

                    case "keyword":
                        // Get shader keywords from the shader's global keywords
                        var shaderKeywords = GetShaderKeywords(shader);
                        foreach (var keyword in shaderKeywords)
                        {
                            if (MatchesPattern(keyword, searchPattern, patternRegex))
                            {
                                isMatch = true;
                                matchReasons.Add($"Keyword: {keyword}");
                            }
                        }
                        break;

                    default:
                        throw MCPException.InvalidParams($"Unknown search_type: '{searchType}'. Valid types: name, property, keyword");
                }

                if (isMatch)
                {
                    matches.Add(new
                    {
                        path,
                        name = shader.name,
                        guid,
                        matchReasons
                    });
                }
            }

            return new
            {
                success = true,
                searchPattern,
                searchType = normalizedSearchType,
                folder = folderPath ?? "(all)",
                matchCount = matches.Count,
                matches
            };
        }

        /// <summary>
        /// Gets shader keywords (global or per-material).
        /// </summary>
        private static object HandleGetKeywords(string shaderPath, string shaderName, string materialPath)
        {
            // If material path is provided, get material keywords
            if (!string.IsNullOrWhiteSpace(materialPath))
            {
                string normalizedMaterialPath = PathUtilities.NormalizePath(materialPath);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(normalizedMaterialPath);

                if (material == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Material not found at '{normalizedMaterialPath}'."
                    };
                }

                return new
                {
                    success = true,
                    materialPath = normalizedMaterialPath,
                    materialName = material.name,
                    shaderName = material.shader?.name ?? "(none)",
                    enabledKeywords = material.shaderKeywords,
                    enabledKeywordCount = material.shaderKeywords.Length
                };
            }

            // Get global shader keywords
            var globalKeywords = Shader.globalKeywords;
            var enabledGlobalKeywords = new List<string>();
            var disabledGlobalKeywords = new List<string>();

            foreach (var keyword in globalKeywords)
            {
                if (Shader.IsKeywordEnabled(keyword))
                {
                    enabledGlobalKeywords.Add(keyword.name);
                }
                else
                {
                    disabledGlobalKeywords.Add(keyword.name);
                }
            }

            // If shader is specified, also get shader-specific info
            object shaderInfo = null;
            if (!string.IsNullOrWhiteSpace(shaderPath) || !string.IsNullOrWhiteSpace(shaderName))
            {
                Shader shader = ResolveShader(shaderPath, shaderName);
                if (shader != null)
                {
                    var shaderKeywords = GetShaderKeywords(shader);
                    shaderInfo = new
                    {
                        name = shader.name,
                        keywords = shaderKeywords
                    };
                }
            }

            return new
            {
                success = true,
                globalKeywords = new
                {
                    enabledCount = enabledGlobalKeywords.Count,
                    enabled = enabledGlobalKeywords,
                    disabledCount = disabledGlobalKeywords.Count,
                    disabled = disabledGlobalKeywords
                },
                shader = shaderInfo
            };
        }

        /// <summary>
        /// Sets shader keywords (global or per-material).
        /// </summary>
        private static object HandleSetKeywords(List<object> keywords, bool enable, string materialPath)
        {
            if (keywords == null || keywords.Count == 0)
            {
                throw MCPException.InvalidParams("The 'keywords' parameter is required for set_keywords action.");
            }

            var keywordStrings = keywords.Select(k => k?.ToString()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();

            if (keywordStrings.Count == 0)
            {
                throw MCPException.InvalidParams("No valid keywords provided.");
            }

            // Per-material keywords
            if (!string.IsNullOrWhiteSpace(materialPath))
            {
                string normalizedMaterialPath = PathUtilities.NormalizePath(materialPath);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(normalizedMaterialPath);

                if (material == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Material not found at '{normalizedMaterialPath}'."
                    };
                }

                Undo.RecordObject(material, $"{(enable ? "Enable" : "Disable")} Material Keywords");

                var results = new List<object>();
                foreach (string keyword in keywordStrings)
                {
                    bool wasEnabled = material.IsKeywordEnabled(keyword);

                    if (enable)
                    {
                        material.EnableKeyword(keyword);
                    }
                    else
                    {
                        material.DisableKeyword(keyword);
                    }

                    results.Add(new
                    {
                        keyword,
                        action = enable ? "enabled" : "disabled",
                        previousState = wasEnabled ? "enabled" : "disabled"
                    });
                }

                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"{(enable ? "Enabled" : "Disabled")} {keywordStrings.Count} keyword(s) on material.",
                    materialPath = normalizedMaterialPath,
                    materialName = material.name,
                    results,
                    currentKeywords = material.shaderKeywords
                };
            }

            // Global keywords
            var globalResults = new List<object>();
            foreach (string keyword in keywordStrings)
            {
                // Check if keyword exists as a global keyword
                GlobalKeyword globalKeyword = GlobalKeyword.Create(keyword);
                bool wasEnabled = Shader.IsKeywordEnabled(globalKeyword);

                if (enable)
                {
                    Shader.EnableKeyword(globalKeyword);
                }
                else
                {
                    Shader.DisableKeyword(globalKeyword);
                }

                globalResults.Add(new
                {
                    keyword,
                    action = enable ? "enabled" : "disabled",
                    previousState = wasEnabled ? "enabled" : "disabled"
                });
            }

            return new
            {
                success = true,
                message = $"{(enable ? "Enabled" : "Disabled")} {keywordStrings.Count} global shader keyword(s).",
                scope = "global",
                results = globalResults,
                note = "Global keyword changes affect all materials using shaders with these keywords."
            };
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Resolves a shader from path or name.
        /// </summary>
        private static Shader ResolveShader(string shaderPath, string shaderName)
        {
            // Try path first
            if (!string.IsNullOrWhiteSpace(shaderPath))
            {
                string normalizedPath = PathUtilities.NormalizePath(shaderPath);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(normalizedPath);
                if (shader != null)
                {
                    return shader;
                }
            }

            // Try name
            if (!string.IsNullOrWhiteSpace(shaderName))
            {
                Shader shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    return shader;
                }

                // Try common aliases
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Standard", "Standard" },
                    { "URP/Lit", "Universal Render Pipeline/Lit" },
                    { "URP/Unlit", "Universal Render Pipeline/Unlit" },
                    { "URP/SimpleLit", "Universal Render Pipeline/Simple Lit" },
                    { "URP/BakedLit", "Universal Render Pipeline/Baked Lit" }
                };

                if (aliases.TryGetValue(shaderName, out string fullName))
                {
                    return Shader.Find(fullName);
                }
            }

            return null;
        }

        /// <summary>
        /// Builds detailed shader information.
        /// </summary>
        private static object BuildDetailedShaderInfo(Shader shader)
        {
            string assetPath = AssetDatabase.GetAssetPath(shader);
            bool isBuiltIn = string.IsNullOrEmpty(assetPath) || assetPath.StartsWith("Resources/");

            // Get properties
            int propertyCount = shader.GetPropertyCount();
            var properties = new List<object>();

            for (int i = 0; i < propertyCount; i++)
            {
                string propertyName = shader.GetPropertyName(i);
                ShaderPropertyType propertyType = shader.GetPropertyType(i);
                string description = shader.GetPropertyDescription(i);
                ShaderPropertyFlags flags = shader.GetPropertyFlags(i);

                var propertyInfo = new Dictionary<string, object>
                {
                    { "name", propertyName },
                    { "type", propertyType.ToString() },
                    { "description", description }
                };

                // Add range info for Range type
                if (propertyType == ShaderPropertyType.Range)
                {
                    Vector2 rangeLimits = shader.GetPropertyRangeLimits(i);
                    propertyInfo["min"] = rangeLimits.x;
                    propertyInfo["max"] = rangeLimits.y;
                    propertyInfo["defaultValue"] = shader.GetPropertyDefaultFloatValue(i);
                }
                else if (propertyType == ShaderPropertyType.Float)
                {
                    propertyInfo["defaultValue"] = shader.GetPropertyDefaultFloatValue(i);
                }
                else if (propertyType == ShaderPropertyType.Int)
                {
                    propertyInfo["defaultValue"] = shader.GetPropertyDefaultIntValue(i);
                }
                else if (propertyType == ShaderPropertyType.Vector)
                {
                    Vector4 defaultVector = shader.GetPropertyDefaultVectorValue(i);
                    propertyInfo["defaultValue"] = new { x = defaultVector.x, y = defaultVector.y, z = defaultVector.z, w = defaultVector.w };
                }
                else if (propertyType == ShaderPropertyType.Texture)
                {
                    propertyInfo["textureDimension"] = shader.GetPropertyTextureDimension(i).ToString();
                    propertyInfo["defaultTextureName"] = shader.GetPropertyTextureDefaultName(i);
                }

                // Add flags if not None
                if (flags != ShaderPropertyFlags.None)
                {
                    var flagsList = new List<string>();
                    if ((flags & ShaderPropertyFlags.HideInInspector) != 0) flagsList.Add("HideInInspector");
                    if ((flags & ShaderPropertyFlags.PerRendererData) != 0) flagsList.Add("PerRendererData");
                    if ((flags & ShaderPropertyFlags.NoScaleOffset) != 0) flagsList.Add("NoScaleOffset");
                    if ((flags & ShaderPropertyFlags.Normal) != 0) flagsList.Add("Normal");
                    if ((flags & ShaderPropertyFlags.HDR) != 0) flagsList.Add("HDR");
                    if ((flags & ShaderPropertyFlags.Gamma) != 0) flagsList.Add("Gamma");
                    if ((flags & ShaderPropertyFlags.NonModifiableTextureData) != 0) flagsList.Add("NonModifiableTextureData");
                    if ((flags & ShaderPropertyFlags.MainTexture) != 0) flagsList.Add("MainTexture");
                    if ((flags & ShaderPropertyFlags.MainColor) != 0) flagsList.Add("MainColor");
                    propertyInfo["flags"] = flagsList;
                }

                properties.Add(propertyInfo);
            }

            // Get passes
            int passCount = shader.passCount;
            var passes = new List<object>();

            for (int i = 0; i < passCount; i++)
            {
                passes.Add(new
                {
                    index = i,
                    name = $"Pass {i}"
                });
            }

            // Get shader keywords
            var shaderKeywords = GetShaderKeywords(shader);

            // Build result
            var result = new Dictionary<string, object>
            {
                { "name", shader.name },
                { "isSupported", shader.isSupported },
                { "isBuiltIn", isBuiltIn },
                { "renderQueue", shader.renderQueue },
                { "propertyCount", propertyCount },
                { "properties", properties },
                { "passCount", passCount },
                { "passes", passes },
                { "keywordCount", shaderKeywords.Count },
                { "keywords", shaderKeywords }
            };

            if (!isBuiltIn && !string.IsNullOrEmpty(assetPath))
            {
                result["path"] = assetPath;
                result["guid"] = AssetDatabase.AssetPathToGUID(assetPath);
            }

            // Note: ShaderUtil.GetSubshaderCount may not be available in all Unity versions

            return result;
        }

        /// <summary>
        /// Gets keywords defined in a shader.
        /// </summary>
        private static List<string> GetShaderKeywords(Shader shader)
        {
            var keywords = new HashSet<string>();

            // Note: Detailed shader keyword extraction via ShaderUtil.GetShaderData may not be available in all Unity versions

            // Also check global keywords that might relate to this shader
            foreach (var globalKeyword in Shader.globalKeywords)
            {
                // Include commonly used keywords
                keywords.Add(globalKeyword.name);
            }

            return keywords.OrderBy(k => k).ToList();
        }

        /// <summary>
        /// Checks if a value matches a pattern.
        /// </summary>
        private static bool MatchesPattern(string value, string pattern, Regex patternRegex)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (patternRegex != null)
            {
                return patternRegex.IsMatch(value);
            }

            // Simple contains match
            return value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion
    }
}
