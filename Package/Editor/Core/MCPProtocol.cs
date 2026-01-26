using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// JSON-RPC 2.0 standard error codes
    /// </summary>
    public static class MCPErrorCodes
    {
        /// <summary>Invalid JSON was received by the server</summary>
        public const int ParseError = -32700;

        /// <summary>The JSON sent is not a valid Request object</summary>
        public const int InvalidRequest = -32600;

        /// <summary>The method does not exist or is not available</summary>
        public const int MethodNotFound = -32601;

        /// <summary>Invalid method parameter(s)</summary>
        public const int InvalidParams = -32602;

        /// <summary>Internal JSON-RPC error</summary>
        public const int InternalError = -32603;
    }

    /// <summary>
    /// JSON-RPC 2.0 request message
    /// </summary>
    [Serializable]
    public class MCPRequest
    {
        public string jsonrpc = "2.0";
        public string method;
        public string @params;
        public string id;

        /// <summary>
        /// Parses the params field as a specific type using JsonUtility
        /// </summary>
        public T GetParams<T>()
        {
            if (string.IsNullOrEmpty(@params))
            {
                return default;
            }
            return JsonUtility.FromJson<T>(@params);
        }

        /// <summary>
        /// Checks if this is a notification (no id means no response expected)
        /// </summary>
        public bool IsNotification => string.IsNullOrEmpty(id);
    }

    /// <summary>
    /// JSON-RPC 2.0 response message
    /// </summary>
    [Serializable]
    public class MCPResponse
    {
        public string jsonrpc = "2.0";
        public string result;
        public MCPError error;
        public string id;

        /// <summary>
        /// Creates a successful response with the given result
        /// </summary>
        public static MCPResponse Success(object resultData, string requestId = null)
        {
            string resultJson = resultData != null ? JsonUtility.ToJson(resultData) : null;
            return new MCPResponse
            {
                result = resultJson,
                id = requestId
            };
        }

        /// <summary>
        /// Creates a successful response with a raw JSON string result
        /// </summary>
        public static MCPResponse SuccessRaw(string resultJson, string requestId = null)
        {
            return new MCPResponse
            {
                result = resultJson,
                id = requestId
            };
        }

        /// <summary>
        /// Creates an error response
        /// </summary>
        public static MCPResponse Error(int code, string message, object data = null, string requestId = null)
        {
            return new MCPResponse
            {
                error = new MCPError
                {
                    code = code,
                    message = message,
                    data = data != null ? JsonUtility.ToJson(data) : null
                },
                id = requestId
            };
        }

        /// <summary>
        /// Creates a parse error response
        /// </summary>
        public static MCPResponse ParseError(string message = "Parse error", string requestId = null)
        {
            return Error(MCPErrorCodes.ParseError, message, null, requestId);
        }

        /// <summary>
        /// Creates an invalid request error response
        /// </summary>
        public static MCPResponse InvalidRequest(string message = "Invalid Request", string requestId = null)
        {
            return Error(MCPErrorCodes.InvalidRequest, message, null, requestId);
        }

        /// <summary>
        /// Creates a method not found error response
        /// </summary>
        public static MCPResponse MethodNotFound(string message = "Method not found", string requestId = null)
        {
            return Error(MCPErrorCodes.MethodNotFound, message, null, requestId);
        }

        /// <summary>
        /// Creates an invalid params error response
        /// </summary>
        public static MCPResponse InvalidParams(string message = "Invalid params", string requestId = null)
        {
            return Error(MCPErrorCodes.InvalidParams, message, null, requestId);
        }

        /// <summary>
        /// Creates an internal error response
        /// </summary>
        public static MCPResponse InternalError(string message = "Internal error", string requestId = null)
        {
            return Error(MCPErrorCodes.InternalError, message, null, requestId);
        }

        /// <summary>
        /// Creates an error response from an exception
        /// </summary>
        public static MCPResponse FromException(Exception exception, string requestId = null)
        {
            if (exception is MCPException mcpException)
            {
                return Error(mcpException.ErrorCode, mcpException.Message, mcpException.Data, requestId);
            }
            return InternalError(exception.Message, requestId);
        }

        /// <summary>
        /// Checks if this response represents an error
        /// </summary>
        public bool IsError => error != null;
    }

    /// <summary>
    /// JSON-RPC 2.0 error object
    /// </summary>
    [Serializable]
    public class MCPError
    {
        public int code;
        public string message;
        public string data;
    }

    /// <summary>
    /// Exception class for MCP tool invocation errors
    /// </summary>
    public class MCPException : Exception
    {
        public int ErrorCode { get; }
        public new object Data { get; }

        public MCPException(string message, int errorCode = MCPErrorCodes.InternalError, object data = null)
            : base(message)
        {
            ErrorCode = errorCode;
            Data = data;
        }

        public MCPException(string message, Exception innerException, int errorCode = MCPErrorCodes.InternalError, object data = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            Data = data;
        }

        /// <summary>
        /// Creates an exception for invalid parameters
        /// </summary>
        public static MCPException InvalidParams(string message, object data = null)
        {
            return new MCPException(message, MCPErrorCodes.InvalidParams, data);
        }

        /// <summary>
        /// Creates an exception for method not found
        /// </summary>
        public static MCPException MethodNotFound(string methodName)
        {
            return new MCPException($"Method not found: {methodName}", MCPErrorCodes.MethodNotFound);
        }

        /// <summary>
        /// Creates an exception for internal errors
        /// </summary>
        public static MCPException InternalError(string message, object data = null)
        {
            return new MCPException(message, MCPErrorCodes.InternalError, data);
        }
    }

    /// <summary>
    /// JSON Schema representation for tool input parameters
    /// </summary>
    [Serializable]
    public class InputSchema
    {
        public string type = "object";
        public Dictionary<string, PropertySchema> properties = new Dictionary<string, PropertySchema>();
        public List<string> required = new List<string>();

        /// <summary>
        /// Adds a property to the schema
        /// </summary>
        public InputSchema AddProperty(string name, PropertySchema property, bool isRequired = false)
        {
            properties[name] = property;
            if (isRequired && !required.Contains(name))
            {
                required.Add(name);
            }
            return this;
        }

        /// <summary>
        /// Adds a string property to the schema
        /// </summary>
        public InputSchema AddString(string name, string description, bool isRequired = false)
        {
            return AddProperty(name, PropertySchema.String(description), isRequired);
        }

        /// <summary>
        /// Adds a number property to the schema
        /// </summary>
        public InputSchema AddNumber(string name, string description, bool isRequired = false)
        {
            return AddProperty(name, PropertySchema.Number(description), isRequired);
        }

        /// <summary>
        /// Adds an integer property to the schema
        /// </summary>
        public InputSchema AddInteger(string name, string description, bool isRequired = false)
        {
            return AddProperty(name, PropertySchema.Integer(description), isRequired);
        }

        /// <summary>
        /// Adds a boolean property to the schema
        /// </summary>
        public InputSchema AddBoolean(string name, string description, bool isRequired = false)
        {
            return AddProperty(name, PropertySchema.Boolean(description), isRequired);
        }

        /// <summary>
        /// Adds an array property to the schema
        /// </summary>
        public InputSchema AddArray(string name, string description, PropertySchema itemSchema, bool isRequired = false)
        {
            return AddProperty(name, PropertySchema.Array(description, itemSchema), isRequired);
        }
    }

    /// <summary>
    /// JSON Schema property definition
    /// </summary>
    [Serializable]
    public class PropertySchema
    {
        public string type;
        public string description;
        public List<string> @enum;
        public PropertySchema items;
        public object @default;

        /// <summary>
        /// Creates a string property schema
        /// </summary>
        public static PropertySchema String(string description, string defaultValue = null)
        {
            return new PropertySchema
            {
                type = "string",
                description = description,
                @default = defaultValue
            };
        }

        /// <summary>
        /// Creates a string enum property schema
        /// </summary>
        public static PropertySchema StringEnum(string description, List<string> enumValues, string defaultValue = null)
        {
            return new PropertySchema
            {
                type = "string",
                description = description,
                @enum = enumValues,
                @default = defaultValue
            };
        }

        /// <summary>
        /// Creates a number property schema
        /// </summary>
        public static PropertySchema Number(string description, double? defaultValue = null)
        {
            return new PropertySchema
            {
                type = "number",
                description = description,
                @default = defaultValue
            };
        }

        /// <summary>
        /// Creates an integer property schema
        /// </summary>
        public static PropertySchema Integer(string description, int? defaultValue = null)
        {
            return new PropertySchema
            {
                type = "integer",
                description = description,
                @default = defaultValue
            };
        }

        /// <summary>
        /// Creates a boolean property schema
        /// </summary>
        public static PropertySchema Boolean(string description, bool? defaultValue = null)
        {
            return new PropertySchema
            {
                type = "boolean",
                description = description,
                @default = defaultValue
            };
        }

        /// <summary>
        /// Creates an array property schema
        /// </summary>
        public static PropertySchema Array(string description, PropertySchema itemSchema)
        {
            return new PropertySchema
            {
                type = "array",
                description = description,
                items = itemSchema
            };
        }

        /// <summary>
        /// Creates an object property schema
        /// </summary>
        public static PropertySchema Object(string description)
        {
            return new PropertySchema
            {
                type = "object",
                description = description
            };
        }
    }

    /// <summary>
    /// MCP tool definition for tools/list response
    /// </summary>
    [Serializable]
    public class ToolDefinition
    {
        public string name;
        public string description;
        public string category;
        public InputSchema inputSchema;

        public ToolDefinition() { }

        public ToolDefinition(string name, string description, InputSchema inputSchema = null)
        {
            this.name = name;
            this.description = description;
            this.inputSchema = inputSchema ?? new InputSchema();
        }
    }

    /// <summary>
    /// MCP resource definition for resources/list response
    /// </summary>
    [Serializable]
    public class ResourceDefinition
    {
        public string uri;
        public string name;
        public string description;
        public string mimeType;

        public ResourceDefinition() { }

        public ResourceDefinition(string uri, string name, string description = null, string mimeType = null)
        {
            this.uri = uri;
            this.name = name;
            this.description = description;
            this.mimeType = mimeType;
        }
    }

    /// <summary>
    /// MCP resource template for resources/templates/list response
    /// </summary>
    [Serializable]
    public class ResourceTemplate
    {
        public string uriTemplate;
        public string name;
        public string description;
        public string mimeType;

        public ResourceTemplate() { }

        public ResourceTemplate(string uriTemplate, string name, string description = null, string mimeType = null)
        {
            this.uriTemplate = uriTemplate;
            this.name = name;
            this.description = description;
            this.mimeType = mimeType;
        }
    }

    /// <summary>
    /// MCP resource content for resources/read response
    /// </summary>
    [Serializable]
    public class ResourceContent
    {
        public string uri;
        public string mimeType;
        public string text;
        public string blob;

        /// <summary>
        /// Creates a text resource content
        /// </summary>
        public static ResourceContent Text(string uri, string text, string mimeType = "text/plain")
        {
            return new ResourceContent
            {
                uri = uri,
                mimeType = mimeType,
                text = text
            };
        }

        /// <summary>
        /// Creates a JSON resource content
        /// </summary>
        public static ResourceContent Json(string uri, object data)
        {
            return new ResourceContent
            {
                uri = uri,
                mimeType = "application/json",
                text = JsonUtility.ToJson(data)
            };
        }

        /// <summary>
        /// Creates a binary resource content (base64 encoded)
        /// </summary>
        public static ResourceContent Binary(string uri, byte[] data, string mimeType = "application/octet-stream")
        {
            return new ResourceContent
            {
                uri = uri,
                mimeType = mimeType,
                blob = Convert.ToBase64String(data)
            };
        }
    }

    /// <summary>
    /// MCP tool call result content
    /// </summary>
    [Serializable]
    public class ToolResultContent
    {
        public string type;
        public string text;
        public string mimeType;
        public string data;

        /// <summary>
        /// Creates a text result
        /// </summary>
        public static ToolResultContent Text(string text)
        {
            return new ToolResultContent
            {
                type = "text",
                text = text
            };
        }

        /// <summary>
        /// Creates an image result (base64 encoded)
        /// </summary>
        public static ToolResultContent Image(byte[] imageData, string mimeType = "image/png")
        {
            return new ToolResultContent
            {
                type = "image",
                mimeType = mimeType,
                data = Convert.ToBase64String(imageData)
            };
        }
    }

    /// <summary>
    /// MCP tool call result
    /// </summary>
    [Serializable]
    public class ToolResult
    {
        public List<ToolResultContent> content = new List<ToolResultContent>();
        public bool isError;

        /// <summary>
        /// Creates a successful text result
        /// </summary>
        public static ToolResult Success(string text)
        {
            return new ToolResult
            {
                content = new List<ToolResultContent> { ToolResultContent.Text(text) },
                isError = false
            };
        }

        /// <summary>
        /// Creates a successful result with multiple content items
        /// </summary>
        public static ToolResult Success(List<ToolResultContent> contentList)
        {
            return new ToolResult
            {
                content = contentList,
                isError = false
            };
        }

        /// <summary>
        /// Creates an error result
        /// </summary>
        public static ToolResult Error(string errorMessage)
        {
            return new ToolResult
            {
                content = new List<ToolResultContent> { ToolResultContent.Text(errorMessage) },
                isError = true
            };
        }
    }
}
