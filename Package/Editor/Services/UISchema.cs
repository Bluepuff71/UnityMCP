using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_MCP_TMP
using TMPro;
#endif

namespace UnityMCP.Editor.Services
{
    /// <summary>
    /// Static service class providing shared UI definitions consumed by all UI tools.
    /// Contains element type registry, anchor presets, style definitions, and helper methods
    /// for creating and configuring Unity UI elements.
    /// </summary>
    public static class UISchema
    {
        // ─────────────────────────────────────────────
        //  Anchor Preset Definitions
        // ─────────────────────────────────────────────

        /// <summary>
        /// Anchor preset data: anchorMin, anchorMax, pivot.
        /// </summary>
        public struct AnchorPreset
        {
            public readonly Vector2 AnchorMin;
            public readonly Vector2 AnchorMax;
            public readonly Vector2 Pivot;

            public AnchorPreset(Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
            {
                AnchorMin = anchorMin;
                AnchorMax = anchorMax;
                Pivot = pivot;
            }
        }

        /// <summary>
        /// All 16 named anchor presets mapping string names to anchorMin/anchorMax/pivot values.
        /// </summary>
        public static readonly Dictionary<string, AnchorPreset> AnchorPresets = new Dictionary<string, AnchorPreset>
        {
            // Top row
            { "top_left",     new AnchorPreset(new Vector2(0, 1),   new Vector2(0, 1),   new Vector2(0, 1)) },
            { "top_center",   new AnchorPreset(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1)) },
            { "top_right",    new AnchorPreset(new Vector2(1, 1),   new Vector2(1, 1),   new Vector2(1, 1)) },
            { "top_stretch",  new AnchorPreset(new Vector2(0, 1),   new Vector2(1, 1),   new Vector2(0.5f, 1)) },

            // Middle row
            { "middle_left",    new AnchorPreset(new Vector2(0, 0.5f),   new Vector2(0, 0.5f),   new Vector2(0, 0.5f)) },
            { "middle_center",  new AnchorPreset(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)) },
            { "middle_right",   new AnchorPreset(new Vector2(1, 0.5f),   new Vector2(1, 0.5f),   new Vector2(1, 0.5f)) },
            { "middle_stretch", new AnchorPreset(new Vector2(0, 0.5f),   new Vector2(1, 0.5f),   new Vector2(0.5f, 0.5f)) },

            // Bottom row
            { "bottom_left",    new AnchorPreset(new Vector2(0, 0),   new Vector2(0, 0),   new Vector2(0, 0)) },
            { "bottom_center",  new AnchorPreset(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0)) },
            { "bottom_right",   new AnchorPreset(new Vector2(1, 0),   new Vector2(1, 0),   new Vector2(1, 0)) },
            { "bottom_stretch", new AnchorPreset(new Vector2(0, 0),   new Vector2(1, 0),   new Vector2(0.5f, 0)) },

