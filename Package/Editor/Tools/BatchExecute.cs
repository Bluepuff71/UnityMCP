using System;
using System.Collections.Generic;
using UnityEngine;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for executing multiple MCP commands in a single batch operation.
    /// Supports fail-fast behavior and returns detailed results for each command.
    /// </summary>
    public static class BatchExecute
    {
        private const int MaxCommandsPerBatch = 25;

        /// <summary>
        /// Executes multiple MCP commands atomically with optional fail-fast behavior.
        /// </summary>
        /// <param name="commands">
        /// Array of command objects. Each command should have:
        /// - "tool": string - the name of the tool to invoke
        /// - "params": object - optional parameters to pass to the tool
        /// </param>
        /// <param name="failFast">
        /// If true, stop execution on first failure. If false, continue executing
        /// remaining commands even if some fail. Default is false.
        /// </param>
        /// <returns>
        /// An object containing:
        /// - success: bool - true if all commands succeeded, false if any failed
        /// - total: int - total number of commands
        /// - succeeded: int - number of commands that succeeded
        /// - failed: int - number of commands that failed
        /// - results: array of individual command results
        /// </returns>
        [MCPTool("batch_execute", "Execute multiple MCP commands in a single batch operation with optional fail-fast behavior", Category = "Editor")]
        public static object Execute(
            [MCPParam("commands", "Array of command objects, each with 'tool' (string) and optional 'params' (object)", required: true)] List<object> commands,
            [MCPParam("fail_fast", "Stop execution on first failure (default: false)")] bool failFast = false)
        {
            // Validate commands parameter
            if (commands == null || commands.Count == 0)
            {
                return new
                {
                    success = false,
                    error = "No commands provided. The 'commands' parameter is required and must contain at least one command."
                };
            }

            // Enforce maximum commands limit
            if (commands.Count > MaxCommandsPerBatch)
            {
                return new
                {
                    success = false,
                    error = $"Too many commands. Maximum is {MaxCommandsPerBatch} commands per batch, but {commands.Count} were provided."
                };
            }

            var results = new List<object>();
            int succeededCount = 0;
            int failedCount = 0;
            bool overallSuccess = true;

            for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
            {
                var command = commands[commandIndex];
                var commandResult = ExecuteSingleCommand(commandIndex, command);

                results.Add(commandResult);

                // Check if this command succeeded
                bool commandSucceeded = GetCommandSuccess(commandResult);
                if (commandSucceeded)
                {
                    succeededCount++;
                }
                else
                {
                    failedCount++;
                    overallSuccess = false;

                    // If fail_fast is enabled, stop processing remaining commands
                    if (failFast)
                    {
                        // Add skipped placeholders for remaining commands
                        for (int skippedIndex = commandIndex + 1; skippedIndex < commands.Count; skippedIndex++)
                        {
                            results.Add(new
                            {
                                index = skippedIndex,
                                success = false,
                                skipped = true,
                                error = "Skipped due to fail_fast after previous command failure"
                            });
                        }
                        break;
                    }
                }
            }

            return new
            {
                success = overallSuccess,
                total = commands.Count,
                succeeded = succeededCount,
                failed = failedCount,
                fail_fast = failFast,
                results
            };
        }

        /// <summary>
        /// Executes a single command and returns its result.
        /// </summary>
        private static object ExecuteSingleCommand(int index, object command)
        {
            // Validate command is a dictionary
            if (command is not Dictionary<string, object> commandDict)
            {
                return new
                {
                    index,
                    success = false,
                    error = $"Invalid command format at index {index}. Expected an object with 'tool' and optional 'params' properties."
                };
            }

            // Extract tool name
            if (!commandDict.TryGetValue("tool", out var toolValue) || toolValue == null)
            {
                return new
                {
                    index,
                    success = false,
                    error = $"Missing 'tool' property in command at index {index}."
                };
            }

            string toolName = toolValue.ToString();
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return new
                {
                    index,
                    success = false,
                    error = $"Empty 'tool' property in command at index {index}."
                };
            }

            // Prevent recursive batch_execute calls
            if (toolName == "batch_execute")
            {
                return new
                {
                    index,
                    success = false,
                    tool = toolName,
                    error = "Recursive batch_execute calls are not allowed."
                };
            }

            // Extract params (optional)
            Dictionary<string, object> toolParams = new Dictionary<string, object>();
            if (commandDict.TryGetValue("params", out var paramsValue) && paramsValue != null)
            {
                if (paramsValue is Dictionary<string, object> paramsDict)
                {
                    toolParams = paramsDict;
                }
                else
                {
                    return new
                    {
                        index,
                        success = false,
                        tool = toolName,
                        error = $"Invalid 'params' format in command at index {index}. Expected an object."
                    };
                }
            }

            // Check if tool exists before invoking
            if (!ToolRegistry.HasTool(toolName))
            {
                return new
                {
                    index,
                    success = false,
                    tool = toolName,
                    error = $"Unknown tool: {toolName}"
                };
            }

            // Execute the tool
            try
            {
                var result = ToolRegistry.Invoke(toolName, toolParams);

                // Check if the result indicates success
                bool resultSuccess = GetResultSuccess(result);

                return new
                {
                    index,
                    success = resultSuccess,
                    tool = toolName,
                    result
                };
            }
            catch (MCPException mcpException)
            {
                Debug.LogWarning($"[BatchExecute] Tool '{toolName}' at index {index} failed with MCPException: {mcpException.Message}");
                return new
                {
                    index,
                    success = false,
                    tool = toolName,
                    error = mcpException.Message,
                    error_code = mcpException.ErrorCode
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BatchExecute] Tool '{toolName}' at index {index} failed with exception: {exception.Message}");
                return new
                {
                    index,
                    success = false,
                    tool = toolName,
                    error = $"Tool execution failed: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Extracts the success status from a command result object.
        /// </summary>
        private static bool GetCommandSuccess(object commandResult)
        {
            if (commandResult == null)
            {
                return false;
            }

            // Use reflection to check for 'success' property
            var resultType = commandResult.GetType();
            var successProperty = resultType.GetProperty("success");
            if (successProperty != null)
            {
                var successValue = successProperty.GetValue(commandResult);
                if (successValue is bool boolValue)
                {
                    return boolValue;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if a tool result indicates success.
        /// Tool results may have a 'success' property or may be considered successful
        /// if they don't have an 'error' property.
        /// </summary>
        private static bool GetResultSuccess(object result)
        {
            if (result == null)
            {
                return true; // No result is considered success
            }

            var resultType = result.GetType();

            // Check for explicit 'success' property
            var successProperty = resultType.GetProperty("success");
            if (successProperty != null)
            {
                var successValue = successProperty.GetValue(result);
                if (successValue is bool boolValue)
                {
                    return boolValue;
                }
            }

            // Check for 'error' property - if present and not null/empty, it's a failure
            var errorProperty = resultType.GetProperty("error");
            if (errorProperty != null)
            {
                var errorValue = errorProperty.GetValue(result);
                if (errorValue != null && !string.IsNullOrEmpty(errorValue.ToString()))
                {
                    return false;
                }
            }

            // Default to success if no explicit failure indicators
            return true;
        }
    }
}
