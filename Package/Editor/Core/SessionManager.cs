using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Tracks connected agent sessions with lifecycle management.
    /// Supports up to <see cref="MaxSessions"/> simultaneous sessions.
    /// Thread-safe via lock on all dictionary access.
    /// </summary>
    public static class SessionManager
    {
        public class SessionInfo
        {
            public string SessionId;
            public string FriendlyName;
            public DateTime ConnectTime;
            public DateTime LastActivity;
            public int RequestCount;
        }

        private static readonly Dictionary<string, SessionInfo> s_sessions = new Dictionary<string, SessionInfo>();
        private static readonly object s_lock = new object();
        private const int MaxSessions = 10;
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(15);
        /// <summary>
        /// Sessions that never received a request after initialize are likely ghost sessions
        /// from clients that reconnected. Expire these much faster than active sessions.
        /// </summary>
        private static readonly TimeSpan UnusedSessionTimeout = TimeSpan.FromSeconds(60);
        private static int s_agentCounter = 0;

        /// <summary>Fired when sessions are added or removed. UI subscribes to trigger Repaint.</summary>
        public static event Action OnSessionsChanged;

        /// <summary>
        /// Creates a new session with the given ID. Auto-assigns a friendly name like "Agent-1".
        /// Returns null if at capacity after pruning expired sessions.
        /// </summary>
        /// <param name="sessionId">Unique session identifier.</param>
        /// <returns>The created <see cref="SessionInfo"/>, or null if at capacity.</returns>
        public static SessionInfo CreateSession(string sessionId)
        {
            SessionInfo sessionInfo = null;
            List<string> evictedSessionIds = null;

            lock (s_lock)
            {
                // If session already exists, just return it
                if (s_sessions.TryGetValue(sessionId, out var existingSession))
                    return existingSession;

                // Prune expired sessions to free capacity
                var prunedIds = PruneExpiredSessionsInternal();
                if (prunedIds.Count > 0)
                    evictedSessionIds = prunedIds;

                // If still at capacity, evict the oldest inactive session (LRU)
                // to prevent a single reconnecting client from exhausting all slots
                if (s_sessions.Count >= MaxSessions)
                {
                    var oldestSessionId = s_sessions
                        .OrderBy(kvp => kvp.Value.LastActivity)
                        .First().Key;

                    s_sessions.Remove(oldestSessionId);

                    evictedSessionIds ??= new List<string>();
                    evictedSessionIds.Add(oldestSessionId);
                }

                s_agentCounter++;
                sessionInfo = new SessionInfo
                {
                    SessionId = sessionId,
                    FriendlyName = $"Agent-{s_agentCounter}",
                    ConnectTime = DateTime.Now,
                    LastActivity = DateTime.Now,
                    RequestCount = 0
                };

                s_sessions[sessionId] = sessionInfo;
            }

            // Release locks for evicted sessions outside the lock
            if (evictedSessionIds != null)
            {
                foreach (var evictedId in evictedSessionIds)
                    LockManager.ReleaseAllForSession(evictedId);
            }

            OnSessionsChanged?.Invoke();
            return sessionInfo;
        }

        /// <summary>
        /// Updates LastActivity and increments RequestCount for the given session.
        /// Returns false if the session does not exist (caller should reject the request).
        /// </summary>
        /// <param name="sessionId">The session to touch.</param>
        /// <returns>True if the session was found and updated, false if unknown.</returns>
        public static bool TouchSession(string sessionId)
        {
            lock (s_lock)
            {
                if (s_sessions.TryGetValue(sessionId, out var sessionInfo))
                {
                    sessionInfo.LastActivity = DateTime.Now;
                    sessionInfo.RequestCount++;
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Removes a session and releases all its locks via <see cref="LockManager.ReleaseAllForSession"/>.
        /// </summary>
        /// <param name="sessionId">The session to remove.</param>
        public static void RemoveSession(string sessionId)
        {
            bool removed = false;

            lock (s_lock)
            {
                removed = s_sessions.Remove(sessionId);
            }

            if (removed)
            {
                LockManager.ReleaseAllForSession(sessionId);
                OnSessionsChanged?.Invoke();
            }
        }

        /// <summary>
        /// Returns the <see cref="SessionInfo"/> for the given session, or null if not found.
        /// </summary>
        /// <param name="sessionId">The session to look up.</param>
        public static SessionInfo GetSession(string sessionId)
        {
            lock (s_lock)
            {
                s_sessions.TryGetValue(sessionId, out var sessionInfo);
                return sessionInfo;
            }
        }

        /// <summary>
        /// Returns a snapshot copy of all active sessions.
        /// </summary>
        public static List<SessionInfo> GetAllSessions()
        {
            lock (s_lock)
            {
                return s_sessions.Values.ToList();
            }
        }

        /// <summary>
        /// Returns the number of currently active sessions.
        /// </summary>
        public static int GetSessionCount()
        {
            lock (s_lock)
            {
                return s_sessions.Count;
            }
        }

        /// <summary>
        /// Removes sessions whose LastActivity exceeds <see cref="SessionTimeout"/>.
        /// Releases locks for each pruned session.
        /// </summary>
        /// <returns>The number of sessions that were pruned.</returns>
        public static int PruneExpiredSessions()
        {
            List<string> prunedSessionIds;

            lock (s_lock)
            {
                prunedSessionIds = PruneExpiredSessionsInternal();
            }

            foreach (var sessionId in prunedSessionIds)
                LockManager.ReleaseAllForSession(sessionId);

            if (prunedSessionIds.Count > 0)
                OnSessionsChanged?.Invoke();

            return prunedSessionIds.Count;
        }

        /// <summary>
        /// Sets the friendly name for a session. Name is truncated to 32 characters.
        /// </summary>
        /// <param name="sessionId">The session to rename.</param>
        /// <param name="name">The new friendly name.</param>
        /// <returns>True if the session was found and renamed, false otherwise.</returns>
        public static bool SetSessionName(string sessionId, string name)
        {
            lock (s_lock)
            {
                if (!s_sessions.TryGetValue(sessionId, out var sessionInfo))
                    return false;

                if (name != null && name.Length > 32)
                    name = name.Substring(0, 32);

                if (name != null)
                    name = DeduplicateName(name, sessionId);

                sessionInfo.FriendlyName = name;
                return true;
            }
        }

        /// <summary>
        /// Appends a numeric suffix if another session already uses the same name.
        /// Must be called while holding <see cref="s_lock"/>.
        /// </summary>
        private static string DeduplicateName(string name, string excludeSessionId)
        {
            bool IsTaken(string candidate)
            {
                foreach (var kvp in s_sessions)
                {
                    if (kvp.Key != excludeSessionId && kvp.Value.FriendlyName == candidate)
                        return true;
                }
                return false;
            }

            if (!IsTaken(name))
                return name;

            for (int i = 2; i < 100; i++)
            {
                string candidate = $"{name} ({i})";
                if (!IsTaken(candidate))
                    return candidate;
            }

            return name;
        }

        /// <summary>
        /// Internal pruning that assumes the lock is already held.
        /// Uses two-tier timeout: unused sessions (request_count == 0) expire after
        /// <see cref="UnusedSessionTimeout"/>, active sessions after <see cref="SessionTimeout"/>.
        /// Returns the list of pruned session IDs so the caller can release locks outside the lock.
        /// </summary>
        private static List<string> PruneExpiredSessionsInternal()
        {
            var now = DateTime.Now;
            var expiredSessionIds = s_sessions
                .Where(kvp =>
                {
                    var idleTime = now - kvp.Value.LastActivity;
                    var timeout = kvp.Value.RequestCount == 0 ? UnusedSessionTimeout : SessionTimeout;
                    return idleTime > timeout;
                })
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var sessionId in expiredSessionIds)
                s_sessions.Remove(sessionId);

            return expiredSessionIds;
        }
    }
}
