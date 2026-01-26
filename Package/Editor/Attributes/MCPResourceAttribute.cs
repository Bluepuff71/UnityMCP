using System;

namespace UnityMCP.Editor
{
    /// <summary>
    /// Marks a static method as an MCP resource provider.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class MCPResourceAttribute : Attribute
    {
        /// <summary>
        /// The URI pattern for the resource (e.g., "unity://scene/hierarchy").
        /// </summary>
        public string Uri { get; }

        /// <summary>
        /// A human-readable description of the resource.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Creates a new MCP resource attribute.
        /// </summary>
        /// <param name="uri">The URI pattern for the resource.</param>
        /// <param name="description">A human-readable description of the resource.</param>
        public MCPResourceAttribute(string uri, string description)
        {
            Uri = uri;
            Description = description;
        }
    }
}