            // Stretch row
            { "stretch_left",   new AnchorPreset(new Vector2(0, 0), new Vector2(0, 1),   new Vector2(0, 0.5f)) },
            { "stretch_center", new AnchorPreset(new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f)) },
            { "stretch_right",  new AnchorPreset(new Vector2(1, 0), new Vector2(1, 1),   new Vector2(1, 0.5f)) },
            { "stretch_full",   new AnchorPreset(new Vector2(0, 0), new Vector2(1, 1),   new Vector2(0.5f, 0.5f)) },
        };

        // ─────────────────────────────────────────────
        //  Element Type Registry
        // ─────────────────────────────────────────────

        /// <summary>
        /// Set of all recognized element type names.
        /// </summary>
        public static readonly HashSet<string> ElementTypes = new HashSet<string>
        {
            "panel", "text", "image", "raw_image",
            "button", "toggle", "slider",
            "dropdown", "scrollview",
            "input_field",
            "grid_layout", "horizontal_layout", "vertical_layout",
            "spacer",
            "text_tmp", "button_tmp", "dropdown_tmp", "input_field_tmp",
        };

        /// <summary>
        /// Returns the list of available element type names (TMP types included only when available).
        /// </summary>
        public static List<string> GetAvailableElementTypes()
        {
            var types = new List<string>(ElementTypes);
#if !UNITY_MCP_TMP
            types.RemoveAll(t => t.EndsWith("_tmp"));
#endif
            return types;
        }

        /// <summary>
        /// Returns the list of available anchor preset names.
        /// </summary>
        public static List<string> GetAvailableAnchorPresets()
        {
            return new List<string>(AnchorPresets.Keys);
        }

        // ─────────────────────────────────────────────
        //  Style Property Definitions
        // ─────────────────────────────────────────────

        /// <summary>
        /// Returns the set of supported style property names for a given element type.
        /// </summary>
        public static HashSet<string> GetStyleProperties(string elementType)
        {
            var props = new HashSet<string> { "color", "raycast_target" };

            switch (elementType)
            {
                case "text":
                case "text_tmp":
                    props.UnionWith(new[] { "font_size", "alignment", "font_style", "overflow", "line_spacing" });
                    break;

                case "image":
                case "raw_image":
                    props.UnionWith(new[] { "sprite_path", "image_type", "preserve_aspect", "fill_center" });
                    break;

                case "button":
                case "button_tmp":
                    props.UnionWith(new[] { "normal_color", "highlighted_color", "pressed_color", "disabled_color", "fade_duration" });
                    break;

                case "toggle":
                    props.UnionWith(new[] { "normal_color", "highlighted_color", "pressed_color", "disabled_color", "fade_duration" });
                    break;

                case "slider":
                    props.UnionWith(new[] { "normal_color", "highlighted_color", "pressed_color", "disabled_color", "fade_duration" });
                    break;

                case "dropdown":
                case "dropdown_tmp":
                    props.UnionWith(new[] { "normal_color", "highlighted_color", "pressed_color", "disabled_color", "fade_duration" });
                    break;

                case "input_field":
                case "input_field_tmp":
                    props.UnionWith(new[] { "font_size", "alignment", "font_style" });
                    break;

                case "scrollview":
                    props.UnionWith(new[] { "horizontal", "vertical", "movement_type", "elasticity", "inertia" });
                    break;

                case "grid_layout":
                    props.UnionWith(new[] { "padding", "spacing", "child_alignment", "child_force_expand_width", "child_force_expand_height", "cell_size", "constraint", "constraint_count" });
                    break;

                case "horizontal_layout":
                case "vertical_layout":
                    props.UnionWith(new[] { "padding", "spacing", "child_alignment", "child_force_expand_width", "child_force_expand_height" });
                    break;
            }

            return props;
        }

        // ─────────────────────────────────────────────
        //  ApplyAnchorPreset
        // ─────────────────────────────────────────────

        /// <summary>
        /// Applies a named anchor preset to a RectTransform.
        /// When keepPosition is true, the anchoredPosition and sizeDelta are adjusted
        /// so the element stays visually in the same place.
        /// </summary>
        public static bool ApplyAnchorPreset(RectTransform rt, string preset, bool keepPosition = false)
        {
            if (rt == null || !AnchorPresets.TryGetValue(preset, out var p))
                return false;

            if (keepPosition)
            {
                // Record old world corners before changing anchors
                var oldAnchorMin = rt.anchorMin;
                var oldAnchorMax = rt.anchorMax;
                var oldPivot = rt.pivot;
                var oldAnchoredPos = rt.anchoredPosition;
                var oldSizeDelta = rt.sizeDelta;

                // Get parent rect size
                var parentRT = rt.parent as RectTransform;
                Vector2 parentSize = parentRT != null ? parentRT.rect.size : new Vector2(Screen.width, Screen.height);

                // Calculate current world-space offset from parent
                float oldOffsetMinX = oldAnchorMin.x * parentSize.x + oldAnchoredPos.x - oldPivot.x * oldSizeDelta.x;
                float oldOffsetMinY = oldAnchorMin.y * parentSize.y + oldAnchoredPos.y - oldPivot.y * oldSizeDelta.y;
                float oldOffsetMaxX = oldAnchorMax.x * parentSize.x + oldAnchoredPos.x + (1 - oldPivot.x) * oldSizeDelta.x;
                float oldOffsetMaxY = oldAnchorMax.y * parentSize.y + oldAnchoredPos.y + (1 - oldPivot.y) * oldSizeDelta.y;

                // Apply new anchors and pivot
                rt.anchorMin = p.AnchorMin;
                rt.anchorMax = p.AnchorMax;
                rt.pivot = p.Pivot;

                // Calculate new offsets to maintain same visual position
                float newAnchorMinPx = p.AnchorMin.x * parentSize.x;
                float newAnchorMinPy = p.AnchorMin.y * parentSize.y;
                float newAnchorMaxPx = p.AnchorMax.x * parentSize.x;
                float newAnchorMaxPy = p.AnchorMax.y * parentSize.y;

                rt.offsetMin = new Vector2(oldOffsetMinX - newAnchorMinPx, oldOffsetMinY - newAnchorMinPy);
                rt.offsetMax = new Vector2(oldOffsetMaxX - newAnchorMaxPx, oldOffsetMaxY - newAnchorMaxPy);
            }
            else
            {
                rt.anchorMin = p.AnchorMin;
                rt.anchorMax = p.AnchorMax;
                rt.pivot = p.Pivot;
                rt.anchoredPosition = Vector2.zero;

                // For stretch presets, zero out offset so the element fills the anchor area
                bool stretchX = !Mathf.Approximately(p.AnchorMin.x, p.AnchorMax.x);
                bool stretchY = !Mathf.Approximately(p.AnchorMin.y, p.AnchorMax.y);
                if (stretchX || stretchY)
                {
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
            }

            return true;
        }

        // ─────────────────────────────────────────────
        //  ApplyStyle
        // ─────────────────────────────────────────────

        /// <summary>
        /// Applies a dictionary of style properties to the given GameObject based on its element type.
        /// Returns a list of warnings for any unrecognized or unsupported properties.
        /// </summary>
        public static List<string> ApplyStyle(GameObject go, string elementType, Dictionary<string, object> style)
        {
            var warnings = new List<string>();
            if (go == null || style == null) return warnings;

            var supported = GetStyleProperties(elementType);

            foreach (var kvp in style)
            {
                if (!supported.Contains(kvp.Key))
                {
                    warnings.Add($"Property '{kvp.Key}' is not supported for element type '{elementType}'");
                    continue;
                }

                try
                {
                    ApplyStyleProperty(go, elementType, kvp.Key, kvp.Value);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to apply '{kvp.Key}': {ex.Message}");
                }
            }

            return warnings;
        }

        private static void ApplyStyleProperty(GameObject go, string elementType, string property, object value)
        {
            switch (property)
            {
                // ── Common ──
                case "color":
                    var color = ParseColor(value);
                    var graphic = go.GetComponent<Graphic>();
                    if (graphic != null) graphic.color = color;
                    break;

                case "raycast_target":
                    var rt = go.GetComponent<Graphic>();
                    if (rt != null) rt.raycastTarget = Convert.ToBoolean(value);
                    break;

                // ── Text / TMP text ──
                case "font_size":
                    ApplyTextProperty(go, elementType, (text) => text.fontSize = Convert.ToInt32(value)
#if UNITY_MCP_TMP
                        , tmpAction: (tmp) => tmp.fontSize = Convert.ToSingle(value)
#endif
                    );
                    break;

                case "alignment":
                    ApplyTextAlignment(go, elementType, Convert.ToString(value));
                    break;

                case "font_style":
                    ApplyFontStyle(go, elementType, Convert.ToString(value));
                    break;

                case "overflow":
                    ApplyOverflow(go, elementType, Convert.ToString(value));
                    break;

                case "line_spacing":
                    ApplyTextProperty(go, elementType, (text) => text.lineSpacing = Convert.ToSingle(value)
#if UNITY_MCP_TMP
                        , tmpAction: (tmp) => tmp.lineSpacing = Convert.ToSingle(value)
#endif
                    );
                    break;

                // ── Image ──
                case "sprite_path":
                    var img = go.GetComponent<Image>();
                    if (img != null)
                    {
                        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Convert.ToString(value));
                        if (sprite != null) img.sprite = sprite;
                    }
                    break;

                case "image_type":
                    var imgType = go.GetComponent<Image>();
                    if (imgType != null && Enum.TryParse<Image.Type>(Convert.ToString(value), true, out var parsedType))
                        imgType.type = parsedType;
                    break;

                case "preserve_aspect":
                    var imgAspect = go.GetComponent<Image>();
                    if (imgAspect != null) imgAspect.preserveAspect = Convert.ToBoolean(value);
                    break;

                case "fill_center":
                    var imgFill = go.GetComponent<Image>();
                    if (imgFill != null) imgFill.fillCenter = Convert.ToBoolean(value);
                    break;

                // ── Selectable colors (Button, Toggle, Slider, Dropdown) ──
                case "normal_color":
                case "highlighted_color":
                case "pressed_color":
                case "disabled_color":
                case "fade_duration":
                    ApplySelectableColor(go, property, value);
                    break;

                // ── Layout groups ──
                case "padding":
                    ApplyPadding(go, value);
                    break;

                case "spacing":
                    ApplySpacing(go, elementType, value);
                    break;

                case "child_alignment":
                    var lg = go.GetComponent<HorizontalOrVerticalLayoutGroup>()
                             ?? (LayoutGroup)go.GetComponent<GridLayoutGroup>();
                    if (lg != null && Enum.TryParse<TextAnchor>(Convert.ToString(value), true, out var anchor))
                        lg.childAlignment = anchor;
                    break;

                case "child_force_expand_width":
                    var hlgW = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
                    if (hlgW != null) hlgW.childForceExpandWidth = Convert.ToBoolean(value);
                    break;

                case "child_force_expand_height":
                    var hlgH = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
                    if (hlgH != null) hlgH.childForceExpandHeight = Convert.ToBoolean(value);
                    break;

                case "cell_size":
                    var glgCell = go.GetComponent<GridLayoutGroup>();
                    if (glgCell != null) glgCell.cellSize = ParseVector2(value);
                    break;

                case "constraint":
                    var glgCon = go.GetComponent<GridLayoutGroup>();
                    if (glgCon != null && Enum.TryParse<GridLayoutGroup.Constraint>(Convert.ToString(value), true, out var constraint))
                        glgCon.constraint = constraint;
                    break;

                case "constraint_count":
                    var glgCount = go.GetComponent<GridLayoutGroup>();
                    if (glgCount != null) glgCount.constraintCount = Convert.ToInt32(value);
                    break;

                // ── ScrollRect ──
                case "horizontal":
                    var srH = go.GetComponent<ScrollRect>();
                    if (srH != null) srH.horizontal = Convert.ToBoolean(value);
                    break;

                case "vertical":
                    var srV = go.GetComponent<ScrollRect>();
                    if (srV != null) srV.vertical = Convert.ToBoolean(value);
                    break;

                case "movement_type":
                    var srM = go.GetComponent<ScrollRect>();
                    if (srM != null && Enum.TryParse<ScrollRect.MovementType>(Convert.ToString(value), true, out var mt))
                        srM.movementType = mt;
                    break;

                case "elasticity":
                    var srE = go.GetComponent<ScrollRect>();
                    if (srE != null) srE.elasticity = Convert.ToSingle(value);
                    break;

                case "inertia":
                    var srI = go.GetComponent<ScrollRect>();
                    if (srI != null) srI.inertia = Convert.ToBoolean(value);
                    break;
            }
        }

        // ─────────────────────────────────────────────
        //  CreateElementHierarchy
        // ─────────────────────────────────────────────

        /// <summary>
        /// Creates a UI element with the appropriate component and sub-hierarchy for the given element type.
        /// All created GameObjects are registered with Undo.
        /// Returns the root GameObject of the created element.
        /// </summary>
        public static GameObject CreateElementHierarchy(string elementType, string name, Transform parent)
        {
            GameObject go;

            switch (elementType)
            {
                case "panel":
                    go = CreateUIGameObject(name, parent);
                    go.AddComponent<Image>();
                    break;

                case "text":
                    go = CreateUIGameObject(name, parent);
                    var text = go.AddComponent<Text>();
                    text.text = name;
                    text.color = Color.black;
                    text.fontSize = 14;
                    break;

                case "text_tmp":
#if UNITY_MCP_TMP
                    go = CreateUIGameObject(name, parent);
                    var tmpText = go.AddComponent<TextMeshProUGUI>();
                    tmpText.text = name;
                    tmpText.color = Color.black;
                    tmpText.fontSize = 14;
#else
                    Debug.LogWarning("TextMeshPro not available. Falling back to legacy Text for 'text_tmp'.");
                    go = CreateUIGameObject(name, parent);
                    var fallbackText = go.AddComponent<Text>();
                    fallbackText.text = name;
                    fallbackText.color = Color.black;
                    fallbackText.fontSize = 14;
#endif
                    break;

                case "image":
                    go = CreateUIGameObject(name, parent);
                    go.AddComponent<Image>();
                    break;

                case "raw_image":
                    go = CreateUIGameObject(name, parent);
                    go.AddComponent<RawImage>();
                    break;

                case "button":
                    go = CreateButtonHierarchy(name, parent, useTMP: false);
                    break;

                case "button_tmp":
#if UNITY_MCP_TMP
                    go = CreateButtonHierarchy(name, parent, useTMP: true);
#else
                    Debug.LogWarning("TextMeshPro not available. Falling back to legacy Button for 'button_tmp'.");
                    go = CreateButtonHierarchy(name, parent, useTMP: false);
#endif
                    break;

                case "toggle":
                    go = CreateToggleHierarchy(name, parent);
                    break;

                case "slider":
                    go = CreateSliderHierarchy(name, parent);
                    break;

                case "dropdown":
                    go = CreateDropdownHierarchy(name, parent, useTMP: false);
                    break;

                case "dropdown_tmp":
#if UNITY_MCP_TMP
                    go = CreateDropdownHierarchy(name, parent, useTMP: true);
#else
                    Debug.LogWarning("TextMeshPro not available. Falling back to legacy Dropdown for 'dropdown_tmp'.");
                    go = CreateDropdownHierarchy(name, parent, useTMP: false);
#endif
                    break;

                case "scrollview":
                    go = CreateScrollViewHierarchy(name, parent);
                    break;

                case "input_field":
                    go = CreateInputFieldHierarchy(name, parent, useTMP: false);
                    break;

                case "input_field_tmp":
#if UNITY_MCP_TMP
                    go = CreateInputFieldHierarchy(name, parent, useTMP: true);
#else
                    Debug.LogWarning("TextMeshPro not available. Falling back to legacy InputField for 'input_field_tmp'.");
                    go = CreateInputFieldHierarchy(name, parent, useTMP: false);
#endif
                    break;

                case "grid_layout":
                    go = CreateUIGameObject(name, parent);
                    go.AddComponent<GridLayoutGroup>();
                    break;

                case "horizontal_layout":
                    go = CreateUIGameObject(name, parent);
                    go.AddComponent<HorizontalLayoutGroup>();
                    break;

                case "vertical_layout":
                    go = CreateUIGameObject(name, parent);
                    go.AddComponent<VerticalLayoutGroup>();
                    break;

                case "spacer":
                    go = CreateUIGameObject(name, parent);
                    go.AddComponent<LayoutElement>();
                    break;

                default:
                    return null;
            }

            Undo.RegisterCreatedObjectUndo(go, $"Create {elementType} '{name}'");
            return go;
        }

        // ─────────────────────────────────────────────
        //  Private Helpers — GameObject Creation
        // ─────────────────────────────────────────────

        private static GameObject CreateUIGameObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject CreateChildUIGameObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, $"Create UI child '{name}'");
            return go;
        }

        // ── Button ──

        private static GameObject CreateButtonHierarchy(string name, Transform parent, bool useTMP)
        {
            var go = CreateUIGameObject(name, parent);
            go.AddComponent<Image>();
            go.AddComponent<Button>();

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160, 30);

            // Text child
            var textGO = CreateChildUIGameObject("Text", go.transform);
            SetStretchFull(textGO.GetComponent<RectTransform>());

