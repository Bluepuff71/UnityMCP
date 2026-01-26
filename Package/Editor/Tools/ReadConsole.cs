using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Provides access to Unity Console log entries using reflection to access internal APIs.
    /// </summary>
    public static class ReadConsole
    {
        #region Constants

        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 500;

        // Mode bit flags for log entry types (based on Unity's internal ConsoleFlags)
        // These values are determined by Unity's internal LogEntry.mode field
        private const int ModeBitError = 1 << 0;              // 1 - Error
        private const int ModeBitAssert = 1 << 1;             // 2 - Assert
        private const int ModeBitLog = 1 << 2;                // 4 - Debug.Log (regular log/info)
        private const int ModeBitFatal = 1 << 4;              // 16 - Fatal errors
        private const int ModeBitAssetImportError = 1 << 6;   // 64 - Asset import errors
        private const int ModeBitAssetImportWarning = 1 << 7; // 128 - Asset import warnings
        private const int ModeBitScriptingError = 1 << 8;     // 256 - Runtime script errors
        private const int ModeBitScriptingWarning = 1 << 9;   // 512 - Runtime script warnings (Debug.LogWarning)
        private const int ModeBitScriptingLog = 1 << 10;      // 1024 - Runtime script logs (Debug.Log from scripts)
        private const int ModeBitScriptCompileError = 1 << 11;   // 2048 - Compilation errors
        private const int ModeBitScriptCompileWarning = 1 << 12; // 4096 - Compilation warnings
        private const int ModeBitScriptingException = 1 << 17;   // 131072 - Runtime exceptions

        // Combined masks for log type categories
        private const int ErrorMask = ModeBitError | ModeBitAssert | ModeBitFatal |
                                      ModeBitAssetImportError | ModeBitScriptingError |
                                      ModeBitScriptCompileError | ModeBitScriptingException;
        private const int WarningMask = ModeBitAssetImportWarning | ModeBitScriptingWarning | ModeBitScriptCompileWarning;
        private const int LogMask = ModeBitLog | ModeBitScriptingLog;

        #endregion

        #region Reflection Setup

        private static Type logEntriesType;
        private static Type logEntryType;

        private static MethodInfo startGettingEntriesMethod;
        private static MethodInfo endGettingEntriesMethod;
        private static MethodInfo clearMethod;
        private static MethodInfo getCountMethod;
        private static MethodInfo getEntryInternalMethod;

        private static FieldInfo modeField;
        private static FieldInfo messageField;
        private static FieldInfo fileField;
        private static FieldInfo lineField;
        private static FieldInfo instanceIdField;

        private static bool isReflectionInitialized;
        private static string reflectionError;

        /// <summary>
        /// Initializes reflection for accessing internal Unity Console APIs.
        /// </summary>
        static ReadConsole()
        {
            InitializeReflection();
        }

        private static void InitializeReflection()
        {
            try
            {
                // Get LogEntries type
                logEntriesType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntries");
                if (logEntriesType == null)
                {
                    reflectionError = "Could not find UnityEditor.LogEntries type.";
                    return;
                }

                // Get LogEntry type
                logEntryType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntry");
                if (logEntryType == null)
                {
                    reflectionError = "Could not find UnityEditor.LogEntry type.";
                    return;
                }

                // Get static methods on LogEntries
                startGettingEntriesMethod = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public);
                endGettingEntriesMethod = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public);
                clearMethod = logEntriesType.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public);
                getCountMethod = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
                getEntryInternalMethod = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);

                if (startGettingEntriesMethod == null || endGettingEntriesMethod == null ||
                    clearMethod == null || getCountMethod == null || getEntryInternalMethod == null)
                {
                    reflectionError = "Could not find one or more required methods on LogEntries.";
                    return;
                }

                // Get fields on LogEntry
                modeField = logEntryType.GetField("mode", BindingFlags.Instance | BindingFlags.Public);
                messageField = logEntryType.GetField("message", BindingFlags.Instance | BindingFlags.Public);
                fileField = logEntryType.GetField("file", BindingFlags.Instance | BindingFlags.Public);
                lineField = logEntryType.GetField("line", BindingFlags.Instance | BindingFlags.Public);
                instanceIdField = logEntryType.GetField("instanceID", BindingFlags.Instance | BindingFlags.Public);

                if (modeField == null || messageField == null)
                {
                    reflectionError = "Could not find required fields on LogEntry. Some fields may be missing in this Unity version.";
                    return;
                }

                isReflectionInitialized = true;
            }
            catch (Exception exception)
            {
                reflectionError = $"Failed to initialize reflection: {exception.Message}";
            }
        }

        #endregion

        #region Main Tool Entry Point

        /// <summary>
        /// Reads Unity Console log entries with filtering and pagination support.
        /// </summary>
        [MCPTool("console_read", "Reads Unity Console log entries with filtering and pagination", Category = "Console")]
        public static object Read(
            [MCPParam("action", "Action to perform: 'get' to read entries, 'clear' to clear console (default: get)")] string action = "get",
            [MCPParam("types", "Comma-separated log types to include: error, warning, log, all (default: error,warning)")] string types = "error,warning",
            [MCPParam("count", "Maximum entries to return (non-paging mode, overrides page_size if set)")] int? count = null,
            [MCPParam("page_size", "Entries per page (default: 50, max: 500)")] int pageSize = DefaultPageSize,
            [MCPParam("cursor", "Starting index for pagination (default: 0)")] int cursor = 0,
            [MCPParam("filter_text", "Text filter for messages (case-insensitive substring match)")] string filterText = null,
            [MCPParam("format", "Output format: 'plain' or 'detailed' (default: plain)")] string format = "plain",
            [MCPParam("include_stacktrace", "Include stack traces in output (default: false)")] bool includeStacktrace = false)
        {
            // Check if reflection is available
            if (!isReflectionInitialized)
            {
                return new
                {
                    success = false,
                    error = $"Console API not available: {reflectionError}"
                };
            }

            string normalizedAction = (action ?? "get").ToLowerInvariant().Trim();

            return normalizedAction switch
            {
                "get" => GetEntries(types, count, pageSize, cursor, filterText, format, includeStacktrace),
                "clear" => ClearConsole(),
                _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: get, clear")
            };
        }

        #endregion

        #region Actions

        /// <summary>
        /// Gets console log entries with filtering and pagination.
        /// </summary>
        private static object GetEntries(string types, int? count, int pageSize, int cursor, string filterText, string format, bool includeStacktrace)
        {
            try
            {
                // Parse log types filter
                int typeMask = ParseTypeMask(types);

                // Resolve page size
                int resolvedPageSize = count.HasValue
                    ? Mathf.Clamp(count.Value, 1, MaxPageSize)
                    : Mathf.Clamp(pageSize, 1, MaxPageSize);

                int resolvedCursor = Mathf.Max(0, cursor);

                bool isDetailedFormat = (format ?? "plain").Equals("detailed", StringComparison.OrdinalIgnoreCase);

                // Get entries from console
                var entries = new List<object>();
                int totalFilteredCount = 0;
                int skippedCount = 0;
                int totalConsoleCount = 0;

                // Start getting entries
                startGettingEntriesMethod.Invoke(null, null);

                try
                {
                    totalConsoleCount = (int)getCountMethod.Invoke(null, null);

                    // Create a LogEntry instance to receive data
                    object logEntry = Activator.CreateInstance(logEntryType);

                    for (int i = 0; i < totalConsoleCount; i++)
                    {
                        // Get entry data
                        getEntryInternalMethod.Invoke(null, new object[] { i, logEntry });

                        int mode = (int)modeField.GetValue(logEntry);
                        string message = (string)messageField.GetValue(logEntry);

                        // Check type filter
                        if (!MatchesTypeMask(mode, typeMask))
                        {
                            continue;
                        }

                        // Check text filter
                        if (!string.IsNullOrEmpty(filterText))
                        {
                            if (message == null || message.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                        }

                        // This entry passes all filters
                        totalFilteredCount++;

                        // Handle pagination
                        if (skippedCount < resolvedCursor)
                        {
                            skippedCount++;
                            continue;
                        }

                        // Check if we have enough entries
                        if (entries.Count >= resolvedPageSize)
                        {
                            continue; // Keep counting for totalFilteredCount
                        }

                        // Build entry object
                        entries.Add(BuildEntryObject(logEntry, mode, message, i, isDetailedFormat, includeStacktrace));
                    }
                }
                finally
                {
                    // Always end getting entries
                    endGettingEntriesMethod.Invoke(null, null);
                }

                // Calculate pagination info
                bool hasMore = (resolvedCursor + entries.Count) < totalFilteredCount;
                int? nextCursor = hasMore ? resolvedCursor + entries.Count : (int?)null;

                return new
                {
                    success = true,
                    entries,
                    pageSize = resolvedPageSize,
                    cursor = resolvedCursor,
                    nextCursor,
                    totalCount = totalFilteredCount,
                    totalConsoleCount,
                    hasMore
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error reading console entries: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Clears the Unity Console.
        /// </summary>
        private static object ClearConsole()
        {
            try
            {
                clearMethod.Invoke(null, null);

                return new
                {
                    success = true,
                    message = "Console cleared successfully."
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error clearing console: {exception.Message}"
                };
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Parses the types parameter into a bitmask for filtering.
        /// </summary>
        private static int ParseTypeMask(string types)
        {
            if (string.IsNullOrEmpty(types))
            {
                // Default to errors and warnings
                return ErrorMask | WarningMask;
            }

            string[] typeArray = types.ToLowerInvariant().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            int mask = 0;
            foreach (string type in typeArray)
            {
                string trimmedType = type.Trim();
                switch (trimmedType)
                {
                    case "all":
                        return ErrorMask | WarningMask | LogMask;
                    case "error":
                    case "errors":
                        mask |= ErrorMask;
                        break;
                    case "warning":
                    case "warnings":
                        mask |= WarningMask;
                        break;
                    case "log":
                    case "logs":
                        mask |= LogMask;
                        break;
                }
            }

            // If nothing was matched, default to errors and warnings
            if (mask == 0)
            {
                mask = ErrorMask | WarningMask;
            }

            return mask;
        }

        /// <summary>
        /// Checks if a log entry mode matches the type mask.
        /// </summary>
        private static bool MatchesTypeMask(int mode, int typeMask)
        {
            // Check if the entry's mode bits overlap with any of the allowed type bits
            if ((typeMask & ErrorMask) != 0 && (mode & ErrorMask) != 0)
            {
                return true;
            }
            if ((typeMask & WarningMask) != 0 && (mode & WarningMask) != 0)
            {
                return true;
            }
            if ((typeMask & LogMask) != 0 && (mode & LogMask) != 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines the log type string from the mode bits.
        /// </summary>
        private static string GetLogType(int mode)
        {
            if ((mode & ErrorMask) != 0)
            {
                return "error";
            }
            if ((mode & WarningMask) != 0)
            {
                return "warning";
            }
            if ((mode & LogMask) != 0)
            {
                return "log";
            }
            return "unknown";
        }

        /// <summary>
        /// Builds an entry object for the response.
        /// </summary>
        private static object BuildEntryObject(object logEntry, int mode, string message, int index, bool detailed, bool includeStacktrace)
        {
            string logType = GetLogType(mode);

            // Extract message and stacktrace
            string messageText = message ?? string.Empty;
            string stacktrace = null;

            // Unity combines message and stacktrace in the message field, separated by newline
            int newlineIndex = messageText.IndexOf('\n');
            if (newlineIndex >= 0)
            {
                stacktrace = messageText.Substring(newlineIndex + 1);
                messageText = messageText.Substring(0, newlineIndex);
            }

            if (detailed)
            {
                var entryObject = new Dictionary<string, object>
                {
                    { "index", index },
                    { "type", logType },
                    { "message", messageText },
                    { "mode", mode }
                };

                // Add optional fields if available
                if (fileField != null)
                {
                    string file = (string)fileField.GetValue(logEntry);
                    if (!string.IsNullOrEmpty(file))
                    {
                        entryObject["file"] = file;
                    }
                }

                if (lineField != null)
                {
                    int line = (int)lineField.GetValue(logEntry);
                    if (line > 0)
                    {
                        entryObject["line"] = line;
                    }
                }

                if (instanceIdField != null)
                {
                    int instanceId = (int)instanceIdField.GetValue(logEntry);
                    if (instanceId != 0)
                    {
                        entryObject["instanceID"] = instanceId;
                    }
                }

                if (includeStacktrace && !string.IsNullOrEmpty(stacktrace))
                {
                    entryObject["stacktrace"] = stacktrace;
                }

                return entryObject;
            }
            else
            {
                // Plain format - simpler structure
                var entryObject = new Dictionary<string, object>
                {
                    { "type", logType },
                    { "message", messageText }
                };

                if (includeStacktrace && !string.IsNullOrEmpty(stacktrace))
                {
                    entryObject["stacktrace"] = stacktrace;
                }

                return entryObject;
            }
        }

        #endregion
    }
}
