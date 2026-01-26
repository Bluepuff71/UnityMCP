using System.Linq;
using UnityEditorInternal;

namespace UnityMCP.Editor.Resources.Project
{
    /// <summary>
    /// Resource provider for project tags.
    /// </summary>
    public static class Tags
    {
        /// <summary>
        /// Gets all tags defined in the project.
        /// </summary>
        /// <returns>Object containing tag information.</returns>
        [MCPResource("project://tags", "Project tags")]
        public static object Get()
        {
            // Get all tags defined in the project
            string[] allTags = InternalEditorUtility.tags;

            // Built-in tags that Unity provides by default
            string[] builtInTags = new[] { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };

            // Separate built-in from user-defined tags
            var categorizedTags = allTags.Select(tag => new
            {
                name = tag,
                isBuiltIn = builtInTags.Contains(tag)
            }).ToArray();

            var userDefinedTags = categorizedTags
                .Where(tag => !tag.isBuiltIn)
                .Select(tag => tag.name)
                .ToArray();

            return new
            {
                count = allTags.Length,
                userDefinedCount = userDefinedTags.Length,
                builtInCount = allTags.Length - userDefinedTags.Length,
                tags = allTags,
                userDefinedTags = userDefinedTags,
                categorized = categorizedTags
            };
        }
    }
}
