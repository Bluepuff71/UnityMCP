using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Resources.Editor
{
    /// <summary>
    /// Resource provider for open editor windows.
    /// </summary>
    public static class Windows
    {
        /// <summary>
        /// Gets information about all open editor windows.
        /// </summary>
        /// <returns>Object containing information about open editor windows.</returns>
        [MCPResource("editor://windows", "Open editor windows and their states")]
        public static object GetWindows()
        {
            var allWindows = UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>();
            var focusedWindow = EditorWindow.focusedWindow;
            var mouseOverWindow = EditorWindow.mouseOverWindow;

            var windowInfoList = new List<object>();

            foreach (var window in allWindows)
            {
                if (window == null)
                {
                    continue;
                }

                try
                {
                    windowInfoList.Add(new
                    {
                        title = window.titleContent?.text ?? "Unknown",
                        type = window.GetType().Name,
                        fullTypeName = window.GetType().FullName,
                        instanceId = window.GetInstanceID(),
                        position = new
                        {
                            x = window.position.x,
                            y = window.position.y,
                            width = window.position.width,
                            height = window.position.height
                        },
                        minSize = new
                        {
                            width = window.minSize.x,
                            height = window.minSize.y
                        },
                        maxSize = new
                        {
                            width = window.maxSize.x,
                            height = window.maxSize.y
                        },
                        isFocused = focusedWindow == window,
                        isMouseOver = mouseOverWindow == window,
                        hasFocus = window.hasFocus,
                        docked = window.docked,
                        maximized = window.maximized
                    });
                }
                catch
                {
                    // Skip windows that throw exceptions when accessing properties
                }
            }

            // Build window type summary separately to avoid dynamic usage
            var windowTypeCounts = allWindows
                .Where(window => window != null)
                .GroupBy(window => window.GetType().Name)
                .Select(windowGroup => new
                {
                    type = windowGroup.Key,
                    count = windowGroup.Count()
                })
                .OrderByDescending(summary => summary.count)
                .ToArray();

            return new
            {
                totalCount = windowInfoList.Count,
                focusedWindow = focusedWindow != null ? new
                {
                    title = focusedWindow.titleContent?.text ?? "Unknown",
                    type = focusedWindow.GetType().Name,
                    instanceId = focusedWindow.GetInstanceID()
                } : null,
                mouseOverWindow = mouseOverWindow != null ? new
                {
                    title = mouseOverWindow.titleContent?.text ?? "Unknown",
                    type = mouseOverWindow.GetType().Name,
                    instanceId = mouseOverWindow.GetInstanceID()
                } : null,
                windows = windowInfoList.ToArray(),
                windowTypesSummary = windowTypeCounts
            };
        }
    }
}
