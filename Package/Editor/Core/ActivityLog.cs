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
        }

        private static readonly List<Entry> s_entries = new List<Entry>();

        /// <summary>Fired after a new entry is added. UI subscribes to trigger Repaint.</summary>
        public static event Action OnEntryAdded;

        /// <summary>Read-only view of all recorded entries (newest first in UI, stored oldest-first).</summary>
        public static IReadOnlyList<Entry> Entries => s_entries;

        /// <summary>
        /// Records a tool invocation.
        /// </summary>
        /// <param name="toolName">The MCP tool name (e.g. "gameobject_manage").</param>
        /// <param name="success">Whether the tool completed without throwing.</param>
        /// <param name="detail">Optional short detail string.</param>
        public static void Record(string toolName, bool success, string detail = null)
        {
            if (string.IsNullOrEmpty(toolName))
                return;

            var entry = new Entry
            {
                timestamp = DateTime.Now,
                toolName = toolName,
                success = success,
                detail = detail
            };

            if (s_entries.Count >= MaxEntries)
                s_entries.RemoveAt(0);

            s_entries.Add(entry);
            OnEntryAdded?.Invoke();
        }

        /// <summary>Clears all recorded entries.</summary>
        public static void Clear()
        {
            s_entries.Clear();
            OnEntryAdded?.Invoke();
        }
    }
}