#if UNITY_MCP_TMP
            if (useTMP)
            {
                var tmp = textGO.AddComponent<TextMeshProUGUI>();
                tmp.text = name;
                tmp.color = Color.black;
                tmp.fontSize = 14;
                tmp.alignment = TextAlignmentOptions.Center;
            }
            else
#endif
            {
                var txt = textGO.AddComponent<Text>();
                txt.text = name;
                txt.color = Color.black;
                txt.fontSize = 14;
                txt.alignment = TextAnchor.MiddleCenter;
            }

            return go;
        }

        // ── Toggle ──

        private static GameObject CreateToggleHierarchy(string name, Transform parent)
        {
            var go = CreateUIGameObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160, 20);

            // Background
            var bgGO = CreateChildUIGameObject("Background", go.transform);
            var bgImage = bgGO.AddComponent<Image>();
            bgImage.color = Color.white;
            var bgRT = bgGO.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.5f);
            bgRT.anchorMax = new Vector2(0, 0.5f);
            bgRT.pivot = new Vector2(0, 0.5f);
            bgRT.sizeDelta = new Vector2(20, 20);
            bgRT.anchoredPosition = Vector2.zero;

            // Checkmark (child of Background)
            var checkGO = CreateChildUIGameObject("Checkmark", bgGO.transform);
            var checkImage = checkGO.AddComponent<Image>();
            checkImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            SetStretchFull(checkGO.GetComponent<RectTransform>());

            // Label
            var labelGO = CreateChildUIGameObject("Label", go.transform);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = name;
            labelText.color = Color.black;
            labelText.fontSize = 14;
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(23, 0);
            labelRT.offsetMax = Vector2.zero;

            // Toggle component
            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;
            toggle.isOn = true;

            return go;
        }

        // ── Slider ──

        private static GameObject CreateSliderHierarchy(string name, Transform parent)
        {
            var go = CreateUIGameObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160, 20);

            // Background
            var bgGO = CreateChildUIGameObject("Background", go.transform);
            var bgImage = bgGO.AddComponent<Image>();
            bgImage.color = new Color(0.78f, 0.78f, 0.78f, 1f);
            SetStretchFull(bgGO.GetComponent<RectTransform>());

            // Fill Area
            var fillAreaGO = CreateChildUIGameObject("Fill Area", go.transform);
            var fillAreaRT = fillAreaGO.GetComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0, 0.25f);
            fillAreaRT.anchorMax = new Vector2(1, 0.75f);
            fillAreaRT.offsetMin = new Vector2(5, 0);
            fillAreaRT.offsetMax = new Vector2(-15, 0);

            // Fill (child of Fill Area)
            var fillGO = CreateChildUIGameObject("Fill", fillAreaGO.transform);
            var fillImage = fillGO.AddComponent<Image>();
            fillImage.color = new Color(0.39f, 0.71f, 0.96f, 1f);
            var fillRT = fillGO.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = new Vector2(0, 1);
            fillRT.sizeDelta = new Vector2(10, 0);

            // Handle Slide Area
            var handleAreaGO = CreateChildUIGameObject("Handle Slide Area", go.transform);
            var handleAreaRT = handleAreaGO.GetComponent<RectTransform>();
            handleAreaRT.anchorMin = Vector2.zero;
            handleAreaRT.anchorMax = Vector2.one;
            handleAreaRT.offsetMin = new Vector2(10, 0);
            handleAreaRT.offsetMax = new Vector2(-10, 0);

            // Handle (child of Handle Slide Area)
            var handleGO = CreateChildUIGameObject("Handle", handleAreaGO.transform);
            var handleImage = handleGO.AddComponent<Image>();
            handleImage.color = Color.white;
            var handleRT = handleGO.GetComponent<RectTransform>();
            handleRT.anchorMin = Vector2.zero;
            handleRT.anchorMax = new Vector2(0, 1);
            handleRT.sizeDelta = new Vector2(20, 0);

            // Slider component
            var slider = go.AddComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = fillRT;
            slider.handleRect = handleRT;

            return go;
        }

        // ── Dropdown ──

        private static GameObject CreateDropdownHierarchy(string name, Transform parent, bool useTMP)
        {
            var go = CreateUIGameObject(name, parent);
            go.AddComponent<Image>();
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160, 30);

            // Label child
            var labelGO = CreateChildUIGameObject("Label", go.transform);
            var labelRT = labelGO.GetComponent<RectTransform>();
            SetStretchFull(labelRT);
            labelRT.offsetMin = new Vector2(10, 0);
            labelRT.offsetMax = new Vector2(-25, 0);

            // Arrow child
            var arrowGO = CreateChildUIGameObject("Arrow", go.transform);
            var arrowImage = arrowGO.AddComponent<Image>();
            arrowImage.color = Color.black;
            var arrowRT = arrowGO.GetComponent<RectTransform>();
            arrowRT.anchorMin = new Vector2(1, 0.5f);
            arrowRT.anchorMax = new Vector2(1, 0.5f);
            arrowRT.pivot = new Vector2(1, 0.5f);
            arrowRT.sizeDelta = new Vector2(20, 20);
            arrowRT.anchoredPosition = new Vector2(-5, 0);

