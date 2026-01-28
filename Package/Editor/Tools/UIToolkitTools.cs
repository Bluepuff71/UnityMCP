using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tools for querying and inspecting UIToolkit VisualElements in EditorWindows.
    /// </summary>
    public static class UIToolkitTools
    {
        #region Element Reference Registry

        /// <summary>
        /// Maps ref_id strings to VisualElements for drill-down queries.
        /// Cleared and rebuilt with each query to ensure refs are valid.
        /// </summary>
        private static readonly Dictionary<string, VisualElement> s_refRegistry = new Dictionary<string, VisualElement>();
        private static int s_refCounter = 0;
        private static string s_lastWindowType = null;

        /// <summary>
        /// Clears the ref registry. Called at the start of each query.
        /// </summary>
        private static void ClearRefRegistry(string windowType)
        {
            // Only clear if querying a different window
            if (s_lastWindowType != windowType)
            {
                s_refRegistry.Clear();
                s_refCounter = 0;
                s_lastWindowType = windowType;
            }
        }

        /// <summary>
        /// Registers an element and returns its ref_id.
        /// </summary>
        private static string RegisterElement(VisualElement element)
        {
            string refId = $"e{s_refCounter++}";
            s_refRegistry[refId] = element;
            return refId;
        }

        /// <summary>
        /// Gets an element by its ref_id.
        /// </summary>
        private static VisualElement GetElementByRef(string refId)
        {
            return s_refRegistry.TryGetValue(refId, out var element) ? element : null;
        }

        #endregion

        #region Query Tool

        /// <summary>
        /// Queries VisualElements in an EditorWindow. Returns compact overview by default.
        /// Use ref_id to drill into specific elements, or text/selector to search.
        /// </summary>
        [MCPTool("uitoolkit_query", "Query VisualElements in an EditorWindow. Returns compact overview by default (depth 2). Use ref_id to drill into elements, or text/selector/name to search.", Category = "UIToolkit")]
        public static object Query(
            [MCPParam("window_type", "EditorWindow type name (e.g., 'SceneView', 'InspectorWindow')")] string windowType = null,
            [MCPParam("ref_id", "Element reference ID to focus on (from previous query). Returns that element's subtree.")] string refId = null,
            [MCPParam("text", "Search for elements containing this text")] string text = null,
            [MCPParam("selector", "USS selector (e.g., '#element-name', '.class-name', 'Button')")] string selector = null,
            [MCPParam("name", "Element name to search for")] string name = null,
            [MCPParam("class_name", "USS class name to filter by")] string className = null,
            [MCPParam("max_depth", "Maximum depth (default 2 for overview, 5 when using ref_id)")] int? maxDepth = null,
            [MCPParam("filter", "Filter: 'visible' (default) or 'all'")] string filter = "visible")
        {
            try
            {
                // If no window type specified, list available windows
                if (string.IsNullOrEmpty(windowType))
                {
                    return ListAvailableWindows();
                }

                // Find the EditorWindow
                var (window, errorResponse) = FindEditorWindow(windowType);
                if (window == null)
                {
                    return errorResponse;
                }

                VisualElement rootElement = window.rootVisualElement;
                if (rootElement == null)
                {
                    return new { error = $"EditorWindow '{windowType}' has no rootVisualElement." };
                }

                // Determine query mode and set smart defaults
                bool hasSearchParams = !string.IsNullOrEmpty(selector) ||
                                       !string.IsNullOrEmpty(name) ||
                                       !string.IsNullOrEmpty(className) ||
                                       !string.IsNullOrEmpty(text);
                bool hasDrillDown = !string.IsNullOrEmpty(refId);

                // Smart depth defaults: 2 for overview, 5 for drill-down, 3 for search
                int effectiveDepth = maxDepth ?? (hasDrillDown ? 5 : (hasSearchParams ? 3 : 2));

                // Parse filter mode
                FilterMode filterMode = ParseFilterMode(filter);
                Rect? viewportBounds = filterMode == FilterMode.Visible ? rootElement.worldBound : (Rect?)null;

                // Clear ref registry for new window queries (keep refs for drill-down)
                if (!hasDrillDown)
                {
                    ClearRefRegistry(windowType);
                }

                // Determine the target element (root or ref_id target)
                VisualElement targetElement = rootElement;
                if (hasDrillDown)
                {
                    targetElement = GetElementByRef(refId);
                    if (targetElement == null)
                    {
                        return new { error = $"Invalid ref_id '{refId}'. Refs expire when querying a different window." };
                    }
                }

                // MODE 1: Text search - find elements containing specific text
                if (!string.IsNullOrEmpty(text))
                {
                    var matches = new List<object>();
                    SearchByText(targetElement, text, matches, filterMode, viewportBounds, effectiveDepth);
                    return new
                    {
                        window = window.GetType().Name,
                        search = text,
                        count = matches.Count,
                        matches
                    };
                }

                // MODE 2: Selector/name/class search
                if (hasSearchParams)
                {
                    var matchingElements = new List<VisualElement>();

                    if (!string.IsNullOrEmpty(selector))
                    {
                        matchingElements.AddRange(QueryBySelector(targetElement, selector, effectiveDepth));
                    }
                    else
                    {
                        CollectMatchingElements(targetElement, name, className, matchingElements, 0, effectiveDepth);
                    }

                    // Filter by visibility if needed
                    if (filterMode == FilterMode.Visible)
                    {
                        matchingElements = matchingElements.Where(e => ShouldIncludeElement(e, filterMode, viewportBounds)).ToList();
                    }

                    var matches = matchingElements.Select(e => BuildCompactElementData(e)).ToList();
                    return new
                    {
                        window = window.GetType().Name,
                        query = selector ?? name ?? className,
                        count = matches.Count,
                        matches
                    };
                }

                // MODE 3: Overview/drill-down - return element tree
                var context = new TreeBuildContext(filterMode, viewportBounds);
                var tree = BuildCompactTree(targetElement, 0, effectiveDepth, context);

                var result = new Dictionary<string, object>
                {
                    { "window", window.GetType().Name },
                    { "root", tree },
                    { "refs", context.ElementCount }
                };

                if (hasDrillDown)
                {
                    result["focused"] = refId;
                }

                if (context.WasTruncated)
                {
                    result["truncated"] = true;
                }

                return result;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UIToolkitTools] Error querying elements: {exception.Message}");
                return new { error = exception.Message };
            }
        }

        /// <summary>
        /// Searches for elements containing specific text.
        /// </summary>
        private static void SearchByText(VisualElement element, string searchText, List<object> results,
            FilterMode filterMode, Rect? viewportBounds, int maxDepth, int currentDepth = 0)
        {
            if (element == null || currentDepth > maxDepth || results.Count >= 20)
                return;

            if (!ShouldIncludeElement(element, filterMode, viewportBounds))
            {
                // Still search children in case they're visible
                foreach (var child in element.Children())
                {
                    SearchByText(child, searchText, results, filterMode, viewportBounds, maxDepth, currentDepth + 1);
                }
                return;
            }

            string elementText = GetElementText(element);
            if (elementText != null && elementText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                results.Add(BuildCompactElementData(element));
            }

            foreach (var child in element.Children())
            {
                SearchByText(child, searchText, results, filterMode, viewportBounds, maxDepth, currentDepth + 1);
            }
        }

        /// <summary>
        /// Builds compact element data with ref for actions.
        /// </summary>
        private static object BuildCompactElementData(VisualElement element)
        {
            string refId = RegisterElement(element);
            var data = new Dictionary<string, object>
            {
                { "ref", refId },
                { "type", element.GetType().Name }
            };

            if (!string.IsNullOrEmpty(element.name))
                data["name"] = element.name;

            string text = GetElementText(element);
            if (text != null)
                data["text"] = text.Length > 50 ? text.Substring(0, 47) + "..." : text;

            // Include key USS classes (skip common unity-* ones, limit to 3)
            var classes = element.GetClasses()
                .Where(c => !c.StartsWith("unity-") || c.Contains("selected") || c.Contains("active") || c.Contains("disabled"))
                .Take(3)
                .ToList();
            if (classes.Count > 0)
                data["classes"] = classes;

            if (element.childCount > 0)
                data["children"] = element.childCount;

            return data;
        }

        /// <summary>
        /// Builds a compact tree representation with refs.
        /// </summary>
        private static object BuildCompactTree(VisualElement element, int currentDepth, int maxDepth, TreeBuildContext context)
        {
            if (element == null)
                return null;

            if (context.ElementCount >= TreeBuildContext.MaxElements)
            {
                context.WasTruncated = true;
                return new { truncated = true };
            }

            // Apply visibility filter
            if (!ShouldIncludeElement(element, context.FilterMode, context.ViewportBounds))
            {
                // Check for visible descendants
                if (!HasVisibleDescendants(element, context.FilterMode, maxDepth - currentDepth, context.ViewportBounds))
                {
                    context.HiddenCount++;
                    return null;
                }
            }

            context.ElementCount++;
            string refId = RegisterElement(element);

            var data = new Dictionary<string, object> { { "ref", refId } };

            // Type - use short form for common types
            string typeName = element.GetType().Name;
            data["t"] = typeName;

            // Name if present
            if (!string.IsNullOrEmpty(element.name))
                data["n"] = element.name;

            // Text if present (truncated)
            string text = GetElementText(element);
            if (text != null)
                data["txt"] = text.Length > 40 ? text.Substring(0, 37) + "..." : text;

            // Key classes only
            var classes = element.GetClasses()
                .Where(c => c.Contains("selected") || c.Contains("active") || c.Contains("disabled") || c.Contains("menu") || c.Contains("tab"))
                .Take(2)
                .ToList();
            if (classes.Count > 0)
                data["cls"] = classes;

            // Children
            if (currentDepth < maxDepth && element.childCount > 0)
            {
                var children = new List<object>();
                foreach (var child in element.Children())
                {
                    var childData = BuildCompactTree(child, currentDepth + 1, maxDepth, context);
                    if (childData != null)
                        children.Add(childData);

                    if (context.ElementCount >= TreeBuildContext.MaxElements)
                    {
                        context.WasTruncated = true;
                        break;
                    }
                }
                if (children.Count > 0)
                    data["c"] = children;
                else if (element.childCount > 0)
                    data["more"] = element.childCount;
            }
            else if (element.childCount > 0)
            {
                data["more"] = element.childCount;
            }

            return data;
        }

        #endregion

        #region Get Styles Tool

        /// <summary>
        /// Gets the computed USS styles for a VisualElement in an EditorWindow.
        /// </summary>
        /// <param name="windowType">The type name of the EditorWindow containing the element.</param>
        /// <param name="elementName">The name of the element to get styles for.</param>
        /// <param name="selector">USS selector to find the element (alternative to elementName).</param>
        /// <returns>Computed style properties of the element.</returns>
        [MCPTool("uitoolkit_get_styles", "Get computed USS styles for a VisualElement", Category = "UIToolkit")]
        public static object GetStyles(
            [MCPParam("window_type", "EditorWindow type name", required: true)] string windowType,
            [MCPParam("element_name", "Element name to get styles for")] string elementName = null,
            [MCPParam("selector", "USS selector to find the element")] string selector = null)
        {
            try
            {
                if (string.IsNullOrEmpty(elementName) && string.IsNullOrEmpty(selector))
                {
                    return new
                    {
                        success = false,
                        error = "Either 'element_name' or 'selector' parameter is required."
                    };
                }

                // Find the EditorWindow
                var (window, errorResponse) = FindEditorWindow(windowType);
                if (window == null)
                {
                    return errorResponse;
                }

                VisualElement rootElement = window.rootVisualElement;
                if (rootElement == null)
                {
                    return new
                    {
                        success = false,
                        error = $"EditorWindow '{windowType}' has no rootVisualElement."
                    };
                }

                // Find the target element
                VisualElement targetElement = null;

                if (!string.IsNullOrEmpty(selector))
                {
                    targetElement = QueryBySelector(rootElement, selector).FirstOrDefault();
                }
                else if (!string.IsNullOrEmpty(elementName))
                {
                    // Try name selector first (with #)
                    targetElement = QueryBySelector(rootElement, "#" + elementName).FirstOrDefault();
                    if (targetElement == null)
                    {
                        // Try finding by name attribute directly
                        targetElement = rootElement.Q(elementName);
                    }
                }

                if (targetElement == null)
                {
                    string searchCriteria = !string.IsNullOrEmpty(selector) ? $"selector '{selector}'" : $"name '{elementName}'";
                    return new
                    {
                        success = false,
                        error = $"Could not find element with {searchCriteria} in window '{windowType}'."
                    };
                }

                // Get resolved styles
                var resolvedStyle = targetElement.resolvedStyle;
                var computedStyles = BuildComputedStyles(resolvedStyle);

                // Get inline styles if any
                var inlineStyles = BuildInlineStyles(targetElement.style);

                // Get USS classes
                var ussClasses = new List<string>();
                foreach (string cssClass in targetElement.GetClasses())
                {
                    ussClasses.Add(cssClass);
                }

                return new
                {
                    success = true,
                    windowType = window.GetType().Name,
                    element = new
                    {
                        name = targetElement.name,
                        typeName = targetElement.GetType().Name,
                        ussClasses
                    },
                    computedStyles,
                    inlineStyles
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UIToolkitTools] Error getting styles: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error getting styles: {exception.Message}"
                };
            }
        }

        #endregion

        #region Click Tool

        /// <summary>
        /// Clicks a button, toggle, or clickable element in an EditorWindow.
        /// </summary>
        [MCPTool("uitoolkit_click", "Click a button, toggle, or clickable element in an EditorWindow", Category = "UIToolkit")]
        public static object Click(
            [MCPParam("window_type", "EditorWindow type name", required: true)] string windowType,
            [MCPParam("selector", "USS selector to find element")] string selector = null,
            [MCPParam("name", "Element name to match")] string name = null,
            [MCPParam("class_name", "USS class to filter by")] string className = null,
            [MCPParam("button_text", "Find clickable by visible text")] string buttonText = null)
        {
            try
            {
                // Find the EditorWindow
                var (window, windowError) = FindEditorWindow(windowType);
                if (window == null)
                {
                    return windowError;
                }

                VisualElement rootElement = window.rootVisualElement;
                if (rootElement == null)
                {
                    return new
                    {
                        success = false,
                        error = $"EditorWindow '{windowType}' has no rootVisualElement."
                    };
                }

                // Find the target element
                var (element, findError) = FindElement(rootElement, selector, name, className, buttonText);
                if (element == null)
                {
                    return findError;
                }

                // Validate element state
                if (!IsElementVisible(element))
                {
                    return new
                    {
                        success = false,
                        error = "Element is not visible.",
                        element = BuildBasicElementInfo(element)
                    };
                }

                if (!IsElementEnabled(element))
                {
                    return new
                    {
                        success = false,
                        error = "Element is disabled.",
                        element = BuildBasicElementInfo(element)
                    };
                }

                // Perform the click based on element type
                bool clicked = PerformClick(element);

                if (!clicked)
                {
                    return new
                    {
                        success = false,
                        error = "Element is not clickable.",
                        element = BuildBasicElementInfo(element),
                        suggestion = "This element type may not support click interactions."
                    };
                }

                return new
                {
                    success = true,
                    clicked = BuildBasicElementInfo(element)
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UIToolkitTools] Error clicking element: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error clicking element: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Performs a click action on the element based on its type.
        /// </summary>
        private static bool PerformClick(VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            switch (element)
            {
                case Button button:
                    // Simulate a proper click by sending PointerDown + PointerUp sequence
                    // The Clickable manipulator listens for this sequence, not ClickEvent directly
                    SimulatePointerClick(button);
                    return true;

                case Toggle toggle:
                    toggle.value = !toggle.value;
                    return true;

                case Foldout foldout:
                    foldout.value = !foldout.value;
                    return true;

                default:
                    // For other clickable elements, try the pointer sequence
                    if (element.pickingMode == PickingMode.Position)
                    {
                        SimulatePointerClick(element);
                        return true;
                    }

                    return false;
            }
        }

        /// <summary>
        /// Simulates a pointer click by sending PointerDown + PointerUp events through the panel.
        /// This properly triggers the Clickable manipulator that buttons use.
        /// Uses UnityEngine.Event to properly initialize pointer events as recommended by Unity docs.
        /// </summary>
        private static void SimulatePointerClick(VisualElement element)
        {
            var panel = element.panel;
            if (panel == null)
            {
                Debug.LogWarning("[UIToolkitTools] Element has no panel, cannot simulate click");
                return;
            }

            // Get the center position of the element in world coordinates
            Rect worldBound = element.worldBound;
            Vector2 clickPosition = worldBound.center;

            // Create UnityEngine.Event for MouseDown
            var mouseDownEvt = new Event()
            {
                type = EventType.MouseDown,
                mousePosition = clickPosition,
                button = 0,  // Left mouse button
                clickCount = 1
            };

            // Create and send PointerDownEvent initialized from UnityEngine.Event
            using (var pointerDownEvent = PointerDownEvent.GetPooled(mouseDownEvt))
            {
                pointerDownEvent.target = element;
                panel.visualTree.SendEvent(pointerDownEvent);
            }

            // Create UnityEngine.Event for MouseUp
            var mouseUpEvt = new Event()
            {
                type = EventType.MouseUp,
                mousePosition = clickPosition,
                button = 0,  // Left mouse button
                clickCount = 1
            };

            // Create and send PointerUpEvent initialized from UnityEngine.Event
            using (var pointerUpEvent = PointerUpEvent.GetPooled(mouseUpEvt))
            {
                pointerUpEvent.target = element;
                panel.visualTree.SendEvent(pointerUpEvent);
            }
        }

        #endregion

        #region Get Value Tool

        /// <summary>
        /// Gets the current value from an input field or control in an EditorWindow.
        /// </summary>
        [MCPTool("uitoolkit_get_value", "Get the current value from an input field or control", Category = "UIToolkit")]
        public static object GetValue(
            [MCPParam("window_type", "EditorWindow type name", required: true)] string windowType,
            [MCPParam("selector", "USS selector to find element")] string selector = null,
            [MCPParam("name", "Element name to match")] string name = null,
            [MCPParam("class_name", "USS class to filter by")] string className = null)
        {
            try
            {
                // Find the EditorWindow
                var (window, windowError) = FindEditorWindow(windowType);
                if (window == null)
                {
                    return windowError;
                }

                VisualElement rootElement = window.rootVisualElement;
                if (rootElement == null)
                {
                    return new
                    {
                        success = false,
                        error = $"EditorWindow '{windowType}' has no rootVisualElement."
                    };
                }

                // Find the target element
                var (element, findError) = FindElement(rootElement, selector, name, className);
                if (element == null)
                {
                    return findError;
                }

                // Extract value based on element type
                return ExtractElementValue(element);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UIToolkitTools] Error getting value: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error getting value: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Extracts the value from an element based on its type.
        /// </summary>
        private static object ExtractElementValue(VisualElement element)
        {
            var elementInfo = BuildBasicElementInfo(element);
            bool isEditable = IsEditable(element);

            switch (element)
            {
                case TextField textField:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = textField.value,
                        editable = isEditable
                    };

                case IntegerField intField:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = intField.value,
                        editable = isEditable
                    };

                case FloatField floatField:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = floatField.value,
                        editable = isEditable
                    };

                case Toggle toggle:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = toggle.value,
                        label = toggle.label,
                        editable = isEditable
                    };

                case DropdownField dropdownField:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = dropdownField.value,
                        index = dropdownField.index,
                        choices = dropdownField.choices?.ToList(),
                        editable = isEditable
                    };

                case EnumField enumField:
                    var enumType = enumField.value?.GetType();
                    string[] enumOptions = null;
                    if (enumType != null && enumType.IsEnum)
                    {
                        enumOptions = Enum.GetNames(enumType);
                    }
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = enumField.value?.ToString(),
                        enumType = enumType?.Name,
                        options = enumOptions,
                        editable = isEditable
                    };

                case Slider slider:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = slider.value,
                        min = slider.lowValue,
                        max = slider.highValue,
                        editable = isEditable
                    };

                case SliderInt sliderInt:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = sliderInt.value,
                        min = sliderInt.lowValue,
                        max = sliderInt.highValue,
                        editable = isEditable
                    };

                case MinMaxSlider minMaxSlider:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        minValue = minMaxSlider.minValue,
                        maxValue = minMaxSlider.maxValue,
                        limitMin = minMaxSlider.lowLimit,
                        limitMax = minMaxSlider.highLimit,
                        editable = isEditable
                    };

                case ObjectField objectField:
                    var obj = objectField.value;
                    object valueInfo = null;
                    if (obj != null)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(obj);
                        valueInfo = new
                        {
                            name = obj.name,
                            type = obj.GetType().Name,
                            assetPath = string.IsNullOrEmpty(assetPath) ? null : assetPath,
                            instanceId = obj.GetInstanceID()
                        };
                    }
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = valueInfo,
                        objectType = objectField.objectType?.FullName,
                        allowSceneObjects = objectField.allowSceneObjects,
                        editable = isEditable
                    };

                case RadioButtonGroup radioGroup:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = radioGroup.value,
                        choices = radioGroup.choices?.ToList(),
                        editable = isEditable
                    };

                case Label label:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = label.text,
                        editable = false
                    };

                case Foldout foldout:
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        value = foldout.value,
                        text = foldout.text,
                        editable = false
                    };

                default:
                    // Try to get a generic value property
                    string textValue = GetElementText(element);
                    if (textValue != null)
                    {
                        return new
                        {
                            success = true,
                            element = elementInfo,
                            value = textValue,
                            editable = false,
                            note = "Value extracted from text property; element type may not be fully supported."
                        };
                    }

                    return new
                    {
                        success = false,
                        element = elementInfo,
                        error = $"Element type '{element.GetType().Name}' does not have a readable value.",
                        suggestion = "This element type may not support value extraction."
                    };
            }
        }

        #endregion

        #region Set Value Tool

        /// <summary>
        /// Sets the value of an input field or control in an EditorWindow.
        /// </summary>
        [MCPTool("uitoolkit_set_value", "Set the value of an input field or control", Category = "UIToolkit")]
        public static object SetValue(
            [MCPParam("window_type", "EditorWindow type name", required: true)] string windowType,
            [MCPParam("value", "Value to set (string, number, bool, or asset path)", required: true)] object value,
            [MCPParam("selector", "USS selector to find element")] string selector = null,
            [MCPParam("name", "Element name to match")] string name = null,
            [MCPParam("class_name", "USS class to filter by")] string className = null)
        {
            try
            {
                // Find the EditorWindow
                var (window, windowError) = FindEditorWindow(windowType);
                if (window == null)
                {
                    return windowError;
                }

                VisualElement rootElement = window.rootVisualElement;
                if (rootElement == null)
                {
                    return new
                    {
                        success = false,
                        error = $"EditorWindow '{windowType}' has no rootVisualElement."
                    };
                }

                // Find the target element
                var (element, findError) = FindElement(rootElement, selector, name, className);
                if (element == null)
                {
                    return findError;
                }

                // Validate element state
                if (!IsElementVisible(element))
                {
                    return new
                    {
                        success = false,
                        error = "Element is not visible.",
                        element = BuildBasicElementInfo(element)
                    };
                }

                if (!IsElementEnabled(element))
                {
                    return new
                    {
                        success = false,
                        error = "Element is disabled.",
                        element = BuildBasicElementInfo(element)
                    };
                }

                if (!IsEditable(element))
                {
                    return new
                    {
                        success = false,
                        error = $"Element type '{element.GetType().Name}' is not editable.",
                        element = BuildBasicElementInfo(element)
                    };
                }

                // Apply the value
                return ApplyElementValue(element, value);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UIToolkitTools] Error setting value: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error setting value: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Applies a value to an element based on its type.
        /// </summary>
        private static object ApplyElementValue(VisualElement element, object value)
        {
            var elementInfo = BuildBasicElementInfo(element);

            switch (element)
            {
                case TextField textField:
                {
                    string previousValue = textField.value;
                    string newValue = value?.ToString() ?? "";
                    textField.value = newValue;
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        previousValue,
                        newValue
                    };
                }

                case IntegerField intField:
                {
                    int previousValue = intField.value;
                    if (!TryConvertToInt(value, out int newValue))
                    {
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = $"Cannot convert '{value}' to integer."
                        };
                    }
                    intField.value = newValue;
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        previousValue,
                        newValue
                    };
                }

                case FloatField floatField:
                {
                    float previousValue = floatField.value;
                    if (!TryConvertToFloat(value, out float newValue))
                    {
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = $"Cannot convert '{value}' to float."
                        };
                    }
                    floatField.value = newValue;
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        previousValue,
                        newValue
                    };
                }

                case Toggle toggle:
                {
                    bool previousValue = toggle.value;
                    if (!TryConvertToBool(value, out bool newValue))
                    {
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = $"Cannot convert '{value}' to boolean."
                        };
                    }
                    toggle.value = newValue;
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        previousValue,
                        newValue
                    };
                }

                case DropdownField dropdownField:
                {
                    string previousValue = dropdownField.value;
                    string newValue = value?.ToString() ?? "";

                    // Check if the value exists in choices
                    if (dropdownField.choices != null && !dropdownField.choices.Contains(newValue))
                    {
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = $"Value '{newValue}' is not in the available choices.",
                            choices = dropdownField.choices.ToList()
                        };
                    }

                    dropdownField.value = newValue;
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        previousValue,
                        newValue
                    };
                }

                case EnumField enumField:
                {
                    var previousValue = enumField.value;
                    string valueStr = value?.ToString() ?? "";

                    var enumType = enumField.value?.GetType();
                    if (enumType == null || !enumType.IsEnum)
                    {
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = "EnumField does not have a valid enum type set."
                        };
                    }

                    if (!Enum.TryParse(enumType, valueStr, true, out object newEnumValue))
                    {
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = $"'{valueStr}' is not a valid value for enum type '{enumType.Name}'.",
                            options = Enum.GetNames(enumType)
                        };
                    }

                    enumField.value = (Enum)newEnumValue;
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        previousValue = previousValue?.ToString(),
                        newValue = newEnumValue.ToString()
                    };
                }

                case Slider slider:
                {
                    float previousValue = slider.value;
                    if (!TryConvertToFloat(value, out float newValue))
                    {
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = $"Cannot convert '{value}' to float."
                        };
                    }

                    // Clamp to slider range
                    newValue = Mathf.Clamp(newValue, slider.lowValue, slider.highValue);
                    slider.value = newValue;
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        previousValue,
                        newValue,
                        range = new { min = slider.lowValue, max = slider.highValue }
                    };
                }

                case SliderInt sliderInt:
                {
                    int previousValue = sliderInt.value;
                    if (!TryConvertToInt(value, out int newValue))
                    {
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = $"Cannot convert '{value}' to integer."
                        };
                    }

                    // Clamp to slider range
                    newValue = Mathf.Clamp(newValue, sliderInt.lowValue, sliderInt.highValue);
                    sliderInt.value = newValue;
                    return new
                    {
                        success = true,
                        element = elementInfo,
                        previousValue,
                        newValue,
                        range = new { min = sliderInt.lowValue, max = sliderInt.highValue }
                    };
                }

                case MinMaxSlider minMaxSlider:
                {
                    // Expect value to be an object with minValue and maxValue
                    float previousMin = minMaxSlider.minValue;
                    float previousMax = minMaxSlider.maxValue;

                    if (!TryParseMinMax(value, out float newMin, out float newMax))
                    {
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = "Value must be an object with 'minValue' and 'maxValue' properties, or a string like '0.5,0.8'."
                        };
                    }

                    // Clamp to limits
                    newMin = Mathf.Clamp(newMin, minMaxSlider.lowLimit, minMaxSlider.highLimit);
                    newMax = Mathf.Clamp(newMax, minMaxSlider.lowLimit, minMaxSlider.highLimit);

                    minMaxSlider.minValue = newMin;
                    minMaxSlider.maxValue = newMax;

                    return new
                    {
                        success = true,
                        element = elementInfo,
                        previousMinValue = previousMin,
                        previousMaxValue = previousMax,
                        newMinValue = newMin,
                        newMaxValue = newMax,
                        limits = new { min = minMaxSlider.lowLimit, max = minMaxSlider.highLimit }
                    };
                }

                case ObjectField objectField:
                {
                    var previousObj = objectField.value;
                    object previousValue = null;
                    if (previousObj != null)
                    {
                        previousValue = new
                        {
                            name = previousObj.name,
                            type = previousObj.GetType().Name,
                            assetPath = AssetDatabase.GetAssetPath(previousObj)
                        };
                    }

                    // Handle null/clear
                    if (value == null || (value is string strVal && string.IsNullOrEmpty(strVal)))
                    {
                        objectField.value = null;
                        return new
                        {
                            success = true,
                            element = elementInfo,
                            previousValue,
                            newValue = (object)null
                        };
                    }

                    UnityEngine.Object newObj = null;
                    string resolvedSource = null;

                    // Strategy 1: Try as instance ID (integer)
                    // Scene objects typically have negative instance IDs
                    if (TryConvertToInt(value, out int instanceId))
                    {
                        newObj = EditorUtility.EntityIdToObject(instanceId);
                        if (newObj != null)
                        {
                            resolvedSource = $"instanceId:{instanceId}";
                        }
                    }

                    // Strategy 2: Try as scene object name (string without path separators)
                    if (newObj == null && value is string nameOrPath)
                    {
                        // If it doesn't look like an asset path, try finding in scene
                        if (!nameOrPath.StartsWith("Assets/") && !nameOrPath.Contains("/"))
                        {
                            // Try finding GameObject in scene by name
                            var sceneObj = GameObject.Find(nameOrPath);
                            if (sceneObj != null)
                            {
                                // Check if the ObjectField expects a GameObject or Component
                                var expectedType = objectField.objectType ?? typeof(UnityEngine.Object);
                                if (expectedType == typeof(GameObject) || expectedType.IsAssignableFrom(typeof(GameObject)))
                                {
                                    newObj = sceneObj;
                                    resolvedSource = $"sceneName:{nameOrPath}";
                                }
                                else if (typeof(Component).IsAssignableFrom(expectedType))
                                {
                                    // Try to get the expected component type
                                    var component = sceneObj.GetComponent(expectedType);
                                    if (component != null)
                                    {
                                        newObj = component;
                                        resolvedSource = $"sceneComponent:{nameOrPath}";
                                    }
                                }
                            }
                        }

                        // Strategy 3: Try as asset path
                        if (newObj == null)
                        {
                            newObj = AssetDatabase.LoadAssetAtPath(nameOrPath, objectField.objectType ?? typeof(UnityEngine.Object));
                            if (newObj != null)
                            {
                                resolvedSource = $"assetPath:{nameOrPath}";
                            }
                        }
                    }

                    if (newObj == null)
                    {
                        string valueStr = value.ToString();
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = $"Could not find object '{valueStr}'. Tried: instance ID, scene object name, and asset path.",
                            expectedType = objectField.objectType?.Name,
                            suggestion = "For scene objects, use instance ID (integer) or exact GameObject name. For assets, use full path starting with 'Assets/'."
                        };
                    }

                    // Validate type compatibility
                    var requiredType = objectField.objectType;
                    if (requiredType != null && !requiredType.IsInstanceOfType(newObj))
                    {
                        return new
                        {
                            success = false,
                            element = elementInfo,
                            error = $"Object '{newObj.name}' is of type '{newObj.GetType().Name}', but ObjectField requires '{requiredType.Name}'.",
                            resolvedSource
                        };
                    }

                    objectField.value = newObj;

                    string newAssetPath = AssetDatabase.GetAssetPath(newObj);
                    bool isSceneObject = string.IsNullOrEmpty(newAssetPath);

                    return new
                    {
                        success = true,
                        element = elementInfo,
                        previousValue,
                        newValue = new
                        {
                            name = newObj.name,
                            type = newObj.GetType().Name,
                            instanceId = newObj.GetInstanceID(),
                            isSceneObject,
                            assetPath = isSceneObject ? null : newAssetPath
                        },
                        resolvedSource
                    };
                }

                default:
                    return new
                    {
                        success = false,
                        element = elementInfo,
                        error = $"Setting values for element type '{element.GetType().Name}' is not supported."
                    };
            }
        }

        /// <summary>
        /// Tries to convert a value to an integer.
        /// </summary>
        private static bool TryConvertToInt(object value, out int result)
        {
            result = 0;

            if (value is int intVal)
            {
                result = intVal;
                return true;
            }

            if (value is long longVal)
            {
                result = (int)longVal;
                return true;
            }

            if (value is float floatVal)
            {
                result = (int)floatVal;
                return true;
            }

            if (value is double doubleVal)
            {
                result = (int)doubleVal;
                return true;
            }

            if (value is string strVal && int.TryParse(strVal, out result))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to convert a value to a float.
        /// </summary>
        private static bool TryConvertToFloat(object value, out float result)
        {
            result = 0f;

            if (value is float floatVal)
            {
                result = floatVal;
                return true;
            }

            if (value is double doubleVal)
            {
                result = (float)doubleVal;
                return true;
            }

            if (value is int intVal)
            {
                result = intVal;
                return true;
            }

            if (value is long longVal)
            {
                result = longVal;
                return true;
            }

            if (value is string strVal && float.TryParse(strVal, out result))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to convert a value to a boolean.
        /// </summary>
        private static bool TryConvertToBool(object value, out bool result)
        {
            result = false;

            if (value is bool boolVal)
            {
                result = boolVal;
                return true;
            }

            if (value is string strVal)
            {
                if (bool.TryParse(strVal, out result))
                {
                    return true;
                }

                // Handle common string representations
                strVal = strVal.Trim().ToLowerInvariant();
                if (strVal == "1" || strVal == "yes" || strVal == "on")
                {
                    result = true;
                    return true;
                }
                if (strVal == "0" || strVal == "no" || strVal == "off")
                {
                    result = false;
                    return true;
                }
            }

            if (value is int intVal)
            {
                result = intVal != 0;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to parse min/max values from various formats.
        /// </summary>
        private static bool TryParseMinMax(object value, out float minValue, out float maxValue)
        {
            minValue = 0;
            maxValue = 0;

            if (value is string strVal)
            {
                // Try "min,max" format
                var parts = strVal.Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0].Trim(), out minValue) &&
                    float.TryParse(parts[1].Trim(), out maxValue))
                {
                    return true;
                }
            }

            // Try dictionary/JSON object format
            if (value is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue("minValue", out object minObj) &&
                    dict.TryGetValue("maxValue", out object maxObj) &&
                    TryConvertToFloat(minObj, out minValue) &&
                    TryConvertToFloat(maxObj, out maxValue))
                {
                    return true;
                }
            }

            // Try Newtonsoft JObject
            try
            {
                var valueType = value.GetType();
                var minProp = valueType.GetProperty("minValue") ?? valueType.GetProperty("MinValue");
                var maxProp = valueType.GetProperty("maxValue") ?? valueType.GetProperty("MaxValue");

                if (minProp != null && maxProp != null)
                {
                    var minObj = minProp.GetValue(value);
                    var maxObj = maxProp.GetValue(value);

                    if (TryConvertToFloat(minObj, out minValue) && TryConvertToFloat(maxObj, out maxValue))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore reflection errors
            }

            return false;
        }

        #endregion

        #region Navigate Tool

        /// <summary>
        /// Navigates UI elements like foldouts and tabs in an EditorWindow.
        /// </summary>
        [MCPTool("uitoolkit_navigate", "Expand/collapse foldouts or select tabs in an EditorWindow", Category = "UIToolkit")]
        public static object Navigate(
            [MCPParam("window_type", "EditorWindow type name", required: true)] string windowType,
            [MCPParam("selector", "USS selector to find element")] string selector = null,
            [MCPParam("name", "Element name to match")] string name = null,
            [MCPParam("class_name", "USS class to filter by")] string className = null,
            [MCPParam("foldout_text", "Find foldout by header text")] string foldoutText = null,
            [MCPParam("tab_text", "Find tab by label text")] string tabText = null,
            [MCPParam("expand", "For foldouts: true=expand, false=collapse (toggles if omitted)")] bool? expand = null)
        {
            try
            {
                // Find the EditorWindow
                var (window, windowError) = FindEditorWindow(windowType);
                if (window == null)
                {
                    return windowError;
                }

                VisualElement rootElement = window.rootVisualElement;
                if (rootElement == null)
                {
                    return new
                    {
                        success = false,
                        error = $"EditorWindow '{windowType}' has no rootVisualElement."
                    };
                }

                // Determine search text (foldout_text or tab_text takes precedence)
                string searchText = foldoutText ?? tabText;

                // Find the target element
                var (element, findError) = FindElement(rootElement, selector, name, className, searchText);
                if (element == null)
                {
                    return findError;
                }

                // Validate element state
                if (!IsElementVisible(element))
                {
                    return new
                    {
                        success = false,
                        error = "Element is not visible.",
                        element = BuildBasicElementInfo(element)
                    };
                }

                if (!IsElementEnabled(element))
                {
                    return new
                    {
                        success = false,
                        error = "Element is disabled.",
                        element = BuildBasicElementInfo(element)
                    };
                }

                // Perform navigation based on element type
                return PerformNavigation(element, expand);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UIToolkitTools] Error navigating: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error navigating: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Performs navigation action on the element based on its type.
        /// </summary>
        private static object PerformNavigation(VisualElement element, bool? expand)
        {
            var elementInfo = BuildBasicElementInfo(element);

            switch (element)
            {
                case Foldout foldout:
                {
                    bool previousState = foldout.value;
                    bool newState = expand ?? !previousState; // Toggle if expand not specified
                    foldout.value = newState;

                    return new
                    {
                        success = true,
                        element = elementInfo,
                        action = newState ? "expanded" : "collapsed",
                        previousState,
                        newState
                    };
                }

                case Toggle toggle:
                {
                    // Some UI patterns use toggles as collapsible headers
                    bool previousState = toggle.value;
                    bool newState = expand ?? !previousState;
                    toggle.value = newState;

                    return new
                    {
                        success = true,
                        element = elementInfo,
                        action = newState ? "enabled" : "disabled",
                        previousState,
                        newState
                    };
                }

                case TreeView treeView:
                {
                    // TreeView navigation - expand/collapse all or specific items
                    // Note: Full TreeView support would need item IDs
                    return new
                    {
                        success = false,
                        element = elementInfo,
                        error = "TreeView navigation requires item IDs. Use uitoolkit_query to discover items first.",
                        suggestion = "TreeView item navigation will be supported in a future update."
                    };
                }

                case ListView listView:
                {
                    // ListView - scroll to item, select
                    return new
                    {
                        success = false,
                        element = elementInfo,
                        error = "ListView navigation requires item indices. Use uitoolkit_query to discover items first.",
                        suggestion = "ListView item navigation will be supported in a future update."
                    };
                }

                default:
                {
                    // Check if element looks like a tab (Button inside tab bar, or has tab-like classes)
                    if (element is Button button)
                    {
                        // Try clicking it as a tab
                        using (var clickEvent = ClickEvent.GetPooled())
                        {
                            clickEvent.target = button;
                            button.SendEvent(clickEvent);
                        }

                        return new
                        {
                            success = true,
                            element = elementInfo,
                            action = "clicked",
                            note = "Element clicked as potential tab button."
                        };
                    }

                    return new
                    {
                        success = false,
                        element = elementInfo,
                        error = $"Element type '{element.GetType().Name}' is not a navigable element.",
                        suggestion = "Navigable elements include: Foldout, Toggle (as header), or Button (as tab)."
                    };
                }
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Lists all currently open EditorWindows.
        /// </summary>
        private static object ListAvailableWindows()
        {
            var windows = UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>();
            var windowDataList = new List<object>();

            foreach (var window in windows)
            {
                if (window == null)
                {
                    continue;
                }

                windowDataList.Add(new
                {
                    typeName = window.GetType().Name,
                    fullTypeName = window.GetType().FullName,
                    title = window.titleContent?.text ?? "Untitled",
                    hasRootVisualElement = window.rootVisualElement != null
                });
            }

            return new
            {
                success = true,
                message = "No window_type specified. Listing available EditorWindows.",
                windowCount = windowDataList.Count,
                windows = windowDataList
            };
        }

        /// <summary>
        /// Finds an EditorWindow by type name.
        /// </summary>
        /// <param name="windowTypeName">The type name of the window to find.</param>
        /// <returns>Tuple of the found window and error response (window is null if not found).</returns>
        private static (EditorWindow window, object errorResponse) FindEditorWindow(string windowTypeName)
        {
            if (string.IsNullOrEmpty(windowTypeName))
            {
                return (null, new
                {
                    success = false,
                    error = "The 'window_type' parameter is required."
                });
            }

            string normalizedTypeName = windowTypeName.Trim();

            // Find all open EditorWindows
            var allWindows = UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>();

            // Try exact match first
            EditorWindow foundWindow = allWindows.FirstOrDefault(window =>
                window != null &&
                (window.GetType().Name.Equals(normalizedTypeName, StringComparison.OrdinalIgnoreCase) ||
                 window.GetType().FullName?.Equals(normalizedTypeName, StringComparison.OrdinalIgnoreCase) == true));

            // Try partial match if exact match fails
            if (foundWindow == null)
            {
                foundWindow = allWindows.FirstOrDefault(window =>
                    window != null &&
                    (window.GetType().Name.Contains(normalizedTypeName, StringComparison.OrdinalIgnoreCase) ||
                     window.GetType().FullName?.Contains(normalizedTypeName, StringComparison.OrdinalIgnoreCase) == true));
            }

            if (foundWindow == null)
            {
                var availableTypes = allWindows
                    .Where(w => w != null)
                    .Select(w => w.GetType().Name)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                return (null, new
                {
                    success = false,
                    error = $"Could not find open EditorWindow with type '{windowTypeName}'.",
                    availableWindows = availableTypes
                });
            }

            return (foundWindow, null);
        }

        /// <summary>
        /// Finds a single VisualElement using flexible search criteria.
        /// </summary>
        private static (VisualElement element, object error) FindElement(
            VisualElement root,
            string selector = null,
            string name = null,
            string className = null,
            string text = null,
            int maxDepth = 20)
        {
            if (root == null)
            {
                return (null, new
                {
                    success = false,
                    error = "Root element is null."
                });
            }

            bool hasSearchCriteria = !string.IsNullOrEmpty(selector) ||
                                     !string.IsNullOrEmpty(name) ||
                                     !string.IsNullOrEmpty(className) ||
                                     !string.IsNullOrEmpty(text);

            if (!hasSearchCriteria)
            {
                return (null, new
                {
                    success = false,
                    error = "At least one search parameter (selector, name, class_name, or text) is required.",
                    suggestion = "Use uitoolkit_query to explore available elements."
                });
            }

            VisualElement foundElement = null;

            // Strategy 1: USS selector query
            if (!string.IsNullOrEmpty(selector))
            {
                var candidates = QueryBySelector(root, selector, maxDepth);

                // Apply additional filters if specified
                foreach (var candidate in candidates)
                {
                    if (MatchesFilters(candidate, name, className, text))
                    {
                        foundElement = candidate;
                        break;
                    }
                }
            }
            else
            {
                // Strategy 2: Recursive search
                foundElement = FindElementRecursive(root, name, className, text, 0, maxDepth);
            }

            if (foundElement == null)
            {
                string searchDesc = BuildSearchDescription(selector, name, className, text);
                return (null, new
                {
                    success = false,
                    error = $"Could not find element matching {searchDesc}.",
                    suggestion = "Use uitoolkit_query to explore available elements."
                });
            }

            return (foundElement, null);
        }

        /// <summary>
        /// Recursively searches for an element matching the given criteria.
        /// </summary>
        private static VisualElement FindElementRecursive(
            VisualElement element,
            string targetName,
            string targetClassName,
            string targetText,
            int currentDepth,
            int maxDepth)
        {
            if (element == null || currentDepth > maxDepth)
            {
                return null;
            }

            if (MatchesFilters(element, targetName, targetClassName, targetText))
            {
                return element;
            }

            foreach (var child in element.Children())
            {
                var found = FindElementRecursive(child, targetName, targetClassName, targetText, currentDepth + 1, maxDepth);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if an element matches the given filter criteria.
        /// </summary>
        private static bool MatchesFilters(VisualElement element, string name, string className, string text)
        {
            if (element == null)
            {
                return false;
            }

            // Name filter
            if (!string.IsNullOrEmpty(name))
            {
                if (string.IsNullOrEmpty(element.name) ||
                    !element.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Class filter
            if (!string.IsNullOrEmpty(className))
            {
                if (!element.ClassListContains(className))
                {
                    return false;
                }
            }

            // Text filter
            if (!string.IsNullOrEmpty(text))
            {
                string elementText = GetElementText(element);
                if (string.IsNullOrEmpty(elementText) ||
                    !elementText.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds a human-readable description of search criteria.
        /// </summary>
        private static string BuildSearchDescription(string selector, string name, string className, string text)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(selector))
                parts.Add($"selector '{selector}'");
            if (!string.IsNullOrEmpty(name))
                parts.Add($"name '{name}'");
            if (!string.IsNullOrEmpty(className))
                parts.Add($"class '{className}'");
            if (!string.IsNullOrEmpty(text))
                parts.Add($"text containing '{text}'");

            return string.Join(" and ", parts);
        }

        /// <summary>
        /// Queries elements using USS-style selector syntax.
        /// Unity's Query(string) only accepts element names, so we parse the selector
        /// and use the appropriate query method:
        /// - "#name" → Query by element name (strips #)
        /// - ".class" → Query by USS class (strips .)
        /// - "TypeName" → Query by C# type name (recursive search)
        /// </summary>
        private static List<VisualElement> QueryBySelector(VisualElement root, string selector, int maxDepth = 20)
        {
            if (root == null || string.IsNullOrEmpty(selector))
            {
                return new List<VisualElement>();
            }

            selector = selector.Trim();

            // Name selector: #element-name
            if (selector.StartsWith("#"))
            {
                string elementName = selector.Substring(1);
                return root.Query(elementName).ToList();
            }

            // Class selector: .class-name
            if (selector.StartsWith("."))
            {
                string className = selector.Substring(1);
                return root.Query(null, className).ToList();
            }

            // Type selector: TypeName (no prefix)
            // Unity's Query API doesn't support type selectors via string,
            // so we do a recursive search matching element.GetType().Name
            var results = new List<VisualElement>();
            CollectElementsByType(root, selector, results, 0, maxDepth);
            return results;
        }

        /// <summary>
        /// Recursively collects elements that match the specified type name.
        /// </summary>
        private static void CollectElementsByType(VisualElement element, string typeName, List<VisualElement> results, int currentDepth, int maxDepth)
        {
            if (element == null || currentDepth > maxDepth)
            {
                return;
            }

            // Check if this element's type matches (case-insensitive)
            string elementTypeName = element.GetType().Name;
            if (elementTypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(element);
            }

            // Continue searching children
            foreach (var child in element.Children())
            {
                CollectElementsByType(child, typeName, results, currentDepth + 1, maxDepth);
            }
        }

        /// <summary>
        /// Checks if an element is visible in the hierarchy.
        /// </summary>
        private static bool IsElementVisible(VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            var current = element;
            while (current != null)
            {
                if (!current.visible || current.resolvedStyle.display == DisplayStyle.None)
                {
                    return false;
                }
                current = current.parent;
            }

            return true;
        }

        /// <summary>
        /// Checks if an element is enabled in the hierarchy.
        /// </summary>
        private static bool IsElementEnabled(VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            var current = element;
            while (current != null)
            {
                if (!current.enabledSelf)
                {
                    return false;
                }
                current = current.parent;
            }

            return true;
        }

        /// <summary>
        /// Checks if an element can be clicked.
        /// </summary>
        private static bool IsClickable(VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            // Check for known clickable types
            if (element is Button || element is Toggle || element is Foldout)
            {
                return true;
            }

            // Elements with Position picking mode can receive pointer/click events
            return element.pickingMode == PickingMode.Position;
        }

        /// <summary>
        /// Checks if an element can have its value set.
        /// </summary>
        private static bool IsEditable(VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            return element is TextField ||
                   element is IntegerField ||
                   element is FloatField ||
                   element is Toggle ||
                   element is DropdownField ||
                   element is EnumField ||
                   element is Slider ||
                   element is SliderInt ||
                   element is MinMaxSlider ||
                   element is ObjectField;
        }

        /// <summary>
        /// Builds basic element info for response objects.
        /// </summary>
        private static object BuildBasicElementInfo(VisualElement element)
        {
            if (element == null)
            {
                return null;
            }

            string elementText = GetElementText(element);
            var info = new Dictionary<string, object>
            {
                { "name", element.name },
                { "typeName", element.GetType().Name }
            };

            if (elementText != null)
            {
                info["text"] = elementText;
            }

            return info;
        }

        /// <summary>
        /// Recursively collects elements matching the given name and/or class criteria.
        /// </summary>
        private static void CollectMatchingElements(
            VisualElement element,
            string targetName,
            string targetClassName,
            List<VisualElement> results,
            int currentDepth,
            int maxDepth)
        {
            if (element == null || currentDepth > maxDepth)
            {
                return;
            }

            bool nameMatches = string.IsNullOrEmpty(targetName) ||
                               (!string.IsNullOrEmpty(element.name) &&
                                element.name.Contains(targetName, StringComparison.OrdinalIgnoreCase));

            bool classMatches = string.IsNullOrEmpty(targetClassName) ||
                                element.ClassListContains(targetClassName);

            // Both criteria must match (if specified)
            if (nameMatches && classMatches &&
                (!string.IsNullOrEmpty(targetName) || !string.IsNullOrEmpty(targetClassName)))
            {
                results.Add(element);
            }

            // Recurse into children
            foreach (var child in element.Children())
            {
                CollectMatchingElements(child, targetName, targetClassName, results, currentDepth + 1, maxDepth);
            }
        }

        /// <summary>
        /// Builds element data for a single VisualElement.
        /// </summary>
        private static object BuildElementData(VisualElement element)
        {
            if (element == null)
            {
                return null;
            }

            var ussClasses = new List<string>();
            foreach (string cssClass in element.GetClasses())
            {
                ussClasses.Add(cssClass);
            }

            var boundingBox = element.worldBound;
            string elementText = GetElementText(element);

            var result = new Dictionary<string, object>
            {
                { "name", element.name },
                { "typeName", element.GetType().Name },
                { "ussClasses", ussClasses },
                { "visible", element.visible },
                { "enabled", element.enabledSelf },
                { "pickable", element.pickingMode == PickingMode.Position },
                { "bounds", new { x = boundingBox.x, y = boundingBox.y, width = boundingBox.width, height = boundingBox.height } },
                { "childCount", element.childCount }
            };

            // Only include text if present
            if (elementText != null)
            {
                result["text"] = elementText;
            }

            return result;
        }

        /// <summary>
        /// Extracts displayable text content from a VisualElement.
        /// </summary>
        /// <param name="element">The element to extract text from.</param>
        /// <param name="maxLength">Maximum text length before truncation (default 500).</param>
        /// <returns>The text content, or null if no text is available.</returns>
        private static string GetElementText(VisualElement element, int maxLength = 500)
        {
            if (element == null)
            {
                return null;
            }

            string text = null;

            // Try specific element types first
            switch (element)
            {
                case Label label:
                    text = label.text;
                    break;
                case Button button:
                    text = button.text;
                    break;
                case TextField textField:
                    text = textField.value;
                    break;
                case Foldout foldout:
                    text = foldout.text;
                    break;
                case Toggle toggle:
                    text = toggle.label;
                    break;
                case DropdownField dropdownField:
                    text = dropdownField.value;
                    break;
                case EnumField enumField:
                    text = enumField.value?.ToString();
                    break;
                case TextElement textElement:
                    text = textElement.text;
                    break;
                default:
                    // Try reflection for unknown types with a 'text' property
                    try
                    {
                        var textProperty = element.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                        if (textProperty != null && textProperty.PropertyType == typeof(string))
                        {
                            text = textProperty.GetValue(element) as string;
                        }
                    }
                    catch
                    {
                        // Ignore reflection errors
                    }
                    break;
            }

            // Truncate if needed
            if (!string.IsNullOrEmpty(text) && text.Length > maxLength)
            {
                text = text.Substring(0, maxLength) + "...";
            }

            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// Filter modes for element queries.
        /// </summary>
        private enum FilterMode
        {
            /// <summary>Only include visible elements (display != none, visibility != hidden).</summary>
            Visible,
            /// <summary>Include all elements regardless of visibility.</summary>
            All
        }

        /// <summary>
        /// Parses a filter mode string to the enum value.
        /// </summary>
        private static FilterMode ParseFilterMode(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return FilterMode.Visible; // Default
            }

            return filter.ToLowerInvariant() switch
            {
                "visible" => FilterMode.Visible,
                "all" => FilterMode.All,
                _ => FilterMode.Visible // Default for unknown values
            };
        }

        /// <summary>
        /// Checks if an element should be included based on the filter mode.
        /// </summary>
        /// <param name="element">The element to check.</param>
        /// <param name="filterMode">The filter mode to apply.</param>
        /// <param name="viewportBounds">The visible viewport bounds (optional, for bounds checking).</param>
        private static bool ShouldIncludeElement(VisualElement element, FilterMode filterMode, Rect? viewportBounds = null)
        {
            if (element == null)
            {
                return false;
            }

            switch (filterMode)
            {
                case FilterMode.All:
                    return true;

                case FilterMode.Visible:
                    // Check if element itself is visible (CSS visibility)
                    if (!element.visible || element.resolvedStyle.display == DisplayStyle.None)
                    {
                        return false;
                    }
                    // Check opacity - elements with 0 opacity are effectively hidden
                    if (element.resolvedStyle.opacity <= 0)
                    {
                        return false;
                    }
                    // Check if element has zero size (effectively invisible)
                    var bounds = element.worldBound;
                    if (bounds.width <= 0 || bounds.height <= 0)
                    {
                        return false;
                    }
                    // Check if element is within the visible viewport
                    if (viewportBounds.HasValue)
                    {
                        if (!bounds.Overlaps(viewportBounds.Value))
                        {
                            return false;
                        }
                    }
                    return true;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Checks if an element has any visible descendants (used to include parent containers).
        /// </summary>
        private static bool HasVisibleDescendants(VisualElement element, FilterMode filterMode, int maxDepth, Rect? viewportBounds = null, int currentDepth = 0)
        {
            if (element == null || currentDepth > maxDepth)
            {
                return false;
            }

            foreach (var child in element.Children())
            {
                if (ShouldIncludeElement(child, filterMode, viewportBounds))
                {
                    return true;
                }
                if (HasVisibleDescendants(child, filterMode, maxDepth, viewportBounds, currentDepth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds a tree structure representing the visual element hierarchy with size tracking.
        /// Uses an internal class to track element count during recursion.
        /// </summary>
        private static object BuildElementTree(VisualElement element, int currentDepth, int maxDepth)
        {
            var context = new TreeBuildContext(FilterMode.All);
            return BuildElementTreeInternal(element, currentDepth, maxDepth, context);
        }

        /// <summary>
        /// Context for tracking state during tree building.
        /// </summary>
        private class TreeBuildContext
        {
            /// <summary>
            /// Maximum number of elements to include in the tree.
            /// This is a safety limit to prevent excessively large responses.
            /// With ~200 bytes per element average, 300 elements ≈ 60KB.
            /// </summary>
            public const int MaxElements = 300;

            /// <summary>
            /// The filter mode to apply.
            /// </summary>
            public FilterMode FilterMode { get; }

            /// <summary>
            /// The visible viewport bounds for bounds-based filtering.
            /// Elements outside these bounds are considered not visible.
            /// </summary>
            public Rect? ViewportBounds { get; }

            /// <summary>
            /// Current count of elements added to the tree.
            /// </summary>
            public int ElementCount { get; set; }

            /// <summary>
            /// Count of elements skipped due to filtering.
            /// </summary>
            public int HiddenCount { get; set; }

            /// <summary>
            /// Whether the tree was truncated due to size limits.
            /// </summary>
            public bool WasTruncated { get; set; }

            public TreeBuildContext(FilterMode filterMode, Rect? viewportBounds = null)
            {
                FilterMode = filterMode;
                ViewportBounds = viewportBounds;
            }
        }

        /// <summary>
        /// Internal implementation of BuildElementTree with context tracking and filtering.
        /// </summary>
        private static object BuildElementTreeInternal(
            VisualElement element,
            int currentDepth,
            int maxDepth,
            TreeBuildContext context)
        {
            if (element == null)
            {
                return null;
            }

            // Check if we've hit the element limit
            if (context.ElementCount >= TreeBuildContext.MaxElements)
            {
                context.WasTruncated = true;
                return new Dictionary<string, object>
                {
                    { "truncated", true },
                    { "reason", "Element limit reached" }
                };
            }

            // Apply filter - but always include root element and elements with visible descendants
            bool includeThisElement = ShouldIncludeElement(element, context.FilterMode, context.ViewportBounds);
            bool hasVisibleChildren = false;

            if (!includeThisElement && context.FilterMode != FilterMode.All)
            {
                // Check if this element has any visible descendants we need to include
                hasVisibleChildren = HasVisibleDescendants(element, context.FilterMode, maxDepth - currentDepth, context.ViewportBounds);
                if (!hasVisibleChildren)
                {
                    context.HiddenCount++;
                    return null; // Skip this element entirely
                }
            }

            context.ElementCount++;

            var ussClasses = new List<string>();
            foreach (string cssClass in element.GetClasses())
            {
                ussClasses.Add(cssClass);
            }

            string elementText = GetElementText(element);

            var elementData = new Dictionary<string, object>
            {
                { "name", element.name },
                { "typeName", element.GetType().Name },
                { "ussClasses", ussClasses },
                { "childCount", element.childCount }
            };

            // Mark if this element is just a container for visible children
            if (!includeThisElement && hasVisibleChildren)
            {
                elementData["isContainer"] = true;
            }

            // Only include text if present
            if (elementText != null)
            {
                elementData["text"] = elementText;
            }

            // Add children if within depth limit and element limit
            if (currentDepth < maxDepth && element.childCount > 0)
            {
                // Check if we're approaching the limit
                if (context.ElementCount >= TreeBuildContext.MaxElements)
                {
                    context.WasTruncated = true;
                    elementData["childrenTruncated"] = true;
                    elementData["truncationReason"] = "Element limit reached";
                }
                else
                {
                    var childrenList = new List<object>();
                    int skippedByFilter = 0;

                    foreach (var child in element.Children())
                    {
                        var childData = BuildElementTreeInternal(child, currentDepth + 1, maxDepth, context);

                        // Skip null children (filtered out)
                        if (childData == null)
                        {
                            skippedByFilter++;
                            continue;
                        }

                        childrenList.Add(childData);

                        // Stop adding children if we hit the limit
                        if (context.ElementCount >= TreeBuildContext.MaxElements)
                        {
                            context.WasTruncated = true;
                            break;
                        }
                    }

                    // Only include children array if there are visible children
                    if (childrenList.Count > 0)
                    {
                        elementData["children"] = childrenList;
                    }

                    // Update childCount to reflect visible children only
                    elementData["childCount"] = childrenList.Count;
                    if (skippedByFilter > 0)
                    {
                        elementData["hiddenChildren"] = skippedByFilter;
                    }

                    // Note if some children were skipped due to element limit
                    if (childrenList.Count + skippedByFilter < element.childCount)
                    {
                        elementData["childrenTruncated"] = true;
                        elementData["childrenIncluded"] = childrenList.Count;
                    }
                }
            }
            else if (element.childCount > 0)
            {
                elementData["childrenTruncated"] = true;
            }

            return elementData;
        }

        /// <summary>
        /// Builds a dictionary of computed (resolved) style properties.
        /// </summary>
        private static object BuildComputedStyles(IResolvedStyle resolvedStyle)
        {
            return new
            {
                // Layout
                width = resolvedStyle.width,
                height = resolvedStyle.height,
                minWidth = resolvedStyle.minWidth.value,
                minHeight = resolvedStyle.minHeight.value,
                maxWidth = resolvedStyle.maxWidth.value,
                maxHeight = resolvedStyle.maxHeight.value,

                // Positioning
                left = resolvedStyle.left,
                top = resolvedStyle.top,
                right = resolvedStyle.right,
                bottom = resolvedStyle.bottom,

                // Margin
                marginLeft = resolvedStyle.marginLeft,
                marginTop = resolvedStyle.marginTop,
                marginRight = resolvedStyle.marginRight,
                marginBottom = resolvedStyle.marginBottom,

                // Padding
                paddingLeft = resolvedStyle.paddingLeft,
                paddingTop = resolvedStyle.paddingTop,
                paddingRight = resolvedStyle.paddingRight,
                paddingBottom = resolvedStyle.paddingBottom,

                // Border Width
                borderLeftWidth = resolvedStyle.borderLeftWidth,
                borderTopWidth = resolvedStyle.borderTopWidth,
                borderRightWidth = resolvedStyle.borderRightWidth,
                borderBottomWidth = resolvedStyle.borderBottomWidth,

                // Border Radius
                borderTopLeftRadius = resolvedStyle.borderTopLeftRadius,
                borderTopRightRadius = resolvedStyle.borderTopRightRadius,
                borderBottomLeftRadius = resolvedStyle.borderBottomLeftRadius,
                borderBottomRightRadius = resolvedStyle.borderBottomRightRadius,

                // Border Color
                borderLeftColor = ColorToHex(resolvedStyle.borderLeftColor),
                borderTopColor = ColorToHex(resolvedStyle.borderTopColor),
                borderRightColor = ColorToHex(resolvedStyle.borderRightColor),
                borderBottomColor = ColorToHex(resolvedStyle.borderBottomColor),

                // Colors
                backgroundColor = ColorToHex(resolvedStyle.backgroundColor),
                color = ColorToHex(resolvedStyle.color),
                unityBackgroundImageTintColor = ColorToHex(resolvedStyle.unityBackgroundImageTintColor),

                // Text
                fontSize = resolvedStyle.fontSize,
                unityFontStyleAndWeight = resolvedStyle.unityFontStyleAndWeight.ToString(),
                unityTextAlign = resolvedStyle.unityTextAlign.ToString(),
                whiteSpace = resolvedStyle.whiteSpace.ToString(),
                letterSpacing = resolvedStyle.letterSpacing,
                wordSpacing = resolvedStyle.wordSpacing,
                unityParagraphSpacing = resolvedStyle.unityParagraphSpacing,

                // Flex
                flexDirection = resolvedStyle.flexDirection.ToString(),
                flexWrap = resolvedStyle.flexWrap.ToString(),
                flexGrow = resolvedStyle.flexGrow,
                flexShrink = resolvedStyle.flexShrink,
                alignItems = resolvedStyle.alignItems.ToString(),
                alignContent = resolvedStyle.alignContent.ToString(),
                alignSelf = resolvedStyle.alignSelf.ToString(),
                justifyContent = resolvedStyle.justifyContent.ToString(),

                // Display
                display = resolvedStyle.display.ToString(),
                visibility = resolvedStyle.visibility.ToString(),
                opacity = resolvedStyle.opacity
            };
        }

        /// <summary>
        /// Builds a dictionary of inline style properties that are explicitly set.
        /// </summary>
        private static object BuildInlineStyles(IStyle style)
        {
            var inlineStyles = new Dictionary<string, object>();

            // Check which styles are explicitly set (not inherited/default)
            // IStyle uses StyleXxx structs that have a 'keyword' property for checking if set
            AddInlineStyleIfSet(inlineStyles, "width", style.width);
            AddInlineStyleIfSet(inlineStyles, "height", style.height);
            AddInlineStyleIfSet(inlineStyles, "backgroundColor", style.backgroundColor);
            AddInlineStyleIfSet(inlineStyles, "color", style.color);
            AddInlineStyleIfSet(inlineStyles, "display", style.display);
            AddInlineStyleIfSet(inlineStyles, "visibility", style.visibility);
            AddInlineStyleIfSet(inlineStyles, "opacity", style.opacity);
            AddInlineStyleIfSet(inlineStyles, "flexGrow", style.flexGrow);
            AddInlineStyleIfSet(inlineStyles, "flexShrink", style.flexShrink);
            AddInlineStyleIfSet(inlineStyles, "flexDirection", style.flexDirection);

            return inlineStyles.Count > 0 ? inlineStyles : null;
        }

        /// <summary>
        /// Adds an inline style to the dictionary if it has an explicit value set.
        /// </summary>
        private static void AddInlineStyleIfSet<T>(Dictionary<string, object> styles, string propertyName, T styleValue)
        {
            // Try to get the keyword property via reflection to check if it's set
            try
            {
                var keywordProperty = styleValue.GetType().GetProperty("keyword", BindingFlags.Public | BindingFlags.Instance);
                if (keywordProperty != null)
                {
                    var keyword = keywordProperty.GetValue(styleValue);
                    // StyleKeyword.Undefined means no explicit value set
                    if (keyword.ToString() != "Undefined")
                    {
                        var valueProperty = styleValue.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
                        if (valueProperty != null)
                        {
                            var value = valueProperty.GetValue(styleValue);
                            if (value is Color colorValue)
                            {
                                styles[propertyName] = ColorToHex(colorValue);
                            }
                            else
                            {
                                styles[propertyName] = value?.ToString();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore reflection errors - the style simply won't be included
            }
        }

        /// <summary>
        /// Converts a Color to a hex string.
        /// </summary>
        private static string ColorToHex(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
        }

        #endregion
    }
}
