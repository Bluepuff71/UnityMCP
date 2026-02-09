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
        /// Valid enum values for string parameters. When set, these values are included in the JSON Schema.
        /// </summary>
        public string[] Enum { get; set; }

        /// <summary>
        /// Minimum value for numeric parameters. Uses double.NaN to indicate no constraint.
        /// </summary>
        public double Minimum { get; set; } = double.NaN;

        /// <summary>
        /// Maximum value for numeric parameters. Uses double.NaN to indicate no constraint.
        /// </summary>
        public double Maximum { get; set; } = double.NaN;

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