#if UNITY_MCP_TMP
            if (useTMP)
            {
                var label = labelGO.AddComponent<TextMeshProUGUI>();
                label.text = name;
                label.color = Color.black;
                label.fontSize = 14;
                label.alignment = TextAlignmentOptions.Left;

                var dropdown = go.AddComponent<TMP_Dropdown>();
                dropdown.targetGraphic = go.GetComponent<Image>();
                dropdown.captionText = label;
            }
            else
#endif
            {
                var label = labelGO.AddComponent<Text>();
                label.text = name;
                label.color = Color.black;
                label.fontSize = 14;
                label.alignment = TextAnchor.MiddleLeft;

                var dropdown = go.AddComponent<Dropdown>();
                dropdown.targetGraphic = go.GetComponent<Image>();
                dropdown.captionText = label;
            }

            return go;
        }

        // ── ScrollView ──

        private static GameObject CreateScrollViewHierarchy(string name, Transform parent)
        {
            var go = CreateUIGameObject(name, parent);
            var bgImage = go.AddComponent<Image>();
            bgImage.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            var scrollRT = go.GetComponent<RectTransform>();
            scrollRT.sizeDelta = new Vector2(200, 200);

            // Viewport
            var viewportGO = CreateChildUIGameObject("Viewport", go.transform);
            var viewportImage = viewportGO.AddComponent<Image>();
            viewportImage.color = Color.white;
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;
            var viewportRT = viewportGO.GetComponent<RectTransform>();
            SetStretchFull(viewportRT);

            // Content (child of Viewport)
            var contentGO = CreateChildUIGameObject("Content", viewportGO.transform);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0, 1);
            contentRT.sizeDelta = new Vector2(0, 300);
            contentRT.anchoredPosition = Vector2.zero;

            // ScrollRect component
            var scrollRect = go.AddComponent<ScrollRect>();
            scrollRect.viewport = viewportRT;
            scrollRect.content = contentRT;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            return go;
        }

        // ── InputField ──

        private static GameObject CreateInputFieldHierarchy(string name, Transform parent, bool useTMP)
        {
            var go = CreateUIGameObject(name, parent);
            go.AddComponent<Image>();
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160, 30);

