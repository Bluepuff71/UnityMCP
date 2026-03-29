using System;
using System.Collections.Generic;
using System.Linq;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tools for discovering and managing connected MCP agents (sessions).
    /// </summary>
    public static class AgentTools
    {
        private const int MaxAgents = 10;
        private const int MaxNameLength = 32;

        /// <summary>
        /// Lists all currently connected agents with their session info and held locks.
        /// </summary>
        [MCPTool("agent_list", "List all connected agents with session info and held locks",
            ReadOnlyHint = true, IdempotentHint = true, Category = "Agent")]
        public static object List()
        {
            var allSessions = SessionManager.GetAllSessions();
            var agentEntries = new List<object>();

            foreach (var session in allSessions)
            {
                var sessionLocks = LockManager.QueryLocks(session.SessionId);
                var lockEntries = sessionLocks.Select(lockInfo => new
                {
                    resource = lockInfo.ResourceKey,
                    reason = lockInfo.Reason,
                    is_auto = lockInfo.IsAutoLock
                }).ToList();

                agentEntries.Add(new
                {
                    session_id = session.SessionId,
                    name = session.FriendlyName,
                    connected_at = session.ConnectTime.ToString("o"),
                    last_activity = session.LastActivity.ToString("o"),
                    request_count = session.RequestCount,
                    locks = lockEntries
                });
            }

            return new
            {
                total_agents = allSessions.Count,
                max_agents = MaxAgents,
                agents = agentEntries
            };
        }

        /// <summary>
        /// Returns detailed information for a single agent including all held locks.
        /// </summary>
        [MCPTool("agent_info", "Get detailed info for a specific agent session",
            ReadOnlyHint = true, IdempotentHint = true, Category = "Agent")]
        public static object Info(
            [MCPParam("session_id", "Session ID of the agent to query. Defaults to the calling agent if omitted.")]
            string sessionId = null)
        {
            string resolvedSessionId = sessionId ?? RequestContext.CurrentSessionId;

            if (string.IsNullOrEmpty(resolvedSessionId))
            {
                throw new MCPException("No active session. Cannot determine agent identity.");
            }

            var session = SessionManager.GetSession(resolvedSessionId);
            if (session == null)
            {
                throw new MCPException($"Session not found: '{resolvedSessionId}'");
            }

            var sessionLocks = LockManager.QueryLocks(resolvedSessionId);
            var lockEntries = sessionLocks.Select(lockInfo => new
            {
                resource = lockInfo.ResourceKey,
                reason = lockInfo.Reason,
                is_auto = lockInfo.IsAutoLock,
                acquired_at = lockInfo.AcquiredAt.ToString("o")
            }).ToList();

            return new
            {
                session_id = session.SessionId,
                name = session.FriendlyName,
                connected_at = session.ConnectTime.ToString("o"),
                last_activity = session.LastActivity.ToString("o"),
                request_count = session.RequestCount,
                locks = lockEntries,
                lock_count = lockEntries.Count
            };
        }

        /// <summary>
        /// Sets the friendly display name for the calling agent's session.
        /// </summary>
        [MCPTool("agent_set_name", "Set the friendly display name for the calling agent",
            IdempotentHint = true, DestructiveHint = false, Category = "Agent")]
        public static object SetName(
            [MCPParam("name", "Friendly display name for this agent (max 32 characters)", required: true)]
            string name)
        {
            string currentSessionId = RequestContext.CurrentSessionId;
            if (string.IsNullOrEmpty(currentSessionId))
            {
                throw new MCPException("No active session. Cannot set agent name.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new MCPException("Name cannot be empty or whitespace.");
            }

            if (name.Length > MaxNameLength)
            {
                throw new MCPException($"Name exceeds maximum length of {MaxNameLength} characters (got {name.Length}).");
            }

            bool wasSet = SessionManager.SetSessionName(currentSessionId, name);
            if (!wasSet)
            {
                throw new MCPException($"Failed to set name. Session '{currentSessionId}' may no longer be active.");
            }

            return new
            {
                session_id = currentSessionId,
                name,
                message = $"Name set to '{name}'"
            };
        }
    }
}
