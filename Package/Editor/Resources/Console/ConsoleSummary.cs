using System;
using System.Reflection;
using UnityEditor;

namespace UnityMCP.Editor.Resources.Console
{
    /// <summary>
    /// Resource provider for console log summary information.
    /// </summary>
    public static class ConsoleSummary
    {
        /// <summary>
        /// Gets a summary of console log counts (errors, warnings, info messages).
        /// Uses reflection to access internal Unity console log entry counts.
        /// </summary>
        /// <returns>Object containing error, warning, and info counts.</returns>
        [MCPResource("console://summary", "Quick error/warning/info counts from the console")]
        public static object GetConsoleSummary()
        {
            int errorCount = 0;
            int warningCount = 0;
            int infoCount = 0;

            try
            {
                // Use reflection to get console log counts from LogEntries
                var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");

                if (logEntriesType != null)
                {
                    // Try to get counts using GetCountsByType method
                    var getCountsByTypeMethod = logEntriesType.GetMethod(
                        "GetCountsByType",
                        BindingFlags.Static | BindingFlags.Public);

                    if (getCountsByTypeMethod != null)
                    {
                        // Parameters: out int errorCount, out int warningCount, out int logCount
                        object[] parameters = new object[] { 0, 0, 0 };
                        getCountsByTypeMethod.Invoke(null, parameters);

                        errorCount = (int)parameters[0];
                        warningCount = (int)parameters[1];
                        infoCount = (int)parameters[2];
                    }
                }
            }
            catch (Exception exception)
            {
                return new
                {
                    error = true,
                    message = $"Failed to retrieve console counts: {exception.Message}",
                    counts = new
                    {
                        errors = 0,
                        warnings = 0,
                        info = 0,
                        total = 0
                    }
                };
            }

            bool hasErrors = errorCount > 0;
            bool hasWarnings = warningCount > 0;

            return new
            {
                counts = new
                {
                    errors = errorCount,
                    warnings = warningCount,
                    info = infoCount,
                    total = errorCount + warningCount + infoCount
                },
                status = new
                {
                    hasErrors = hasErrors,
                    hasWarnings = hasWarnings,
                    isClean = !hasErrors && !hasWarnings
                },
                summary = hasErrors
                    ? $"{errorCount} error(s), {warningCount} warning(s), {infoCount} info message(s)"
                    : hasWarnings
                        ? $"{warningCount} warning(s), {infoCount} info message(s)"
                        : $"{infoCount} info message(s)"
            };
        }
    }
}