#if UNITY_MCP_TMP
            if (useTMP)
            {
                // Text Area
                var textAreaGO = CreateChildUIGameObject("Text Area", go.transform);
                var textAreaRT = textAreaGO.GetComponent<RectTransform>();
                SetStretchFull(textAreaRT);
                textAreaRT.offsetMin = new Vector2(10, 6);
                textAreaRT.offsetMax = new Vector2(-10, -7);
                textAreaGO.AddComponent<RectMask2D>();

                // Placeholder child (inside Text Area)
                var placeholderGO = CreateChildUIGameObject("Placeholder", textAreaGO.transform);
                SetStretchFull(placeholderGO.GetComponent<RectTransform>());
                var placeholderTMP = placeholderGO.AddComponent<TextMeshProUGUI>();
                placeholderTMP.text = "Enter text...";
                placeholderTMP.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);
                placeholderTMP.fontSize = 14;
                placeholderTMP.fontStyle = FontStyles.Italic;

                // Text child (inside Text Area)
                var textGO = CreateChildUIGameObject("Text", textAreaGO.transform);
                SetStretchFull(textGO.GetComponent<RectTransform>());
                var textTMP = textGO.AddComponent<TextMeshProUGUI>();
                textTMP.text = "";
                textTMP.color = Color.black;
                textTMP.fontSize = 14;

                var inputField = go.AddComponent<TMP_InputField>();
                inputField.targetGraphic = go.GetComponent<Image>();
                inputField.textComponent = textTMP;
                inputField.placeholder = placeholderTMP;
                inputField.textViewport = textAreaRT;
            }
            else
