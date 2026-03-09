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
    /// Registry for discovering and invoking MCP recipes marked with [MCPRecipe] attribute.
    /// </summary>
    public static class RecipeRegistry
    {
        private static Dictionary<string, RecipeInfo> _recipes = new Dictionary<string, RecipeInfo>();
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the number of registered recipes.
        /// </summary>
        public static int Count { get { lock (_lock) { return _recipes.Count; } } }

        /// <summary>
        /// Auto-discover recipes when the editor loads.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void DiscoverRecipes()
        {
            RefreshRecipes();
        }

        /// <summary>
        /// Manually refresh the recipe registry. Useful for testing or after loading new assemblies.
        /// </summary>
        public static void RefreshRecipes()
        {
            lock (_lock)
            {
                _recipes.Clear();
                _isInitialized = false;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
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
                        DiscoverRecipesInAssembly(assembly);
                    }
                    catch (ReflectionTypeLoadException reflectionException)
                    {
                        Debug.LogWarning($"[RecipeRegistry] Failed to load types from assembly {assembly.GetName().Name}: {reflectionException.Message}");
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[RecipeRegistry] Error scanning assembly {assembly.GetName().Name}: {exception.Message}");
                    }
                }

                _isInitialized = true;
                if (MCPProxy.VerboseLogging) Debug.Log($"[RecipeRegistry] Discovered {_recipes.Count} MCP recipes");
            }
        }

        private static void DiscoverRecipesInAssembly(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    try
                    {
                        var recipeAttribute = method.GetCustomAttribute<MCPRecipeAttribute>();
                        if (recipeAttribute != null)
                        {
                            if (_recipes.ContainsKey(recipeAttribute.Name))
                            {
                                Debug.LogWarning($"[RecipeRegistry] Duplicate recipe name '{recipeAttribute.Name}' found in {type.FullName}.{method.Name}. Skipping.");
                                continue;
                            }

                            var recipeInfo = new RecipeInfo(recipeAttribute, method);
                            _recipes[recipeAttribute.Name] = recipeInfo;
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[RecipeRegistry] Error processing method {type.FullName}.{method.Name}: {exception.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Gets all recipe definitions for listing.
        /// </summary>
        public static IEnumerable<RecipeInfo> GetDefinitions()
        {
            EnsureInitialized();
            List<RecipeInfo> snapshot;
            lock (_lock)
            {
                snapshot = _recipes.Values.ToList();
            }
            return snapshot;
        }

        /// <summary>
        /// Checks if a recipe with the given name exists.
        /// </summary>
        public static bool HasRecipe(string name)
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _recipes.ContainsKey(name);
            }
        }

        /// <summary>
        /// Invokes a recipe by name with the given arguments.
        /// </summary>
        /// <param name="name">The name of the recipe to invoke.</param>
        /// <param name="arguments">A dictionary of argument names to their values.</param>
        /// <returns>The result of the recipe invocation.</returns>
        /// <exception cref="MCPException">Thrown if the recipe is not found or invocation fails.</exception>
        public static object Invoke(string name, Dictionary<string, object> arguments)
        {
            EnsureInitialized();

            RecipeInfo recipeInfo;
            lock (_lock)
            {
                if (!_recipes.TryGetValue(name, out recipeInfo))
                {
                    throw new MCPException($"Unknown recipe: {name}", MCPErrorCodes.MethodNotFound);
                }
            }

            return recipeInfo.Invoke(arguments);
        }

        /// <summary>
        /// Invokes a recipe by name with arguments parsed from a JSON string.
        /// </summary>
        /// <param name="name">The name of the recipe to invoke.</param>
        /// <param name="jsonArguments">JSON string containing the arguments, or null.</param>
        /// <returns>The result of the recipe invocation.</returns>
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
            lock (_lock)
            {
                if (!_isInitialized)
                {
                    RefreshRecipes();
                }
            }
        }
    }

    /// <summary>
    /// Internal class holding metadata and invocation logic for a discovered recipe.
    /// </summary>
    public class RecipeInfo
    {
        private readonly MCPRecipeAttribute _attribute;
        private readonly MethodInfo _method;
        private readonly ParameterInfo[] _parameters;
        private readonly Dictionary<string, RecipeParameterMetadata> _parameterMetadata;

        public string Name => _attribute.Name;
        public string Description => _attribute.Description;

        public RecipeInfo(MCPRecipeAttribute attribute, MethodInfo method)
        {
            _attribute = attribute;
            _method = method;
            _parameters = method.GetParameters();
            _parameterMetadata = new Dictionary<string, RecipeParameterMetadata>();

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

                _parameterMetadata[parameterName] = new RecipeParameterMetadata
                {
                    Name = parameterName,
                    Description = parameterDescription,
                    Required = isRequired,
                    ParameterInfo = parameter,
                    JsonType = GetJsonSchemaType(parameter.ParameterType),
                    McpParamAttribute = mcpParamAttribute
                };
            }
        }

        /// <summary>
        /// Gets the parameter metadata for listing recipe parameters.
        /// </summary>
        public IEnumerable<RecipeParameterMetadata> GetParameterMetadata()
        {
            return _parameterMetadata.Values;
        }

        /// <summary>
        /// Invokes the recipe with the given arguments.
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
                var innerException = invocationException.InnerException ?? invocationException;
                if (innerException is MCPException)
                {
                    throw innerException;
                }
                throw new MCPException($"Recipe execution failed: {innerException.Message}", innerException, MCPErrorCodes.InternalError);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new MCPException($"Recipe invocation failed: {exception.Message}", exception, MCPErrorCodes.InternalError);
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

            // Floating point types
            if (targetType == typeof(float))
            {
                return Convert.ToSingle(value);
            }
            if (targetType == typeof(double))
            {
                return Convert.ToDouble(value);
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

            // Last resort: direct conversion
            return Convert.ChangeType(value, targetType);
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

            return "object";
        }
    }

    /// <summary>
    /// Internal metadata for a recipe parameter.
    /// </summary>
    public class RecipeParameterMetadata
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public string JsonType { get; set; }
        public ParameterInfo ParameterInfo { get; set; }
        public MCPParamAttribute McpParamAttribute { get; set; }
    }
}
