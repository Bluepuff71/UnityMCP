using System;
using System.Collections.Generic;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Services;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Executes a single batchable tool multiple times with different arguments.
    /// Automatically saves a checkpoint before execution and provides restore hints on failure.
    /// Destructive tools are never allowed regardless of BatchableHint setting.
    /// </summary>
    public static class BatchExecute
    {
        [MCPTool("batch_execute",
            "Execute a single tool multiple times with different arguments in one call. " +
            "Only tools marked as batchable can be used. Destructive tools are never allowed. " +
            "A checkpoint is automatically saved before execution for safety.",
            Category = "Utility", IdempotentHint = false)]
        public static object Execute(
            [MCPParam("tool", "Name of the tool to execute repeatedly", required: true)]
            string toolName,
            [MCPParam("calls", "Array of argument objects, one per invocation", required: true)]
            List<object> calls)
        {
            // Validate tool exists
            var toolInfo = ToolRegistry.GetToolInfo(toolName);
            if (toolInfo == null)
                throw MCPException.InvalidParams($"Unknown tool: '{toolName}'");

            // Reject destructive tools
            if (toolInfo.IsDestructive)
                throw MCPException.InvalidParams($"Tool '{toolName}' is destructive and cannot be batched");

            // Prevent recursive batching
            if (toolName == "batch_execute")
                throw MCPException.InvalidParams("Cannot batch batch_execute recursively");

            // Reject non-batchable tools
            if (!toolInfo.IsBatchable)
                throw MCPException.InvalidParams($"Tool '{toolName}' is not marked as batchable");

            // Reject if no calls
            if (calls == null || calls.Count == 0)
                throw MCPException.InvalidParams("'calls' array must contain at least one entry");

            // Reject if exceeds max batch size
            int maxBatchSize = MCPProxy.BatchMaxSize;
            if (calls.Count > maxBatchSize)
                throw MCPException.InvalidParams(
                    $"Batch size {calls.Count} exceeds maximum of {maxBatchSize}. " +
                    $"Reduce the number of calls or increase the batch size limit in the UnityMCP settings window.");

            // Auto-save checkpoint
            string checkpointId = null;
            var checkpoint = CheckpointManager.SaveCheckpoint($"batch_{toolName}");
            if (checkpoint != null && checkpoint != CheckpointManager.NothingToSave)
            {
                checkpointId = checkpoint.id;
            }

            // Execute batch
            int maxConsecutiveErrors = MCPProxy.BatchMaxConsecutiveErrors;
            int maxErrorPercent = MCPProxy.BatchMaxErrorPercent;
            int consecutiveErrors = 0;
            int completedCount = 0;
            int failedCount = 0;
            int? stoppedAt = null;
            var results = new List<Dictionary<string, object>>();

            for (int callIndex = 0; callIndex < calls.Count; callIndex++)
            {
                var callArguments = ConvertCallArguments(calls[callIndex], callIndex);

                try
                {
                    object result = ToolRegistry.Invoke(toolName, callArguments);

                    results.Add(new Dictionary<string, object>
                    {
                        { "index", callIndex },
                        { "success", true },
                        { "result", result }
                    });

                    completedCount++;
                    consecutiveErrors = 0;
                }
                catch (Exception exception)
                {
                    string errorMessage = exception is MCPException mcpException
                        ? mcpException.Message
                        : exception.Message;

                    results.Add(new Dictionary<string, object>
                    {
                        { "index", callIndex },
                        { "success", false },
                        { "error", errorMessage }
                    });

                    failedCount++;
                    consecutiveErrors++;

                    // Check consecutive error threshold
                    if (consecutiveErrors >= maxConsecutiveErrors)
                    {
                        stoppedAt = callIndex;
                        break;
                    }

                    // Check percentage error threshold (require at least 3 calls before applying)
                    int totalProcessed = completedCount + failedCount;
                    if (totalProcessed >= 3)
                    {
                        int errorPercent = (failedCount * 100) / totalProcessed;
                        if (errorPercent >= maxErrorPercent)
                        {
                            stoppedAt = callIndex;
                            break;
                        }
                    }
                }
            }

            // Build response
            var response = new Dictionary<string, object>
            {
                { "tool", toolName },
                { "results", results },
                { "completed", completedCount },
                { "failed", failedCount },
                { "total", calls.Count }
            };

            if (checkpointId != null)
            {
                response["checkpoint_id"] = checkpointId;
            }

            if (stoppedAt.HasValue)
            {
                response["stopped_at"] = stoppedAt.Value;
                response["stop_reason"] = consecutiveErrors >= maxConsecutiveErrors
                    ? $"Stopped after {maxConsecutiveErrors} consecutive errors"
                    : $"Stopped after error rate exceeded {maxErrorPercent}%";
            }

            if (failedCount > 0 && checkpointId != null)
            {
                response["restore_hint"] =
                    $"Use manage_checkpoint action='restore' id='{checkpointId}' to roll back all changes from this batch.";
            }

            return response;
        }

        /// <summary>
        /// Converts a call entry from the array into a Dictionary suitable for ToolRegistry.Invoke.
        /// </summary>
        private static Dictionary<string, object> ConvertCallArguments(object callEntry, int index)
        {
            if (callEntry is Dictionary<string, object> dictArguments)
                return dictArguments;

            if (callEntry is Newtonsoft.Json.Linq.JObject jObject)
                return ToolRegistry.ConvertJObjectToDictionary(jObject);

            throw MCPException.InvalidParams(
                $"calls[{index}] must be a JSON object with tool arguments, got {callEntry?.GetType().Name ?? "null"}");
        }
    }
}
