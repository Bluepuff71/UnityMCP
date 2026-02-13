using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for managing textures: get info, list, find, and modify import settings.
    /// </summary>
    public static class ManageTexture
    {
        /// <summary>
        /// Manages textures: get info, list, find, and modify import settings.
        /// </summary>
        /// <param name="action">The action to perform: get, list, find, set_import_settings</param>
        /// <param name="texturePath">Asset path to texture file</param>
        /// <param name="folderPath">Folder to search in for list/find</param>
        /// <param name="searchPattern">Pattern for find action (name pattern, dimensions, or format)</param>
        /// <param name="searchType">Type of search: name, dimension, format</param>
        /// <param name="minWidth">Minimum width filter for dimension search</param>
        /// <param name="maxWidth">Maximum width filter for dimension search</param>
        /// <param name="minHeight">Minimum height filter for dimension search</param>
        /// <param name="maxHeight">Maximum height filter for dimension search</param>
        /// <param name="format">Texture format to find</param>
        /// <param name="maxSize">Set max size for importer (32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384)</param>
        /// <param name="textureType">Set texture type (Default, NormalMap, Editor GUI, Sprite, Cursor, Cookie, Lightmap, DirectionalLightmap, Shadowmask, SingleChannel)</param>
        /// <param name="compression">Set compression (None, LowQuality, Normal, HighQuality)</param>
        /// <param name="srgb">Set sRGB (gamma) color space flag</param>
        /// <param name="generateMipmaps">Set mipmap generation</param>
        /// <param name="readable">Set read/write enabled</param>
        /// <param name="filterMode">Set filter mode (Point, Bilinear, Trilinear)</param>
        /// <param name="wrapMode">Set wrap mode (Repeat, Clamp, Mirror, MirrorOnce)</param>
        /// <returns>Result object indicating success or failure with appropriate data.</returns>
        [MCPTool("manage_texture", "Manage textures: get info, list, find, modify import settings", Category = "Asset", DestructiveHint = true)]
        public static object Execute(
            [MCPParam("action", "Action: get, list, find, set_import_settings", required: true, Enum = new[] { "get", "list", "find", "set_import_settings" })] string action,
            [MCPParam("texture_path", "Asset path to texture file")] string texturePath = null,
            [MCPParam("folder_path", "Folder to search in for list/find")] string folderPath = null,
            [MCPParam("search_pattern", "Pattern for find action")] string searchPattern = null,
            [MCPParam("search_type", "Type of search: name, dimension, format")] string searchType = "name",
            [MCPParam("min_width", "Minimum width filter for dimension search")] int? minWidth = null,
            [MCPParam("max_width", "Maximum width filter for dimension search")] int? maxWidth = null,
            [MCPParam("min_height", "Minimum height filter for dimension search")] int? minHeight = null,
            [MCPParam("max_height", "Maximum height filter for dimension search")] int? maxHeight = null,
            [MCPParam("format", "Texture format to find")] string format = null,
            [MCPParam("max_size", "Max size: 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384")] int? maxSize = null,
            [MCPParam("texture_type", "Type: Default, NormalMap, Sprite, Editor GUI, Cursor, Cookie, Lightmap, SingleChannel")] string textureType = null,
            [MCPParam("compression", "Compression: None, LowQuality, Normal, HighQuality")] string compression = null,
            [MCPParam("srgb", "sRGB (gamma) color space")] bool? srgb = null,
            [MCPParam("generate_mipmaps", "Generate mipmaps")] bool? generateMipmaps = null,
            [MCPParam("readable", "Read/Write enabled")] bool? readable = null,
            [MCPParam("filter_mode", "Filter mode: Point, Bilinear, Trilinear")] string filterMode = null,
            [MCPParam("wrap_mode", "Wrap mode: Repeat, Clamp, Mirror, MirrorOnce")] string wrapMode = null)
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
                    "get" => HandleGet(texturePath),
                    "list" => HandleList(folderPath),
                    "find" => HandleFind(searchPattern, searchType, folderPath, minWidth, maxWidth, minHeight, maxHeight, format),
                    "set_import_settings" => HandleSetImportSettings(texturePath, maxSize, textureType, compression, srgb, generateMipmaps, readable, filterMode, wrapMode),
                    _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: get, list, find, set_import_settings")
                };
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageTexture] Error executing action '{action}': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action '{action}': {exception.Message}"
                };
            }
        }

        #region Action Handlers

        /// <summary>
        /// Gets detailed information about a texture including import settings.
        /// </summary>
        private static object HandleGet(string texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
            {
                throw MCPException.InvalidParams("The 'texture_path' parameter is required for get action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(texturePath);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalizedPath);

            if (texture == null)
            {
                // Try loading as Texture (for RenderTextures, etc.)
                Texture genericTexture = AssetDatabase.LoadAssetAtPath<Texture>(normalizedPath);
                if (genericTexture == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Texture not found at '{normalizedPath}'."
                    };
                }

                // Return basic info for non-Texture2D textures
                return new
                {
                    success = true,
                    texture = BuildBasicTextureInfo(normalizedPath, genericTexture)
                };
            }

            TextureImporter textureImporter = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;

            return new
            {
                success = true,
                texture = BuildDetailedTextureInfo(normalizedPath, texture, textureImporter)
            };
        }

        /// <summary>
        /// Lists textures in the project or a specific folder.
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

            // Find all texture assets
            string[] guids = searchFolders != null
                ? AssetDatabase.FindAssets("t:Texture2D", searchFolders)
                : AssetDatabase.FindAssets("t:Texture2D");

            var textures = new List<object>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                if (texture == null)
                {
                    continue;
                }

                TextureImporter textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;

                textures.Add(new
                {
                    path,
                    name = texture.name,
                    guid,
                    width = texture.width,
                    height = texture.height,
                    format = texture.format.ToString(),
                    textureType = textureImporter?.textureType.ToString() ?? "Unknown",
                    mipmapCount = texture.mipmapCount
                });
            }

            return new
            {
                success = true,
                folder = folderPath ?? "(all)",
                textureCount = textures.Count,
                textures
            };
        }

        /// <summary>
        /// Finds textures by name pattern, dimension, or format.
        /// </summary>
        private static object HandleFind(
            string searchPattern,
            string searchType,
            string folderPath,
            int? minWidth,
            int? maxWidth,
            int? minHeight,
            int? maxHeight,
            string format)
        {
            string normalizedSearchType = searchType?.ToLowerInvariant().Trim() ?? "name";

            // Validate search parameters
            if (normalizedSearchType == "name" && string.IsNullOrWhiteSpace(searchPattern))
            {
                throw MCPException.InvalidParams("The 'search_pattern' parameter is required for name search.");
            }

            if (normalizedSearchType == "format" && string.IsNullOrWhiteSpace(format) && string.IsNullOrWhiteSpace(searchPattern))
            {
                throw MCPException.InvalidParams("The 'format' or 'search_pattern' parameter is required for format search.");
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

            // Find all texture assets
            string[] guids = searchFolders != null
                ? AssetDatabase.FindAssets("t:Texture2D", searchFolders)
                : AssetDatabase.FindAssets("t:Texture2D");

            var matches = new List<object>();
            Regex patternRegex = null;

            if (!string.IsNullOrWhiteSpace(searchPattern))
            {
                try
                {
                    patternRegex = new Regex(searchPattern, RegexOptions.IgnoreCase);
                }
                catch
                {
                    // If not a valid regex, treat as simple contains match
                    patternRegex = null;
                }
            }

            // Use format from parameter or search pattern
            string targetFormat = !string.IsNullOrWhiteSpace(format) ? format : searchPattern;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                if (texture == null)
                {
                    continue;
                }

                bool isMatch = false;
                List<string> matchReasons = new List<string>();

                switch (normalizedSearchType)
                {
                    case "name":
                        isMatch = MatchesPattern(texture.name, searchPattern, patternRegex) ||
                                  MatchesPattern(path, searchPattern, patternRegex);
                        if (isMatch)
                        {
                            matchReasons.Add($"Name matches: {texture.name}");
                        }
                        break;

                    case "dimension":
                        bool widthMatch = true;
                        bool heightMatch = true;

                        if (minWidth.HasValue && texture.width < minWidth.Value)
                        {
                            widthMatch = false;
                        }
                        if (maxWidth.HasValue && texture.width > maxWidth.Value)
                        {
                            widthMatch = false;
                        }
                        if (minHeight.HasValue && texture.height < minHeight.Value)
                        {
                            heightMatch = false;
                        }
                        if (maxHeight.HasValue && texture.height > maxHeight.Value)
                        {
                            heightMatch = false;
                        }

                        isMatch = widthMatch && heightMatch;
                        if (isMatch)
                        {
                            matchReasons.Add($"Dimensions: {texture.width}x{texture.height}");
                        }
                        break;

                    case "format":
                        string textureFormat = texture.format.ToString();
                        if (textureFormat.IndexOf(targetFormat, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isMatch = true;
                            matchReasons.Add($"Format: {textureFormat}");
                        }
                        break;

                    default:
                        throw MCPException.InvalidParams($"Unknown search_type: '{searchType}'. Valid types: name, dimension, format");
                }

                if (isMatch)
                {
                    matches.Add(new
                    {
                        path,
                        name = texture.name,
                        guid,
                        width = texture.width,
                        height = texture.height,
                        format = texture.format.ToString(),
                        matchReasons
                    });
                }
            }

            var resultInfo = new Dictionary<string, object>
            {
                { "success", true },
                { "searchType", normalizedSearchType },
                { "folder", folderPath ?? "(all)" },
                { "matchCount", matches.Count },
                { "matches", matches }
            };

            // Add search criteria to result
            if (!string.IsNullOrWhiteSpace(searchPattern))
            {
                resultInfo["searchPattern"] = searchPattern;
            }
            if (minWidth.HasValue)
            {
                resultInfo["minWidth"] = minWidth.Value;
            }
            if (maxWidth.HasValue)
            {
                resultInfo["maxWidth"] = maxWidth.Value;
            }
            if (minHeight.HasValue)
            {
                resultInfo["minHeight"] = minHeight.Value;
            }
            if (maxHeight.HasValue)
            {
                resultInfo["maxHeight"] = maxHeight.Value;
            }
            if (!string.IsNullOrWhiteSpace(format))
            {
                resultInfo["format"] = format;
            }

            return resultInfo;
        }

        /// <summary>
        /// Modifies texture importer settings.
        /// </summary>
        private static object HandleSetImportSettings(
            string texturePath,
            int? maxSize,
            string textureType,
            string compression,
            bool? srgb,
            bool? generateMipmaps,
            bool? readable,
            string filterMode,
            string wrapMode)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
            {
                throw MCPException.InvalidParams("The 'texture_path' parameter is required for set_import_settings action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(texturePath);
            TextureImporter textureImporter = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;

            if (textureImporter == null)
            {
                return new
                {
                    success = false,
                    error = $"TextureImporter not found at '{normalizedPath}'. Ensure the path points to an importable texture asset."
                };
            }

            var changes = new List<object>();
            bool hasChanges = false;

            try
            {
                // Set max size
                if (maxSize.HasValue)
                {
                    int[] validSizes = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 };
                    if (!validSizes.Contains(maxSize.Value))
                    {
                        return new
                        {
                            success = false,
                            error = $"Invalid max_size: {maxSize.Value}. Valid values: {string.Join(", ", validSizes)}"
                        };
                    }

                    int previousMaxSize = textureImporter.maxTextureSize;
                    textureImporter.maxTextureSize = maxSize.Value;
                    changes.Add(new { setting = "maxSize", previousValue = previousMaxSize, newValue = maxSize.Value });
                    hasChanges = true;
                }

                // Set texture type
                if (!string.IsNullOrWhiteSpace(textureType))
                {
                    TextureImporterType? parsedType = ParseTextureType(textureType);
                    if (!parsedType.HasValue)
                    {
                        return new
                        {
                            success = false,
                            error = $"Invalid texture_type: '{textureType}'. Valid types: Default, NormalMap, Sprite, Editor GUI, Cursor, Cookie, Lightmap, DirectionalLightmap, Shadowmask, SingleChannel"
                        };
                    }

                    TextureImporterType previousType = textureImporter.textureType;
                    textureImporter.textureType = parsedType.Value;
                    changes.Add(new { setting = "textureType", previousValue = previousType.ToString(), newValue = parsedType.Value.ToString() });
                    hasChanges = true;
                }

                // Set compression
                if (!string.IsNullOrWhiteSpace(compression))
                {
                    TextureImporterCompression? parsedCompression = ParseCompression(compression);
                    if (!parsedCompression.HasValue)
                    {
                        return new
                        {
                            success = false,
                            error = $"Invalid compression: '{compression}'. Valid values: None, LowQuality, Normal, HighQuality"
                        };
                    }

                    TextureImporterCompression previousCompression = textureImporter.textureCompression;
                    textureImporter.textureCompression = parsedCompression.Value;
                    changes.Add(new { setting = "compression", previousValue = previousCompression.ToString(), newValue = parsedCompression.Value.ToString() });
                    hasChanges = true;
                }

                // Set sRGB
                if (srgb.HasValue)
                {
                    bool previousSrgb = textureImporter.sRGBTexture;
                    textureImporter.sRGBTexture = srgb.Value;
                    changes.Add(new { setting = "sRGB", previousValue = previousSrgb, newValue = srgb.Value });
                    hasChanges = true;
                }

                // Set mipmap generation
                if (generateMipmaps.HasValue)
                {
                    bool previousMipmaps = textureImporter.mipmapEnabled;
                    textureImporter.mipmapEnabled = generateMipmaps.Value;
                    changes.Add(new { setting = "generateMipmaps", previousValue = previousMipmaps, newValue = generateMipmaps.Value });
                    hasChanges = true;
                }

                // Set readable (read/write enabled)
                if (readable.HasValue)
                {
                    bool previousReadable = textureImporter.isReadable;
                    textureImporter.isReadable = readable.Value;
                    changes.Add(new { setting = "readable", previousValue = previousReadable, newValue = readable.Value });
                    hasChanges = true;
                }

                // Set filter mode
                if (!string.IsNullOrWhiteSpace(filterMode))
                {
                    FilterMode? parsedFilterMode = ParseFilterMode(filterMode);
                    if (!parsedFilterMode.HasValue)
                    {
                        return new
                        {
                            success = false,
                            error = $"Invalid filter_mode: '{filterMode}'. Valid values: Point, Bilinear, Trilinear"
                        };
                    }

                    FilterMode previousFilterMode = textureImporter.filterMode;
                    textureImporter.filterMode = parsedFilterMode.Value;
                    changes.Add(new { setting = "filterMode", previousValue = previousFilterMode.ToString(), newValue = parsedFilterMode.Value.ToString() });
                    hasChanges = true;
                }

                // Set wrap mode
                if (!string.IsNullOrWhiteSpace(wrapMode))
                {
                    TextureWrapMode? parsedWrapMode = ParseWrapMode(wrapMode);
                    if (!parsedWrapMode.HasValue)
                    {
                        return new
                        {
                            success = false,
                            error = $"Invalid wrap_mode: '{wrapMode}'. Valid values: Repeat, Clamp, Mirror, MirrorOnce"
                        };
                    }

                    TextureWrapMode previousWrapMode = textureImporter.wrapMode;
                    textureImporter.wrapMode = parsedWrapMode.Value;
                    changes.Add(new { setting = "wrapMode", previousValue = previousWrapMode.ToString(), newValue = parsedWrapMode.Value.ToString() });
                    hasChanges = true;
                }

                if (!hasChanges)
                {
                    return new
                    {
                        success = false,
                        error = "No import settings were specified to change. Provide at least one setting (max_size, texture_type, compression, srgb, generate_mipmaps, readable, filter_mode, wrap_mode)."
                    };
                }

                // Save and reimport
                textureImporter.SaveAndReimport();

                // Reload texture to get updated info
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalizedPath);

                return new
                {
                    success = true,
                    message = $"Updated {changes.Count} import setting(s) and reimported texture.",
                    path = normalizedPath,
                    changes,
                    texture = texture != null ? BuildDetailedTextureInfo(normalizedPath, texture, textureImporter) : null
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error setting import settings: {exception.Message}"
                };
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Builds basic texture information for non-Texture2D textures.
        /// </summary>
        private static object BuildBasicTextureInfo(string path, Texture texture)
        {
            return new
            {
                path,
                name = texture.name,
                guid = AssetDatabase.AssetPathToGUID(path),
                type = texture.GetType().Name,
                width = texture.width,
                height = texture.height,
                dimension = texture.dimension.ToString(),
                filterMode = texture.filterMode.ToString(),
                wrapMode = texture.wrapMode.ToString(),
                anisoLevel = texture.anisoLevel
            };
        }

        /// <summary>
        /// Builds detailed texture information including import settings.
        /// </summary>
        private static object BuildDetailedTextureInfo(string path, Texture2D texture, TextureImporter textureImporter)
        {
            var info = new Dictionary<string, object>
            {
                { "path", path },
                { "name", texture.name },
                { "guid", AssetDatabase.AssetPathToGUID(path) },
                { "width", texture.width },
                { "height", texture.height },
                { "format", texture.format.ToString() },
                { "mipmapCount", texture.mipmapCount },
                { "filterMode", texture.filterMode.ToString() },
                { "wrapMode", texture.wrapMode.ToString() },
                { "anisoLevel", texture.anisoLevel },
                { "isReadable", texture.isReadable }
            };

            // Add importer-specific info if available
            if (textureImporter != null)
            {
                info["importSettings"] = new
                {
                    textureType = textureImporter.textureType.ToString(),
                    textureShape = textureImporter.textureShape.ToString(),
                    sRGB = textureImporter.sRGBTexture,
                    alphaSource = textureImporter.alphaSource.ToString(),
                    alphaIsTransparency = textureImporter.alphaIsTransparency,
                    readable = textureImporter.isReadable,
                    mipmapEnabled = textureImporter.mipmapEnabled,
                    mipmapFilter = textureImporter.mipmapFilter.ToString(),
                    streamingMipmaps = textureImporter.streamingMipmaps,
                    borderMipmap = textureImporter.borderMipmap,
                    wrapMode = textureImporter.wrapMode.ToString(),
                    wrapModeU = textureImporter.wrapModeU.ToString(),
                    wrapModeV = textureImporter.wrapModeV.ToString(),
                    wrapModeW = textureImporter.wrapModeW.ToString(),
                    filterMode = textureImporter.filterMode.ToString(),
                    anisoLevel = textureImporter.anisoLevel,
                    maxTextureSize = textureImporter.maxTextureSize,
                    compression = textureImporter.textureCompression.ToString(),
                    crunchedCompression = textureImporter.crunchedCompression,
                    compressionQuality = textureImporter.compressionQuality
                };

                // Add sprite-specific settings if applicable
                if (textureImporter.textureType == TextureImporterType.Sprite)
                {
                    info["spriteSettings"] = new
                    {
                        spriteMode = textureImporter.spriteImportMode.ToString(),
                        pixelsPerUnit = textureImporter.spritePixelsPerUnit,
                        spritePivot = new { x = textureImporter.spritePivot.x, y = textureImporter.spritePivot.y },
                        spriteBorder = new
                        {
                            left = textureImporter.spriteBorder.x,
                            bottom = textureImporter.spriteBorder.y,
                            right = textureImporter.spriteBorder.z,
                            top = textureImporter.spriteBorder.w
                        }
                    };
                }

                // Get platform-specific settings
                var platformSettings = new List<object>();
                string[] platforms = { "Standalone", "WebGL", "iOS", "Android" };

                foreach (string platform in platforms)
                {
                    TextureImporterPlatformSettings settings = textureImporter.GetPlatformTextureSettings(platform);
                    if (settings.overridden)
                    {
                        platformSettings.Add(new
                        {
                            platform,
                            maxTextureSize = settings.maxTextureSize,
                            format = settings.format.ToString(),
                            compression = settings.textureCompression.ToString(),
                            crunchedCompression = settings.crunchedCompression,
                            compressionQuality = settings.compressionQuality
                        });
                    }
                }

                if (platformSettings.Count > 0)
                {
                    info["platformOverrides"] = platformSettings;
                }
            }

            // Get memory size estimate
            long memorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
            info["memorySizeBytes"] = memorySize;
            info["memorySizeFormatted"] = FormatBytes(memorySize);

            return info;
        }

        /// <summary>
        /// Parses texture type from string.
        /// </summary>
        private static TextureImporterType? ParseTextureType(string textureType)
        {
            string normalized = textureType.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");

            return normalized switch
            {
                "default" => TextureImporterType.Default,
                "normalmap" or "normal" => TextureImporterType.NormalMap,
                "editorgui" or "gui" or "editor" => TextureImporterType.GUI,
                "sprite" => TextureImporterType.Sprite,
                "cursor" => TextureImporterType.Cursor,
                "cookie" => TextureImporterType.Cookie,
                "lightmap" => TextureImporterType.Lightmap,
                "directionallightmap" or "directional" => TextureImporterType.DirectionalLightmap,
                "shadowmask" => TextureImporterType.Shadowmask,
                "singlechannel" or "single" => TextureImporterType.SingleChannel,
                _ => null
            };
        }

        /// <summary>
        /// Parses compression from string.
        /// </summary>
        private static TextureImporterCompression? ParseCompression(string compression)
        {
            string normalized = compression.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");

            return normalized switch
            {
                "none" or "uncompressed" => TextureImporterCompression.Uncompressed,
                "lowquality" or "low" => TextureImporterCompression.CompressedLQ,
                "normal" or "compressed" => TextureImporterCompression.Compressed,
                "highquality" or "high" => TextureImporterCompression.CompressedHQ,
                _ => null
            };
        }

        /// <summary>
        /// Parses filter mode from string.
        /// </summary>
        private static FilterMode? ParseFilterMode(string filterMode)
        {
            string normalized = filterMode.Trim().ToLowerInvariant();

            return normalized switch
            {
                "point" or "nearest" => FilterMode.Point,
                "bilinear" or "linear" => FilterMode.Bilinear,
                "trilinear" => FilterMode.Trilinear,
                _ => null
            };
        }

        /// <summary>
        /// Parses wrap mode from string.
        /// </summary>
        private static TextureWrapMode? ParseWrapMode(string wrapMode)
        {
            string normalized = wrapMode.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");

            return normalized switch
            {
                "repeat" or "tile" => TextureWrapMode.Repeat,
                "clamp" or "clamped" or "clamptoedge" => TextureWrapMode.Clamp,
                "mirror" or "mirrored" => TextureWrapMode.Mirror,
                "mirroronce" => TextureWrapMode.MirrorOnce,
                _ => null
            };
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

        /// <summary>
        /// Formats bytes as human-readable string.
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double formattedSize = bytes;
            int sizeIndex = 0;

            while (formattedSize >= 1024 && sizeIndex < sizes.Length - 1)
            {
                formattedSize /= 1024;
                sizeIndex++;
            }

            return $"{formattedSize:0.##} {sizes[sizeIndex]}";
        }

        #endregion
    }
}
