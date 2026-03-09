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
    /// Supports both single-method tools ([MCPTool] on a method) and action-based tools
    /// ([MCPTool] on a class with [MCPAction] methods).
    /// </summary>
    public static class ToolRegistry
    {
        private static Dictionary<string, IToolInfo> _tools = new Dictionary<string, IToolInfo>();
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the number of registered tools.
        /// </summary>
        public static int Count { get { lock (_lock) { return _tools.Count; } } }

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
            lock (_lock)
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
                if (MCPProxy.VerboseLogging) Debug.Log($"[ToolRegistry] Discovered {_tools.Count} MCP tools");
            }
        }

        private static void DiscoverToolsInAssembly(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                try
                {
                    // Check for class-level [MCPTool] (action-based tool)
                    var classToolAttribute = type.GetCustomAttribute<MCPToolAttribute>();
                    if (classToolAttribute != null)
                    {
                        DiscoverActionTool(type, classToolAttribute);
                        continue; // Don't also scan methods on this class for method-level tools
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[ToolRegistry] Error processing class {type.FullName}: {exception.Message}");
                }

                // Check for method-level [MCPTool] (single-method tool)
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

                            var toolInfo = new MethodToolInfo(toolAttribute, method);
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

        private static void DiscoverActionTool(Type type, MCPToolAttribute classAttribute)
        {
            var actionMethods = new Dictionary<string, ActionInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                var actionAttribute = method.GetCustomAttribute<MCPActionAttribute>();
                if (actionAttribute == null) continue;

                string normalizedName = actionAttribute.Name.ToLowerInvariant();
                if (actionMethods.ContainsKey(normalizedName))
                {
                    Debug.LogWarning($"[ToolRegistry] Duplicate action name '{actionAttribute.Name}' in {type.FullName}. Skipping.");
                    continue;
                }

                actionMethods[normalizedName] = new ActionInfo(actionAttribute, method);
            }

            if (actionMethods.Count == 0)
            {
                Debug.LogWarning($"[ToolRegistry] Class {type.FullName} has [MCPTool] but no [MCPAction] methods. Skipping.");
                return;
            }

            if (_tools.ContainsKey(classAttribute.Name))
            {
                Debug.LogWarning($"[ToolRegistry] Duplicate tool name '{classAttribute.Name}' found on class {type.FullName}. Skipping.");
                return;
            }

            var actionToolInfo = new ActionToolInfo(classAttribute, actionMethods);
            _tools[classAttribute.Name] = actionToolInfo;
        }

        /// <summary>
        /// Gets all tool definitions for the MCP tools/list response.
        /// </summary>
        public static IEnumerable<ToolDefinition> GetDefinitions()
        {
            EnsureInitialized();
            List<IToolInfo> snapshot;
            lock (_lock)
            {
                snapshot = _tools.Values.ToList();
            }
            return snapshot.Select(toolInfo => toolInfo.ToDefinition());
        }

        /// <summary>
        /// Gets a specific tool definition by name.
        /// </summary>
        public static ToolDefinition GetDefinition(string name)
        {
            EnsureInitialized();
            lock (_lock)
            {
                if (_tools.TryGetValue(name, out var toolInfo))
                {
                    return toolInfo.ToDefinition();
                }
                return null;
            }
        }

        /// <summary>
        /// Gets all tool definitions grouped by category and ordered.
        /// </summary>
        public static IEnumerable<IGrouping<string, ToolDefinition>> GetDefinitionsByCategory()
        {
            EnsureInitialized();
            List<IToolInfo> snapshot;
            lock (_lock)
            {
                snapshot = _tools.Values.ToList();
            }
            return snapshot
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
            lock (_lock)
            {
                return _tools.ContainsKey(name);
            }
        }

        /// <summary>
        /// Invokes a tool by name with the given arguments.
        /// </summary>
        public static object Invoke(string name, Dictionary<string, object> arguments)
        {
            EnsureInitialized();

            IToolInfo toolInfo;
            lock (_lock)
            {
                if (!_tools.TryGetValue(name, out toolInfo))
                {
                    throw new MCPException($"Unknown tool: {name}", MCPErrorCodes.MethodNotFound);
                }
            }

            return toolInfo.Invoke(arguments);
        }

        /// <summary>
        /// Invokes a tool by name with arguments parsed from a JSON string.
        /// </summary>
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

        internal static Dictionary<string, object> ConvertJObjectToDictionary(JObject jObject)
        {
            var result = new Dictionary<string, object>();
            foreach (var property in jObject.Properties())
            {
                result[property.Name] = ConvertJToken(property.Value);
            }
            return result;
        }

        internal static object ConvertJToken(JToken token)
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
            lock (_lock)
            {
                if (!_isInitialized)
                {
                    RefreshTools();
                }
            }
        }
    }

    /// <summary>
    /// Interface for tool info implementations (method-based and action-based).
    /// </summary>
    internal interface IToolInfo
    {
        string Name { get; }
        string Description { get; }
        string Category { get; }
        ToolDefinition ToDefinition();
        object Invoke(Dictionary<string, object> arguments);
    }

    /// <summary>
    /// Holds metadata and invocation logic for a single-method tool ([MCPTool] on a method).
    /// </summary>
    internal class MethodToolInfo : IToolInfo
    {
        private readonly MCPToolAttribute _attribute;
        private readonly MethodInfo _method;
        private readonly ParameterInfo[] _parameters;
        private readonly Dictionary<string, ParameterMetadata> _parameterMetadata;

        public string Name => _attribute.Name;
        public string Description => _attribute.Description;
        public string Category => _attribute.Category;

        public MethodToolInfo(MCPToolAttribute attribute, MethodInfo method)
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
                    JsonType = ToolSchemaUtils.GetJsonSchemaType(parameter.ParameterType),
                    McpParamAttribute = mcpParamAttribute
                };
            }
        }

        public ToolDefinition ToDefinition()
        {
            var inputSchema = new InputSchema();

            foreach (var metadata in _parameterMetadata.Values)
            {
                var propertySchema = ToolSchemaUtils.CreatePropertySchema(metadata);
                inputSchema.AddProperty(metadata.Name, propertySchema, metadata.Required);
            }

            var definition = new ToolDefinition(_attribute.Name, _attribute.Description, inputSchema);
            definition.category = _attribute.Category;

            // Only include annotations if at least one hint was explicitly set
            bool hasAnnotations = _attribute.ReadOnlyHint || _attribute.DestructiveHint ||
                                  _attribute.IdempotentHint || _attribute.OpenWorldHint ||
                                  _attribute.Title != null;
            if (hasAnnotations)
            {
                definition.annotations = new ToolAnnotations();
                if (_attribute.ReadOnlyHint) definition.annotations.readOnlyHint = true;
                if (_attribute.DestructiveHint) definition.annotations.destructiveHint = true;
                if (_attribute.IdempotentHint) definition.annotations.idempotentHint = true;
                if (_attribute.OpenWorldHint) definition.annotations.openWorldHint = true;
                if (_attribute.Title != null) definition.annotations.title = _attribute.Title;
            }

            return definition;
        }

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
                        invokeArguments[parameterIndex] = ToolSchemaUtils.ConvertValue(argumentValue, parameter.ParameterType);
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
                    invokeArguments[parameterIndex] = ToolSchemaUtils.GetDefaultValue(parameter.ParameterType);
                }
            }

            try
            {
                return _method.Invoke(null, invokeArguments);
            }
            catch (TargetInvocationException invocationException)
            {
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
    }

    /// <summary>
    /// Metadata for a single action within an action-based tool.
    /// </summary>
    internal class ActionInfo
    {
        public MCPActionAttribute Attribute { get; }
        public MethodInfo Method { get; }
        public ParameterInfo[] Parameters { get; }
        public Dictionary<string, ParameterMetadata> ParameterMetadata { get; }

        public ActionInfo(MCPActionAttribute attribute, MethodInfo method)
        {
            Attribute = attribute;
            Method = method;
            Parameters = method.GetParameters();
            ParameterMetadata = new Dictionary<string, ParameterMetadata>();

            foreach (var parameter in Parameters)
            {
                var mcpParamAttribute = parameter.GetCustomAttribute<MCPParamAttribute>();

                string parameterName = mcpParamAttribute?.Name ?? parameter.Name;
                string parameterDescription = mcpParamAttribute?.Description ?? "";
                bool isRequired = mcpParamAttribute?.Required ?? !parameter.HasDefaultValue;

                ParameterMetadata[parameterName] = new ParameterMetadata
                {
                    Name = parameterName,
                    Description = parameterDescription,
                    Required = isRequired,
                    ParameterInfo = parameter,
                    JsonType = ToolSchemaUtils.GetJsonSchemaType(parameter.ParameterType),
                    McpParamAttribute = mcpParamAttribute
                };
            }
        }
    }

    /// <summary>
    /// Holds metadata and invocation logic for an action-based tool ([MCPTool] on a class).
    /// </summary>
    internal class ActionToolInfo : IToolInfo
    {
        private readonly MCPToolAttribute _attribute;
        private readonly Dictionary<string, ActionInfo> _actions;

        public string Name => _attribute.Name;
        public string Description => _attribute.Description;
        public string Category => _attribute.Category;

        public ActionToolInfo(MCPToolAttribute attribute, Dictionary<string, ActionInfo> actions)
        {
            _attribute = attribute;
            _actions = actions;
        }

        public ToolDefinition ToDefinition()
        {
            var inputSchema = new InputSchema();

            // Add "action" parameter as required enum
            var actionNames = _actions.Keys.ToList();
            var actionDescriptions = new List<string>();
            foreach (var actionName in actionNames)
            {
                var actionInfo = _actions[actionName];
                if (!string.IsNullOrEmpty(actionInfo.Attribute.Description))
                {
                    actionDescriptions.Add($"{actionName}: {actionInfo.Attribute.Description}");
                }
            }

            string actionDescription = "Action to perform";
            if (actionDescriptions.Count > 0)
            {
                actionDescription += ": " + string.Join(", ", actionDescriptions);
            }

            inputSchema.AddProperty("action", new PropertySchema
            {
                type = "string",
                description = actionDescription,
                @enum = actionNames
            }, isRequired: true);

            // Collect all parameters from all actions, merging duplicates
            var allParams = new Dictionary<string, MergedParamInfo>();

            foreach (var kvp in _actions)
            {
                string actionName = kvp.Key;
                var actionInfo = kvp.Value;

                foreach (var paramMeta in actionInfo.ParameterMetadata.Values)
                {
                    if (!allParams.TryGetValue(paramMeta.Name, out var merged))
                    {
                        merged = new MergedParamInfo
                        {
                            Metadata = paramMeta,
                            UsedByActions = new List<string>()
                        };
                        allParams[paramMeta.Name] = merged;
                    }
                    merged.UsedByActions.Add(actionName);
                }
            }

            int totalActions = _actions.Count;

            foreach (var kvp in allParams)
            {
                var merged = kvp.Value;
                var propertySchema = ToolSchemaUtils.CreatePropertySchema(merged.Metadata);

                // If param is only used by some actions, annotate the description
                if (merged.UsedByActions.Count < totalActions)
                {
                    string actionList = string.Join(", ", merged.UsedByActions.Select(a => $"'{a}'"));
                    string suffix = merged.UsedByActions.Count == 1
                        ? $" (for action={actionList})"
                        : $" (for actions: {actionList})";
                    propertySchema.description = (propertySchema.description ?? "") + suffix;
                }

                // In the unified schema, action-specific params are never globally required
                // (they're validated per-action at invoke time)
                inputSchema.AddProperty(merged.Metadata.Name, propertySchema, isRequired: false);
            }

            var definition = new ToolDefinition(_attribute.Name, _attribute.Description, inputSchema);
            definition.category = _attribute.Category;

            // Resolve tool-level annotations from per-action annotations
            bool anyDestructive = false;
            bool allReadOnly = true;
            bool allIdempotent = true;
            bool anyOpenWorld = false;
            var destructiveActionNames = new List<string>();

            foreach (var kvp in _actions)
            {
                var actionAttr = kvp.Value.Attribute;
                if (actionAttr.DestructiveHint)
                {
                    anyDestructive = true;
                    destructiveActionNames.Add(kvp.Key);
                }
                if (!actionAttr.ReadOnlyHint) allReadOnly = false;
                if (!actionAttr.IdempotentHint) allIdempotent = false;
                if (actionAttr.OpenWorldHint) anyOpenWorld = true;
            }

            // Also consider class-level defaults
            if (_attribute.DestructiveHint) anyDestructive = true;
            if (_attribute.OpenWorldHint) anyOpenWorld = true;

            bool hasAnnotations = anyDestructive || allReadOnly || allIdempotent || anyOpenWorld ||
                                  _attribute.Title != null;
            if (hasAnnotations)
            {
                definition.annotations = new ToolAnnotations();
                if (anyDestructive) definition.annotations.destructiveHint = true;
                if (allReadOnly) definition.annotations.readOnlyHint = true;
                if (allIdempotent) definition.annotations.idempotentHint = true;
                if (anyOpenWorld) definition.annotations.openWorldHint = true;
                if (_attribute.Title != null) definition.annotations.title = _attribute.Title;
            }

            // Set destructive actions for targeted checkpoint nudge
            if (destructiveActionNames.Count > 0)
            {
                definition.destructiveActions = destructiveActionNames;
            }

            return definition;
        }

        public object Invoke(Dictionary<string, object> arguments)
        {
            arguments = arguments ?? new Dictionary<string, object>();

            // Extract action name
            if (!arguments.TryGetValue("action", out var actionValue) || actionValue == null)
            {
                throw new MCPException("Missing required argument: action", MCPErrorCodes.InvalidParams);
            }

            string actionName = actionValue.ToString().ToLowerInvariant().Trim();

            if (!_actions.TryGetValue(actionName, out var actionInfo))
            {
                string validActions = string.Join(", ", _actions.Keys);
                throw new MCPException($"Unknown action: '{actionName}'. Valid actions: {validActions}", MCPErrorCodes.InvalidParams);
            }

            // Resolve parameters for this specific action
            var invokeArguments = new object[actionInfo.Parameters.Length];

            for (int parameterIndex = 0; parameterIndex < actionInfo.Parameters.Length; parameterIndex++)
            {
                var parameter = actionInfo.Parameters[parameterIndex];
                var metadata = actionInfo.ParameterMetadata.Values.FirstOrDefault(m => m.ParameterInfo == parameter);

                if (metadata == null)
                {
                    throw new MCPException($"Internal error: Parameter metadata not found for {parameter.Name}", MCPErrorCodes.InternalError);
                }

                string argumentName = metadata.Name;

                if (arguments.TryGetValue(argumentName, out var argumentValue))
                {
                    try
                    {
                        invokeArguments[parameterIndex] = ToolSchemaUtils.ConvertValue(argumentValue, parameter.ParameterType);
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
                    invokeArguments[parameterIndex] = ToolSchemaUtils.GetDefaultValue(parameter.ParameterType);
                }
            }

            try
            {
                return actionInfo.Method.Invoke(null, invokeArguments);
            }
            catch (TargetInvocationException invocationException)
            {
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
        /// Helper for tracking merged parameter info across actions.
        /// </summary>
        private class MergedParamInfo
        {
            public ParameterMetadata Metadata;
            public List<string> UsedByActions;
        }
    }

    /// <summary>
    /// Shared utilities for building JSON schemas and converting values.
    /// Used by both MethodToolInfo and ActionToolInfo.
    /// </summary>
    internal static class ToolSchemaUtils
    {
        public static PropertySchema CreatePropertySchema(ParameterMetadata metadata)
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

            // Check MCPParamAttribute for enum values
            if (metadata.McpParamAttribute?.Enum != null && metadata.McpParamAttribute.Enum.Length > 0)
            {
                schema.@enum = new List<string>(metadata.McpParamAttribute.Enum);
            }

            // Check MCPParamAttribute for minimum/maximum
            if (metadata.McpParamAttribute != null)
            {
                if (!double.IsNaN(metadata.McpParamAttribute.Minimum))
                {
                    schema.minimum = metadata.McpParamAttribute.Minimum;
                }
                if (!double.IsNaN(metadata.McpParamAttribute.Maximum))
                {
                    schema.maximum = metadata.McpParamAttribute.Maximum;
                }
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

        public static object ConvertValue(object value, Type targetType)
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
                if (value is bool boolValue) return boolValue;
                if (value is string stringValue) return bool.Parse(stringValue);
                return Convert.ToBoolean(value);
            }

            // Integer types
            if (targetType == typeof(int)) return Convert.ToInt32(value);
            if (targetType == typeof(long)) return Convert.ToInt64(value);
            if (targetType == typeof(short)) return Convert.ToInt16(value);
            if (targetType == typeof(byte)) return Convert.ToByte(value);

            // Floating point types
            if (targetType == typeof(float)) return Convert.ToSingle(value);
            if (targetType == typeof(double)) return Convert.ToDouble(value);
            if (targetType == typeof(decimal)) return Convert.ToDecimal(value);

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

        private static object ConvertToArrayOrList(object value, Type targetType)
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

        public static object GetDefaultValue(Type type)
        {
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }
            return null;
        }

        public static string GetJsonSchemaType(Type type)
        {
            // Handle nullable types
            Type underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                type = underlyingType;
            }

            if (type == typeof(string)) return "string";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type == typeof(bool)) return "boolean";
            if (type.IsArray || (type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))) return "array";

            return "object";
        }

        public static Type GetArrayElementType(Type arrayType)
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
        public MCPParamAttribute McpParamAttribute { get; set; }
    }
}
