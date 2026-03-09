using System;

namespace UnityMCP.Editor
{
    /// <summary>
    /// Marks a static method as an MCP recipe that creates predefined scene setups.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class MCPRecipeAttribute : Attribute
    {
        /// <summary>
        /// The unique name of the recipe used for invocation.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// A human-readable description of what the recipe creates.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Creates a new MCP recipe attribute.
        /// </summary>
        /// <param name="name">The unique name of the recipe.</param>
        /// <param name="description">A human-readable description of the recipe.</param>
        public MCPRecipeAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }
}