#endif
            {
                // Placeholder child
                var placeholderGO = CreateChildUIGameObject("Placeholder", go.transform);
                var placeholderRT = placeholderGO.GetComponent<RectTransform>();
                SetStretchFull(placeholderRT);
                placeholderRT.offsetMin = new Vector2(10, 6);
                placeholderRT.offsetMax = new Vector2(-10, -7);
                var placeholderText = placeholderGO.AddComponent<Text>();
                placeholderText.text = "Enter text...";
                placeholderText.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);
                placeholderText.fontStyle = FontStyle.Italic;
                placeholderText.fontSize = 14;

                // Text child
                var textGO = CreateChildUIGameObject("Text", go.transform);
                var textRT = textGO.GetComponent<RectTransform>();
                SetStretchFull(textRT);
                textRT.offsetMin = new Vector2(10, 6);
                textRT.offsetMax = new Vector2(-10, -7);
                var textComp = textGO.AddComponent<Text>();
                textComp.text = "";
                textComp.color = Color.black;
                textComp.fontSize = 14;
                textComp.supportRichText = false;

                var inputField = go.AddComponent<InputField>();
                inputField.targetGraphic = go.GetComponent<Image>();
                inputField.textComponent = textComp;
                inputField.placeholder = placeholderText;
            }

            return go;
        }

        // ─────────────────────────────────────────────
        //  Private Helpers — Style Application
        // ─────────────────────────────────────────────

        private delegate void TextAction(Text text);
