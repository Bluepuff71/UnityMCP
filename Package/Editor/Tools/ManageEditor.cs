using System;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for managing editor state including play mode, tags, layers, and tool selection.
    /// </summary>
    [MCPTool("manage_editor", "Manage editor state, tags, layers, and tools", Category = "Editor")]
    public static class ManageEditor
    {
        private const int FirstUserLayerIndex = 8;
        private const int TotalLayerCount = 32;

        /// <summary>
        /// Valid editor tools that can be selected.
        /// </summary>
        private static readonly string[] ValidTools = { "View", "Move", "Rotate", "Scale", "Rect", "Transform" };

        [MCPAction("play")]
        public static object Play()
        {
            try
            {
                return HandlePlay();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageEditor] Error executing action 'play': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'play': {exception.Message}"
                };
            }
        }

        [MCPAction("pause")]
        public static object Pause()
        {
            try
            {
                return HandlePause();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageEditor] Error executing action 'pause': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'pause': {exception.Message}"
                };
            }
        }

        [MCPAction("stop")]
        public static object Stop()
        {
            try
            {
                return HandleStop();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageEditor] Error executing action 'stop': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'stop': {exception.Message}"
                };
            }
        }

        [MCPAction("set_active_tool")]
        public static object SetActiveTool(
            [MCPParam("tool_name", "Tool name for set_active_tool: View, Move, Rotate, Scale, Rect, Transform", required: true)] string toolName)
        {
            try
            {
                return HandleSetActiveTool(toolName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageEditor] Error executing action 'set_active_tool': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'set_active_tool': {exception.Message}"
                };
            }
        }

        [MCPAction("add_tag")]
        public static object AddTag(
            [MCPParam("tag_name", "Tag name for add_tag/remove_tag", required: true)] string tagName)
        {
            try
            {
                return HandleAddTag(tagName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageEditor] Error executing action 'add_tag': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'add_tag': {exception.Message}"
                };
            }
        }

        [MCPAction("remove_tag", DestructiveHint = true)]
        public static object RemoveTag(
            [MCPParam("tag_name", "Tag name for add_tag/remove_tag", required: true)] string tagName)
        {
            try
            {
                return HandleRemoveTag(tagName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageEditor] Error executing action 'remove_tag': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'remove_tag': {exception.Message}"
                };
            }
        }

        [MCPAction("add_layer")]
        public static object AddLayer(
            [MCPParam("layer_name", "Layer name for add_layer/remove_layer", required: true)] string layerName)
        {
            try
            {
                return HandleAddLayer(layerName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageEditor] Error executing action 'add_layer': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'add_layer': {exception.Message}"
                };
            }
        }

        [MCPAction("remove_layer", DestructiveHint = true)]
        public static object RemoveLayer(
            [MCPParam("layer_name", "Layer name for add_layer/remove_layer", required: true)] string layerName)
        {
            try
            {
                return HandleRemoveLayer(layerName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageEditor] Error executing action 'remove_layer': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'remove_layer': {exception.Message}"
                };
            }
        }

        #region Play Mode Actions

        /// <summary>
        /// Enters play mode.
        /// </summary>
        private static object HandlePlay()
        {
            if (EditorApplication.isPlaying)
            {
                return new
                {
                    success = true,
                    message = "Already in play mode.",
                    isPlaying = true,
                    isPaused = EditorApplication.isPaused
                };
            }

            if (EditorApplication.isCompiling)
            {
                return new
                {
                    success = false,
                    error = "Cannot enter play mode while scripts are compiling."
                };
            }

            EditorApplication.isPlaying = true;

            return new
            {
                success = true,
                message = "Entering play mode.",
                isPlaying = true,
                isPaused = false
            };
        }

        /// <summary>
        /// Toggles pause state during play mode.
        /// </summary>
        private static object HandlePause()
        {
            if (!EditorApplication.isPlaying)
            {
                return new
                {
                    success = false,
                    error = "Cannot pause when not in play mode. Use 'play' action first."
                };
            }

            bool newPauseState = !EditorApplication.isPaused;
            EditorApplication.isPaused = newPauseState;

            return new
            {
                success = true,
                message = newPauseState ? "Play mode paused." : "Play mode resumed.",
                isPlaying = true,
                isPaused = newPauseState
            };
        }

        /// <summary>
        /// Exits play mode.
        /// </summary>
        private static object HandleStop()
        {
            if (!EditorApplication.isPlaying)
            {
                return new
                {
                    success = true,
                    message = "Already stopped (not in play mode).",
                    isPlaying = false,
                    isPaused = false
                };
            }

            EditorApplication.isPlaying = false;

            return new
            {
                success = true,
                message = "Exiting play mode.",
                isPlaying = false,
                isPaused = false
            };
        }

        #endregion

        #region Tool Selection

        /// <summary>
        /// Sets the active editor tool.
        /// </summary>
        private static object HandleSetActiveTool(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return new
                {
                    success = false,
                    error = "The 'tool_name' parameter is required for set_active_tool action. Valid tools are: View, Move, Rotate, Scale, Rect, Transform."
                };
            }

            string normalizedToolName = toolName.Trim();

            // Find the matching tool (case-insensitive)
            string matchedTool = ValidTools.FirstOrDefault(t =>
                string.Equals(t, normalizedToolName, StringComparison.OrdinalIgnoreCase));

            if (matchedTool == null)
            {
                return new
                {
                    success = false,
                    error = $"Unknown tool: '{toolName}'. Valid tools are: {string.Join(", ", ValidTools)}."
                };
            }

            Tool unityTool = matchedTool switch
            {
                "View" => Tool.View,
                "Move" => Tool.Move,
                "Rotate" => Tool.Rotate,
                "Scale" => Tool.Scale,
                "Rect" => Tool.Rect,
                "Transform" => Tool.Transform,
                _ => Tool.None
            };

            if (unityTool == Tool.None)
            {
                return new
                {
                    success = false,
                    error = $"Could not map tool '{matchedTool}' to Unity tool type."
                };
            }

            Tool previousTool = UnityEditor.Tools.current;
            UnityEditor.Tools.current = unityTool;

            return new
            {
                success = true,
                message = $"Active tool set to '{matchedTool}'.",
                activeTool = matchedTool,
                previousTool = previousTool.ToString()
            };
        }

        #endregion

        #region Tag Management

        /// <summary>
        /// Adds a new project tag.
        /// </summary>
        private static object HandleAddTag(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return new
                {
                    success = false,
                    error = "The 'tag_name' parameter is required for add_tag action."
                };
            }

            string normalizedTagName = tagName.Trim();

            // Check if tag already exists
            string[] existingTags = InternalEditorUtility.tags;
            if (existingTags.Contains(normalizedTagName, StringComparer.Ordinal))
            {
                return new
                {
                    success = false,
                    error = $"Tag '{normalizedTagName}' already exists.",
                    existingTags
                };
            }

            // Validate tag name (no special characters except underscore)
            if (!IsValidTagName(normalizedTagName))
            {
                return new
                {
                    success = false,
                    error = $"Invalid tag name '{normalizedTagName}'. Tag names can only contain letters, numbers, and underscores, and must start with a letter or underscore."
                };
            }

            InternalEditorUtility.AddTag(normalizedTagName);

            return new
            {
                success = true,
                message = $"Tag '{normalizedTagName}' added successfully.",
                tag = normalizedTagName,
                allTags = InternalEditorUtility.tags
            };
        }

        /// <summary>
        /// Removes a project tag.
        /// </summary>
        private static object HandleRemoveTag(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return new
                {
                    success = false,
                    error = "The 'tag_name' parameter is required for remove_tag action."
                };
            }

            string normalizedTagName = tagName.Trim();

            // Check if it's a built-in tag
            if (IsBuiltInTag(normalizedTagName))
            {
                return new
                {
                    success = false,
                    error = $"Cannot remove built-in tag '{normalizedTagName}'."
                };
            }

            // Check if tag exists
            string[] existingTags = InternalEditorUtility.tags;
            if (!existingTags.Contains(normalizedTagName, StringComparer.Ordinal))
            {
                return new
                {
                    success = false,
                    error = $"Tag '{normalizedTagName}' does not exist.",
                    existingTags
                };
            }

            InternalEditorUtility.RemoveTag(normalizedTagName);

            return new
            {
                success = true,
                message = $"Tag '{normalizedTagName}' removed successfully.",
                tag = normalizedTagName,
                allTags = InternalEditorUtility.tags
            };
        }

        /// <summary>
        /// Checks if a tag name is valid.
        /// </summary>
        private static bool IsValidTagName(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
            {
                return false;
            }

            // First character must be a letter or underscore
            char firstChar = tagName[0];
            if (!char.IsLetter(firstChar) && firstChar != '_')
            {
                return false;
            }

            // Remaining characters must be letters, digits, or underscores
            for (int i = 1; i < tagName.Length; i++)
            {
                char c = tagName[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if a tag is a built-in Unity tag that cannot be removed.
        /// </summary>
        private static bool IsBuiltInTag(string tagName)
        {
            string[] builtInTags = { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };
            return builtInTags.Contains(tagName, StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region Layer Management

        /// <summary>
        /// Adds a new user layer.
        /// </summary>
        private static object HandleAddLayer(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
            {
                return new
                {
                    success = false,
                    error = "The 'layer_name' parameter is required for add_layer action."
                };
            }

            string normalizedLayerName = layerName.Trim();

            // Get TagManager asset
            var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets == null || tagManagerAssets.Length == 0)
            {
                return new
                {
                    success = false,
                    error = "Could not load TagManager asset."
                };
            }

            SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
            SerializedProperty layersProperty = tagManager.FindProperty("layers");

            if (layersProperty == null)
            {
                return new
                {
                    success = false,
                    error = "Could not find layers property in TagManager."
                };
            }

            // Check if layer already exists
            for (int i = 0; i < TotalLayerCount; i++)
            {
                SerializedProperty layerProperty = layersProperty.GetArrayElementAtIndex(i);
                if (layerProperty != null && layerProperty.stringValue == normalizedLayerName)
                {
                    return new
                    {
                        success = false,
                        error = $"Layer '{normalizedLayerName}' already exists at index {i}.",
                        layerIndex = i
                    };
                }
            }

            // Find first empty user layer slot (indices 8-31)
            int emptySlotIndex = -1;
            for (int i = FirstUserLayerIndex; i < TotalLayerCount; i++)
            {
                SerializedProperty layerProperty = layersProperty.GetArrayElementAtIndex(i);
                if (layerProperty != null && string.IsNullOrEmpty(layerProperty.stringValue))
                {
                    emptySlotIndex = i;
                    break;
                }
            }

            if (emptySlotIndex == -1)
            {
                return new
                {
                    success = false,
                    error = "No empty user layer slots available. All user layer slots (8-31) are in use."
                };
            }

            // Add the layer
            SerializedProperty newLayerProperty = layersProperty.GetArrayElementAtIndex(emptySlotIndex);
            newLayerProperty.stringValue = normalizedLayerName;
            tagManager.ApplyModifiedProperties();

            return new
            {
                success = true,
                message = $"Layer '{normalizedLayerName}' added at index {emptySlotIndex}.",
                layer = normalizedLayerName,
                layerIndex = emptySlotIndex,
                allLayers = GetAllLayers()
            };
        }

        /// <summary>
        /// Removes a user layer.
        /// </summary>
        private static object HandleRemoveLayer(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
            {
                return new
                {
                    success = false,
                    error = "The 'layer_name' parameter is required for remove_layer action."
                };
            }

            string normalizedLayerName = layerName.Trim();

            // Get TagManager asset
            var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets == null || tagManagerAssets.Length == 0)
            {
                return new
                {
                    success = false,
                    error = "Could not load TagManager asset."
                };
            }

            SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
            SerializedProperty layersProperty = tagManager.FindProperty("layers");

            if (layersProperty == null)
            {
                return new
                {
                    success = false,
                    error = "Could not find layers property in TagManager."
                };
            }

            // Find the layer
            int foundIndex = -1;
            for (int i = 0; i < TotalLayerCount; i++)
            {
                SerializedProperty layerProperty = layersProperty.GetArrayElementAtIndex(i);
                if (layerProperty != null && layerProperty.stringValue == normalizedLayerName)
                {
                    foundIndex = i;
                    break;
                }
            }

            if (foundIndex == -1)
            {
                return new
                {
                    success = false,
                    error = $"Layer '{normalizedLayerName}' not found.",
                    allLayers = GetAllLayers()
                };
            }

            // Check if it's a built-in layer (indices 0-7)
            if (foundIndex < FirstUserLayerIndex)
            {
                return new
                {
                    success = false,
                    error = $"Cannot remove built-in layer '{normalizedLayerName}' at index {foundIndex}. Only user layers (indices 8-31) can be removed."
                };
            }

            // Remove the layer by clearing it
            SerializedProperty layerToRemove = layersProperty.GetArrayElementAtIndex(foundIndex);
            layerToRemove.stringValue = string.Empty;
            tagManager.ApplyModifiedProperties();

            return new
            {
                success = true,
                message = $"Layer '{normalizedLayerName}' removed from index {foundIndex}.",
                layer = normalizedLayerName,
                layerIndex = foundIndex,
                allLayers = GetAllLayers()
            };
        }

        /// <summary>
        /// Gets all defined layers.
        /// </summary>
        private static object[] GetAllLayers()
        {
            var layers = new System.Collections.Generic.List<object>();

            for (int i = 0; i < TotalLayerCount; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    layers.Add(new
                    {
                        index = i,
                        name = layerName,
                        isBuiltIn = i < FirstUserLayerIndex
                    });
                }
            }

            return layers.ToArray();
        }

        #endregion
    }
}
