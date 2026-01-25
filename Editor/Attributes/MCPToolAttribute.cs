using System;

namespace UnityMCP.Editor
{
    /// <summary>
    /// Marks a static method as an MCP tool that can be invoked by clients.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class MCPToolAttribute : Attribute
    {
        /// <summary>
        /// The unique name of the tool used for invocation.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// A human-readable description of what the tool does.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// The category used to group related tools together.
        /// </summary>
        public string Category { get; set; } = "Uncategorized";

        /// <summary>
        /// Creates a new MCP tool attribute.
        /// </summary>
        /// <param name="name">The unique name of the tool used for invocation.</param>
        /// <param name="description">A human-readable description of what the tool does.</param>
        public MCPToolAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }
}
