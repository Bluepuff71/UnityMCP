using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Handles MCP (Model Context Protocol) JSON-RPC requests.
    /// Provides tool discovery and invocation for AI assistants.
    /// HTTP transport is handled by the native proxy; this class only handles request routing and response building.
    /// </summary>
    public class MCPServer
    {
        private static MCPServer _instance;
        private static readonly object InstanceLock = new object();

        private int _port = 8080;
        private const string ServerName = "UnityMCP";
        internal const string ServerVersion = "1.6.2";
        private const int MainThreadTimeoutSeconds = 30;

        /// <summary>
        /// Gets the singleton instance of the MCP server.
        /// </summary>
        public static MCPServer Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MCPServer();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Gets or sets the port the server listens on.
        /// </summary>
        public int Port
        {
            get => _port;
            set => _port = value;
        }

        #region JSON-RPC Response Builders

        private JObject CreateSuccessResponse(JToken result, string requestId)
        {
            var response = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["result"] = result,
            };

            if (requestId != null)
            {
                response["id"] = requestId;
            }

            return response;
        }

        private JObject CreateErrorResponse(int code, string message, string requestId, JToken data = null)
        {
            var errorObject = new JObject
            {
                ["code"] = code,
                ["message"] = message
            };

            if (data != null)
            {
                errorObject["data"] = data;
            }

            var response = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["error"] = errorObject
            };

            if (requestId != null)
            {
                response["id"] = requestId;
            }

            return response;
        }

        #endregion

        #region Public API for Native Proxy

        /// <summary>
        /// Handles a raw JSON-RPC request. Called from MCPProxy.
        /// This method is synchronous and runs on Unity's main thread.
        /// </summary>
        /// <param name="jsonRequest">The raw JSON-RPC request string.</param>
        /// <returns>The JSON-RPC response string.</returns>
        public string HandleRequest(string jsonRequest)
        {
            try
            {
                var requestObject = JObject.Parse(jsonRequest);
                string requestId = requestObject["id"]?.ToString();
                string method = requestObject["method"]?.ToString();

                if (string.IsNullOrEmpty(method))
                {
                    return CreateErrorResponse(MCPErrorCodes.InvalidRequest, "Missing 'method' field", requestId)
                        .ToString(Formatting.None);
                }

                JToken paramsToken = requestObject["params"];

                JObject response = method switch
                {
                    "initialize" => HandleInitialize(requestId),
                    "tools/list" => HandleToolsList(requestId),
                    "tools/call" => HandleToolsCall(paramsToken, requestId),
                    "resources/list" => HandleResourcesList(requestId),
                    "resources/read" => HandleResourcesRead(paramsToken, requestId),
                    "prompts/list" => HandlePromptsList(requestId),
                    "prompts/get" => HandlePromptsGet(paramsToken, requestId),
                    _ => CreateErrorResponse(MCPErrorCodes.MethodNotFound, $"Method not found: {method}", requestId)
                };

                return response.ToString(Formatting.None);
            }
            catch (JsonException jsonException)
            {
                return CreateErrorResponse(MCPErrorCodes.ParseError, $"Parse error: {jsonException.Message}", null)
                    .ToString(Formatting.None);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MCPServer] Error handling request: {exception}");
                return CreateErrorResponse(MCPErrorCodes.InternalError, $"Internal error: {exception.Message}", null)
                    .ToString(Formatting.None);
            }
        }

        #endregion

        #region MCP Method Handlers

        private JObject HandleInitialize(string requestId)
        {
            var result = new JObject
            {
                ["protocolVersion"] = "2025-03-26",
                ["capabilities"] = new JObject
                {
                    ["tools"] = new JObject(),
                    ["resources"] = new JObject(),
                    ["prompts"] = new JObject()
                },
                ["serverInfo"] = new JObject
                {
                    ["name"] = ServerName,
                    ["version"] = ServerVersion
                }
            };

            return CreateSuccessResponse(result, requestId);
        }

        private JObject HandleToolsList(string requestId)
        {
            var toolDefinitions = ToolRegistry.GetDefinitions().ToList();
            var toolsArray = new JArray();

            foreach (var tool in toolDefinitions)
            {
                var toolObject = new JObject
                {
                    ["name"] = tool.name,
                    ["description"] = tool.description,
                    ["inputSchema"] = SerializeInputSchema(tool.inputSchema)
                };

                if (tool.annotations != null)
                {
                    var annotationsObject = new JObject();
                    if (tool.annotations.readOnlyHint.HasValue)
                        annotationsObject["readOnlyHint"] = tool.annotations.readOnlyHint.Value;
                    if (tool.annotations.destructiveHint.HasValue)
                        annotationsObject["destructiveHint"] = tool.annotations.destructiveHint.Value;
                    if (tool.annotations.idempotentHint.HasValue)
                        annotationsObject["idempotentHint"] = tool.annotations.idempotentHint.Value;
                    if (tool.annotations.openWorldHint.HasValue)
                        annotationsObject["openWorldHint"] = tool.annotations.openWorldHint.Value;
                    if (tool.annotations.title != null)
                        annotationsObject["title"] = tool.annotations.title;
                    if (annotationsObject.Count > 0)
                        toolObject["annotations"] = annotationsObject;
                }

                toolsArray.Add(toolObject);
            }

            var result = new JObject
            {
                ["tools"] = toolsArray
            };

            return CreateSuccessResponse(result, requestId);
        }

        private JObject SerializeInputSchema(InputSchema inputSchema)
        {
            if (inputSchema == null)
            {
                return new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject(),
                    ["required"] = new JArray()
                };
            }

            var propertiesObject = new JObject();
            foreach (var property in inputSchema.properties)
            {
                propertiesObject[property.Key] = SerializePropertySchema(property.Value);
            }

            var requiredArray = new JArray();
            foreach (var required in inputSchema.required)
            {
                requiredArray.Add(required);
            }

            return new JObject
            {
                ["type"] = inputSchema.type ?? "object",
                ["properties"] = propertiesObject,
                ["required"] = requiredArray
            };
        }

        private JObject SerializePropertySchema(PropertySchema propertySchema)
        {
            var schemaObject = new JObject
            {
                ["type"] = propertySchema.type
            };

            if (!string.IsNullOrEmpty(propertySchema.description))
            {
                schemaObject["description"] = propertySchema.description;
            }

            if (propertySchema.@enum != null && propertySchema.@enum.Count > 0)
            {
                var enumArray = new JArray();
                foreach (var value in propertySchema.@enum)
                {
                    enumArray.Add(value);
                }
                schemaObject["enum"] = enumArray;
            }

            if (propertySchema.items != null)
            {
                schemaObject["items"] = SerializePropertySchema(propertySchema.items);
            }

            if (propertySchema.@default != null)
            {
                schemaObject["default"] = JToken.FromObject(propertySchema.@default);
            }

            if (propertySchema.minimum.HasValue)
            {
                schemaObject["minimum"] = propertySchema.minimum.Value;
            }

            if (propertySchema.maximum.HasValue)
            {
                schemaObject["maximum"] = propertySchema.maximum.Value;
            }

            return schemaObject;
        }

        private JObject HandleToolsCall(JToken paramsToken, string requestId)
        {
            if (paramsToken == null)
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing params", requestId);
            }

            string toolName = paramsToken["name"]?.ToString();
            if (string.IsNullOrEmpty(toolName))
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing 'name' in params", requestId);
            }

            if (!ToolRegistry.HasTool(toolName))
            {
                return CreateErrorResponse(MCPErrorCodes.MethodNotFound, $"Unknown tool: {toolName}", requestId);
            }

            JObject argumentsObject = paramsToken["arguments"] as JObject ?? new JObject();

            try
            {
                object result = InvokeToolOnMainThread(toolName, argumentsObject);

                var contentArray = new JArray();
                contentArray.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = SerializeToolResult(result)
                });

                var toolResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = false
                };

                return CreateSuccessResponse(toolResult, requestId);
            }
            catch (MCPException mcpException)
            {
                var contentArray = new JArray();
                contentArray.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = mcpException.Message
                });

                var toolResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = true
                };

                return CreateSuccessResponse(toolResult, requestId);
            }
            catch (Exception exception)
            {
                var contentArray = new JArray();
                contentArray.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = $"Tool execution failed: {exception.Message}"
                });

                var toolResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = true
                };

                return CreateSuccessResponse(toolResult, requestId);
            }
        }

        private string SerializeToolResult(object result)
        {
            if (result == null)
            {
                return "null";
            }

            if (result is string stringResult)
            {
                return stringResult;
            }

            if (result is ToolResult toolResult)
            {
                // If it's already a ToolResult, serialize the content
                if (toolResult.content.Count == 1 && toolResult.content[0].type == "text")
                {
                    return toolResult.content[0].text;
                }
                return JsonConvert.SerializeObject(toolResult.content);
            }

            // For other types, serialize to JSON
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }

        /// <summary>
        /// Invokes a tool on Unity's main thread.
        /// Since PollForRequests already runs on the main thread, this executes directly.
        /// </summary>
        private object InvokeToolOnMainThread(string toolName, JObject arguments)
        {
            var argumentsDictionary = ConvertJObjectToDictionary(arguments);
            return ToolRegistry.Invoke(toolName, argumentsDictionary);
        }

        private Dictionary<string, object> ConvertJObjectToDictionary(JObject jObject)
        {
            var result = new Dictionary<string, object>();
            foreach (var property in jObject.Properties())
            {
                result[property.Name] = ConvertJToken(property.Value);
            }
            return result;
        }

        private object ConvertJToken(JToken token)
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

        private JObject HandleResourcesList(string requestId)
        {
            var resourceDefinitions = ResourceRegistry.GetDefinitions().ToList();
            var resourcesArray = new JArray();

            foreach (var resource in resourceDefinitions)
            {
                var resourceObject = new JObject
                {
                    ["uri"] = resource.uri,
                    ["name"] = resource.name,
                    ["description"] = resource.description,
                    ["mimeType"] = resource.mimeType ?? "application/json"
                };
                resourcesArray.Add(resourceObject);
            }

            var result = new JObject
            {
                ["resources"] = resourcesArray
            };

            return CreateSuccessResponse(result, requestId);
        }

        private JObject HandleResourcesRead(JToken paramsToken, string requestId)
        {
            if (paramsToken == null)
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing params", requestId);
            }

            string resourceUri = paramsToken["uri"]?.ToString();
            if (string.IsNullOrEmpty(resourceUri))
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing 'uri' in params", requestId);
            }

            if (!ResourceRegistry.HasResource(resourceUri))
            {
                return CreateErrorResponse(MCPErrorCodes.MethodNotFound, $"Unknown resource: {resourceUri}", requestId);
            }

            try
            {
                ResourceContent content = InvokeResourceOnMainThread(resourceUri);

                var contentsArray = new JArray();
                var contentObject = new JObject
                {
                    ["uri"] = content.uri,
                    ["mimeType"] = content.mimeType ?? "application/json"
                };

                if (!string.IsNullOrEmpty(content.text))
                {
                    contentObject["text"] = content.text;
                }
                else if (!string.IsNullOrEmpty(content.blob))
                {
                    contentObject["blob"] = content.blob;
                }

                contentsArray.Add(contentObject);

                var resourceResult = new JObject
                {
                    ["contents"] = contentsArray
                };

                return CreateSuccessResponse(resourceResult, requestId);
            }
            catch (MCPException mcpException)
            {
                return CreateErrorResponse(mcpException.ErrorCode, mcpException.Message, requestId);
            }
            catch (Exception exception)
            {
                return CreateErrorResponse(MCPErrorCodes.InternalError, $"Resource read failed: {exception.Message}", requestId);
            }
        }

        /// <summary>
        /// Invokes a resource on Unity's main thread.
        /// Since PollForRequests already runs on the main thread, this executes directly.
        /// </summary>
        private ResourceContent InvokeResourceOnMainThread(string resourceUri)
        {
            return ResourceRegistry.Invoke(resourceUri);
        }

        private JObject HandlePromptsList(string requestId)
        {
            var promptDefinitions = PromptRegistry.GetDefinitions().ToList();
            var promptsArray = new JArray();

            foreach (var prompt in promptDefinitions)
            {
                var promptObject = new JObject
                {
                    ["name"] = prompt.name,
                    ["description"] = prompt.description
                };

                if (prompt.arguments != null && prompt.arguments.Count > 0)
                {
                    var argumentsArray = new JArray();
                    foreach (var argument in prompt.arguments)
                    {
                        var argumentObject = new JObject
                        {
                            ["name"] = argument.name
                        };
                        if (!string.IsNullOrEmpty(argument.description))
                        {
                            argumentObject["description"] = argument.description;
                        }
                        argumentObject["required"] = argument.required;
                        argumentsArray.Add(argumentObject);
                    }
                    promptObject["arguments"] = argumentsArray;
                }

                promptsArray.Add(promptObject);
            }

            var result = new JObject
            {
                ["prompts"] = promptsArray
            };

            return CreateSuccessResponse(result, requestId);
        }

        private JObject HandlePromptsGet(JToken paramsToken, string requestId)
        {
            if (paramsToken == null)
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing params", requestId);
            }

            string promptName = paramsToken["name"]?.ToString();
            if (string.IsNullOrEmpty(promptName))
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing 'name' in params", requestId);
            }

            if (!PromptRegistry.HasPrompt(promptName))
            {
                return CreateErrorResponse(MCPErrorCodes.MethodNotFound, $"Unknown prompt: {promptName}", requestId);
            }

            // Extract arguments
            var arguments = new Dictionary<string, string>();
            var argumentsToken = paramsToken["arguments"] as JObject;
            if (argumentsToken != null)
            {
                foreach (var property in argumentsToken.Properties())
                {
                    arguments[property.Name] = property.Value?.ToString();
                }
            }

            try
            {
                PromptResult promptResult = InvokePromptOnMainThread(promptName, arguments);

                var messagesArray = new JArray();
                if (promptResult.messages != null)
                {
                    foreach (var message in promptResult.messages)
                    {
                        var messageObject = new JObject
                        {
                            ["role"] = message.role,
                            ["content"] = new JObject
                            {
                                ["type"] = message.content.type,
                                ["text"] = message.content.text
                            }
                        };
                        messagesArray.Add(messageObject);
                    }
                }

                var resultObject = new JObject
                {
                    ["messages"] = messagesArray
                };

                if (!string.IsNullOrEmpty(promptResult.description))
                {
                    resultObject["description"] = promptResult.description;
                }

                return CreateSuccessResponse(resultObject, requestId);
            }
            catch (MCPException mcpException)
            {
                return CreateErrorResponse(mcpException.ErrorCode, mcpException.Message, requestId);
            }
            catch (Exception exception)
            {
                return CreateErrorResponse(MCPErrorCodes.InternalError, $"Prompt execution failed: {exception.Message}", requestId);
            }
        }

        /// <summary>
        /// Invokes a prompt on Unity's main thread.
        /// Since PollForRequests already runs on the main thread, this executes directly.
        /// </summary>
        private PromptResult InvokePromptOnMainThread(string promptName, Dictionary<string, string> arguments)
        {
            return PromptRegistry.Invoke(promptName, arguments);
        }

        #endregion
    }
}
