using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Registry for discovering and invoking MCP tools marked with [MCPTool] attribute.
    /// </summary>
    public static class ToolRegistry
    {
        private static Dictionary<string, ToolInfo> _tools = new Dictionary<string, ToolInfo>();
        private static bool _isInitialized = false;

        /// <summary>
        /// Gets the number of registered tools.
        /// </summary>
        public static int Count => _tools.Count;

        /// <summary>
        /// Auto-discover tools when the editor loads.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void DiscoverTools()
        {
            RefreshTools();
        }

        /// <summary>
        /// Manually refresh the tool registry. Useful for testing or after loading new assemblies.
        /// </summary>
        public static void RefreshTools()
        {
            _tools.Clear();
            _isInitialized = false;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip system and Unity assemblies for performance
                string assemblyName = assembly.FullName;
                if (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Unity.", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("mscorlib", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("netstandard", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Mono.", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    DiscoverToolsInAssembly(assembly);
                }
                catch (ReflectionTypeLoadException reflectionException)
                {
                    Debug.LogWarning($"[ToolRegistry] Failed to load types from assembly {assembly.GetName().Name}: {reflectionException.Message}");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[ToolRegistry] Error scanning assembly {assembly.GetName().Name}: {exception.Message}");
                }
            }

            _isInitialized = true;
            if (NativeProxy.VerboseLogging) Debug.Log($"[ToolRegistry] Discovered {_tools.Count} MCP tools");
        }

        private static void DiscoverToolsInAssembly(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    try
                    {
                        var toolAttribute = method.GetCustomAttribute<MCPToolAttribute>();
                        if (toolAttribute != null)
                        {
                            if (_tools.ContainsKey(toolAttribute.Name))
                            {
                                Debug.LogWarning($"[ToolRegistry] Duplicate tool name '{toolAttribute.Name}' found in {type.FullName}.{method.Name}. Skipping.");
                                continue;
                            }

                            var toolInfo = new ToolInfo(toolAttribute, method);
                            _tools[toolAttribute.Name] = toolInfo;
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[ToolRegistry] Error processing method {type.FullName}.{method.Name}: {exception.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Gets all tool definitions for the MCP tools/list response.
        /// </summary>
        public static IEnumerable<ToolDefinition> GetDefinitions()
        {
            EnsureInitialized();
            return _tools.Values.Select(toolInfo => toolInfo.ToDefinition());
        }

        /// <summary>
        /// Gets a specific tool definition by name.
        /// </summary>
        public static ToolDefinition GetDefinition(string name)
        {
            EnsureInitialized();
            if (_tools.TryGetValue(name, out var toolInfo))
            {
                return toolInfo.ToDefinition();
            }
            return null;
        }

        /// <summary>
        /// Gets all tool definitions grouped by category and ordered.
        /// </summary>
        public static IEnumerable<IGrouping<string, ToolDefinition>> GetDefinitionsByCategory()
        {
            EnsureInitialized();
            return _tools.Values
                .Select(t => (Category: t.Category, Definition: t.ToDefinition()))
                .GroupBy(t => t.Category, t => t.Definition)
                .OrderBy(g => GetCategoryOrder(g.Key));
        }

        private static int GetCategoryOrder(string category)
        {
            return category switch
            {
                "Scene" => 0,
                "GameObject" => 1,
                "Component" => 2,
                "Asset" => 3,
                "VFX" => 4,
                "Console" => 5,
                "Tests" => 6,
                "Profiler" => 7,
                "Build" => 8,
                "UIToolkit" => 9,
                "Editor" => 10,
                "Debug" => 99,
                "Uncategorized" => 100,
                _ => 50
            };
        }

        /// <summary>
        /// Checks if a tool with the given name exists.
        /// </summary>
        public static bool HasTool(string name)
        {
            EnsureInitialized();
            return _tools.ContainsKey(name);
        }

        /// <summary>
        /// Invokes a tool by name with the given arguments.
        /// </summary>
        /// <param name="name">The name of the tool to invoke.</param>
        /// <param name="arguments">A dictionary of argument names to their JSON-parsed values.</param>
        /// <returns>The result of the tool invocation.</returns>
        /// <exception cref="MCPException">Thrown if the tool is not found or invocation fails.</exception>
        public static object Invoke(string name, Dictionary<string, object> arguments)
        {
            EnsureInitialized();

            if (!_tools.TryGetValue(name, out var toolInfo))
            {
                throw new MCPException($"Unknown tool: {name}", MCPErrorCodes.MethodNotFound);
            }

            return toolInfo.Invoke(arguments);
        }

        /// <summary>
        /// Invokes a tool by name with arguments parsed from a JSON string.
        /// </summary>
        /// <param name="name">The name of the tool to invoke.</param>
        /// <param name="jsonArguments">JSON string containing the arguments.</param>
        /// <returns>The result of the tool invocation.</returns>
        public static object InvokeWithJson(string name, string jsonArguments)
        {
            Dictionary<string, object> arguments;
            if (string.IsNullOrEmpty(jsonArguments))
            {
                arguments = new Dictionary<string, object>();
            }
            else
            {
                var jObject = JObject.Parse(jsonArguments);
                arguments = ConvertJObjectToDictionary(jObject);
            }
            return Invoke(name, arguments);
        }

        private static Dictionary<string, object> ConvertJObjectToDictionary(JObject jObject)
        {
            var result = new Dictionary<string, object>();
            foreach (var property in jObject.Properties())
            {
                result[property.Name] = ConvertJToken(property.Value);
            }
            return result;
        }

        private static object ConvertJToken(JToken token)
        {
            return token.Type switch
            {
                JTokenType.Object => ConvertJObjectToDictionary((JObject)token),
                JTokenType.Array => token.Select(ConvertJToken).ToList(),
                JTokenType.String => token.Value<string>(),
                JTokenType.Integer => token.Value<long>(),
                JTokenType.Float => token.Value<double>(),
                JTokenType.Boolean => token.Value<bool>(),
                JTokenType.Null => null,
                _ => token.ToString()
            };
        }

        private static void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                RefreshTools();
            }
        }
    }

    /// <summary>
    /// Internal class holding metadata and invocation logic for a discovered tool.
    /// </summary>
    internal class ToolInfo
    {
        private readonly MCPToolAttribute _attribute;
        private readonly MethodInfo _method;
        private readonly ParameterInfo[] _parameters;
        private readonly Dictionary<string, ParameterMetadata> _parameterMetadata;

        public string Name => _attribute.Name;
        public string Description => _attribute.Description;
        public string Category => _attribute.Category;

        public ToolInfo(MCPToolAttribute attribute, MethodInfo method)
        {
            _attribute = attribute;
            _method = method;
            _parameters = method.GetParameters();
            _parameterMetadata = new Dictionary<string, ParameterMetadata>();

            BuildParameterMetadata();
        }

        private void BuildParameterMetadata()
        {
            foreach (var parameter in _parameters)
            {
                var mcpParamAttribute = parameter.GetCustomAttribute<MCPParamAttribute>();

                string parameterName = mcpParamAttribute?.Name ?? parameter.Name;
                string parameterDescription = mcpParamAttribute?.Description ?? "";
                bool isRequired = mcpParamAttribute?.Required ?? !parameter.HasDefaultValue;

                _parameterMetadata[parameterName] = new ParameterMetadata
                {
                    Name = parameterName,
                    Description = parameterDescription,
                    Required = isRequired,
                    ParameterInfo = parameter,
                    JsonType = GetJsonSchemaType(parameter.ParameterType)
                };
            }
        }

        /// <summary>
        /// Creates a ToolDefinition for the MCP tools/list response.
        /// </summary>
        public ToolDefinition ToDefinition()
        {
            var inputSchema = new InputSchema();

            foreach (var metadata in _parameterMetadata.Values)
            {
                var propertySchema = CreatePropertySchema(metadata);
                inputSchema.AddProperty(metadata.Name, propertySchema, metadata.Required);
            }

            var definition = new ToolDefinition(_attribute.Name, _attribute.Description, inputSchema);
            definition.category = _attribute.Category;
            return definition;
        }

        private PropertySchema CreatePropertySchema(ParameterMetadata metadata)
        {
            var schema = new PropertySchema
            {
                type = metadata.JsonType,
                description = metadata.Description
            };

            // Add default value if available
            if (metadata.ParameterInfo.HasDefaultValue && metadata.ParameterInfo.DefaultValue != null)
            {
                schema.@default = metadata.ParameterInfo.DefaultValue;
            }

            // Handle array item types
            if (metadata.JsonType == "array")
            {
                var elementType = GetArrayElementType(metadata.ParameterInfo.ParameterType);
                if (elementType != null)
                {
                    schema.items = new PropertySchema
                    {
                        type = GetJsonSchemaType(elementType)
                    };
                }
            }

            return schema;
        }

        /// <summary>
        /// Invokes the tool with the given arguments.
        /// </summary>
        public object Invoke(Dictionary<string, object> arguments)
        {
            arguments = arguments ?? new Dictionary<string, object>();

            var invokeArguments = new object[_parameters.Length];

            for (int parameterIndex = 0; parameterIndex < _parameters.Length; parameterIndex++)
            {
                var parameter = _parameters[parameterIndex];
                var metadata = _parameterMetadata.Values.FirstOrDefault(m => m.ParameterInfo == parameter);

                if (metadata == null)
                {
                    throw new MCPException($"Internal error: Parameter metadata not found for {parameter.Name}", MCPErrorCodes.InternalError);
                }

                string argumentName = metadata.Name;

                if (arguments.TryGetValue(argumentName, out var argumentValue))
                {
                    try
                    {
                        invokeArguments[parameterIndex] = ConvertValue(argumentValue, parameter.ParameterType);
                    }
                    catch (Exception conversionException)
                    {
                        throw new MCPException(
                            $"Failed to convert argument '{argumentName}' to type {parameter.ParameterType.Name}: {conversionException.Message}",
                            MCPErrorCodes.InvalidParams);
                    }
                }
                else if (metadata.Required)
                {
                    throw new MCPException($"Missing required argument: {argumentName}", MCPErrorCodes.InvalidParams);
                }
                else if (parameter.HasDefaultValue)
                {
                    invokeArguments[parameterIndex] = parameter.DefaultValue;
                }
                else
                {
                    invokeArguments[parameterIndex] = GetDefaultValue(parameter.ParameterType);
                }
            }

            try
            {
                return _method.Invoke(null, invokeArguments);
            }
            catch (TargetInvocationException invocationException)
            {
                // Unwrap the inner exception for cleaner error messages
                var innerException = invocationException.InnerException ?? invocationException;

                if (innerException is MCPException)
                {
                    throw innerException;
                }

                throw new MCPException($"Tool execution failed: {innerException.Message}", innerException, MCPErrorCodes.InternalError);
            }
            catch (Exception exception)
            {
                throw new MCPException($"Tool invocation failed: {exception.Message}", exception, MCPErrorCodes.InternalError);
            }
        }

        /// <summary>
        /// Converts a JSON-parsed value to the target type.
        /// </summary>
        private object ConvertValue(object value, Type targetType)
        {
            if (value == null)
            {
                return GetDefaultValue(targetType);
            }

            Type valueType = value.GetType();

            // Handle nullable types
            Type underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                targetType = underlyingType;
            }

            // Direct type match
            if (targetType.IsAssignableFrom(valueType))
            {
                return value;
            }

            // String conversion
            if (targetType == typeof(string))
            {
                return value.ToString();
            }

            // Boolean conversion
            if (targetType == typeof(bool))
            {
                if (value is bool boolValue)
                {
                    return boolValue;
                }
                if (value is string stringValue)
                {
                    return bool.Parse(stringValue);
                }
                return Convert.ToBoolean(value);
            }

            // Integer types
            if (targetType == typeof(int))
            {
                return Convert.ToInt32(value);
            }
            if (targetType == typeof(long))
            {
                return Convert.ToInt64(value);
            }
            if (targetType == typeof(short))
            {
                return Convert.ToInt16(value);
            }
            if (targetType == typeof(byte))
            {
                return Convert.ToByte(value);
            }

            // Floating point types
            if (targetType == typeof(float))
            {
                return Convert.ToSingle(value);
            }
            if (targetType == typeof(double))
            {
                return Convert.ToDouble(value);
            }
            if (targetType == typeof(decimal))
            {
                return Convert.ToDecimal(value);
            }

            // Enum conversion
            if (targetType.IsEnum)
            {
                if (value is string enumString)
                {
                    return Enum.Parse(targetType, enumString, ignoreCase: true);
                }
                return Enum.ToObject(targetType, Convert.ToInt32(value));
            }

            // Array/List conversion
            if (targetType.IsArray || (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>)))
            {
                return ConvertToArrayOrList(value, targetType);
            }

            // For complex objects, try JSON serialization via Unity
            if (value is string jsonString)
            {
                return JsonUtility.FromJson(jsonString, targetType);
            }

            // Last resort: direct conversion
            return Convert.ChangeType(value, targetType);
        }

        private object ConvertToArrayOrList(object value, Type targetType)
        {
            if (value is not System.Collections.IList sourceList)
            {
                throw new InvalidCastException($"Cannot convert {value.GetType().Name} to array or list");
            }

            Type elementType;
            bool isArray = targetType.IsArray;

            if (isArray)
            {
                elementType = targetType.GetElementType();
            }
            else
            {
                elementType = targetType.GetGenericArguments()[0];
            }

            var convertedList = new List<object>();
            foreach (var item in sourceList)
            {
                convertedList.Add(ConvertValue(item, elementType));
            }

            if (isArray)
            {
                var resultArray = Array.CreateInstance(elementType, convertedList.Count);
                for (int arrayIndex = 0; arrayIndex < convertedList.Count; arrayIndex++)
                {
                    resultArray.SetValue(convertedList[arrayIndex], arrayIndex);
                }
                return resultArray;
            }
            else
            {
                var resultList = Activator.CreateInstance(targetType) as System.Collections.IList;
                foreach (var item in convertedList)
                {
                    resultList.Add(item);
                }
                return resultList;
            }
        }

        private static object GetDefaultValue(Type type)
        {
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }
            return null;
        }

        private static string GetJsonSchemaType(Type type)
        {
            // Handle nullable types
            Type underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                type = underlyingType;
            }

            if (type == typeof(string))
            {
                return "string";
            }
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            {
                return "integer";
            }
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                return "number";
            }
            if (type == typeof(bool))
            {
                return "boolean";
            }
            if (type.IsArray || (type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)))
            {
                return "array";
            }

            return "object";
        }

        private static Type GetArrayElementType(Type arrayType)
        {
            if (arrayType.IsArray)
            {
                return arrayType.GetElementType();
            }
            if (arrayType.IsGenericType)
            {
                var genericArgs = arrayType.GetGenericArguments();
                if (genericArgs.Length > 0)
                {
                    return genericArgs[0];
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Internal metadata for a tool parameter.
    /// </summary>
    internal class ParameterMetadata
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public string JsonType { get; set; }
        public ParameterInfo ParameterInfo { get; set; }
    }
}
