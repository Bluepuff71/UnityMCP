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
        #region Query Tool

        /// <summary>
        /// Queries VisualElements in an EditorWindow by selector, name, type, or USS class.
        /// </summary>
        /// <param name="windowType">The type name of the EditorWindow to query (e.g., "SceneView", "InspectorWindow").</param>
        /// <param name="selector">USS selector to query elements (e.g., "#element-name", ".class-name", "Button").</param>
        /// <param name="name">Element name to search for.</param>
        /// <param name="className">USS class name to filter by.</param>
        /// <param name="maxDepth">Maximum depth to traverse in the visual tree (default 10).</param>
        /// <returns>Matching elements with their properties.</returns>
        [MCPTool("uitoolkit_query", "Query VisualElements by selector, name, type, or USS class in an EditorWindow", Category = "UIToolkit")]
        public static object Query(
            [MCPParam("window_type", "EditorWindow type name (e.g., 'SceneView', 'InspectorWindow')")] string windowType = null,
            [MCPParam("selector", "USS selector (e.g., '#element-name', '.class-name', 'Button')")] string selector = null,
            [MCPParam("name", "Element name to search for")] string name = null,
            [MCPParam("class_name", "USS class name to filter by")] string className = null,
            [MCPParam("max_depth", "Maximum depth to traverse (default 10)")] int maxDepth = 10)
        {
            try
            {
                // At least one query parameter must be provided (unless we just want to list windows)
                bool hasQueryParams = !string.IsNullOrEmpty(selector) ||
                                      !string.IsNullOrEmpty(name) ||
                                      !string.IsNullOrEmpty(className);

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
                    return new
                    {
                        success = false,
                        error = $"EditorWindow '{windowType}' has no rootVisualElement."
                    };
                }

                // If no query params, return the root element structure
                if (!hasQueryParams)
                {
                    return new
                    {
                        success = true,
                        windowType = window.GetType().Name,
                        windowTitle = window.titleContent.text,
                        rootElement = BuildElementTree(rootElement, 0, maxDepth)
                    };
                }

                // Query elements
                var matchingElements = new List<VisualElement>();

                if (!string.IsNullOrEmpty(selector))
                {
                    // Use USS selector query
                    var queryResults = rootElement.Query(selector).ToList();
                    matchingElements.AddRange(queryResults);
                }
                else
                {
                    // Manual search by name and/or class
                    CollectMatchingElements(rootElement, name, className, matchingElements, 0, maxDepth);
                }

                // Build result data
                var elementsData = matchingElements.Select(element => BuildElementData(element)).ToList();

                return new
                {
                    success = true,
                    windowType = window.GetType().Name,
                    windowTitle = window.titleContent.text,
                    matchCount = matchingElements.Count,
                    query = new
                    {
                        selector,
                        name,
                        className
                    },
                    elements = elementsData
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UIToolkitTools] Error querying elements: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error querying elements: {exception.Message}"
                };
            }
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
                    targetElement = rootElement.Query(selector).First();
                }
                else if (!string.IsNullOrEmpty(elementName))
                {
                    targetElement = rootElement.Query(elementName).First();
                    if (targetElement == null)
                    {
                        // Try finding by name attribute
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
        /// Builds a tree structure representing the visual element hierarchy.
        /// </summary>
        private static object BuildElementTree(VisualElement element, int currentDepth, int maxDepth)
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

            var elementData = new Dictionary<string, object>
            {
                { "name", element.name },
                { "typeName", element.GetType().Name },
                { "ussClasses", ussClasses },
                { "childCount", element.childCount }
            };

            // Add children if within depth limit
            if (currentDepth < maxDepth && element.childCount > 0)
            {
                var childrenList = new List<object>();
                foreach (var child in element.Children())
                {
                    childrenList.Add(BuildElementTree(child, currentDepth + 1, maxDepth));
                }
                elementData["children"] = childrenList;
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
