using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace UnityMCP.Editor.Resources.Console
{
    /// <summary>
    /// Resource provider for detailed console error information.
    /// </summary>
    public static class ConsoleErrors
    {
        /// <summary>
        /// Gets detailed compilation and runtime errors from the Unity console.
        /// Uses reflection to access internal Unity LogEntries API.
        /// </summary>
        /// <returns>Object containing detailed error and warning information.</returns>
        [MCPResource("console://errors", "Detailed compilation/runtime errors with file paths and line numbers")]
        public static object GetConsoleErrors()
        {
            var errors = new List<object>();
            var warnings = new List<object>();
            var messages = new List<object>();

            try
            {
                var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                if (logEntriesType == null)
                {
                    return new
                    {
                        error = true,
                        message = "Failed to access LogEntries type via reflection"
                    };
                }

                // Get total count of log entries
                var getCountMethod = logEntriesType.GetMethod(
                    "GetCount",
                    BindingFlags.Static | BindingFlags.Public);

                if (getCountMethod == null)
                {
                    return new
                    {
                        error = true,
                        message = "Failed to access GetCount method"
                    };
                }

                int totalCount = (int)getCountMethod.Invoke(null, null);

                if (totalCount == 0)
                {
                    return new
                    {
                        counts = new
                        {
                            errors = 0,
                            warnings = 0,
                            messages = 0,
                            total = 0
                        },
                        errors = Array.Empty<object>(),
                        warnings = Array.Empty<object>(),
                        messages = Array.Empty<object>()
                    };
                }

                // Start reading entries
                var startGettingEntriesMethod = logEntriesType.GetMethod(
                    "StartGettingEntries",
                    BindingFlags.Static | BindingFlags.Public);

                var endGettingEntriesMethod = logEntriesType.GetMethod(
                    "EndGettingEntries",
                    BindingFlags.Static | BindingFlags.Public);

                var getEntryInternalMethod = logEntriesType.GetMethod(
                    "GetEntryInternal",
                    BindingFlags.Static | BindingFlags.Public);

                if (startGettingEntriesMethod == null || endGettingEntriesMethod == null || getEntryInternalMethod == null)
                {
                    return new
                    {
                        error = true,
                        message = "Failed to access LogEntry methods via reflection"
                    };
                }

                // Get the LogEntry type
                var logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor");
                if (logEntryType == null)
                {
                    return new
                    {
                        error = true,
                        message = "Failed to access LogEntry type via reflection"
                    };
                }

                // Get field accessors
                var messageField = logEntryType.GetField("message", BindingFlags.Instance | BindingFlags.Public);
                var fileField = logEntryType.GetField("file", BindingFlags.Instance | BindingFlags.Public);
                var lineField = logEntryType.GetField("line", BindingFlags.Instance | BindingFlags.Public);
                var columnField = logEntryType.GetField("column", BindingFlags.Instance | BindingFlags.Public);
                var modeField = logEntryType.GetField("mode", BindingFlags.Instance | BindingFlags.Public);
                var instanceIdField = logEntryType.GetField("instanceID", BindingFlags.Instance | BindingFlags.Public);

                // Mode flags for log type classification
                // Based on Unity's internal LogEntry.mode field
                const int ErrorFlag = 1 << 0;              // 1 - Error
                const int AssertFlag = 1 << 1;             // 2 - Assert
                const int FatalFlag = 1 << 4;              // 16 - Fatal
                const int AssetImportErrorFlag = 1 << 6;   // 64 - Asset import errors
                const int AssetImportWarningFlag = 1 << 7; // 128 - Asset import warnings
                const int ScriptingErrorFlag = 1 << 8;     // 256 - Runtime script errors
                const int ScriptingWarningFlag = 1 << 9;   // 512 - Runtime script warnings
                const int ScriptCompileError = 1 << 11;    // 2048 - Compilation errors
                const int ScriptCompileWarning = 1 << 12;  // 4096 - Compilation warnings
                const int ScriptingExceptionFlag = 1 << 17; // 131072 - Runtime exceptions

                startGettingEntriesMethod.Invoke(null, null);

                try
                {
                    var logEntry = Activator.CreateInstance(logEntryType);

                    for (int entryIndex = 0; entryIndex < totalCount; entryIndex++)
                    {
                        bool success = (bool)getEntryInternalMethod.Invoke(null, new object[] { entryIndex, logEntry });
                        if (!success)
                        {
                            continue;
                        }

                        string messageText = messageField?.GetValue(logEntry) as string ?? "";
                        string filePath = fileField?.GetValue(logEntry) as string ?? "";
                        int lineNumber = lineField != null ? (int)lineField.GetValue(logEntry) : 0;
                        int columnNumber = columnField != null ? (int)columnField.GetValue(logEntry) : 0;
                        int mode = modeField != null ? (int)modeField.GetValue(logEntry) : 0;
                        int instanceId = instanceIdField != null ? (int)instanceIdField.GetValue(logEntry) : 0;

                        // Classify the entry type
                        string entryType = ClassifyLogEntry(mode, messageText);
                        string errorCode = ExtractErrorCode(messageText);

                        var entryData = new
                        {
                            message = messageText,
                            file = filePath,
                            line = lineNumber,
                            column = columnNumber,
                            type = entryType,
                            code = errorCode,
                            instanceId = instanceId,
                            modeFlags = mode,
                            isCompilationError = (mode & ScriptCompileError) != 0,
                            isCompilationWarning = (mode & ScriptCompileWarning) != 0
                        };

                        // Categorize based on mode flags
                        bool isError = (mode & ErrorFlag) != 0 ||
                                       (mode & AssertFlag) != 0 ||
                                       (mode & FatalFlag) != 0 ||
                                       (mode & AssetImportErrorFlag) != 0 ||
                                       (mode & ScriptingErrorFlag) != 0 ||
                                       (mode & ScriptCompileError) != 0 ||
                                       (mode & ScriptingExceptionFlag) != 0;
                        bool isWarning = (mode & AssetImportWarningFlag) != 0 ||
                                         (mode & ScriptingWarningFlag) != 0 ||
                                         (mode & ScriptCompileWarning) != 0;

                        if (isError)
                        {
                            errors.Add(entryData);
                        }
                        else if (isWarning)
                        {
                            warnings.Add(entryData);
                        }
                        else
                        {
                            messages.Add(entryData);
                        }
                    }
                }
                finally
                {
                    endGettingEntriesMethod.Invoke(null, null);
                }

                return new
                {
                    counts = new
                    {
                        errors = errors.Count,
                        warnings = warnings.Count,
                        messages = messages.Count,
                        total = errors.Count + warnings.Count + messages.Count
                    },
                    errors = errors.ToArray(),
                    warnings = warnings.ToArray(),
                    messages = messages.ToArray()
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    error = true,
                    message = $"Failed to retrieve console errors: {exception.Message}",
                    stackTrace = exception.StackTrace
                };
            }
        }

        /// <summary>
        /// Classifies a log entry based on its mode flags and message content.
        /// </summary>
        private static string ClassifyLogEntry(int mode, string message)
        {
            // Mode flags matching Unity's internal LogEntry.mode field
            const int ErrorFlag = 1 << 0;              // 1
            const int AssertFlag = 1 << 1;             // 2
            const int FatalFlag = 1 << 4;              // 16
            const int AssetImportErrorFlag = 1 << 6;   // 64
            const int AssetImportWarningFlag = 1 << 7; // 128
            const int ScriptingErrorFlag = 1 << 8;     // 256
            const int ScriptingWarningFlag = 1 << 9;   // 512
            const int ScriptCompileError = 1 << 11;    // 2048
            const int ScriptCompileWarning = 1 << 12;  // 4096
            const int ScriptingExceptionFlag = 1 << 17; // 131072

            if ((mode & ScriptCompileError) != 0)
            {
                return "CompilationError";
            }

            if ((mode & ScriptCompileWarning) != 0)
            {
                return "CompilationWarning";
            }

            if ((mode & AssertFlag) != 0)
            {
                return "Assert";
            }

            if ((mode & ScriptingExceptionFlag) != 0)
            {
                // Runtime exception - try to determine specific type from message
                return ClassifyExceptionFromMessage(message);
            }

            if ((mode & (ErrorFlag | FatalFlag | AssetImportErrorFlag | ScriptingErrorFlag)) != 0)
            {
                // Try to determine more specific error type from message
                return ClassifyExceptionFromMessage(message);
            }

            if ((mode & (AssetImportWarningFlag | ScriptingWarningFlag)) != 0)
            {
                return "Warning";
            }

            return "Info";
        }

        /// <summary>
        /// Classifies an exception/error based on the message content.
        /// </summary>
        private static string ClassifyExceptionFromMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "RuntimeError";
            }

            if (message.Contains("NullReferenceException"))
            {
                return "NullReferenceException";
            }

            if (message.Contains("MissingReferenceException"))
            {
                return "MissingReferenceException";
            }

            if (message.Contains("IndexOutOfRangeException"))
            {
                return "IndexOutOfRangeException";
            }

            if (message.Contains("ArgumentException"))
            {
                return "ArgumentException";
            }

            if (message.Contains("InvalidOperationException"))
            {
                return "InvalidOperationException";
            }

            if (message.Contains("KeyNotFoundException"))
            {
                return "KeyNotFoundException";
            }

            if (message.Contains("FormatException"))
            {
                return "FormatException";
            }

            if (message.Contains("Exception"))
            {
                return "Exception";
            }

            return "RuntimeError";
        }

        /// <summary>
        /// Extracts an error code from the message if present (e.g., CS0001, CS1061).
        /// </summary>
        private static string ExtractErrorCode(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }

            // Look for C# compiler error codes (CS####)
            int csIndex = message.IndexOf("CS", StringComparison.Ordinal);
            if (csIndex >= 0 && csIndex + 6 <= message.Length)
            {
                string potentialCode = message.Substring(csIndex, 6);
                if (potentialCode.Length == 6 &&
                    potentialCode.StartsWith("CS") &&
                    char.IsDigit(potentialCode[2]) &&
                    char.IsDigit(potentialCode[3]) &&
                    char.IsDigit(potentialCode[4]) &&
                    char.IsDigit(potentialCode[5]))
                {
                    return potentialCode;
                }
            }

            // Look for Unity-specific error codes (UNT####, USG####)
            foreach (string prefix in new[] { "UNT", "USG" })
            {
                int prefixIndex = message.IndexOf(prefix, StringComparison.Ordinal);
                if (prefixIndex >= 0 && prefixIndex + 7 <= message.Length)
                {
                    string potentialCode = message.Substring(prefixIndex, 7);
                    if (potentialCode.Length == 7 &&
                        potentialCode.StartsWith(prefix) &&
                        char.IsDigit(potentialCode[3]) &&
                        char.IsDigit(potentialCode[4]) &&
                        char.IsDigit(potentialCode[5]) &&
                        char.IsDigit(potentialCode[6]))
                    {
                        return potentialCode;
                    }
                }
            }

            return null;
        }
    }
}
