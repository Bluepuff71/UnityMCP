using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Services;
#if UNITY_MCP_TMP
using TMPro;
#endif


#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Inspects Canvas UI hierarchies: view element trees, deep-inspect elements,
    /// find by type/name/component, or get high-level summaries of all Canvas UIs.
    /// All actions are read-only.
    /// </summary>
    [MCPTool("inspect_ui", "Inspects Canvas UI hierarchies: view element trees, deep-inspect elements, find by type/name, or get summaries.", Category = "UI")]
    public static class UIInspectTools
    {
        #region Action Methods

        /// <summary>
        /// Gets the full uGUI tree under a Canvas.
        /// </summary>
        [MCPAction("hierarchy", Description = "Get full uGUI tree under a Canvas", ReadOnlyHint = true)]
        public static object Hierarchy(
            [MCPParam("canvas_target", "Instance ID or name/path of the Canvas")] string canvasTarget = null,
            [MCPParam("max_depth", "Maximum tree depth to traverse", Minimum = 1, Maximum = 20)] int? maxDepth = null,
            [MCPParam("include_style", "Include style properties in output")] bool? includeStyle = null)
        {
            try
            {
                return HandleHierarchy(canvasTarget, maxDepth ?? 10, includeStyle ?? false);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error inspecting UI hierarchy: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Deep-inspects a single UI element with full detail.
        /// </summary>
        [MCPAction("element", Description = "Deep-inspect a single UI element", ReadOnlyHint = true)]
        public static object Element(
            [MCPParam("target", "Instance ID or name/path of the UI element", required: true)] string target = null)
        {
            try
            {
                return HandleElement(target);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error inspecting UI element: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Finds UI elements by type, name pattern, or component.
        /// </summary>
        [MCPAction("find", Description = "Find UI elements by type, name, or component", ReadOnlyHint = true)]
        public static object Find(
            [MCPParam("canvas_target", "Instance ID or name/path of the Canvas (searches all if omitted)")] string canvasTarget = null,
            [MCPParam("element_type", "Element type to match (e.g. button, text, image)")] string elementType = null,
            [MCPParam("name_pattern", "Name substring or regex pattern (case-insensitive)")] string namePattern = null,
            [MCPParam("has_component", "Component type name to filter by")] string hasComponent = null,
            [MCPParam("max_results", "Maximum number of results to return", Minimum = 1, Maximum = 500)] int? maxResults = null)
        {
            try
            {
                return HandleFind(canvasTarget, elementType, namePattern, hasComponent, maxResults ?? 100);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error finding UI elements: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Provides a high-level summary of all Canvas UIs in the scene.
        /// </summary>
        [MCPAction("summary", Description = "High-level summary of all Canvas UIs in the scene", ReadOnlyHint = true)]
        public static object Summary()
        {
            try
            {
                return HandleSummary();
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error generating UI summary: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Handler Methods

        // ─────────────────────────────────────────────
        //  Hierarchy Handler
        // ─────────────────────────────────────────────

        private static object HandleHierarchy(string canvasTarget, int maxDepth, bool includeStyle)
        {
            Canvas canvas = ResolveCanvas(canvasTarget);
            if (canvas == null)
            {
                if (!string.IsNullOrEmpty(canvasTarget))
                {
                    return new
                    {
                        success = false,
                        error = $"Canvas '{canvasTarget}' not found."
                    };
                }

                return new
                {
                    success = false,
                    error = "No Canvas found in the scene."
                };
            }

            var tree = BuildNode(canvas.transform, 0, maxDepth, includeStyle);

            return new
            {
                success = true,
                canvas_name = canvas.gameObject.name,
                instance_id = canvas.gameObject.GetInstanceID(),
                max_depth = maxDepth,
                include_style = includeStyle,
                tree
            };
        }

        // ─────────────────────────────────────────────
        //  Element Handler
        // ─────────────────────────────────────────────

        private static object HandleElement(string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("'target' parameter is required for element action.");
            }

            GameObject go = FindGameObject(target);
            if (go == null)
            {
                return new
                {
                    success = false,
                    error = $"UI element '{target}' not found."
                };
            }

            var rt = go.GetComponent<RectTransform>();
            string elementType = DetectElementType(go);

            var result = new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "instance_id", go.GetInstanceID() },
                { "active", go.activeSelf },
                { "active_in_hierarchy", go.activeInHierarchy },
                { "element_type", elementType },
                { "sibling_index", go.transform.GetSiblingIndex() },
                { "child_count", go.transform.childCount }
            };

            // Rect info
            if (rt != null)
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);

                result["rect"] = new Dictionary<string, object>
                {
                    { "anchored_position", new[] { rt.anchoredPosition.x, rt.anchoredPosition.y } },
                    { "size", new[] { rt.sizeDelta.x, rt.sizeDelta.y } },
                    { "anchor_min", new[] { rt.anchorMin.x, rt.anchorMin.y } },
                    { "anchor_max", new[] { rt.anchorMax.x, rt.anchorMax.y } },
                    { "pivot", new[] { rt.pivot.x, rt.pivot.y } },
                    { "world_corners", new[]
                        {
                            new[] { corners[0].x, corners[0].y, corners[0].z },
                            new[] { corners[1].x, corners[1].y, corners[1].z },
                            new[] { corners[2].x, corners[2].y, corners[2].z },
                            new[] { corners[3].x, corners[3].y, corners[3].z }
                        }
                    }
                };
            }

            // Parent info
            if (go.transform.parent != null)
            {
                result["parent"] = new Dictionary<string, object>
                {
                    { "name", go.transform.parent.gameObject.name },
                    { "instance_id", go.transform.parent.gameObject.GetInstanceID() }
                };
            }

            // Components
            var components = go.GetComponents<Component>();
            var componentNames = new List<string>();
            foreach (var c in components)
            {
                if (c != null)
                {
                    componentNames.Add(c.GetType().Name);
                }
            }
            result["components"] = componentNames;

            // Style properties
            var style = ExtractStyleProperties(go, elementType);
            if (style.Count > 0)
            {
                result["style"] = style;
            }

            return result;
        }

        // ─────────────────────────────────────────────
        //  Find Handler
        // ─────────────────────────────────────────────

        private static object HandleFind(string canvasTarget, string elementType, string namePattern, string hasComponent, int maxResults)
        {
            if (string.IsNullOrEmpty(elementType) && string.IsNullOrEmpty(namePattern) && string.IsNullOrEmpty(hasComponent))
            {
                throw MCPException.InvalidParams("At least one filter is required: element_type, name_pattern, or has_component.");
            }

            // Get canvases to search
            var canvases = new List<Canvas>();
            if (!string.IsNullOrEmpty(canvasTarget))
            {
                Canvas canvas = ResolveCanvas(canvasTarget);
                if (canvas == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Canvas '{canvasTarget}' not found."
                    };
                }
                canvases.Add(canvas);
            }
            else
            {
                canvases.AddRange(UnityEngine.Object.FindObjectsOfType<Canvas>(true));
            }

            // Compile regex if name_pattern is provided
            Regex nameRegex = null;
            if (!string.IsNullOrEmpty(namePattern))
            {
                try
                {
                    nameRegex = new Regex(namePattern, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException)
                {
                    // Fall back to simple substring match if pattern is not valid regex
                    nameRegex = null;
                }
            }

            var matches = new List<object>();

            foreach (var canvas in canvases)
            {
                if (canvas == null) continue;

                var allTransforms = canvas.GetComponentsInChildren<Transform>(true);
                foreach (var t in allTransforms)
                {
                    if (matches.Count >= maxResults) break;

                    var go = t.gameObject;
                    string detectedType = DetectElementType(go);

                    // Filter by element_type
                    if (!string.IsNullOrEmpty(elementType))
                    {
                        if (!string.Equals(detectedType, elementType, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    // Filter by name_pattern
                    if (!string.IsNullOrEmpty(namePattern))
                    {
                        if (nameRegex != null)
                        {
                            if (!nameRegex.IsMatch(go.name))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            // Substring match fallback
                            if (go.name.IndexOf(namePattern, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                        }
                    }

                    // Filter by has_component
                    if (!string.IsNullOrEmpty(hasComponent))
                    {
                        bool found = false;
                        foreach (var c in go.GetComponents<Component>())
                        {
                            if (c != null && string.Equals(c.GetType().Name, hasComponent, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found) continue;
                    }

                    var rt = go.GetComponent<RectTransform>();
                    var match = new Dictionary<string, object>
                    {
                        { "name", go.name },
                        { "instance_id", go.GetInstanceID() },
                        { "active", go.activeSelf },
                        { "element_type", detectedType },
                        { "canvas", canvas.gameObject.name },
                        { "path", GetHierarchyPath(go.transform) }
                    };

                    if (rt != null)
                    {
                        match["rect"] = new Dictionary<string, object>
                        {
                            { "anchored_position", new[] { rt.anchoredPosition.x, rt.anchoredPosition.y } },
                            { "size", new[] { rt.sizeDelta.x, rt.sizeDelta.y } }
                        };
                    }

                    matches.Add(match);
                }

                if (matches.Count >= maxResults) break;
            }

            return new
            {
                success = true,
                count = matches.Count,
                truncated = matches.Count >= maxResults,
                filters = new Dictionary<string, object>
                {
                    { "element_type", elementType ?? (object)null },
                    { "name_pattern", namePattern ?? (object)null },
                    { "has_component", hasComponent ?? (object)null },
                    { "canvas_target", canvasTarget ?? (object)null }
                },
                matches
            };
        }

        // ─────────────────────────────────────────────
        //  Summary Handler
        // ─────────────────────────────────────────────

        private static object HandleSummary()
        {
            var allCanvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
            var canvasSummaries = new List<object>();

            foreach (var canvas in allCanvases)
            {
                if (canvas == null) continue;

                var go = canvas.gameObject;
                var allTransforms = canvas.GetComponentsInChildren<Transform>(true);

                // Count element types
                var typeCounts = new Dictionary<string, int>();
                int totalElements = 0;
                int maxDepthFound = 0;

                foreach (var t in allTransforms)
                {
                    totalElements++;
                    string eType = DetectElementType(t.gameObject);
                    if (typeCounts.ContainsKey(eType))
                    {
                        typeCounts[eType]++;
                    }
                    else
                    {
                        typeCounts[eType] = 1;
                    }

                    int depth = GetDepthRelativeTo(t, canvas.transform);
                    if (depth > maxDepthFound)
                    {
                        maxDepthFound = depth;
                    }
                }

                canvasSummaries.Add(new Dictionary<string, object>
                {
                    { "name", go.name },
                    { "instance_id", go.GetInstanceID() },
                    { "active", go.activeInHierarchy },
                    { "render_mode", canvas.renderMode.ToString() },
                    { "sort_order", canvas.sortingOrder },
                    { "element_count", totalElements },
                    { "max_depth", maxDepthFound },
                    { "element_types", typeCounts }
                });
            }

            return new
            {
                success = true,
                canvas_count = canvasSummaries.Count,
                canvases = canvasSummaries
            };
        }

        #endregion

        #region Tree Building

        private static Dictionary<string, object> BuildNode(Transform t, int depth, int maxDepth, bool includeStyle)
        {
            var go = t.gameObject;
            var rt = go.GetComponent<RectTransform>();
            string elementType = DetectElementType(go);

            var node = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["instance_id"] = go.GetInstanceID(),
                ["active"] = go.activeSelf,
                ["element_type"] = elementType
            };

            if (rt != null)
            {
                node["rect"] = new Dictionary<string, object>
                {
                    ["anchored_position"] = new[] { rt.anchoredPosition.x, rt.anchoredPosition.y },
                    ["size"] = new[] { rt.sizeDelta.x, rt.sizeDelta.y },
                    ["anchor_min"] = new[] { rt.anchorMin.x, rt.anchorMin.y },
                    ["anchor_max"] = new[] { rt.anchorMax.x, rt.anchorMax.y },
                    ["pivot"] = new[] { rt.pivot.x, rt.pivot.y }
                };
            }

            if (includeStyle)
            {
                var style = ExtractStyleProperties(go, elementType);
                if (style.Count > 0)
                {
                    node["style"] = style;
                }
            }

            if (depth < maxDepth)
            {
                var children = new List<object>();
                for (int i = 0; i < t.childCount; i++)
                {
                    children.Add(BuildNode(t.GetChild(i), depth + 1, maxDepth, includeStyle));
                }
                if (children.Count > 0)
                {
                    node["children"] = children;
                }
            }
            else if (t.childCount > 0)
            {
                node["children_truncated"] = t.childCount;
            }

            return node;
        }

        #endregion

        #region Style Extraction

        private static Dictionary<string, object> ExtractStyleProperties(GameObject go, string elementType)
        {
            var style = new Dictionary<string, object>();

            // Common: Graphic color and raycast_target
            var graphic = go.GetComponent<Graphic>();
            if (graphic != null)
            {
                style["color"] = new[] { graphic.color.r, graphic.color.g, graphic.color.b, graphic.color.a };
                style["raycast_target"] = graphic.raycastTarget;
            }

            // Type-specific extraction
            switch (elementType)
            {
                case "text":
                    ExtractTextStyle(go, style);
                    break;

                case "text_tmp":
                    ExtractTextTMPStyle(go, style);
                    break;

                case "image":
                case "panel":
                    ExtractImageStyle(go, style);
                    break;

                case "raw_image":
                    ExtractRawImageStyle(go, style);
                    break;

                case "button":
                case "button_tmp":
                    ExtractSelectableStyle(go, style);
                    break;

                case "toggle":
                    ExtractSelectableStyle(go, style);
                    var toggle = go.GetComponent<Toggle>();
                    if (toggle != null)
                    {
                        style["is_on"] = toggle.isOn;
                    }
                    break;

                case "slider":
                    ExtractSelectableStyle(go, style);
                    var slider = go.GetComponent<Slider>();
                    if (slider != null)
                    {
                        style["value"] = slider.value;
                        style["min_value"] = slider.minValue;
                        style["max_value"] = slider.maxValue;
                        style["whole_numbers"] = slider.wholeNumbers;
                        style["direction"] = slider.direction.ToString();
                    }
                    break;

                case "dropdown":
                case "dropdown_tmp":
                    ExtractSelectableStyle(go, style);
                    ExtractDropdownStyle(go, elementType, style);
                    break;

                case "input_field":
                    ExtractInputFieldStyle(go, style);
                    break;

                case "input_field_tmp":
                    ExtractInputFieldTMPStyle(go, style);
                    break;

                case "scrollview":
                    var scrollRect = go.GetComponent<ScrollRect>();
                    if (scrollRect != null)
                    {
                        style["horizontal"] = scrollRect.horizontal;
                        style["vertical"] = scrollRect.vertical;
                        style["movement_type"] = scrollRect.movementType.ToString();
                        style["elasticity"] = scrollRect.elasticity;
                        style["inertia"] = scrollRect.inertia;
                    }
                    break;

                case "grid_layout":
                    var grid = go.GetComponent<GridLayoutGroup>();
                    if (grid != null)
                    {
                        style["cell_size"] = new[] { grid.cellSize.x, grid.cellSize.y };
                        style["spacing"] = new[] { grid.spacing.x, grid.spacing.y };
                        style["child_alignment"] = grid.childAlignment.ToString();
                        style["constraint"] = grid.constraint.ToString();
                        style["constraint_count"] = grid.constraintCount;
                        style["padding"] = new { left = grid.padding.left, right = grid.padding.right, top = grid.padding.top, bottom = grid.padding.bottom };
                    }
                    break;

                case "horizontal_layout":
                    var hLayout = go.GetComponent<HorizontalLayoutGroup>();
                    if (hLayout != null)
                    {
                        style["spacing"] = hLayout.spacing;
                        style["child_alignment"] = hLayout.childAlignment.ToString();
                        style["child_force_expand_width"] = hLayout.childForceExpandWidth;
                        style["child_force_expand_height"] = hLayout.childForceExpandHeight;
                        style["padding"] = new { left = hLayout.padding.left, right = hLayout.padding.right, top = hLayout.padding.top, bottom = hLayout.padding.bottom };
                    }
                    break;

                case "vertical_layout":
                    var vLayout = go.GetComponent<VerticalLayoutGroup>();
                    if (vLayout != null)
                    {
                        style["spacing"] = vLayout.spacing;
                        style["child_alignment"] = vLayout.childAlignment.ToString();
                        style["child_force_expand_width"] = vLayout.childForceExpandWidth;
                        style["child_force_expand_height"] = vLayout.childForceExpandHeight;
                        style["padding"] = new { left = vLayout.padding.left, right = vLayout.padding.right, top = vLayout.padding.top, bottom = vLayout.padding.bottom };
                    }
                    break;
            }

            return style;
        }

        private static void ExtractTextStyle(GameObject go, Dictionary<string, object> style)
        {
            var text = go.GetComponent<Text>();
            if (text == null) return;

            style["text"] = text.text;
            style["font_size"] = text.fontSize;
            style["alignment"] = text.alignment.ToString();
            style["font_style"] = text.fontStyle.ToString();
            style["line_spacing"] = text.lineSpacing;
            style["overflow"] = text.horizontalOverflow.ToString();
        }

        private static void ExtractTextTMPStyle(GameObject go, Dictionary<string, object> style)
        {
#if UNITY_MCP_TMP
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp == null) return;

            style["text"] = tmp.text;
            style["font_size"] = tmp.fontSize;
            style["alignment"] = tmp.alignment.ToString();
            style["font_style"] = tmp.fontStyle.ToString();
            style["line_spacing"] = tmp.lineSpacing;
            style["overflow"] = tmp.overflowMode.ToString();
#endif
        }

        private static void ExtractImageStyle(GameObject go, Dictionary<string, object> style)
        {
            var image = go.GetComponent<Image>();
            if (image == null) return;

            style["image_type"] = image.type.ToString();
            style["preserve_aspect"] = image.preserveAspect;
            style["fill_center"] = image.fillCenter;
            if (image.sprite != null)
            {
                style["sprite_path"] = AssetDatabase.GetAssetPath(image.sprite);
                style["sprite_name"] = image.sprite.name;
            }
        }

        private static void ExtractRawImageStyle(GameObject go, Dictionary<string, object> style)
        {
            var rawImage = go.GetComponent<RawImage>();
            if (rawImage == null) return;

            if (rawImage.texture != null)
            {
                style["texture_path"] = AssetDatabase.GetAssetPath(rawImage.texture);
                style["texture_name"] = rawImage.texture.name;
            }
            style["uv_rect"] = new[] { rawImage.uvRect.x, rawImage.uvRect.y, rawImage.uvRect.width, rawImage.uvRect.height };
        }

        private static void ExtractSelectableStyle(GameObject go, Dictionary<string, object> style)
        {
            var selectable = go.GetComponent<Selectable>();
            if (selectable == null) return;

            style["interactable"] = selectable.interactable;
            style["transition"] = selectable.transition.ToString();

            if (selectable.transition == Selectable.Transition.ColorTint)
            {
                var colors = selectable.colors;
                style["normal_color"] = new[] { colors.normalColor.r, colors.normalColor.g, colors.normalColor.b, colors.normalColor.a };
                style["highlighted_color"] = new[] { colors.highlightedColor.r, colors.highlightedColor.g, colors.highlightedColor.b, colors.highlightedColor.a };
                style["pressed_color"] = new[] { colors.pressedColor.r, colors.pressedColor.g, colors.pressedColor.b, colors.pressedColor.a };
                style["disabled_color"] = new[] { colors.disabledColor.r, colors.disabledColor.g, colors.disabledColor.b, colors.disabledColor.a };
                style["fade_duration"] = colors.fadeDuration;
            }
        }

        private static void ExtractDropdownStyle(GameObject go, string elementType, Dictionary<string, object> style)
        {
#if UNITY_MCP_TMP
            if (elementType == "dropdown_tmp")
            {
                var dropdown = go.GetComponent<TMP_Dropdown>();
                if (dropdown != null)
                {
                    style["value"] = dropdown.value;
                    style["options"] = dropdown.options.Select(o => o.text).ToList();
                }
                return;
            }
#endif
            var legacyDropdown = go.GetComponent<Dropdown>();
            if (legacyDropdown != null)
            {
                style["value"] = legacyDropdown.value;
                style["options"] = legacyDropdown.options.Select(o => o.text).ToList();
            }
        }

        private static void ExtractInputFieldStyle(GameObject go, Dictionary<string, object> style)
        {
            var inputField = go.GetComponent<InputField>();
            if (inputField == null) return;

            style["text"] = inputField.text;
            style["placeholder_text"] = inputField.placeholder is Text pt ? pt.text : null;
            style["character_limit"] = inputField.characterLimit;
            style["content_type"] = inputField.contentType.ToString();
            style["line_type"] = inputField.lineType.ToString();
        }

        private static void ExtractInputFieldTMPStyle(GameObject go, Dictionary<string, object> style)
        {
#if UNITY_MCP_TMP
            var inputField = go.GetComponent<TMP_InputField>();
            if (inputField == null) return;

            style["text"] = inputField.text;
            style["placeholder_text"] = inputField.placeholder is TextMeshProUGUI pt ? pt.text : null;
            style["character_limit"] = inputField.characterLimit;
            style["content_type"] = inputField.contentType.ToString();
            style["line_type"] = inputField.lineType.ToString();
#endif
        }

        #endregion

        #region Helper Methods

        private static Canvas ResolveCanvas(string canvasTarget)
        {
            if (!string.IsNullOrEmpty(canvasTarget))
            {
                GameObject go = FindGameObject(canvasTarget);
                if (go == null) return null;

                var canvas = go.GetComponent<Canvas>();
                if (canvas == null) return null;

                return canvas;
            }

            // Return the first root Canvas found
            var allCanvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
            foreach (var c in allCanvases)
            {
                // Prefer root canvases (not nested)
                if (c != null && c.isRootCanvas)
                {
                    return c;
                }
            }

            return allCanvases.Length > 0 ? allCanvases[0] : null;
        }

        private static string DetectElementType(GameObject go)
        {
#if UNITY_MCP_TMP
            if (go.GetComponent<TMPro.TMP_InputField>() != null) return "input_field_tmp";
            if (go.GetComponent<TMPro.TMP_Dropdown>() != null) return "dropdown_tmp";
            if (go.GetComponent<TMPro.TextMeshProUGUI>() != null)
            {
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

            return "panel";
        }

        private static string GetHierarchyPath(Transform t)
        {
            var parts = new List<string>();
            var current = t;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static int GetDepthRelativeTo(Transform child, Transform root)
        {
            int depth = 0;
            var current = child;
            while (current != null && current != root)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }

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
            }

            // Try name-based lookup
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

        #endregion
    }
}
