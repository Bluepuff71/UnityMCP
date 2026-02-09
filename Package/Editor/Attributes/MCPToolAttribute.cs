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
        /// If true, the tool does not modify any state (read-only operation).
        /// </summary>
        public bool ReadOnlyHint { get; set; } = false;

        /// <summary>
        /// If true, the tool may perform irreversible or destructive operations.
        /// </summary>
        public bool DestructiveHint { get; set; } = false;

        /// <summary>
        /// If true, calling the tool with the same arguments yields the same result.
        /// </summary>
        public bool IdempotentHint { get; set; } = false;

        /// <summary>
        /// If true, the tool interacts with external systems beyond the local environment.
        /// </summary>
        public bool OpenWorldHint { get; set; } = false;

        /// <summary>
        /// An optional human-readable display title for the tool.
        /// </summary>
        public string Title { get; set; } = null;

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
