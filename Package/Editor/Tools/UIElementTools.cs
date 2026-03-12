using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Services;


#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Handles individual uGUI element operations: create, modify, delete, duplicate, reorder, and add_effect.
    /// All operations validate that elements are under a Canvas and register with Undo for full undo support.
    /// </summary>
    [MCPTool("manage_ui_element", "Manages individual uGUI elements: create, modify, delete, duplicate, reorder, or add effects.", Category = "UI")]
    public static class UIElementTools
    {
        #region Action Methods

        /// <summary>
        /// Creates a new UI element under a Canvas hierarchy.
        /// </summary>
        [MCPAction("create", Description = "Create a new UI element")]
        public static object Create(
            [MCPParam("element_type", "Type of UI element", required: true, Enum = new[] {
                "panel", "text", "text_tmp", "image", "raw_image",
                "button", "button_tmp", "toggle", "slider",
                "dropdown", "dropdown_tmp", "scrollview",
                "input_field", "input_field_tmp",
                "grid_layout", "horizontal_layout", "vertical_layout", "spacer"
            })] string elementType = null,
            [MCPParam("parent", "Instance ID or name/path of parent (must be under a Canvas)")] string parent = null,
            [MCPParam("name", "Name for the new element")] string name = null,
            [MCPParam("anchor_preset", "Anchor preset name", Enum = new[] {
                "top_left", "top_center", "top_right", "top_stretch",
                "middle_left", "middle_center", "middle_right", "middle_stretch",
                "bottom_left", "bottom_center", "bottom_right", "bottom_stretch",
                "stretch_left", "stretch_center", "stretch_right", "stretch_full"
            })] string anchorPreset = null,
            [MCPParam("position", "Local position as [x,y] array")] object position = null,
            [MCPParam("size", "Size as [width,height] array")] object size = null,
            [MCPParam("pivot", "Pivot as [x,y] array (0-1 range)")] object pivot = null,
            [MCPParam("style", "Style properties as JSON object")] Dictionary<string, object> style = null,
            [MCPParam("text", "Text content for text-based elements")] string text = null,
            [MCPParam("sprite_path", "Asset path to sprite for image elements")] string spritePath = null,
            [MCPParam("interactable", "Whether the element is interactable (for buttons, toggles, etc.)")] bool? interactable = null)
        {
            try
            {
                return HandleCreate(elementType, parent, name, anchorPreset, position, size, pivot, style, text, spritePath, interactable);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error creating UI element: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Modifies an existing UI element's properties.
        /// </summary>
        [MCPAction("modify", Description = "Modify an existing UI element")]
        public static object Modify(
            [MCPParam("target", "Instance ID or name/path of the UI element", required: true)] string target = null,
            [MCPParam("name", "New name for the element")] string name = null,
            [MCPParam("anchor_preset", "Anchor preset name", Enum = new[] {
                "top_left", "top_center", "top_right", "top_stretch",
                "middle_left", "middle_center", "middle_right", "middle_stretch",
                "bottom_left", "bottom_center", "bottom_right", "bottom_stretch",
                "stretch_left", "stretch_center", "stretch_right", "stretch_full"
            })] string anchorPreset = null,
            [MCPParam("position", "Local position as [x,y] array")] object position = null,
            [MCPParam("size", "Size as [width,height] array")] object size = null,
            [MCPParam("pivot", "Pivot as [x,y] array (0-1 range)")] object pivot = null,
            [MCPParam("style", "Style properties as JSON object")] Dictionary<string, object> style = null,
            [MCPParam("text", "Text content for text-based elements")] string text = null,
            [MCPParam("sprite_path", "Asset path to sprite for image elements")] string spritePath = null,
            [MCPParam("interactable", "Whether the element is interactable")] bool? interactable = null,
            [MCPParam("set_active", "Activate or deactivate the element")] bool? setActive = null)
        {
            try
            {
                return HandleModify(target, name, anchorPreset, position, size, pivot, style, text, spritePath, interactable, setActive);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error modifying UI element: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Removes a UI element from the scene.
        /// </summary>
        [MCPAction("delete", Description = "Remove a UI element", DestructiveHint = true)]
        public static object Delete(
            [MCPParam("target", "Instance ID or name/path of the UI element", required: true)] string target = null)
        {
            try
            {
                return HandleDelete(target);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error deleting UI element: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Clones a UI element and its entire child hierarchy.
        /// </summary>
        [MCPAction("duplicate", Description = "Clone element tree")]
        public static object Duplicate(
            [MCPParam("target", "Instance ID or name/path of the UI element to clone", required: true)] string target = null,
            [MCPParam("new_parent", "Instance ID or name/path of new parent (optional, defaults to same parent)")] string newParent = null,
            [MCPParam("new_name", "Name for the duplicated element")] string newName = null)
        {
            try
            {
                return HandleDuplicate(target, newParent, newName);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error duplicating UI element: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Changes the sibling index (draw order) of a UI element.
        /// </summary>
        [MCPAction("reorder", Description = "Change sibling index (draw order)")]
        public static object Reorder(
            [MCPParam("target", "Instance ID or name/path of the UI element", required: true)] string target = null,
            [MCPParam("sibling_index", "Explicit sibling index to set")] int? siblingIndex = null,
            [MCPParam("move", "Relative move direction", Enum = new[] { "first", "last", "up", "down" })] string move = null)
        {
            try
            {
                return HandleReorder(target, siblingIndex, move);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error reordering UI element: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Adds a visual effect component to a UI element.
        /// </summary>
        [MCPAction("add_effect", Description = "Add visual effects to a UI element")]
        public static object AddEffect(
            [MCPParam("target", "Instance ID or name/path of the UI element", required: true)] string target = null,
            [MCPParam("effect_type", "Type of effect to add", required: true, Enum = new[] {
                "outline", "shadow", "mask", "content_size_fitter", "aspect_ratio_fitter"
            })] string effectType = null,
            [MCPParam("properties", "Effect-specific properties as JSON object")] Dictionary<string, object> properties = null)
        {
            try
            {
                return HandleAddEffect(target, effectType, properties);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error adding effect to UI element: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Action Handlers

        // ─────────────────────────────────────────────
        //  Create Handler
        // ─────────────────────────────────────────────

        private static object HandleCreate(
            string elementType,
            string parentTarget,
            string name,
            string anchorPreset,
            object position,
            object size,
            object pivot,
            Dictionary<string, object> style,
            string text,
            string spritePath,
            bool? interactable)
        {
            // Validate element type
            if (string.IsNullOrEmpty(elementType))
            {
                throw MCPException.InvalidParams("'element_type' parameter is required for create action.");
            }

            if (!UISchema.ElementTypes.Contains(elementType))
            {
                string validTypes = string.Join(", ", UISchema.GetAvailableElementTypes());
                throw MCPException.InvalidParams($"Invalid element_type: '{elementType}'. Valid types: {validTypes}");
            }

            // Check TMP availability
            bool isTmpType = elementType.EndsWith("_tmp");
#if !UNITY_MCP_TMP
            if (isTmpType)
            {
                string legacyType = elementType.Replace("_tmp", "");
                Debug.LogWarning($"[UIElementTools] TextMeshPro not available. '{elementType}' will fall back to legacy '{legacyType}'.");
            }
#endif

            // Resolve parent
            Transform parentTransform = null;
            if (!string.IsNullOrEmpty(parentTarget))
            {
                GameObject parentGO = FindGameObject(parentTarget);
                if (parentGO == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Parent '{parentTarget}' not found."
                    };
                }
                parentTransform = parentGO.transform;
            }

            // Validate parent is under a Canvas
            if (parentTransform != null)
            {
                Canvas parentCanvas = GetParentCanvas(parentTransform);
                if (parentCanvas == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Parent '{parentTarget}' is not under a Canvas. UI elements must be children of a Canvas."
                    };
                }
            }
            else
            {
                // No parent specified — try to find an existing Canvas
                var existingCanvas = UnityEngine.Object.FindObjectOfType<Canvas>();
                if (existingCanvas == null)
                {
                    return new
                    {
                        success = false,
                        error = "No Canvas found in the scene. Create a Canvas first using manage_canvas."
                    };
                }
                parentTransform = existingCanvas.transform;
            }

            // Default name
            if (string.IsNullOrEmpty(name))
            {
                name = FormatDefaultName(elementType);
            }

            // Create element via UISchema
            GameObject go = UISchema.CreateElementHierarchy(elementType, name, parentTransform);
            if (go == null)
            {
                return new
                {
                    success = false,
                    error = $"Failed to create element of type '{elementType}'."
                };
            }

            RectTransform rt = go.GetComponent<RectTransform>();

            // Apply anchor preset
            if (!string.IsNullOrEmpty(anchorPreset))
            {
                if (!UISchema.ApplyAnchorPreset(rt, anchorPreset))
                {
                    string validPresets = string.Join(", ", UISchema.GetAvailableAnchorPresets());
                    Debug.LogWarning($"[UIElementTools] Invalid anchor_preset: '{anchorPreset}'. Valid presets: {validPresets}");
                }
            }

            // Apply position
            Vector2? parsedPosition = ParseVector2(position);
            if (parsedPosition.HasValue)
            {
                rt.anchoredPosition = parsedPosition.Value;
            }

            // Apply size
            Vector2? parsedSize = ParseVector2(size);
            if (parsedSize.HasValue)
            {
                rt.sizeDelta = parsedSize.Value;
            }

            // Apply pivot
            Vector2? parsedPivot = ParseVector2(pivot);
            if (parsedPivot.HasValue)
            {
                rt.pivot = parsedPivot.Value;
            }

            // Apply text content
            var warnings = new List<string>();
            if (!string.IsNullOrEmpty(text))
            {
                ApplyTextContent(go, elementType, text);
            }

            // Apply sprite
            if (!string.IsNullOrEmpty(spritePath))
            {
                ApplySprite(go, spritePath, warnings);
            }

            // Apply interactable
            if (interactable.HasValue)
            {
                ApplyInteractable(go, interactable.Value);
            }

            // Apply style
            if (style != null && style.Count > 0)
            {
                var styleWarnings = UISchema.ApplyStyle(go, elementType, style);
                warnings.AddRange(styleWarnings);
            }

            EditorUtility.SetDirty(go);
            Selection.activeGameObject = go;

            var result = new Dictionary<string, object>
            {
                { "success", true },
                { "message", $"UI element '{name}' ({elementType}) created successfully." },
                { "instance_id", go.GetInstanceID() },
                { "name", go.name },
                { "element_type", elementType },
                { "parent", parentTransform != null ? parentTransform.name : null },
                { "anchor_preset", anchorPreset },
                { "position", new { x = rt.anchoredPosition.x, y = rt.anchoredPosition.y } },
                { "size", new { width = rt.sizeDelta.x, height = rt.sizeDelta.y } }
            };

            if (warnings.Count > 0)
            {
                result["warnings"] = warnings;
            }

            return result;
        }

        // ─────────────────────────────────────────────
        //  Modify Handler
        // ─────────────────────────────────────────────

        private static object HandleModify(
            string target,
            string name,
            string anchorPreset,
            object position,
            object size,
            object pivot,
            Dictionary<string, object> style,
            string text,
            string spritePath,
            bool? interactable,
            bool? setActive)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for modify action.");
            }

            GameObject go = FindGameObject(target, searchInactive: true);
            if (go == null)
            {
                return new
                {
                    success = false,
                    error = $"Target UI element '{target}' not found."
                };
            }

            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null)
            {
                return new
                {
                    success = false,
                    error = $"Target '{target}' does not have a RectTransform. It may not be a UI element."
                };
            }

            Undo.RecordObject(go, "Modify UI Element");
            Undo.RecordObject(rt, "Modify UI Element RectTransform");

            bool modified = false;
            var warnings = new List<string>();
            var changes = new List<string>();

            // Rename
            if (!string.IsNullOrEmpty(name) && go.name != name)
            {
                go.name = name;
                modified = true;
                changes.Add("name");
            }

            // Set active
            if (setActive.HasValue && go.activeSelf != setActive.Value)
            {
                go.SetActive(setActive.Value);
                modified = true;
                changes.Add("active");
            }

            // Apply anchor preset
            if (!string.IsNullOrEmpty(anchorPreset))
            {
                if (UISchema.ApplyAnchorPreset(rt, anchorPreset, keepPosition: true))
                {
                    modified = true;
                    changes.Add("anchor_preset");
                }
                else
                {
                    warnings.Add($"Invalid anchor_preset: '{anchorPreset}'");
                }
            }

            // Apply position
            Vector2? parsedPosition = ParseVector2(position);
            if (parsedPosition.HasValue)
            {
                rt.anchoredPosition = parsedPosition.Value;
                modified = true;
                changes.Add("position");
            }

            // Apply size
            Vector2? parsedSize = ParseVector2(size);
            if (parsedSize.HasValue)
            {
                rt.sizeDelta = parsedSize.Value;
                modified = true;
                changes.Add("size");
            }

            // Apply pivot
            Vector2? parsedPivot = ParseVector2(pivot);
            if (parsedPivot.HasValue)
            {
                rt.pivot = parsedPivot.Value;
                modified = true;
                changes.Add("pivot");
            }

            // Apply text content
            if (!string.IsNullOrEmpty(text))
            {
                string detectedType = DetectElementType(go);
                ApplyTextContent(go, detectedType, text);
                modified = true;
                changes.Add("text");
            }

            // Apply sprite
            if (!string.IsNullOrEmpty(spritePath))
            {
                ApplySprite(go, spritePath, warnings);
                modified = true;
                changes.Add("sprite");
            }

            // Apply interactable
            if (interactable.HasValue)
            {
                ApplyInteractable(go, interactable.Value);
                modified = true;
                changes.Add("interactable");
            }

            // Apply style
            if (style != null && style.Count > 0)
            {
                string detectedType = DetectElementType(go);
                var styleWarnings = UISchema.ApplyStyle(go, detectedType, style);
                warnings.AddRange(styleWarnings);
                modified = true;
                changes.Add("style");
            }

            if (!modified)
            {
                return new
                {
                    success = true,
                    message = $"No modifications applied to UI element '{go.name}'.",
                    instance_id = go.GetInstanceID(),
                    name = go.name
                };
            }

            EditorUtility.SetDirty(go);

            var result = new Dictionary<string, object>
            {
                { "success", true },
                { "message", $"UI element '{go.name}' modified successfully." },
                { "instance_id", go.GetInstanceID() },
                { "name", go.name },
                { "changes", changes },
                { "position", new { x = rt.anchoredPosition.x, y = rt.anchoredPosition.y } },
                { "size", new { width = rt.sizeDelta.x, height = rt.sizeDelta.y } }
            };

            if (warnings.Count > 0)
            {
                result["warnings"] = warnings;
            }

            return result;
        }

        // ─────────────────────────────────────────────
        //  Delete Handler
        // ─────────────────────────────────────────────

        private static object HandleDelete(string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for delete action.");
            }

            GameObject go = FindGameObject(target, searchInactive: true);
            if (go == null)
            {
                return new
                {
                    success = false,
                    error = $"Target UI element '{target}' not found."
                };
            }

            // Verify it has a RectTransform (is a UI element)
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null)
            {
                return new
                {
                    success = false,
                    error = $"Target '{target}' does not have a RectTransform. Use manage_gameobject to delete non-UI objects."
                };
            }

            string elementName = go.name;
            int instanceId = go.GetInstanceID();
            int childCount = go.transform.childCount;

            Undo.DestroyObjectImmediate(go);

            return new
            {
                success = true,
                message = childCount > 0
                    ? $"UI element '{elementName}' and {childCount} child element(s) deleted."
                    : $"UI element '{elementName}' deleted.",
                deleted = new { name = elementName, instance_id = instanceId, children_removed = childCount }
            };
        }

        // ─────────────────────────────────────────────
        //  Duplicate Handler
        // ─────────────────────────────────────────────

        private static object HandleDuplicate(string target, string newParentTarget, string newName)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for duplicate action.");
            }

            GameObject sourceGO = FindGameObject(target);
            if (sourceGO == null)
            {
                return new
                {
                    success = false,
                    error = $"Target UI element '{target}' not found."
                };
            }

            // Verify it has a RectTransform
            if (sourceGO.GetComponent<RectTransform>() == null)
            {
                return new
                {
                    success = false,
                    error = $"Target '{target}' does not have a RectTransform. Use manage_gameobject to duplicate non-UI objects."
                };
            }

            // Determine parent
            Transform parentTransform = sourceGO.transform.parent;
            if (!string.IsNullOrEmpty(newParentTarget))
            {
                GameObject newParentGO = FindGameObject(newParentTarget);
                if (newParentGO == null)
                {
                    return new
                    {
                        success = false,
                        error = $"New parent '{newParentTarget}' not found."
                    };
                }

                // Validate new parent is under a Canvas
                if (GetParentCanvas(newParentGO.transform) == null)
                {
                    return new
                    {
                        success = false,
                        error = $"New parent '{newParentTarget}' is not under a Canvas."
                    };
                }

                parentTransform = newParentGO.transform;
            }

            // Duplicate
            GameObject duplicate = UnityEngine.Object.Instantiate(sourceGO, parentTransform);
            Undo.RegisterCreatedObjectUndo(duplicate, $"Duplicate UI Element '{sourceGO.name}'");

            // Set name
            if (!string.IsNullOrEmpty(newName))
            {
                duplicate.name = newName;
            }
            else
            {
                duplicate.name = sourceGO.name.Replace("(Clone)", "").Trim() + "_Copy";
            }

            EditorUtility.SetDirty(duplicate);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = duplicate;

            RectTransform rt = duplicate.GetComponent<RectTransform>();

            return new
            {
                success = true,
                message = $"Duplicated '{sourceGO.name}' as '{duplicate.name}'.",
                instance_id = duplicate.GetInstanceID(),
                name = duplicate.name,
                original_instance_id = sourceGO.GetInstanceID(),
                original_name = sourceGO.name,
                parent = parentTransform != null ? parentTransform.name : null,
                position = new { x = rt.anchoredPosition.x, y = rt.anchoredPosition.y },
                size = new { width = rt.sizeDelta.x, height = rt.sizeDelta.y }
            };
        }

        // ─────────────────────────────────────────────
        //  Reorder Handler
        // ─────────────────────────────────────────────

        private static object HandleReorder(string target, int? siblingIndex, string move)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for reorder action.");
            }

            if (!siblingIndex.HasValue && string.IsNullOrEmpty(move))
            {
                throw MCPException.InvalidParams("Either 'sibling_index' or 'move' parameter is required for reorder action.");
            }

            GameObject go = FindGameObject(target);
            if (go == null)
            {
                return new
                {
                    success = false,
                    error = $"Target UI element '{target}' not found."
                };
            }

            Transform t = go.transform;
            if (t.parent == null)
            {
                return new
                {
                    success = false,
                    error = $"Cannot reorder root-level object '{go.name}'."
                };
            }

            Undo.RecordObject(t, "Reorder UI Element");

            int oldIndex = t.GetSiblingIndex();
            int maxIndex = t.parent.childCount - 1;

            if (siblingIndex.HasValue)
            {
                int idx = Mathf.Clamp(siblingIndex.Value, 0, maxIndex);
                t.SetSiblingIndex(idx);
            }
            else if (!string.IsNullOrEmpty(move))
            {
                switch (move.ToLowerInvariant())
                {
                    case "first":
                        t.SetAsFirstSibling();
                        break;
                    case "last":
                        t.SetAsLastSibling();
                        break;
                    case "up":
                        if (oldIndex > 0)
                            t.SetSiblingIndex(oldIndex - 1);
                        break;
                    case "down":
                        if (oldIndex < maxIndex)
                            t.SetSiblingIndex(oldIndex + 1);
                        break;
                    default:
                        throw MCPException.InvalidParams($"Invalid move direction: '{move}'. Valid values: first, last, up, down");
                }
            }

            int newIndex = t.GetSiblingIndex();

            EditorUtility.SetDirty(go);

            return new
            {
                success = true,
                message = oldIndex != newIndex
                    ? $"UI element '{go.name}' moved from index {oldIndex} to {newIndex}."
                    : $"UI element '{go.name}' is already at index {newIndex}.",
                instance_id = go.GetInstanceID(),
                name = go.name,
                old_sibling_index = oldIndex,
                new_sibling_index = newIndex,
                sibling_count = t.parent.childCount
            };
        }

        // ─────────────────────────────────────────────
        //  AddEffect Handler
        // ─────────────────────────────────────────────

        private static object HandleAddEffect(string target, string effectType, Dictionary<string, object> properties)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for add_effect action.");
            }

            if (string.IsNullOrEmpty(effectType))
            {
                throw MCPException.InvalidParams("'effect_type' parameter is required for add_effect action.");
            }

            GameObject go = FindGameObject(target);
            if (go == null)
            {
                return new
                {
                    success = false,
                    error = $"Target UI element '{target}' not found."
                };
            }

            Component addedComponent = null;
            var warnings = new List<string>();

            switch (effectType.ToLowerInvariant())
            {
                case "outline":
                    addedComponent = AddOrGetComponent<Outline>(go);
                    if (properties != null)
                    {
                        ApplyOutlineProperties((Outline)addedComponent, properties, warnings);
                    }
                    break;

                case "shadow":
                    addedComponent = AddOrGetComponent<Shadow>(go);
                    if (properties != null)
                    {
                        ApplyShadowProperties((Shadow)addedComponent, properties, warnings);
                    }
                    break;

                case "mask":
                    addedComponent = AddOrGetComponent<Mask>(go);
                    if (properties != null)
                    {
                        ApplyMaskProperties((Mask)addedComponent, properties, warnings);
                    }
                    break;

                case "content_size_fitter":
                    addedComponent = AddOrGetComponent<ContentSizeFitter>(go);
                    if (properties != null)
                    {
                        ApplyContentSizeFitterProperties((ContentSizeFitter)addedComponent, properties, warnings);
                    }
                    break;

                case "aspect_ratio_fitter":
                    addedComponent = AddOrGetComponent<AspectRatioFitter>(go);
                    if (properties != null)
                    {
                        ApplyAspectRatioFitterProperties((AspectRatioFitter)addedComponent, properties, warnings);
                    }
                    break;

                default:
                    throw MCPException.InvalidParams(
                        $"Invalid effect_type: '{effectType}'. Valid types: outline, shadow, mask, content_size_fitter, aspect_ratio_fitter");
            }

            EditorUtility.SetDirty(go);

            var result = new Dictionary<string, object>
            {
                { "success", true },
                { "message", $"Effect '{effectType}' added to '{go.name}'." },
                { "instance_id", go.GetInstanceID() },
                { "name", go.name },
                { "effect_type", effectType }
            };

            if (warnings.Count > 0)
            {
                result["warnings"] = warnings;
            }

            return result;
        }

        #endregion

        #region Effect Property Helpers

        private static void ApplyOutlineProperties(Outline outline, Dictionary<string, object> properties, List<string> warnings)
        {
            Undo.RecordObject(outline, "Configure Outline");

            foreach (var kvp in properties)
            {
                try
                {
                    switch (kvp.Key.ToLowerInvariant())
                    {
                        case "color":
                            outline.effectColor = ParseColor(kvp.Value);
                            break;
                        case "distance":
                            outline.effectDistance = ParseVector2Value(kvp.Value);
                            break;
                        case "use_graphic_alpha":
                            outline.useGraphicAlpha = Convert.ToBoolean(kvp.Value);
                            break;
                        default:
                            warnings.Add($"Unknown outline property: '{kvp.Key}'");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to apply outline property '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void ApplyShadowProperties(Shadow shadow, Dictionary<string, object> properties, List<string> warnings)
        {
            Undo.RecordObject(shadow, "Configure Shadow");

            foreach (var kvp in properties)
            {
                try
                {
                    switch (kvp.Key.ToLowerInvariant())
                    {
                        case "color":
                            shadow.effectColor = ParseColor(kvp.Value);
                            break;
                        case "distance":
                            shadow.effectDistance = ParseVector2Value(kvp.Value);
                            break;
                        case "use_graphic_alpha":
                            shadow.useGraphicAlpha = Convert.ToBoolean(kvp.Value);
                            break;
                        default:
                            warnings.Add($"Unknown shadow property: '{kvp.Key}'");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to apply shadow property '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void ApplyMaskProperties(Mask mask, Dictionary<string, object> properties, List<string> warnings)
        {
            Undo.RecordObject(mask, "Configure Mask");

            foreach (var kvp in properties)
            {
                try
                {
                    switch (kvp.Key.ToLowerInvariant())
                    {
                        case "show_mask_graphic":
                            mask.showMaskGraphic = Convert.ToBoolean(kvp.Value);
                            break;
                        default:
                            warnings.Add($"Unknown mask property: '{kvp.Key}'");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to apply mask property '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void ApplyContentSizeFitterProperties(ContentSizeFitter fitter, Dictionary<string, object> properties, List<string> warnings)
        {
            Undo.RecordObject(fitter, "Configure ContentSizeFitter");

            foreach (var kvp in properties)
            {
                try
                {
                    switch (kvp.Key.ToLowerInvariant())
                    {
                        case "horizontal_fit":
                            if (Enum.TryParse<ContentSizeFitter.FitMode>(Convert.ToString(kvp.Value), true, out var hFit))
                                fitter.horizontalFit = hFit;
                            else
                                warnings.Add($"Invalid horizontal_fit value: '{kvp.Value}'. Valid: Unconstrained, MinSize, PreferredSize");
                            break;
                        case "vertical_fit":
                            if (Enum.TryParse<ContentSizeFitter.FitMode>(Convert.ToString(kvp.Value), true, out var vFit))
                                fitter.verticalFit = vFit;
                            else
                                warnings.Add($"Invalid vertical_fit value: '{kvp.Value}'. Valid: Unconstrained, MinSize, PreferredSize");
                            break;
                        default:
                            warnings.Add($"Unknown content_size_fitter property: '{kvp.Key}'");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to apply content_size_fitter property '{kvp.Key}': {ex.Message}");
                }
            }
        }

        private static void ApplyAspectRatioFitterProperties(AspectRatioFitter fitter, Dictionary<string, object> properties, List<string> warnings)
        {
            Undo.RecordObject(fitter, "Configure AspectRatioFitter");

            foreach (var kvp in properties)
            {
                try
                {
                    switch (kvp.Key.ToLowerInvariant())
                    {
                        case "aspect_mode":
                            if (Enum.TryParse<AspectRatioFitter.AspectMode>(Convert.ToString(kvp.Value), true, out var mode))
                                fitter.aspectMode = mode;
                            else
                                warnings.Add($"Invalid aspect_mode value: '{kvp.Value}'. Valid: None, WidthControlsHeight, HeightControlsWidth, FitInParent, EnvelopeParent");
                            break;
                        case "aspect_ratio":
                            fitter.aspectRatio = Convert.ToSingle(kvp.Value);
                            break;
                        default:
                            warnings.Add($"Unknown aspect_ratio_fitter property: '{kvp.Key}'");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to apply aspect_ratio_fitter property '{kvp.Key}': {ex.Message}");
                }
            }
        }

        #endregion

        #region Helper Methods

        // ─────────────────────────────────────────────
        //  FindGameObject (reusable pattern)
        // ─────────────────────────────────────────────

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
                    if (root == null) continue;

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
                if (gameObject.name.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    return gameObject;
                }
            }

            return null;
        }

        // ─────────────────────────────────────────────
        //  Scene Helpers
        // ─────────────────────────────────────────────

        private static Scene GetActiveScene()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                return prefabStage.scene;
            }
            return EditorSceneManager.GetActiveScene();
        }

        private static List<GameObject> GetAllSceneObjects(bool includeInactive)
        {
            var results = new List<GameObject>();
            var roots = GetActiveScene().GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root == null) continue;
                results.Add(root);
                foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactive))
                {
                    if (child.gameObject != root)
                    {
                        results.Add(child.gameObject);
                    }
                }
            }
            return results;
        }

        // ─────────────────────────────────────────────
        //  Canvas Validation
        // ─────────────────────────────────────────────

        private static Canvas GetParentCanvas(Transform t)
        {
            return t.GetComponentInParent<Canvas>();
        }

        // ─────────────────────────────────────────────
        //  Vector / Color Parsing
        // ─────────────────────────────────────────────

        private static Vector2? ParseVector2(object value)
        {
            if (value == null) return null;

            if (value is IList<object> list && list.Count >= 2)
            {
                return new Vector2(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]));
            }

            if (value is IDictionary<string, object> dict)
            {
                float x = dict.ContainsKey("x") ? Convert.ToSingle(dict["x"]) :
                           dict.ContainsKey("width") ? Convert.ToSingle(dict["width"]) : 0f;
                float y = dict.ContainsKey("y") ? Convert.ToSingle(dict["y"]) :
                           dict.ContainsKey("height") ? Convert.ToSingle(dict["height"]) : 0f;
                return new Vector2(x, y);
            }

            return null;
        }

        private static Vector2 ParseVector2Value(object value)
        {
            var parsed = ParseVector2(value);
            if (parsed.HasValue) return parsed.Value;

            // Try single number as uniform distance
            if (value is IConvertible)
            {
                float v = Convert.ToSingle(value);
                return new Vector2(v, -v);
            }

            return new Vector2(1, -1);
        }

        private static Color ParseColor(object value)
        {
            if (value is string str)
            {
                if (ColorUtility.TryParseHtmlString(str, out Color color))
                    return color;

                // Try named colors
                switch (str.ToLowerInvariant())
                {
                    case "white": return Color.white;
                    case "black": return Color.black;
                    case "red": return Color.red;
                    case "green": return Color.green;
                    case "blue": return Color.blue;
                    case "yellow": return Color.yellow;
                    case "cyan": return Color.cyan;
                    case "magenta": return Color.magenta;
                    case "gray":
                    case "grey": return Color.gray;
                    case "clear": return Color.clear;
                }
            }

            if (value is IList<object> list)
            {
                float r = list.Count > 0 ? Convert.ToSingle(list[0]) : 0f;
                float g = list.Count > 1 ? Convert.ToSingle(list[1]) : 0f;
                float b = list.Count > 2 ? Convert.ToSingle(list[2]) : 0f;
                float a = list.Count > 3 ? Convert.ToSingle(list[3]) : 1f;
                return new Color(r, g, b, a);
            }

            if (value is IDictionary<string, object> dict)
            {
                float r = dict.ContainsKey("r") ? Convert.ToSingle(dict["r"]) : 0f;
                float g = dict.ContainsKey("g") ? Convert.ToSingle(dict["g"]) : 0f;
                float b = dict.ContainsKey("b") ? Convert.ToSingle(dict["b"]) : 0f;
                float a = dict.ContainsKey("a") ? Convert.ToSingle(dict["a"]) : 1f;
                return new Color(r, g, b, a);
            }

            return Color.white;
        }

        // ─────────────────────────────────────────────
        //  Component Helpers
        // ─────────────────────────────────────────────

        private static T AddOrGetComponent<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            if (existing != null) return existing;

            return Undo.AddComponent<T>(go);
        }

        // ─────────────────────────────────────────────
        //  Text Content Helpers
        // ─────────────────────────────────────────────

        private static void ApplyTextContent(GameObject go, string elementType, string text)
        {
            // Try direct Text component
            var textComp = go.GetComponent<Text>();
            if (textComp != null)
            {
                Undo.RecordObject(textComp, "Set Text Content");
                textComp.text = text;
                return;
            }

#if UNITY_MCP_TMP
            // Try TMP component
            var tmpComp = go.GetComponent<TMPro.TMP_Text>();
            if (tmpComp != null)
            {
                Undo.RecordObject(tmpComp, "Set TMP Text Content");
                tmpComp.text = text;
                return;
            }
#endif

            // For composite elements (button, input_field, etc.), try to find text in children
            var childText = go.GetComponentInChildren<Text>();
            if (childText != null)
            {
                Undo.RecordObject(childText, "Set Child Text Content");
                childText.text = text;
                return;
            }

#if UNITY_MCP_TMP
            var childTmp = go.GetComponentInChildren<TMPro.TMP_Text>();
            if (childTmp != null)
            {
                Undo.RecordObject(childTmp, "Set Child TMP Text Content");
                childTmp.text = text;
                return;
            }
#endif
        }

        // ─────────────────────────────────────────────
        //  Sprite Helper
        // ─────────────────────────────────────────────

        private static void ApplySprite(GameObject go, string spritePath, List<string> warnings)
        {
            var image = go.GetComponent<Image>();
            if (image != null)
            {
                Undo.RecordObject(image, "Set Sprite");
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sprite != null)
                {
                    image.sprite = sprite;
                }
                else
                {
                    warnings.Add($"Sprite not found at path: '{spritePath}'");
                }
                return;
            }

            var rawImage = go.GetComponent<RawImage>();
            if (rawImage != null)
            {
                Undo.RecordObject(rawImage, "Set Texture");
                var texture = AssetDatabase.LoadAssetAtPath<Texture>(spritePath);
                if (texture != null)
                {
                    rawImage.texture = texture;
                }
                else
                {
                    warnings.Add($"Texture not found at path: '{spritePath}'");
                }
                return;
            }

            warnings.Add("No Image or RawImage component found on target element.");
        }

        // ─────────────────────────────────────────────
        //  Interactable Helper
        // ─────────────────────────────────────────────

        private static void ApplyInteractable(GameObject go, bool interactable)
        {
            var selectable = go.GetComponent<Selectable>();
            if (selectable != null)
            {
                Undo.RecordObject(selectable, "Set Interactable");
                selectable.interactable = interactable;
            }
        }

        // ─────────────────────────────────────────────
        //  Element Type Detection
        // ─────────────────────────────────────────────

        /// <summary>
        /// Attempts to detect the element type of an existing UI GameObject based on its components.
        /// Used for style application when modifying elements.
        /// </summary>
        private static string DetectElementType(GameObject go)
        {
#if UNITY_MCP_TMP
            if (go.GetComponent<TMPro.TMP_InputField>() != null) return "input_field_tmp";
            if (go.GetComponent<TMPro.TMP_Dropdown>() != null) return "dropdown_tmp";
            if (go.GetComponent<TMPro.TextMeshProUGUI>() != null)
            {
                // Check if it's a button with TMP text
                if (go.GetComponent<Button>() != null) return "button_tmp";
                return "text_tmp";
            }
#endif
            if (go.GetComponent<InputField>() != null) return "input_field";
            if (go.GetComponent<Dropdown>() != null) return "dropdown";
            if (go.GetComponent<Slider>() != null) return "slider";
            if (go.GetComponent<Toggle>() != null) return "toggle";
            if (go.GetComponent<ScrollRect>() != null) return "scrollview";
            if (go.GetComponent<Button>() != null) return "button";
            if (go.GetComponent<GridLayoutGroup>() != null) return "grid_layout";
            if (go.GetComponent<HorizontalLayoutGroup>() != null) return "horizontal_layout";
            if (go.GetComponent<VerticalLayoutGroup>() != null) return "vertical_layout";
            if (go.GetComponent<LayoutElement>() != null && go.GetComponent<Graphic>() == null) return "spacer";
            if (go.GetComponent<RawImage>() != null) return "raw_image";
            if (go.GetComponent<Text>() != null) return "text";
            if (go.GetComponent<Image>() != null) return "panel";

            return "panel"; // Fallback
        }

        // ─────────────────────────────────────────────
        //  Name Formatting
        // ─────────────────────────────────────────────

        private static string FormatDefaultName(string elementType)
        {
            // Convert snake_case to PascalCase for default naming
            var parts = elementType.Split('_');
            return string.Join("", parts.Select(p =>
                p.Length > 0 ? char.ToUpper(p[0]) + p.Substring(1) : p));
        }

        #endregion
    }
}