#if UNITY_MCP_TMP
        private delegate void TMPAction(TextMeshProUGUI tmp);
#endif

        /// <summary>
        /// Applies an action to the Text or TMP component on the given GO (or its children for composite elements).
        /// </summary>
        private static void ApplyTextProperty(GameObject go, string elementType,
            TextAction textAction
#if UNITY_MCP_TMP
            , TMPAction tmpAction = null
#endif
        )
        {
#if UNITY_MCP_TMP
            var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null && tmpAction != null) { tmpAction(tmp); return; }
#endif
            var text = go.GetComponent<Text>() ?? go.GetComponentInChildren<Text>();
            if (text != null) textAction(text);
        }

        private static void ApplyTextAlignment(GameObject go, string elementType, string alignment)
        {
#if UNITY_MCP_TMP
            var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                if (Enum.TryParse<TextAlignmentOptions>(alignment, true, out var tmpAlign))
                    tmp.alignment = tmpAlign;
                return;
            }
#endif
            var text = go.GetComponent<Text>() ?? go.GetComponentInChildren<Text>();
            if (text != null && Enum.TryParse<TextAnchor>(alignment, true, out var anchor))
                text.alignment = anchor;
        }

        private static void ApplyFontStyle(GameObject go, string elementType, string style)
        {
#if UNITY_MCP_TMP
            var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                if (Enum.TryParse<FontStyles>(style, true, out var tmpStyle))
                    tmp.fontStyle = tmpStyle;
                return;
            }
#endif
            var text = go.GetComponent<Text>() ?? go.GetComponentInChildren<Text>();
            if (text != null && Enum.TryParse<FontStyle>(style, true, out var fontStyle))
                text.fontStyle = fontStyle;
        }

        private static void ApplyOverflow(GameObject go, string elementType, string overflow)
        {
#if UNITY_MCP_TMP
            var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                if (Enum.TryParse<TextOverflowModes>(overflow, true, out var tmpOverflow))
                    tmp.overflowMode = tmpOverflow;
                return;
            }
