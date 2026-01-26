using System;

namespace UnityMCP.Editor
{
    /// <summary>
    /// Provides metadata for a parameter of an MCP tool method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public class MCPParamAttribute : Attribute
    {
        /// <summary>
        /// The name of the parameter as exposed to MCP clients.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// A human-readable description of the parameter.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Whether the parameter is required for tool invocation.
        /// </summary>
        public bool Required { get; }

        /// <summary>
        /// Creates a new MCP parameter attribute.
        /// </summary>
        /// <param name="name">The name of the parameter as exposed to MCP clients.</param>
        /// <param name="description">A human-readable description of the parameter.</param>
        /// <param name="required">Whether the parameter is required for tool invocation.</param>
        public MCPParamAttribute(string name, string description = null, bool required = false)
        {
            Name = name;
            Description = description;
            Required = required;
        }
    }
}
