using System;

namespace UnityMCP.Editor
{
    /// <summary>
    /// Marks a static method as an action within a class-level [MCPTool].
    /// Each action becomes a value in the tool's "action" enum parameter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class MCPActionAttribute : Attribute
    {
        /// <summary>
        /// The action name used as the enum value in the "action" parameter.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// A human-readable description of what this action does.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// If true, this action does not modify any state (read-only operation).
        /// </summary>
        public bool ReadOnlyHint { get; set; } = false;

        /// <summary>
        /// If true, this action may perform irreversible or destructive operations.
        /// </summary>
        public bool DestructiveHint { get; set; } = false;

        /// <summary>
        /// If true, calling this action with the same arguments yields the same result.
        /// </summary>
        public bool IdempotentHint { get; set; } = false;

        /// <summary>
        /// If true, this action interacts with external systems beyond the local environment.
        /// </summary>
        public bool OpenWorldHint { get; set; } = false;

        /// <summary>
        /// Creates a new MCP action attribute.
        /// </summary>
        /// <param name="name">The action name used as the enum value.</param>
        public MCPActionAttribute(string name)
        {
            Name = name;
        }
    }
}