#endif
            var text = go.GetComponent<Text>() ?? go.GetComponentInChildren<Text>();
            if (text != null)
            {
                if (Enum.TryParse<HorizontalWrapMode>(overflow, true, out var wrapMode))
                    text.horizontalOverflow = wrapMode;
            }
        }

        private static void ApplySelectableColor(GameObject go, string property, object value)
        {
            var selectable = go.GetComponent<Selectable>();
            if (selectable == null) return;

            var colors = selectable.colors;

            switch (property)
            {
                case "normal_color":
                    colors.normalColor = ParseColor(value);
                    break;
                case "highlighted_color":
                    colors.highlightedColor = ParseColor(value);
                    break;
                case "pressed_color":
                    colors.pressedColor = ParseColor(value);
                    break;
                case "disabled_color":
                    colors.disabledColor = ParseColor(value);
                    break;
                case "fade_duration":
                    colors.fadeDuration = Convert.ToSingle(value);
                    break;
            }

            selectable.colors = colors;
        }

        private static void ApplyPadding(GameObject go, object value)
        {
            var lg = go.GetComponent<HorizontalOrVerticalLayoutGroup>()
                     ?? (LayoutGroup)go.GetComponent<GridLayoutGroup>();
            if (lg == null) return;

            if (value is Dictionary<string, object> dict)
            {
                if (dict.ContainsKey("left"))   lg.padding.left   = Convert.ToInt32(dict["left"]);
                if (dict.ContainsKey("right"))  lg.padding.right  = Convert.ToInt32(dict["right"]);
                if (dict.ContainsKey("top"))    lg.padding.top    = Convert.ToInt32(dict["top"]);
                if (dict.ContainsKey("bottom")) lg.padding.bottom = Convert.ToInt32(dict["bottom"]);
            }
            else if (value is IList<object> arr)
            {
                // Array format: [left, right, top, bottom]
                int left   = arr.Count > 0 ? Convert.ToInt32(arr[0]) : 0;
                int right  = arr.Count > 1 ? Convert.ToInt32(arr[1]) : 0;
                int top    = arr.Count > 2 ? Convert.ToInt32(arr[2]) : 0;
                int bottom = arr.Count > 3 ? Convert.ToInt32(arr[3]) : 0;
                lg.padding = new RectOffset(left, right, top, bottom);
            }
            else
            {
                // Uniform padding from a single number
                int pad = Convert.ToInt32(value);
                lg.padding = new RectOffset(pad, pad, pad, pad);
            }
        }

        private static void ApplySpacing(GameObject go, string elementType, object value)
        {
            if (elementType == "grid_layout")
            {
                var glg = go.GetComponent<GridLayoutGroup>();
                if (glg != null) glg.spacing = ParseVector2(value);
            }
            else
            {
                var hlg = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
                if (hlg != null) hlg.spacing = Convert.ToSingle(value);
            }
        }

        // ─────────────────────────────────────────────
        //  Private Helpers — Parsing
        // ─────────────────────────────────────────────

        private static Color ParseColor(object value)
        {
            if (value is string str)
            {
                if (ColorUtility.TryParseHtmlString(str, out var color))
                    return color;
                // Try named color
                if (str.Equals("white", StringComparison.OrdinalIgnoreCase)) return Color.white;
                if (str.Equals("black", StringComparison.OrdinalIgnoreCase)) return Color.black;
                if (str.Equals("red", StringComparison.OrdinalIgnoreCase)) return Color.red;
                if (str.Equals("green", StringComparison.OrdinalIgnoreCase)) return Color.green;
                if (str.Equals("blue", StringComparison.OrdinalIgnoreCase)) return Color.blue;
                if (str.Equals("yellow", StringComparison.OrdinalIgnoreCase)) return Color.yellow;
                if (str.Equals("cyan", StringComparison.OrdinalIgnoreCase)) return Color.cyan;
                if (str.Equals("magenta", StringComparison.OrdinalIgnoreCase)) return Color.magenta;
                if (str.Equals("gray", StringComparison.OrdinalIgnoreCase) || str.Equals("grey", StringComparison.OrdinalIgnoreCase)) return Color.gray;
                if (str.Equals("clear", StringComparison.OrdinalIgnoreCase)) return Color.clear;
            }

            if (value is Dictionary<string, object> dict)
            {
                float r = dict.ContainsKey("r") ? Convert.ToSingle(dict["r"]) : 0;
                float g = dict.ContainsKey("g") ? Convert.ToSingle(dict["g"]) : 0;
                float b = dict.ContainsKey("b") ? Convert.ToSingle(dict["b"]) : 0;
                float a = dict.ContainsKey("a") ? Convert.ToSingle(dict["a"]) : 1;
                return new Color(r, g, b, a);
            }

            // Handle [r, g, b] or [r, g, b, a] arrays (arrive as List<object> from JSON deserialization)
            if (value is List<object> list && list.Count >= 3)
            {
                float r = Convert.ToSingle(list[0]);
                float g = Convert.ToSingle(list[1]);
                float b = Convert.ToSingle(list[2]);
                float a = list.Count >= 4 ? Convert.ToSingle(list[3]) : 1f;
                return new Color(r, g, b, a);
            }

            Debug.LogWarning($"UISchema: Unrecognized color value '{value}', defaulting to white.");
            return Color.white;
        }

        private static Vector2 ParseVector2(object value)
        {
            if (value is Dictionary<string, object> dict)
            {
                float x = dict.ContainsKey("x") ? Convert.ToSingle(dict["x"]) : 0;
                float y = dict.ContainsKey("y") ? Convert.ToSingle(dict["y"]) : 0;
                return new Vector2(x, y);
            }

            try
            {
                float v = Convert.ToSingle(value);
                return new Vector2(v, v);
            }
            catch (Exception)
            {
                Debug.LogWarning($"UISchema: Cannot parse '{value}' as Vector2, defaulting to zero.");
                return Vector2.zero;
            }
        }

        private static void SetStretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
