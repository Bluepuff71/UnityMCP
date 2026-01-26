using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for executing Unity Editor menu items by path.
    /// Includes a safety blacklist to prevent dangerous operations.
    /// </summary>
    public static class ExecuteMenuItem
    {
        /// <summary>
        /// Blacklist of menu paths that are not allowed to be executed for safety reasons.
        /// Uses case-insensitive comparison.
        /// </summary>
        private static readonly HashSet<string> MenuPathBlacklist = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            // Application exit
            "File/Quit",
            "File/Exit",

            // Build operations that could be disruptive
            "File/Build And Run",

            // Dangerous project operations
            "Assets/Delete",

            // Editor preferences that could break things
            "Edit/Preferences...",
            "Edit/Project Settings...",

            // Package Manager operations
            "Window/Package Manager"
        };

        /// <summary>
        /// Executes a Unity Editor menu item by its path.
        /// </summary>
        /// <param name="menuPath">The menu item path (e.g., "File/Save", "GameObject/Create Empty").</param>
        /// <returns>Result object indicating success or failure with appropriate message.</returns>
        [MCPTool("execute_menu_item", "Execute a Unity Editor menu item by path", Category = "Editor")]
        public static object Execute(
            [MCPParam("menu_path", "The menu item path (e.g., 'File/Save', 'GameObject/Create Empty')", required: true)] string menuPath)
        {
            // Validate menu_path is provided
            if (string.IsNullOrWhiteSpace(menuPath))
            {
                return new
                {
                    success = false,
                    error = "The 'menu_path' parameter is required and cannot be empty."
                };
            }

            // Normalize the menu path (trim whitespace)
            string normalizedMenuPath = menuPath.Trim();

            // Check if the menu path is blacklisted
            if (IsMenuPathBlacklisted(normalizedMenuPath))
            {
                return new
                {
                    success = false,
                    error = $"The menu item '{normalizedMenuPath}' is blacklisted for safety reasons and cannot be executed.",
                    blacklisted = true
                };
            }

            try
            {
                // Execute the menu item
                bool executed = EditorApplication.ExecuteMenuItem(normalizedMenuPath);

                if (executed)
                {
                    return new
                    {
                        success = true,
                        message = $"Successfully executed menu item: '{normalizedMenuPath}'",
                        menu_path = normalizedMenuPath
                    };
                }
                else
                {
                    // ExecuteMenuItem returns false if the menu item doesn't exist or couldn't be executed
                    return new
                    {
                        success = false,
                        error = $"Failed to execute menu item: '{normalizedMenuPath}'. The menu item may not exist, may be disabled, or may require specific conditions to be met.",
                        menu_path = normalizedMenuPath
                    };
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ExecuteMenuItem] Error executing menu item '{normalizedMenuPath}': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing menu item '{normalizedMenuPath}': {exception.Message}",
                    menu_path = normalizedMenuPath
                };
            }
        }

        /// <summary>
        /// Checks if a menu path is blacklisted.
        /// Performs case-insensitive matching and also checks for partial matches
        /// to catch variations of blacklisted paths.
        /// </summary>
        /// <param name="menuPath">The menu path to check.</param>
        /// <returns>True if the menu path is blacklisted, false otherwise.</returns>
        private static bool IsMenuPathBlacklisted(string menuPath)
        {
            // Direct match (case-insensitive due to HashSet comparer)
            if (MenuPathBlacklist.Contains(menuPath))
            {
                return true;
            }

            // Check for paths that start with blacklisted prefixes
            // This catches variations like "File/Quit " or "File/Quit..."
            foreach (string blacklistedPath in MenuPathBlacklist)
            {
                if (menuPath.StartsWith(blacklistedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
