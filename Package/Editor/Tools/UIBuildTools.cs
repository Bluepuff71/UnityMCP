using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
#if UNITY_MCP_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityMCP.Editor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Services;


#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Batch-builds complete Canvas UIs from JSON tree descriptions, applies templates,
    /// or configures anchors in bulk. Supports recursive tree processing with depth and
    /// element count limits to prevent timeouts.
    /// </summary>
    [MCPTool("build_ui", "Batch-builds complete Canvas UIs from JSON tree descriptions, applies templates, or configures anchors in bulk.", Category = "UI")]
    public static class UIBuildTools
    {
        private const int MaxDepth = 10;
        private const int MaxElements = 200;

        private static readonly string[] TemplateNames = {
            "inventory_grid", "dialog_box", "hud_bars",
            "settings_menu", "list_view", "tab_panel"
        };

        #region Action Methods

        /// <summary>
        /// Returns a comprehensive element format reference document covering all element types,
        /// anchor presets, tree node format, style properties, templates, and limits.
        /// </summary>
        [MCPAction("read_schema", Description = "Returns complete element format reference for building UI trees", ReadOnlyHint = true)]
        public static object ReadSchema()
        {
            try
            {
                return HandleReadSchema();
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error reading schema: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Batch-creates a complete UI hierarchy from a JSON tree description.
        /// </summary>
        [MCPAction("from_tree", Description = "Batch-create complete UI from a JSON tree description")]
        public static object FromTree(
            [MCPParam("tree", "JSON tree describing the UI hierarchy", required: true)] Dictionary<string, object> tree = null,
            [MCPParam("canvas_target", "Instance ID or name/path of target Canvas")] string canvasTarget = null,
            [MCPParam("clear_existing", "Clear existing children before building")] bool? clearExisting = null,
            [MCPParam("canvas_config", "Canvas configuration if creating new")] Dictionary<string, object> canvasConfig = null)
        {
            try
            {
                return HandleFromTree(tree, canvasTarget, clearExisting ?? false, canvasConfig);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error building UI tree: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Creates a UI from a named built-in template with optional customization overrides.
        /// </summary>
        [MCPAction("apply_template", Description = "Create UI from named template")]
        public static object ApplyTemplate(
            [MCPParam("template_name", "Name of the template to apply", required: true, Enum = new[] {
                "inventory_grid", "dialog_box", "hud_bars",
                "settings_menu", "list_view", "tab_panel"
            })] string templateName = null,
            [MCPParam("canvas_target", "Instance ID or name/path of target Canvas")] string canvasTarget = null,
            [MCPParam("customization", "JSON overrides to apply to the template tree")] Dictionary<string, object> customization = null)
        {
            try
            {
                return HandleApplyTemplate(templateName, canvasTarget, customization);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error applying template: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        /// <summary>
        /// Batch-configures anchor presets on multiple UI elements at once.
        /// </summary>
        [MCPAction("set_anchors", Description = "Batch anchor configuration")]
        public static object SetAnchors(
            [MCPParam("targets", "Array of instance IDs or name/paths", required: true)] IList<object> targets = null,
            [MCPParam("anchor_preset", "Anchor preset to apply", required: true, Enum = new[] {
                "top_left", "top_center", "top_right", "top_stretch",
                "middle_left", "middle_center", "middle_right", "middle_stretch",
                "bottom_left", "bottom_center", "bottom_right", "bottom_stretch",
                "stretch_left", "stretch_center", "stretch_right", "stretch_full"
            })] string anchorPreset = null)
        {
            try
            {
                return HandleSetAnchors(targets, anchorPreset);
            }
            catch (MCPException) { throw; }
            catch (Exception ex) { throw new MCPException($"Error setting anchors: {ex.Message}", ex, MCPErrorCodes.InternalError); }
        }

        #endregion

        #region Handler Methods

        // ─────────────────────────────────────────────
        //  read_schema Handler
        // ─────────────────────────────────────────────

        private static object HandleReadSchema()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("=== UI Build Tools Schema Reference ===");
            sb.AppendLine();

            // Element types
            sb.AppendLine("## Element Types");
            sb.AppendLine();
            var elementTypes = UISchema.GetAvailableElementTypes();
            var typeDescriptions = new Dictionary<string, string>
            {
                { "panel", "Empty panel with Image background (container for other elements)" },
                { "text", "Legacy Unity Text component" },
                { "text_tmp", "TextMeshPro text (preferred over legacy text)" },
                { "image", "Image component for sprites" },
                { "raw_image", "RawImage component for textures" },
                { "button", "Button with legacy Text child" },
                { "button_tmp", "Button with TextMeshPro text child" },
                { "toggle", "Toggle checkbox with label" },
                { "slider", "Slider with fill and handle" },
                { "dropdown", "Dropdown with legacy Text" },
                { "dropdown_tmp", "Dropdown with TextMeshPro text" },
                { "scrollview", "ScrollRect with viewport, content area, and scrollbars" },
                { "input_field", "Input field with legacy Text" },
                { "input_field_tmp", "Input field with TextMeshPro text" },
                { "grid_layout", "Panel with GridLayoutGroup for grid arrangements" },
                { "horizontal_layout", "Panel with HorizontalLayoutGroup" },
                { "vertical_layout", "Panel with VerticalLayoutGroup" },
                { "spacer", "Invisible LayoutElement spacer for layout spacing" },
            };
            foreach (var t in elementTypes)
            {
                string desc = typeDescriptions.ContainsKey(t) ? typeDescriptions[t] : t;
                sb.AppendLine($"  - {t}: {desc}");
            }
            sb.AppendLine();

            // Anchor presets
            sb.AppendLine("## Anchor Presets");
            sb.AppendLine();
            foreach (var kvp in UISchema.AnchorPresets)
            {
                var p = kvp.Value;
                sb.AppendLine($"  - {kvp.Key}: anchorMin=({p.AnchorMin.x},{p.AnchorMin.y}) anchorMax=({p.AnchorMax.x},{p.AnchorMax.y}) pivot=({p.Pivot.x},{p.Pivot.y})");
            }
            sb.AppendLine();

            // Tree node format
            sb.AppendLine("## Tree Node Format");
            sb.AppendLine();
            sb.AppendLine("Each node in the tree is a JSON object with these properties:");
            sb.AppendLine("  - name (string, optional): Name for the GameObject. Defaults to the element type.");
            sb.AppendLine("  - type (string, required): One of the element types listed above.");
            sb.AppendLine("  - anchor (string, optional): Anchor preset name to apply.");
            sb.AppendLine("  - size (array [w,h], optional): sizeDelta for the RectTransform.");
            sb.AppendLine("  - position (array [x,y], optional): anchoredPosition for the RectTransform.");
            sb.AppendLine("  - pivot (array [x,y], optional): Pivot point (0-1 range).");
            sb.AppendLine("  - margins (array [left,top,right,bottom], optional): Offset from anchored edges (sets offsetMin/offsetMax).");
            sb.AppendLine("  - text (string, optional): Text content for text-based elements (text, text_tmp, button, button_tmp, etc.).");
            sb.AppendLine("  - sprite_path (string, optional): Asset path to a sprite for image elements.");
            sb.AppendLine("  - interactable (bool, optional): Whether the element is interactable.");
            sb.AppendLine("  - style (object, optional): Style properties specific to the element type (see below).");
            sb.AppendLine("  - children (array, optional): Array of child tree nodes.");
            sb.AppendLine();

            // Style properties per element type
            sb.AppendLine("## Style Properties by Element Type");
            sb.AppendLine();
            foreach (var t in elementTypes)
            {
                var props = UISchema.GetStyleProperties(t);
                sb.AppendLine($"  {t}: {string.Join(", ", props)}");
            }
            sb.AppendLine();

            // Style property details
            sb.AppendLine("## Common Style Property Values");
            sb.AppendLine();
            sb.AppendLine("  - color: [r,g,b,a] array (0-1 range) or hex string \"#RRGGBB\" / \"#RRGGBBAA\"");
            sb.AppendLine("  - font_size: integer");
            sb.AppendLine("  - alignment: \"left\", \"center\", \"right\", \"justified\" (or Unity TextAnchor names)");
            sb.AppendLine("  - font_style: \"normal\", \"bold\", \"italic\", \"bold_italic\"");
            sb.AppendLine("  - cell_size: [w,h] array");
            sb.AppendLine("  - spacing: [x,y] array or single number");
            sb.AppendLine("  - constraint: \"flexible\", \"fixed_column_count\", \"fixed_row_count\"");
            sb.AppendLine("  - constraint_count: integer");
            sb.AppendLine("  - padding: [left,right,top,bottom] array or single number");
            sb.AppendLine("  - child_alignment: \"upper_left\", \"upper_center\", \"upper_right\", \"middle_left\", \"middle_center\", \"middle_right\", \"lower_left\", \"lower_center\", \"lower_right\"");
            sb.AppendLine("  - normal_color, highlighted_color, pressed_color, disabled_color: [r,g,b,a] or hex");
            sb.AppendLine("  - image_type: \"simple\", \"sliced\", \"tiled\", \"filled\"");
            sb.AppendLine("  - movement_type: \"unrestricted\", \"elastic\", \"clamped\"");
            sb.AppendLine();

            // Templates
            sb.AppendLine("## Built-in Templates (use with apply_template action)");
            sb.AppendLine();
            sb.AppendLine("  - inventory_grid: Grid layout with header panel and scrollable item grid");
            sb.AppendLine("  - dialog_box: Centered panel with title, message text, and OK/Cancel button row");
            sb.AppendLine("  - hud_bars: Health/Mana/Stamina bars with labels anchored to top-left");
            sb.AppendLine("  - settings_menu: Scrollable settings panel with toggle and slider pairs");
            sb.AppendLine("  - list_view: Scrollable list with header and vertical item layout");
            sb.AppendLine("  - tab_panel: Tab button bar with switchable content panels");
            sb.AppendLine();

            // Limits
            sb.AppendLine("## Limits");
            sb.AppendLine();
            sb.AppendLine($"  - Max tree depth: {MaxDepth}");
            sb.AppendLine($"  - Max elements per tree: {MaxElements}");
            sb.AppendLine();

            // Example
            sb.AppendLine("## Example Tree");
            sb.AppendLine();
            sb.AppendLine(@"{
  ""name"": ""MyPanel"",
  ""type"": ""panel"",
  ""anchor"": ""stretch_full"",
  ""style"": { ""color"": [0.1, 0.1, 0.15, 0.95] },
  ""children"": [
    {
      ""name"": ""Title"",
      ""type"": ""text_tmp"",
      ""anchor"": ""top_stretch"",
      ""size"": [0, 40],
      ""text"": ""My Title"",
      ""style"": { ""font_size"": 24, ""alignment"": ""center"" }
    }
  ]
}");

            return new
            {
                success = true,
                schema = sb.ToString()
            };
        }

        // ─────────────────────────────────────────────
        //  from_tree Handler
        // ─────────────────────────────────────────────

        private static object HandleFromTree(Dictionary<string, object> tree, string canvasTarget, bool clearExisting, Dictionary<string, object> canvasConfig)
        {
            if (tree == null)
            {
                throw MCPException.InvalidParams("'tree' parameter is required.");
            }

            if (!tree.ContainsKey("type"))
            {
                string debugKeys = string.Join(", ", tree.Keys);
                string debugTypes = string.Join(", ", tree.Keys.Select(k => $"{k}={tree[k]?.GetType().Name ?? "null"}"));
                throw MCPException.InvalidParams($"Root tree node must have a 'type' property. Found keys: [{debugKeys}]. Types: [{debugTypes}]");
            }

            // Set up undo group first so all operations are atomic
            Undo.SetCurrentGroupName("Build UI Tree");
            int undoGroup = Undo.GetCurrentGroup();

            // Resolve or create canvas
            Transform parentTransform = ResolveCanvasParent(canvasTarget, canvasConfig);

            // Clear existing children if requested
            if (clearExisting)
            {
                ClearChildren(parentTransform);
            }

            var manifest = new List<object>();
            var warnings = new List<string>();
            int elementCount = 0;

            try
            {
                ProcessNode(tree, parentTransform, manifest, warnings, 0, ref elementCount);
            }
            catch (Exception ex)
            {
                Undo.CollapseUndoOperations(undoGroup);
                throw new MCPException($"Error during tree processing: {ex.Message}", ex, MCPErrorCodes.InternalError);
            }

            Undo.CollapseUndoOperations(undoGroup);

            EditorSceneManager.MarkSceneDirty(GetActiveScene());

            return new
            {
                success = true,
                message = $"Built UI tree with {elementCount} element(s).",
                element_count = elementCount,
                manifest,
                warnings = warnings.Count > 0 ? warnings : null
            };
        }

        // ─────────────────────────────────────────────
        //  apply_template Handler
        // ─────────────────────────────────────────────

        private static object HandleApplyTemplate(string templateName, string canvasTarget, Dictionary<string, object> customization)
        {
            if (string.IsNullOrEmpty(templateName))
            {
                throw MCPException.InvalidParams("'template_name' parameter is required.");
            }

            Dictionary<string, object> templateTree = GetTemplate(templateName);
            if (templateTree == null)
            {
                string valid = string.Join(", ", TemplateNames);
                throw MCPException.InvalidParams($"Unknown template: '{templateName}'. Available templates: {valid}");
            }

            // Apply customizations
            if (customization != null && customization.Count > 0)
            {
                ApplyCustomization(templateTree, customization);
            }

            return HandleFromTree(templateTree, canvasTarget, false, null);
        }

        // ─────────────────────────────────────────────
        //  set_anchors Handler
        // ─────────────────────────────────────────────

        private static object HandleSetAnchors(IList<object> targets, string anchorPreset)
        {
            if (targets == null || targets.Count == 0)
            {
                throw MCPException.InvalidParams("'targets' parameter is required and must not be empty.");
            }

            if (string.IsNullOrEmpty(anchorPreset))
            {
                throw MCPException.InvalidParams("'anchor_preset' parameter is required.");
            }

            if (!UISchema.AnchorPresets.ContainsKey(anchorPreset))
            {
                string valid = string.Join(", ", UISchema.GetAvailableAnchorPresets());
                throw MCPException.InvalidParams($"Invalid anchor_preset: '{anchorPreset}'. Valid presets: {valid}");
            }

            Undo.SetCurrentGroupName("Set Anchors Batch");
            int undoGroup = Undo.GetCurrentGroup();

            var results = new List<object>();
            int successCount = 0;
            int failCount = 0;

            foreach (var target in targets)
            {
                string targetStr = target?.ToString();
                if (string.IsNullOrEmpty(targetStr))
                {
                    results.Add(new { target = targetStr, success = false, error = "Empty target" });
                    failCount++;
                    continue;
                }

                GameObject go = FindGameObject(targetStr);
                if (go == null)
                {
                    results.Add(new { target = targetStr, success = false, error = "Not found" });
                    failCount++;
                    continue;
                }

                RectTransform rt = go.GetComponent<RectTransform>();
                if (rt == null)
                {
                    results.Add(new { target = targetStr, success = false, error = "No RectTransform" });
                    failCount++;
                    continue;
                }

                Undo.RecordObject(rt, "Set Anchor Preset");
                bool applied = UISchema.ApplyAnchorPreset(rt, anchorPreset, false);

                if (applied)
                {
                    EditorUtility.SetDirty(go);
                    results.Add(new { target = targetStr, success = true, instance_id = go.GetInstanceID(), name = go.name });
                    successCount++;
                }
                else
                {
                    results.Add(new { target = targetStr, success = false, error = "Failed to apply preset" });
                    failCount++;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            return new
            {
                success = failCount == 0,
                message = $"Set anchors on {successCount}/{targets.Count} element(s) to '{anchorPreset}'.",
                anchor_preset = anchorPreset,
                success_count = successCount,
                fail_count = failCount,
                results
            };
        }

        #endregion

        #region Tree Processing

        /// <summary>
        /// Recursively processes a tree node, creating GameObjects and applying properties.
        /// </summary>
        private static void ProcessNode(Dictionary<string, object> node, Transform parent, List<object> manifest, List<string> warnings, int depth, ref int elementCount)
        {
            if (depth > MaxDepth)
            {
                warnings.Add($"Max depth ({MaxDepth}) exceeded at depth {depth}. Skipping subtree.");
                return;
            }

            if (elementCount >= MaxElements)
            {
                warnings.Add($"Max element count ({MaxElements}) reached. Skipping remaining nodes.");
                return;
            }

            // Get type (required)
            string type = GetStringValue(node, "type");
            if (string.IsNullOrEmpty(type))
            {
                warnings.Add($"Node at depth {depth} missing 'type'. Skipping.");
                return;
            }

            if (!UISchema.ElementTypes.Contains(type))
            {
                warnings.Add($"Unknown element type '{type}' at depth {depth}. Skipping.");
                return;
            }

            // Get name
            string name = GetStringValue(node, "name") ?? type;

            // Create element
            GameObject go = UISchema.CreateElementHierarchy(type, name, parent);
            if (go == null)
            {
                warnings.Add($"Failed to create element '{name}' of type '{type}'.");
                return;
            }

            elementCount++;

            RectTransform rt = go.GetComponent<RectTransform>();

            // Apply anchor preset
            string anchor = GetStringValue(node, "anchor");
            if (!string.IsNullOrEmpty(anchor))
            {
                if (!UISchema.ApplyAnchorPreset(rt, anchor, false))
                {
                    warnings.Add($"Invalid anchor preset '{anchor}' on '{name}'.");
                }
            }

            // Apply size
            if (node.ContainsKey("size"))
            {
                Vector2? parsedSize = ParseVector2(node["size"]);
                if (parsedSize.HasValue)
                {
                    rt.sizeDelta = parsedSize.Value;
                }
            }

            // Apply position
            if (node.ContainsKey("position"))
            {
                Vector2? parsedPos = ParseVector2(node["position"]);
                if (parsedPos.HasValue)
                {
                    rt.anchoredPosition = parsedPos.Value;
                }
            }

            // Apply pivot
            if (node.ContainsKey("pivot"))
            {
                Vector2? parsedPivot = ParseVector2(node["pivot"]);
                if (parsedPivot.HasValue)
                {
                    rt.pivot = parsedPivot.Value;
                }
            }

            // Apply margins [left, top, right, bottom]
            if (node.ContainsKey("margins"))
            {
                ApplyMargins(rt, node["margins"]);
            }

            // Apply text content
            string text = GetStringValue(node, "text");
            if (!string.IsNullOrEmpty(text))
            {
                ApplyTextContent(go, type, text);
            }

            // Apply sprite_path
            string spritePath = GetStringValue(node, "sprite_path");
            if (!string.IsNullOrEmpty(spritePath))
            {
                ApplySprite(go, spritePath, warnings);
            }

            // Apply interactable
            if (node.ContainsKey("interactable"))
            {
                try
                {
                    bool interactable = Convert.ToBoolean(node["interactable"]);
                    var selectable = go.GetComponent<Selectable>();
                    if (selectable != null)
                    {
                        Undo.RecordObject(selectable, "Set Interactable");
                        selectable.interactable = interactable;
                    }
                }
                catch (Exception)
                {
                    warnings.Add($"Invalid 'interactable' value on '{name}', skipping.");
                }
            }

            // Apply style
            if (node.ContainsKey("style") && node["style"] is Dictionary<string, object> style)
            {
                var styleWarnings = UISchema.ApplyStyle(go, type, style);
                warnings.AddRange(styleWarnings);
            }

            EditorUtility.SetDirty(go);

            // Add to manifest
            manifest.Add(new
            {
                instance_id = go.GetInstanceID(),
                name = go.name,
                element_type = type,
                depth
            });

            // Recurse children
            if (node.ContainsKey("children") && node["children"] is IList<object> children)
            {
                foreach (var child in children)
                {
                    if (child is Dictionary<string, object> childNode)
                    {
                        ProcessNode(childNode, go.transform, manifest, warnings, depth + 1, ref elementCount);
                    }
                    else
                    {
                        warnings.Add($"Invalid child node under '{name}' (not a JSON object). Skipping.");
                    }
                }
            }
        }

        #endregion

        #region Templates

        private static Dictionary<string, object> GetTemplate(string templateName)
        {
            switch (templateName?.ToLowerInvariant())
            {
                case "inventory_grid": return TemplateInventoryGrid();
                case "dialog_box": return TemplateDialogBox();
                case "hud_bars": return TemplateHudBars();
                case "settings_menu": return TemplateSettingsMenu();
                case "list_view": return TemplateListView();
                case "tab_panel": return TemplateTabPanel();
                default: return null;
            }
        }

        private static Dictionary<string, object> TemplateInventoryGrid()
        {
            return new Dictionary<string, object>
            {
                { "name", "InventoryScreen" },
                { "type", "panel" },
                { "anchor", "stretch_full" },
                { "style", new Dictionary<string, object> { { "color", new object[] { 0.1f, 0.1f, 0.15f, 0.95f } } } },
                { "children", new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            { "name", "Header" },
                            { "type", "panel" },
                            { "anchor", "top_stretch" },
                            { "size", new object[] { 0, 60 } },
                            { "style", new Dictionary<string, object> { { "color", new object[] { 0.15f, 0.15f, 0.2f, 1f } } } },
                            { "children", new List<object>
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "name", "Title" },
                                        { "type", "text_tmp" },
                                        { "anchor", "middle_center" },
                                        { "text", "Inventory" },
                                        { "style", new Dictionary<string, object> { { "font_size", 28 }, { "alignment", "center" } } }
                                    }
                                }
                            }
                        },
                        new Dictionary<string, object>
                        {
                            { "name", "ItemScrollView" },
                            { "type", "scrollview" },
                            { "anchor", "stretch_full" },
                            { "margins", new object[] { 10, 70, 10, 10 } },
                            { "children", new List<object>
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "name", "ItemGrid" },
                                        { "type", "grid_layout" },
                                        { "anchor", "stretch_full" },
                                        { "style", new Dictionary<string, object>
                                            {
                                                { "cell_size", new object[] { 80, 80 } },
                                                { "spacing", new object[] { 5, 5 } },
                                                { "constraint", "fixed_column_count" },
                                                { "constraint_count", 5 }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static Dictionary<string, object> TemplateDialogBox()
        {
            return new Dictionary<string, object>
            {
                { "name", "DialogBox" },
                { "type", "panel" },
                { "anchor", "middle_center" },
                { "size", new object[] { 400, 250 } },
                { "style", new Dictionary<string, object> { { "color", new object[] { 0.2f, 0.2f, 0.25f, 0.98f } } } },
                { "children", new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            { "name", "Title" },
                            { "type", "text_tmp" },
                            { "anchor", "top_stretch" },
                            { "size", new object[] { 0, 40 } },
                            { "margins", new object[] { 20, 10, 20, 0 } },
                            { "text", "Dialog Title" },
                            { "style", new Dictionary<string, object> { { "font_size", 22 }, { "alignment", "center" } } }
                        },
                        new Dictionary<string, object>
                        {
                            { "name", "Message" },
                            { "type", "text_tmp" },
                            { "anchor", "stretch_full" },
                            { "margins", new object[] { 20, 60, 20, 60 } },
                            { "text", "Dialog message goes here." },
                            { "style", new Dictionary<string, object> { { "font_size", 16 }, { "alignment", "center" } } }
                        },
                        new Dictionary<string, object>
                        {
                            { "name", "ButtonRow" },
                            { "type", "horizontal_layout" },
                            { "anchor", "bottom_stretch" },
                            { "size", new object[] { 0, 50 } },
                            { "margins", new object[] { 20, 0, 20, 10 } },
                            { "style", new Dictionary<string, object> { { "spacing", 10 }, { "child_alignment", "middle_center" } } },
                            { "children", new List<object>
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "name", "OKButton" },
                                        { "type", "button_tmp" },
                                        { "size", new object[] { 120, 40 } },
                                        { "text", "OK" }
                                    },
                                    new Dictionary<string, object>
                                    {
                                        { "name", "CancelButton" },
                                        { "type", "button_tmp" },
                                        { "size", new object[] { 120, 40 } },
                                        { "text", "Cancel" }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static Dictionary<string, object> TemplateHudBars()
        {
            var bars = new List<object>();
            var barNames = new[] { "Health", "Mana", "Stamina" };
            var barColors = new[]
            {
                new object[] { 0.8f, 0.2f, 0.2f, 1f },
                new object[] { 0.2f, 0.4f, 0.9f, 1f },
                new object[] { 0.2f, 0.8f, 0.3f, 1f }
            };

            for (int i = 0; i < barNames.Length; i++)
            {
                bars.Add(new Dictionary<string, object>
                {
                    { "name", $"{barNames[i]}Bar" },
                    { "type", "horizontal_layout" },
                    { "size", new object[] { 250, 30 } },
                    { "style", new Dictionary<string, object> { { "spacing", 8 }, { "child_alignment", "middle_left" } } },
                    { "children", new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "name", $"{barNames[i]}Label" },
                                { "type", "text_tmp" },
                                { "size", new object[] { 70, 25 } },
                                { "text", barNames[i] },
                                { "style", new Dictionary<string, object> { { "font_size", 14 }, { "alignment", "left" } } }
                            },
                            new Dictionary<string, object>
                            {
                                { "name", $"{barNames[i]}Slider" },
                                { "type", "slider" },
                                { "size", new object[] { 170, 20 } },
                                { "interactable", false },
                                { "style", new Dictionary<string, object> { { "normal_color", barColors[i] } } }
                            }
                        }
                    }
                });
            }

            return new Dictionary<string, object>
            {
                { "name", "HUDBars" },
                { "type", "panel" },
                { "anchor", "top_left" },
                { "size", new object[] { 270, 120 } },
                { "position", new object[] { 10, -10 } },
                { "style", new Dictionary<string, object> { { "color", new object[] { 0f, 0f, 0f, 0.5f } }, { "raycast_target", false } } },
                { "children", new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            { "name", "BarLayout" },
                            { "type", "vertical_layout" },
                            { "anchor", "stretch_full" },
                            { "margins", new object[] { 10, 10, 10, 10 } },
                            { "style", new Dictionary<string, object> { { "spacing", 5 }, { "child_alignment", "upper_left" } } },
                            { "children", bars }
                        }
                    }
                }
            };
        }

        private static Dictionary<string, object> TemplateSettingsMenu()
        {
            var settingItems = new List<object>();

            // Toggle settings
            var toggleSettings = new[] { "Fullscreen", "VSync", "Show FPS" };
            foreach (var setting in toggleSettings)
            {
                settingItems.Add(new Dictionary<string, object>
                {
                    { "name", $"{setting.Replace(" ", "")}Row" },
                    { "type", "horizontal_layout" },
                    { "size", new object[] { 0, 40 } },
                    { "style", new Dictionary<string, object> { { "spacing", 10 }, { "child_alignment", "middle_left" } } },
                    { "children", new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "name", $"{setting.Replace(" ", "")}Label" },
                                { "type", "text_tmp" },
                                { "size", new object[] { 200, 30 } },
                                { "text", setting },
                                { "style", new Dictionary<string, object> { { "font_size", 16 }, { "alignment", "left" } } }
                            },
                            new Dictionary<string, object>
                            {
                                { "name", $"{setting.Replace(" ", "")}Toggle" },
                                { "type", "toggle" },
                                { "size", new object[] { 30, 30 } }
                            }
                        }
                    }
                });
            }

            // Slider settings
            var sliderSettings = new[] { "Volume", "Brightness", "Sensitivity" };
            foreach (var setting in sliderSettings)
            {
                settingItems.Add(new Dictionary<string, object>
                {
                    { "name", $"{setting}Row" },
                    { "type", "horizontal_layout" },
                    { "size", new object[] { 0, 40 } },
                    { "style", new Dictionary<string, object> { { "spacing", 10 }, { "child_alignment", "middle_left" } } },
                    { "children", new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "name", $"{setting}Label" },
                                { "type", "text_tmp" },
                                { "size", new object[] { 200, 30 } },
                                { "text", setting },
                                { "style", new Dictionary<string, object> { { "font_size", 16 }, { "alignment", "left" } } }
                            },
                            new Dictionary<string, object>
                            {
                                { "name", $"{setting}Slider" },
                                { "type", "slider" },
                                { "size", new object[] { 200, 20 } }
                            }
                        }
                    }
                });
            }

            return new Dictionary<string, object>
            {
                { "name", "SettingsMenu" },
                { "type", "panel" },
                { "anchor", "middle_center" },
                { "size", new object[] { 500, 400 } },
                { "style", new Dictionary<string, object> { { "color", new object[] { 0.15f, 0.15f, 0.2f, 0.98f } } } },
                { "children", new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            { "name", "Header" },
                            { "type", "panel" },
                            { "anchor", "top_stretch" },
                            { "size", new object[] { 0, 50 } },
                            { "style", new Dictionary<string, object> { { "color", new object[] { 0.2f, 0.2f, 0.25f, 1f } } } },
                            { "children", new List<object>
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "name", "Title" },
                                        { "type", "text_tmp" },
                                        { "anchor", "middle_center" },
                                        { "text", "Settings" },
                                        { "style", new Dictionary<string, object> { { "font_size", 24 }, { "alignment", "center" } } }
                                    }
                                }
                            }
                        },
                        new Dictionary<string, object>
                        {
                            { "name", "SettingsScrollView" },
                            { "type", "scrollview" },
                            { "anchor", "stretch_full" },
                            { "margins", new object[] { 10, 60, 10, 10 } },
                            { "children", new List<object>
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "name", "SettingsList" },
                                        { "type", "vertical_layout" },
                                        { "anchor", "stretch_full" },
                                        { "style", new Dictionary<string, object> { { "spacing", 5 }, { "padding", new object[] { 10, 10, 10, 10 } } } },
                                        { "children", settingItems }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static Dictionary<string, object> TemplateListView()
        {
            return new Dictionary<string, object>
            {
                { "name", "ListView" },
                { "type", "panel" },
                { "anchor", "stretch_full" },
                { "style", new Dictionary<string, object> { { "color", new object[] { 0.12f, 0.12f, 0.16f, 0.95f } } } },
                { "children", new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            { "name", "Header" },
                            { "type", "panel" },
                            { "anchor", "top_stretch" },
                            { "size", new object[] { 0, 50 } },
                            { "style", new Dictionary<string, object> { { "color", new object[] { 0.18f, 0.18f, 0.22f, 1f } } } },
                            { "children", new List<object>
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "name", "Title" },
                                        { "type", "text_tmp" },
                                        { "anchor", "middle_center" },
                                        { "text", "List View" },
                                        { "style", new Dictionary<string, object> { { "font_size", 22 }, { "alignment", "center" } } }
                                    }
                                }
                            }
                        },
                        new Dictionary<string, object>
                        {
                            { "name", "ListScrollView" },
                            { "type", "scrollview" },
                            { "anchor", "stretch_full" },
                            { "margins", new object[] { 5, 55, 5, 5 } },
                            { "children", new List<object>
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "name", "ItemLayout" },
                                        { "type", "vertical_layout" },
                                        { "anchor", "stretch_full" },
                                        { "style", new Dictionary<string, object> { { "spacing", 2 }, { "padding", new object[] { 5, 5, 5, 5 } } } }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static Dictionary<string, object> TemplateTabPanel()
        {
            var tabNames = new[] { "Tab1", "Tab2", "Tab3" };

            // Tab buttons
            var tabButtons = new List<object>();
            foreach (var tab in tabNames)
            {
                tabButtons.Add(new Dictionary<string, object>
                {
                    { "name", $"{tab}Button" },
                    { "type", "button_tmp" },
                    { "size", new object[] { 100, 35 } },
                    { "text", tab }
                });
            }

            // Content panels
            var contentPanels = new List<object>();
            for (int i = 0; i < tabNames.Length; i++)
            {
                contentPanels.Add(new Dictionary<string, object>
                {
                    { "name", $"{tabNames[i]}Content" },
                    { "type", "panel" },
                    { "anchor", "stretch_full" },
                    { "style", new Dictionary<string, object> { { "color", new object[] { 0.15f, 0.15f, 0.2f, 1f } } } },
                    { "children", new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "name", $"{tabNames[i]}Label" },
                                { "type", "text_tmp" },
                                { "anchor", "middle_center" },
                                { "text", $"{tabNames[i]} Content" },
                                { "style", new Dictionary<string, object> { { "font_size", 18 }, { "alignment", "center" } } }
                            }
                        }
                    }
                });
            }

            return new Dictionary<string, object>
            {
                { "name", "TabPanel" },
                { "type", "panel" },
                { "anchor", "stretch_full" },
                { "style", new Dictionary<string, object> { { "color", new object[] { 0.12f, 0.12f, 0.16f, 0.98f } } } },
                { "children", new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            { "name", "TabBar" },
                            { "type", "horizontal_layout" },
                            { "anchor", "top_stretch" },
                            { "size", new object[] { 0, 40 } },
                            { "style", new Dictionary<string, object>
                                {
                                    { "spacing", 2 },
                                    { "child_alignment", "middle_left" },
                                    { "color", new object[] { 0.18f, 0.18f, 0.22f, 1f } }
                                }
                            },
                            { "children", tabButtons }
                        },
                        new Dictionary<string, object>
                        {
                            { "name", "ContentArea" },
                            { "type", "panel" },
                            { "anchor", "stretch_full" },
                            { "margins", new object[] { 0, 45, 0, 0 } },
                            { "style", new Dictionary<string, object> { { "color", new object[] { 0.1f, 0.1f, 0.14f, 1f } } } },
                            { "children", contentPanels }
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Applies top-level customization overrides to a template tree.
        /// Supports overriding name, anchor, size, position, style, and text at the root level.
        /// </summary>
        private static void ApplyCustomization(Dictionary<string, object> tree, Dictionary<string, object> customization)
        {
            foreach (var kvp in customization)
            {
                switch (kvp.Key)
                {
                    case "name":
                    case "anchor":
                    case "text":
                    case "sprite_path":
                        tree[kvp.Key] = kvp.Value;
                        break;
                    case "size":
                    case "position":
                    case "pivot":
                    case "margins":
                        tree[kvp.Key] = kvp.Value;
                        break;
                    case "style":
                        if (kvp.Value is Dictionary<string, object> newStyle)
                        {
                            if (tree.ContainsKey("style") && tree["style"] is Dictionary<string, object> existingStyle)
                            {
                                foreach (var s in newStyle)
                                {
                                    existingStyle[s.Key] = s.Value;
                                }
                            }
                            else
                            {
                                tree["style"] = newStyle;
                            }
                        }
                        break;
                }
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Resolves or creates the Canvas parent transform for tree building.
        /// </summary>
        private static Transform ResolveCanvasParent(string canvasTarget, Dictionary<string, object> canvasConfig)
        {
            // If a target canvas is specified, find it
            if (!string.IsNullOrEmpty(canvasTarget))
            {
                GameObject canvasGO = FindGameObject(canvasTarget);
                if (canvasGO == null)
                {
                    throw MCPException.InvalidParams($"Canvas target '{canvasTarget}' not found.");
                }

                Canvas canvas = canvasGO.GetComponent<Canvas>();
                if (canvas == null)
                {
                    // Allow targeting non-Canvas GameObjects that are under a Canvas
                    Canvas parentCanvas = canvasGO.GetComponentInParent<Canvas>();
                    if (parentCanvas == null)
                    {
                        throw MCPException.InvalidParams($"Target '{canvasTarget}' is not a Canvas and is not under a Canvas.");
                    }
                }

                if (canvasGO.GetComponent<RectTransform>() == null)
                {
                    throw MCPException.InvalidParams($"Target '{canvasTarget}' does not have a RectTransform.");
                }

                return canvasGO.transform;
            }

            // Try to find an existing Canvas
            var existingCanvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (existingCanvas != null)
            {
                return existingCanvas.transform;
            }

            // Create a new Canvas
            return CreateDefaultCanvas(canvasConfig).transform;
        }

        /// <summary>
        /// Creates a default Canvas with optional configuration.
        /// </summary>
        private static GameObject CreateDefaultCanvas(Dictionary<string, object> config)
        {
            string name = "Canvas";
            string scalerMode = "scale_with_screen_size";
            float matchWidthOrHeight = 0.5f;
            Vector2 referenceResolution = new Vector2(1920, 1080);

            if (config != null)
            {
                if (config.ContainsKey("name")) name = config["name"] as string ?? name;
                if (config.ContainsKey("scaler_mode")) scalerMode = config["scaler_mode"] as string ?? scalerMode;
                if (config.ContainsKey("match_width_or_height")) matchWidthOrHeight = Convert.ToSingle(config["match_width_or_height"]);
                if (config.ContainsKey("reference_resolution"))
                {
                    Vector2? parsed = ParseVector2(config["reference_resolution"]);
                    if (parsed.HasValue) referenceResolution = parsed.Value;
                }
            }

            var canvasGO = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(canvasGO, $"Create Canvas '{name}'");

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            switch (scalerMode)
            {
                case "scale_with_screen_size":
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = referenceResolution;
                    scaler.matchWidthOrHeight = matchWidthOrHeight;
                    break;
                case "constant_pixel_size":
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    break;
                case "constant_physical_size":
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPhysicalSize;
                    break;
            }

            canvasGO.AddComponent<GraphicRaycaster>();

            // Create EventSystem if needed
            var existingEventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (existingEventSystem == null)
            {
                var eventSystemGO = new GameObject("EventSystem");
                eventSystemGO.AddComponent<EventSystem>();
#if UNITY_MCP_INPUT_SYSTEM
                eventSystemGO.AddComponent<InputSystemUIInputModule>();
#else
                eventSystemGO.AddComponent<StandaloneInputModule>();
#endif
                Undo.RegisterCreatedObjectUndo(eventSystemGO, "Create EventSystem");
            }

            return canvasGO;
        }

        /// <summary>
        /// Clears all children of a transform.
        /// </summary>
        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// Applies margins as [left, top, right, bottom] offsets to a RectTransform.
        /// Margins set offsetMin (left, bottom) and offsetMax (-right, -top).
        /// </summary>
        private static void ApplyMargins(RectTransform rt, object marginsValue)
        {
            if (marginsValue is IList<object> list && list.Count >= 4)
            {
                float left = Convert.ToSingle(list[0]);
                float top = Convert.ToSingle(list[1]);
                float right = Convert.ToSingle(list[2]);
                float bottom = Convert.ToSingle(list[3]);

                rt.offsetMin = new Vector2(left, bottom);
                rt.offsetMax = new Vector2(-right, -top);
            }
        }

        /// <summary>
        /// Applies text content to a UI element, checking for Text, TMP_Text, and child text components.
        /// </summary>
        private static void ApplyTextContent(GameObject go, string elementType, string text)
        {
            var textComp = go.GetComponent<Text>();
            if (textComp != null)
            {
                Undo.RecordObject(textComp, "Set Text Content");
                textComp.text = text;
                return;
            }

#if UNITY_MCP_TMP
            var tmpComp = go.GetComponent<TMPro.TMP_Text>();
            if (tmpComp != null)
            {
                Undo.RecordObject(tmpComp, "Set TMP Text Content");
                tmpComp.text = text;
                return;
            }
#endif

            // For composite elements (button, input_field, etc.), try children
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

        /// <summary>
        /// Applies a sprite to an Image or texture to a RawImage component.
        /// </summary>
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

            warnings.Add($"No Image or RawImage component found on '{go.name}' for sprite_path.");
        }

        private static string GetStringValue(Dictionary<string, object> dict, string key)
        {
            if (dict.ContainsKey(key))
            {
                return dict[key] as string;
            }
            return null;
        }

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
