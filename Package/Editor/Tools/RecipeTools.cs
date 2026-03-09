using System.Collections.Generic;
using System.Linq;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// MCP tools for listing and executing scene recipes.
    /// </summary>
    public static class RecipeTools
    {
        /// <summary>
        /// Lists all available scene recipes with descriptions and parameters.
        /// </summary>
        [MCPTool("list_recipes", "Lists all available scene recipes with descriptions and parameters", Category = "Recipes", ReadOnlyHint = true)]
        public static object ListRecipes()
        {
            var definitions = RecipeRegistry.GetDefinitions();

            var recipeList = definitions.Select(recipeInfo =>
            {
                var parameters = recipeInfo.GetParameterMetadata().Select(parameterMetadata =>
                {
                    var parameterEntry = new Dictionary<string, object>
                    {
                        { "name", parameterMetadata.Name },
                        { "type", parameterMetadata.JsonType },
                        { "required", parameterMetadata.Required }
                    };

                    if (!string.IsNullOrEmpty(parameterMetadata.Description))
                    {
                        parameterEntry["description"] = parameterMetadata.Description;
                    }

                    if (parameterMetadata.ParameterInfo.HasDefaultValue && parameterMetadata.ParameterInfo.DefaultValue != null)
                    {
                        parameterEntry["default"] = parameterMetadata.ParameterInfo.DefaultValue;
                    }

                    return parameterEntry;
                }).ToList();

                return new Dictionary<string, object>
                {
                    { "name", recipeInfo.Name },
                    { "description", recipeInfo.Description },
                    { "parameters", parameters }
                };
            }).ToList();

            return new
            {
                success = true,
                recipes = recipeList
            };
        }

        /// <summary>
        /// Executes a scene recipe by name with optional parameters.
        /// </summary>
        [MCPTool("execute_recipe", "Execute a scene recipe by name", Category = "Recipes", DestructiveHint = true)]
        public static object ExecuteRecipe(
            [MCPParam("name", "Name of the recipe to execute", required: true)] string name,
            [MCPParam("params", "JSON object of recipe parameters")] string paramsJson = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw MCPException.InvalidParams("Recipe name is required.");
            }

            if (!RecipeRegistry.HasRecipe(name))
            {
                throw new MCPException($"Unknown recipe: {name}", MCPErrorCodes.MethodNotFound);
            }

            return RecipeRegistry.InvokeWithJson(name, paramsJson);
        }
    }
}
