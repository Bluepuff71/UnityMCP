using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Tools
{
    public static class SearchTools
    {
        [MCPTool("search_tools", "Search available tools by name, description, or category. Use with no args for a category overview.", Category = "Editor", ReadOnlyHint = true)]
        public static object SearchAvailableTools(
            [MCPParam("query", "Search names and descriptions")] string query = null,
            [MCPParam("category", "Filter by category")] string category = null)
        {
            bool hasQuery = !string.IsNullOrEmpty(query);
            bool hasCategory = !string.IsNullOrEmpty(category);

            // No args: return compact category summary
            if (!hasQuery && !hasCategory)
            {
                return BuildCategorySummary();
            }

            // With category and/or query: return matching tool details
            return BuildToolDetails(query, category);
        }

        private static string BuildCategorySummary()
        {
            var categoryGroups = ToolRegistry.GetDefinitionsByCategory();

            var summaryBuilder = new StringBuilder();
            summaryBuilder.AppendLine("Available tool categories:");

            foreach (var categoryGroup in categoryGroups)
            {
                var toolNames = categoryGroup.Select(tool => tool.name).ToList();
                int toolCount = toolNames.Count;
                string toolNameList = string.Join(", ", toolNames);
                summaryBuilder.AppendLine($"- {categoryGroup.Key} ({toolCount} tools): {toolNameList}");
            }

            summaryBuilder.Append("Use search_tools with 'category' or 'query' to explore further.");

            return summaryBuilder.ToString();
        }

        private static object BuildToolDetails(string query, string category)
        {
            var allDefinitions = ToolRegistry.GetDefinitions();
            var filteredDefinitions = allDefinitions;

            // Filter by category (case-insensitive)
            if (!string.IsNullOrEmpty(category))
            {
                filteredDefinitions = filteredDefinitions.Where(
                    tool => tool.category != null &&
                            tool.category.ToLowerInvariant() == category.ToLowerInvariant());
            }

            // Filter by query (case-insensitive substring match on name and description)
            if (!string.IsNullOrEmpty(query))
            {
                string lowerQuery = query.ToLowerInvariant();
                filteredDefinitions = filteredDefinitions.Where(
                    tool => (tool.name != null && tool.name.ToLowerInvariant().Contains(lowerQuery)) ||
                            (tool.description != null && tool.description.ToLowerInvariant().Contains(lowerQuery)));
            }

            var matchingTools = filteredDefinitions.ToList();

            var toolEntries = new List<Dictionary<string, object>>();

            foreach (var tool in matchingTools)
            {
                var toolEntry = new Dictionary<string, object>
                {
                    { "name", tool.name },
                    { "description", tool.description },
                    { "category", tool.category ?? "Uncategorized" }
                };

                if (tool.annotations != null)
                {
                    var annotationsDict = new Dictionary<string, object>();

                    if (tool.annotations.readOnlyHint.HasValue)
                        annotationsDict["readOnlyHint"] = tool.annotations.readOnlyHint.Value;
                    if (tool.annotations.destructiveHint.HasValue)
                        annotationsDict["destructiveHint"] = tool.annotations.destructiveHint.Value;
                    if (tool.annotations.idempotentHint.HasValue)
                        annotationsDict["idempotentHint"] = tool.annotations.idempotentHint.Value;
                    if (tool.annotations.openWorldHint.HasValue)
                        annotationsDict["openWorldHint"] = tool.annotations.openWorldHint.Value;
                    if (tool.annotations.title != null)
                        annotationsDict["title"] = tool.annotations.title;

                    if (annotationsDict.Count > 0)
                    {
                        toolEntry["annotations"] = annotationsDict;
                    }
                }

                toolEntries.Add(toolEntry);
            }

            var result = new Dictionary<string, object>
            {
                { "tools", toolEntries }
            };

            return result;
        }
    }
}
