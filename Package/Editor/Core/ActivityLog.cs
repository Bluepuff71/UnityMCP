using System;
using System.Collections.Generic;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Records tool invocations for display in the MCP server window.
    /// Thread-safe ring buffer limited to MaxEntries.
    /// </summary>
    public static class ActivityLog
    {
        public const int MaxEntries = 100;

        public struct Entry
        {
            public DateTime timestamp;
            public string toolName;
            public bool success;
            public string detail;
            public long durationMs;
            public string argumentsSummary;
            public int responseBytes;
            public string sessionId;
        }

        private static readonly List<Entry> s_entries = new List<Entry>();

        /// <summary>Fired after a new entry is added. UI subscribes to trigger Repaint.</summary>
        public static event Action OnEntryAdded;

        /// <summary>Read-only view of all recorded entries (newest first in UI, stored oldest-first).</summary>
        public static IReadOnlyList<Entry> Entries => s_entries;

        /// <summary>
        /// Records a tool invocation.
        /// </summary>
        /// <param name="toolName">The MCP tool name (e.g. "manage_gameobject").</param>
        /// <param name="success">Whether the tool completed without throwing.</param>
        /// <param name="detail">Optional short detail string.</param>
        public static void Record(string toolName, bool success, string detail = null)
        {
            AddEntry(toolName, success, detail, 0, null, 0, null);
        }

        /// <summary>
        /// Records a tool invocation with enriched data.
        /// </summary>
        /// <param name="toolName">The MCP tool name (e.g. "manage_gameobject").</param>
        /// <param name="success">Whether the tool completed without throwing.</param>
        /// <param name="detail">Optional short detail string.</param>
        /// <param name="durationMs">How long the tool call took in milliseconds.</param>
        /// <param name="argumentsSummary">Summary of the arguments passed to the tool.</param>
        /// <param name="responseBytes">Size of the response in bytes.</param>
        public static void Record(string toolName, bool success, string detail,
            long durationMs, string argumentsSummary, int responseBytes)
        {
            AddEntry(toolName, success, detail, durationMs, argumentsSummary, responseBytes, null);
        }

        /// <summary>
        /// Records a tool invocation with enriched data and session tracking.
        /// </summary>
        /// <param name="toolName">The MCP tool name (e.g. "manage_gameobject").</param>
        /// <param name="success">Whether the tool completed without throwing.</param>
        /// <param name="detail">Optional short detail string.</param>
        /// <param name="durationMs">How long the tool call took in milliseconds.</param>
        /// <param name="argumentsSummary">Summary of the arguments passed to the tool.</param>
        /// <param name="responseBytes">Size of the response in bytes.</param>
        /// <param name="sessionId">The agent session ID that made the tool call.</param>
        public static void Record(string toolName, bool success, string detail,
            long durationMs, string argumentsSummary, int responseBytes, string sessionId)
        {
            AddEntry(toolName, success, detail, durationMs, argumentsSummary, responseBytes, sessionId);
        }

        /// <summary>Clears all recorded entries.</summary>
        public static void Clear()
        {
            s_entries.Clear();
            OnEntryAdded?.Invoke();
        }

        /// <summary>
        /// Constructs an Entry and adds it to the ring buffer, firing the OnEntryAdded event.
        /// </summary>
        private static void AddEntry(string toolName, bool success, string detail,
            long durationMs, string argumentsSummary, int responseBytes, string sessionId)
        {
            if (string.IsNullOrEmpty(toolName))
                return;

            var entry = new Entry
            {
                timestamp = DateTime.Now,
                toolName = toolName,
                success = success,
                detail = detail,
                durationMs = durationMs,
                argumentsSummary = argumentsSummary,
                responseBytes = responseBytes,
                sessionId = sessionId
            };

            if (s_entries.Count >= MaxEntries)
                s_entries.RemoveAt(0);

            s_entries.Add(entry);
            OnEntryAdded?.Invoke();
        }
    }
}
