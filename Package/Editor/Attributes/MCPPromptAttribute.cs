using System;

namespace UnityMCP.Editor
{
    /// <summary>
    /// Marks a static method as an MCP prompt provider.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class MCPPromptAttribute : Attribute
    {
        /// <summary>
        /// The name of the prompt as exposed to MCP clients.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// A human-readable description of the prompt.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Creates a new MCP prompt attribute.
        /// </summary>
        /// <param name="name">The name of the prompt.</param>
        /// <param name="description">A human-readable description of the prompt.</param>
        public MCPPromptAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }
}
