using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace UnityMCP.Editor.Resources.Menu
{
    /// <summary>
    /// Resource provider for available Unity Editor menu items.
    /// </summary>
    public static class MenuItems
    {
        /// <summary>
        /// Gets available Unity Editor menu items organized by category.
        /// Uses reflection to access internal Unity APIs for comprehensive menu listing.
        /// Falls back to common menu paths if internal APIs are unavailable.
        /// </summary>
        /// <returns>Object containing categorized menu items.</returns>
        [MCPResource("menu://items", "Available Unity Editor menu items")]
        public static object Get()
        {
            var menuItemsByCategory = new Dictionary<string, List<string>>();
            var allMenuItems = new List<string>();
            string source = "unknown";

            try
            {
                // Try to get menu items using Unity's internal Unsupported.GetSubmenus
                allMenuItems = GetMenuItemsViaReflection();
                source = allMenuItems.Count > 0 ? "reflection" : "fallback";
            }
            catch
            {
                source = "fallback";
            }

            // If reflection didn't work or returned empty, use fallback
            if (allMenuItems.Count == 0)
            {
                allMenuItems = GetFallbackMenuItems();
                source = "fallback";
            }

            // Organize menu items by their top-level category
            foreach (string menuPath in allMenuItems)
            {
                string category = GetMenuCategory(menuPath);

                if (!menuItemsByCategory.ContainsKey(category))
                {
                    menuItemsByCategory[category] = new List<string>();
                }

                menuItemsByCategory[category].Add(menuPath);
            }

            // Sort items within each category
            foreach (var categoryItems in menuItemsByCategory.Values)
            {
                categoryItems.Sort(StringComparer.OrdinalIgnoreCase);
            }

            // Build categorized result
            var categorized = menuItemsByCategory
                .OrderBy(kvp => GetCategoryOrder(kvp.Key))
                .ThenBy(kvp => kvp.Key)
                .Select(kvp => new
                {
                    category = kvp.Key,
                    count = kvp.Value.Count,
                    items = kvp.Value.ToArray()
                })
                .ToArray();

            return new
            {
                totalCount = allMenuItems.Count,
                categoryCount = menuItemsByCategory.Count,
                source = source,
                categories = categorized,
                allItems = allMenuItems.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        /// <summary>
        /// Attempts to get menu items using Unity's internal Unsupported.GetSubmenus method.
        /// </summary>
        /// <returns>List of menu paths, or empty list if reflection fails.</returns>
        private static List<string> GetMenuItemsViaReflection()
        {
            var menuItems = new List<string>();

            // Try to access Unsupported.GetSubmenus via reflection
            Type unsupportedType = typeof(EditorApplication).Assembly.GetType("UnityEditor.Unsupported");

            if (unsupportedType == null)
            {
                return menuItems;
            }

            MethodInfo getSubmenusMethod = unsupportedType.GetMethod(
                "GetSubmenus",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new Type[] { typeof(string) },
                null);

            if (getSubmenusMethod == null)
            {
                return menuItems;
            }

            // Get submenus for main menu categories
            string[] mainMenus = { "File", "Edit", "Assets", "GameObject", "Component", "Window", "Help" };

            foreach (string mainMenu in mainMenus)
            {
                try
                {
                    object result = getSubmenusMethod.Invoke(null, new object[] { mainMenu });

                    if (result is string[] submenus)
                    {
                        menuItems.AddRange(submenus);
                    }
                }
                catch
                {
                    // Skip this category if it fails
                }
            }

            // Also try to get the root level menus
            try
            {
                object rootResult = getSubmenusMethod.Invoke(null, new object[] { "" });

                if (rootResult is string[] rootMenus)
                {
                    // Add any additional root-level items not already captured
                    foreach (string item in rootMenus)
                    {
                        if (!menuItems.Contains(item))
                        {
                            menuItems.Add(item);
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors for root menu
            }

            return menuItems;
        }

        /// <summary>
        /// Returns a fallback list of common Unity Editor menu items.
        /// </summary>
        /// <returns>List of common menu paths.</returns>
        private static List<string> GetFallbackMenuItems()
        {
            return new List<string>
            {
                // File menu
                "File/New Scene",
                "File/Open Scene",
                "File/Save",
                "File/Save As...",
                "File/Save Project",
                "File/Build Settings...",
                "File/Build And Run",

                // Edit menu
                "Edit/Undo",
                "Edit/Redo",
                "Edit/Cut",
                "Edit/Copy",
                "Edit/Paste",
                "Edit/Delete",
                "Edit/Duplicate",
                "Edit/Select All",
                "Edit/Deselect All",
                "Edit/Frame Selected",
                "Edit/Lock View to Selected",
                "Edit/Find",
                "Edit/Play",
                "Edit/Pause",
                "Edit/Step",

                // Assets menu
                "Assets/Create/Folder",
                "Assets/Create/C# Script",
                "Assets/Create/Shader/Standard Surface Shader",
                "Assets/Create/Material",
                "Assets/Create/Prefab",
                "Assets/Create/Scene",
                "Assets/Refresh",
                "Assets/Import New Asset...",
                "Assets/Import Package/Custom Package...",
                "Assets/Export Package...",
                "Assets/Open",
                "Assets/Rename",
                "Assets/Reimport",
                "Assets/Reimport All",

                // GameObject menu
                "GameObject/Create Empty",
                "GameObject/Create Empty Child",
                "GameObject/3D Object/Cube",
                "GameObject/3D Object/Sphere",
                "GameObject/3D Object/Capsule",
                "GameObject/3D Object/Cylinder",
                "GameObject/3D Object/Plane",
                "GameObject/3D Object/Quad",
                "GameObject/2D Object/Sprite",
                "GameObject/2D Object/Tilemap",
                "GameObject/Light/Directional Light",
                "GameObject/Light/Point Light",
                "GameObject/Light/Spot Light",
                "GameObject/Light/Area Light",
                "GameObject/Audio/Audio Source",
                "GameObject/Audio/Audio Listener",
                "GameObject/Camera",
                "GameObject/UI/Canvas",
                "GameObject/UI/Panel",
                "GameObject/UI/Button",
                "GameObject/UI/Text",
                "GameObject/UI/Image",
                "GameObject/UI/Input Field",
                "GameObject/UI/Slider",
                "GameObject/UI/Scrollbar",
                "GameObject/UI/Toggle",
                "GameObject/UI/Event System",
                "GameObject/Effects/Particle System",
                "GameObject/Move To View",
                "GameObject/Align With View",
                "GameObject/Align View to Selected",
                "GameObject/Set as first sibling",
                "GameObject/Set as last sibling",

                // Component menu
                "Component/Add...",
                "Component/Mesh/Mesh Filter",
                "Component/Mesh/Mesh Renderer",
                "Component/Physics/Rigidbody",
                "Component/Physics/Box Collider",
                "Component/Physics/Sphere Collider",
                "Component/Physics/Capsule Collider",
                "Component/Physics/Mesh Collider",
                "Component/Physics/Character Controller",
                "Component/Physics 2D/Rigidbody 2D",
                "Component/Physics 2D/Box Collider 2D",
                "Component/Physics 2D/Circle Collider 2D",
                "Component/Audio/Audio Source",
                "Component/Audio/Audio Listener",
                "Component/Rendering/Camera",
                "Component/Rendering/Light",
                "Component/Scripts",

                // Window menu
                "Window/General/Scene",
                "Window/General/Game",
                "Window/General/Inspector",
                "Window/General/Hierarchy",
                "Window/General/Project",
                "Window/General/Console",
                "Window/Animation/Animation",
                "Window/Animation/Animator",
                "Window/Audio/Audio Mixer",
                "Window/Analysis/Profiler",
                "Window/Analysis/Frame Debugger",
                "Window/Asset Management/Version Control",
                "Window/Package Manager",
                "Window/Layouts/Default",
                "Window/Layouts/2 by 3",
                "Window/Layouts/4 Split",
                "Window/Layouts/Tall",
                "Window/Layouts/Wide",
                "Window/Panels/Test Runner",

                // Help menu
                "Help/About Unity...",
                "Help/Unity Manual",
                "Help/Scripting Reference",
                "Help/Report a Bug...",
                "Help/Check for Updates"
            };
        }

        /// <summary>
        /// Extracts the top-level category from a menu path.
        /// </summary>
        /// <param name="menuPath">The full menu path.</param>
        /// <returns>The top-level category name.</returns>
        private static string GetMenuCategory(string menuPath)
        {
            if (string.IsNullOrEmpty(menuPath))
            {
                return "Other";
            }

            int slashIndex = menuPath.IndexOf('/');

            if (slashIndex > 0)
            {
                return menuPath.Substring(0, slashIndex);
            }

            return menuPath;
        }

        /// <summary>
        /// Returns an ordering value for standard Unity menu categories.
        /// </summary>
        /// <param name="category">The category name.</param>
        /// <returns>An integer for ordering purposes.</returns>
        private static int GetCategoryOrder(string category)
        {
            return category switch
            {
                "File" => 0,
                "Edit" => 1,
                "Assets" => 2,
                "GameObject" => 3,
                "Component" => 4,
                "Window" => 5,
                "Help" => 6,
                _ => 100 // Put unknown categories at the end
            };
        }
    }
}
