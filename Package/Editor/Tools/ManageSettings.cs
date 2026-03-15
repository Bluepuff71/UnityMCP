using System;
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Provides dynamic read/write access to Unity Project Settings and Editor Preferences.
    /// Discovers settings files at runtime rather than hardcoding categories.
    /// </summary>
    [MCPTool("manage_settings", "Manage Project Settings and Editor Preferences dynamically", Category = "Editor")]
    public static class ManageSettings
    {
        private const int MaxListResults = 50;
        private const int MaxPropertyResults = 50;
        private const int MaxSearchResults = 100;
        private const int DefaultSearchResults = 20;

        /// <summary>
        /// Properties to skip when iterating settings — these are internal Unity fields
        /// that are not useful for inspection or modification.
        /// </summary>
        private static readonly HashSet<string> SkippedProperties = new HashSet<string>
        {
            "m_Script",
            "m_ObjectHideFlags"
        };

        /// <summary>
        /// Cosmetic display name mapping for common settings files.
        /// Used only for display — does NOT limit discovery.
        /// </summary>
        private static readonly Dictionary<string, string> DisplayNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DynamicsManager", "Physics" },
            { "AudioManager", "Audio" },
            { "InputManager", "Input" },
            { "TagManager", "Tags & Layers" },
            { "TimeManager", "Time" },
            { "PlayerSettings", "Player" },
            { "QualitySettings", "Quality" },
            { "Physics2DSettings", "Physics 2D" },
            { "GraphicsSettings", "Graphics" },
            { "EditorSettings", "Editor" },
            { "EditorBuildSettings", "Build" },
            { "NavMeshAreas", "Navigation" },
            { "VFXManager", "VFX" },
            { "UnityConnectSettings", "Services" },
            { "PresetManager", "Preset Manager" },
            { "PackageManagerSettings", "Package Manager" }
        };

        #region Actions

        /// <summary>
        /// Scans ProjectSettings/*.asset and returns file name, asset type, and loadable status.
        /// </summary>
        [MCPAction("list", Description = "List all discoverable Project Settings files", ReadOnlyHint = true)]
        public static object List()
        {
            try
            {
                return HandleList();
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
                    error = $"Error listing settings: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Reads all properties of a settings file via SerializedObject.
        /// </summary>
        [MCPAction("inspect", Description = "Read all serialized properties of a settings file", ReadOnlyHint = true)]
        public static object Inspect(
            [MCPParam("settings_file", "Settings file name (e.g. 'DynamicsManager') or full path (e.g. 'ProjectSettings/DynamicsManager.asset')", required: true)] string settingsFile,
            [MCPParam("property_filter", "Optional substring to filter property names")] string propertyFilter = null)
        {
            if (string.IsNullOrEmpty(settingsFile))
                throw MCPException.InvalidParams("settings_file is required.");

            try
            {
                return HandleInspect(settingsFile, propertyFilter);
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
                    error = $"Error inspecting settings: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Writes a property value by SerializedProperty path.
        /// </summary>
        [MCPAction("set", Description = "Set a property value in a settings file")]
        public static object Set(
            [MCPParam("settings_file", "Settings file name (e.g. 'DynamicsManager') or full path", required: true)] string settingsFile,
            [MCPParam("property_path", "SerializedProperty path (e.g. 'm_Gravity.y')", required: true)] string propertyPath,
            [MCPParam("value", "The value to set", required: true)] object value = null)
        {
            if (string.IsNullOrEmpty(settingsFile))
                throw MCPException.InvalidParams("settings_file is required.");
            if (string.IsNullOrEmpty(propertyPath))
                throw MCPException.InvalidParams("property_path is required.");

            try
            {
                return HandleSet(settingsFile, propertyPath, value);
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
                    error = $"Error setting property: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Searches for a property name across all settings files.
        /// </summary>
        [MCPAction("search", Description = "Search for a property name across all settings files", ReadOnlyHint = true)]
        public static object Search(
            [MCPParam("query", "Substring to search for in property paths (case-insensitive)", required: true)] string query,
            [MCPParam("max_results", "Maximum number of results to return (default 20, max 100)", Minimum = 1, Maximum = 100)] int maxResults = DefaultSearchResults)
        {
            if (string.IsNullOrEmpty(query))
                throw MCPException.InvalidParams("query is required.");

            try
            {
                return HandleSearch(query, Mathf.Clamp(maxResults, 1, MaxSearchResults));
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
                    error = $"Error searching settings: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Reads an EditorPref by key.
        /// </summary>
        [MCPAction("get_preference", Description = "Read an EditorPrefs value by key", ReadOnlyHint = true)]
        public static object GetPreference(
            [MCPParam("key", "The EditorPrefs key to read", required: true)] string key,
            [MCPParam("type", "Value type: string, int, float, bool (default: string)")] string type = "string")
        {
            if (string.IsNullOrEmpty(key))
                throw MCPException.InvalidParams("key is required.");

            try
            {
                return HandleGetPreference(key, type);
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
                    error = $"Error reading preference: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Writes an EditorPref value.
        /// </summary>
        [MCPAction("set_preference", Description = "Write an EditorPrefs value")]
        public static object SetPreference(
            [MCPParam("key", "The EditorPrefs key to write", required: true)] string key,
            [MCPParam("value", "The value to set", required: true)] object value = null,
            [MCPParam("type", "Value type: string, int, float, bool (default: string)")] string type = "string")
        {
            if (string.IsNullOrEmpty(key))
                throw MCPException.InvalidParams("key is required.");

            try
            {
                return HandleSetPreference(key, value, type);
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
                    error = $"Error writing preference: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Deletes an EditorPref key.
        /// </summary>
        [MCPAction("delete_preference", Description = "Delete an EditorPrefs key", DestructiveHint = true)]
        public static object DeletePreference(
            [MCPParam("key", "The EditorPrefs key to delete", required: true)] string key)
        {
            if (string.IsNullOrEmpty(key))
                throw MCPException.InvalidParams("key is required.");

            try
            {
                return HandleDeletePreference(key);
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
                    error = $"Error deleting preference: {exception.Message}"
                };
            }
        }

        #endregion

        #region Handler Implementations

        private static object HandleList()
        {
            string projectSettingsPath = Path.Combine(Application.dataPath, "..", "ProjectSettings");
            string normalizedPath = Path.GetFullPath(projectSettingsPath);

            if (!Directory.Exists(normalizedPath))
            {
                return new
                {
                    success = false,
                    error = "ProjectSettings directory not found."
                };
            }

            string[] assetFiles = Directory.GetFiles(normalizedPath, "*.asset");
            var settingsList = new List<object>();

            foreach (string filePath in assetFiles)
            {
                if (settingsList.Count >= MaxListResults)
                    break;

                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string relativePath = $"ProjectSettings/{Path.GetFileName(filePath)}";

                // Try to load the asset
                string assetType = null;
                bool loadable = false;

                try
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(relativePath);
                    if (assets != null && assets.Length > 0 && assets[0] != null)
                    {
                        loadable = true;
                        assetType = assets[0].GetType().Name;
                    }
                }
                catch
                {
                    // Asset could not be loaded — leave loadable as false
                }

                var entry = new Dictionary<string, object>
                {
                    { "name", fileName },
                    { "path", relativePath },
                    { "type", assetType },
                    { "loadable", loadable }
                };

                if (DisplayNameMap.TryGetValue(fileName, out string displayName))
                {
                    entry["displayName"] = displayName;
                }

                settingsList.Add(entry);
            }

            return new
            {
                success = true,
                count = settingsList.Count,
                truncated = assetFiles.Length > MaxListResults,
                settings = settingsList
            };
        }

        private static object HandleInspect(string settingsFile, string propertyFilter)
        {
            string settingsPath = NormalizeSettingsPath(settingsFile);
            var asset = LoadSettingsObject(settingsPath);
            var serializedObject = new SerializedObject(asset);

            var properties = new List<object>();
            bool truncated = false;

            IterateVisibleProperties(serializedObject, propertyFilter, MaxPropertyResults,
                (propertyPath, propertyType, serializedProperty) =>
                {
                    properties.Add(new Dictionary<string, object>
                    {
                        { "path", propertyPath },
                        { "type", propertyType },
                        { "value", SerializedPropertyHelper.SerializePropertyValue(serializedProperty) }
                    });
                },
                () => { truncated = true; });

            string fileName = Path.GetFileNameWithoutExtension(settingsPath);

            var result = new Dictionary<string, object>
            {
                { "success", true },
                { "settingsFile", fileName },
                { "path", settingsPath },
                { "assetType", asset.GetType().Name },
                { "propertyCount", properties.Count },
                { "truncated", truncated },
                { "properties", properties }
            };

            if (!string.IsNullOrEmpty(propertyFilter))
            {
                result["filter"] = propertyFilter;
            }

            return result;
        }

        private static object HandleSet(string settingsFile, string propertyPath, object value)
        {
            string settingsPath = NormalizeSettingsPath(settingsFile);
            var asset = LoadSettingsObject(settingsPath);
            var serializedObject = new SerializedObject(asset);

            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                throw MCPException.InvalidParams(
                    $"Property '{propertyPath}' not found in '{Path.GetFileNameWithoutExtension(settingsPath)}'.");
            }

            // Record undo before modification
            Undo.RecordObject(asset, $"ManageSettings Set {propertyPath}");

            bool success = SerializedPropertyHelper.SetSerializedPropertyValue(property, value);
            if (!success)
            {
                return new
                {
                    success = false,
                    error = $"Failed to set property '{propertyPath}' (type: {property.propertyType}). Value may be incompatible."
                };
            }

            serializedObject.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            // Re-read the property to return the new value
            serializedObject.Update();
            var updatedProperty = serializedObject.FindProperty(propertyPath);
            object newValue = updatedProperty != null
                ? SerializedPropertyHelper.SerializePropertyValue(updatedProperty)
                : null;

            return new
            {
                success = true,
                message = $"Property '{propertyPath}' updated in '{Path.GetFileNameWithoutExtension(settingsPath)}'.",
                propertyPath,
                newValue
            };
        }

        private static object HandleSearch(string query, int maxResults)
        {
            string projectSettingsPath = Path.Combine(Application.dataPath, "..", "ProjectSettings");
            string normalizedPath = Path.GetFullPath(projectSettingsPath);

            if (!Directory.Exists(normalizedPath))
            {
                return new
                {
                    success = false,
                    error = "ProjectSettings directory not found."
                };
            }

            string[] assetFiles = Directory.GetFiles(normalizedPath, "*.asset");
            var results = new List<object>();
            int totalMatches = 0;

            foreach (string filePath in assetFiles)
            {
                if (results.Count >= maxResults)
                    break;

                string relativePath = $"ProjectSettings/{Path.GetFileName(filePath)}";
                string fileName = Path.GetFileNameWithoutExtension(filePath);

                UnityEngine.Object asset;
                try
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(relativePath);
                    if (assets == null || assets.Length == 0 || assets[0] == null)
                        continue;
                    asset = assets[0];
                }
                catch
                {
                    continue;
                }

                var serializedObject = new SerializedObject(asset);
                int remainingSlots = maxResults - results.Count;

                IterateVisibleProperties(serializedObject, query, remainingSlots,
                    (propertyPath, propertyType, serializedProperty) =>
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            { "settingsFile", fileName },
                            { "propertyPath", propertyPath },
                            { "propertyType", propertyType },
                            { "value", SerializedPropertyHelper.SerializePropertyValue(serializedProperty) }
                        });
                        totalMatches++;
                    },
                    () => { totalMatches++; });
            }

            return new
            {
                success = true,
                query,
                count = results.Count,
                truncated = results.Count < totalMatches,
                results
            };
        }

        private static object HandleGetPreference(string key, string type)
        {
            if (!EditorPrefs.HasKey(key))
            {
                return new
                {
                    success = false,
                    error = $"EditorPrefs key '{key}' not found."
                };
            }

            string normalizedType = (type ?? "string").ToLowerInvariant();
            object value;

            switch (normalizedType)
            {
                case "int":
                    value = EditorPrefs.GetInt(key);
                    break;
                case "float":
                    value = EditorPrefs.GetFloat(key);
                    break;
                case "bool":
                    value = EditorPrefs.GetBool(key);
                    break;
                case "string":
                    value = EditorPrefs.GetString(key);
                    break;
                default:
                    throw MCPException.InvalidParams(
                        $"Invalid type '{type}'. Must be one of: string, int, float, bool.");
            }

            return new
            {
                success = true,
                key,
                type = normalizedType,
                value
            };
        }

        private static object HandleSetPreference(string key, object value, string type)
        {
            string normalizedType = (type ?? "string").ToLowerInvariant();

            try
            {
                switch (normalizedType)
                {
                    case "int":
                        int intValue = Convert.ToInt32(value);
                        EditorPrefs.SetInt(key, intValue);
                        return new { success = true, message = $"EditorPrefs '{key}' set to {intValue}.", key, type = normalizedType, value = intValue };

                    case "float":
                        float floatValue = Convert.ToSingle(value);
                        EditorPrefs.SetFloat(key, floatValue);
                        return new { success = true, message = $"EditorPrefs '{key}' set to {floatValue}.", key, type = normalizedType, value = floatValue };

                    case "bool":
                        bool boolValue = Convert.ToBoolean(value);
                        EditorPrefs.SetBool(key, boolValue);
                        return new { success = true, message = $"EditorPrefs '{key}' set to {boolValue}.", key, type = normalizedType, value = boolValue };

                    case "string":
                        string stringValue = value is Newtonsoft.Json.Linq.JValue jv
                            ? jv.Value?.ToString() ?? ""
                            : value?.ToString() ?? "";
                        EditorPrefs.SetString(key, stringValue);
                        return new { success = true, message = $"EditorPrefs '{key}' set.", key, type = normalizedType, value = stringValue };

                    default:
                        throw MCPException.InvalidParams(
                            $"Invalid type '{type}'. Must be one of: string, int, float, bool.");
                }
            }
            catch (MCPException)
            {
                throw;
            }
            catch (FormatException)
            {
                return new
                {
                    success = false,
                    error = $"Cannot convert value to {normalizedType}."
                };
            }
        }

        private static object HandleDeletePreference(string key)
        {
            if (!EditorPrefs.HasKey(key))
            {
                return new
                {
                    success = false,
                    error = $"EditorPrefs key '{key}' not found."
                };
            }

            EditorPrefs.DeleteKey(key);

            return new
            {
                success = true,
                message = $"EditorPrefs key '{key}' deleted.",
                key
            };
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Normalizes a settings file input to a full relative path.
        /// Accepts short names like "DynamicsManager" or full paths like "ProjectSettings/DynamicsManager.asset".
        /// </summary>
        private static string NormalizeSettingsPath(string input)
        {
            if (string.IsNullOrEmpty(input))
                throw MCPException.InvalidParams("Settings file name is required.");

            string trimmed = input.Trim();

            // Already a full path
            if (trimmed.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase))
            {
                if (!trimmed.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    trimmed += ".asset";
                return trimmed;
            }

            // Strip .asset extension if provided on short name
            if (trimmed.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - ".asset".Length);

            return $"ProjectSettings/{trimmed}.asset";
        }

        /// <summary>
        /// Loads a settings asset from the given path. Throws MCPException if not found.
        /// </summary>
        private static UnityEngine.Object LoadSettingsObject(string settingsPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(settingsPath);

            if (assets == null || assets.Length == 0 || assets[0] == null)
            {
                throw MCPException.InvalidParams(
                    $"Could not load settings file at '{settingsPath}'. Use action='list' to see available settings.");
            }

            return assets[0];
        }

        /// <summary>
        /// Iterates visible properties of a SerializedObject, applying optional filter and count limit.
        /// Skips internal properties defined in SkippedProperties.
        /// </summary>
        /// <param name="serializedObject">The SerializedObject to iterate.</param>
        /// <param name="filter">Optional substring filter for property paths (case-insensitive).</param>
        /// <param name="maxCount">Maximum number of properties to yield.</param>
        /// <param name="onMatch">Callback invoked for each matching property with (path, typeName, property).</param>
        /// <param name="onTruncated">Callback invoked if results were truncated due to maxCount.</param>
        private static void IterateVisibleProperties(
            SerializedObject serializedObject,
            string filter,
            int maxCount,
            Action<string, string, SerializedProperty> onMatch,
            Action onTruncated)
        {
            var iterator = serializedObject.GetIterator();
            bool hasFilter = !string.IsNullOrEmpty(filter);
            int matchCount = 0;

            // Enter the first property — use Next instead of NextVisible so that
            // fixed-size buffer properties (e.g. m_LayerCollisionMatrix) are included.
            if (!iterator.Next(true))
                return;

            do
            {
                string propertyPath = iterator.propertyPath;

                // Skip internal properties
                if (SkippedProperties.Contains(iterator.name))
                    continue;

                // Apply filter if provided
                if (hasFilter && propertyPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (matchCount >= maxCount)
                {
                    onTruncated?.Invoke();
                    return;
                }

                onMatch(propertyPath, iterator.type, iterator.Copy());
                matchCount++;
            }
            while (iterator.Next(false));
        }

        #endregion
    }
}
