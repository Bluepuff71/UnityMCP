using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Manages per-resource locks for multi-agent coordination.
    /// Locks are always specific to a single resource (never broad).
    /// Thread-safe via lock on all dictionary access.
    /// </summary>
    public static class LockManager
    {
        public class LockInfo
        {
            public string ResourceKey;
            public string SessionId;
            public DateTime AcquiredAt;
            public string Reason;
            public bool IsAutoLock;
        }

        private static readonly Dictionary<string, LockInfo> s_locks = new Dictionary<string, LockInfo>();
        private static readonly object s_lock = new object();

        /// <summary>Keys that are too broad and should be rejected.</summary>
        private static readonly HashSet<string> s_blockedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "scene", "project", "assets", "Assets", "Assets/", "all" };

        /// <summary>Fired when locks are acquired or released. UI subscribes to trigger Repaint.</summary>
        public static event Action OnLocksChanged;

        /// <summary>
        /// Acquires a lock on a resource for a session. Reentrant: if already locked by the same
        /// session, returns true and updates the reason if different. Returns false if locked by
        /// a different session or if the key is blocked.
        /// </summary>
        /// <param name="resourceKey">The resource to lock (e.g., "gameobject:14220").</param>
        /// <param name="sessionId">The session acquiring the lock.</param>
        /// <param name="reason">Why the lock is being acquired.</param>
        /// <param name="isAutoLock">True if server-acquired, false if explicit.</param>
        /// <returns>True if the lock was acquired or already held by this session.</returns>
        public static bool AcquireLock(string resourceKey, string sessionId, string reason, bool isAutoLock = false)
        {
            if (IsBlockedKey(resourceKey))
                return false;

            bool lockChanged = false;

            lock (s_lock)
            {
                if (s_locks.TryGetValue(resourceKey, out var existingLock))
                {
                    if (existingLock.SessionId == sessionId)
                    {
                        // Reentrant: update reason if different
                        if (existingLock.Reason != reason)
                            existingLock.Reason = reason;
                        return true;
                    }

                    // Locked by another session
                    return false;
                }

                s_locks[resourceKey] = new LockInfo
                {
                    ResourceKey = resourceKey,
                    SessionId = sessionId,
                    AcquiredAt = DateTime.Now,
                    Reason = reason,
                    IsAutoLock = isAutoLock
                };
                lockChanged = true;
            }

            if (lockChanged)
                OnLocksChanged?.Invoke();

            return true;
        }

        /// <summary>
        /// Releases a lock on a resource. Only succeeds if the lock is held by the given session.
        /// </summary>
        /// <param name="resourceKey">The resource to unlock.</param>
        /// <param name="sessionId">The session releasing the lock.</param>
        /// <returns>True if the lock was released, false if not held by this session.</returns>
        public static bool ReleaseLock(string resourceKey, string sessionId)
        {
            bool released = false;

            lock (s_lock)
            {
                if (s_locks.TryGetValue(resourceKey, out var lockInfo) && lockInfo.SessionId == sessionId)
                {
                    s_locks.Remove(resourceKey);
                    released = true;
                }
            }

            if (released)
                OnLocksChanged?.Invoke();

            return released;
        }

        /// <summary>
        /// Releases all locks held by a given session. Called during session cleanup.
        /// </summary>
        /// <param name="sessionId">The session whose locks should be released.</param>
        public static void ReleaseAllForSession(string sessionId)
        {
            bool anyReleased = false;

            lock (s_lock)
            {
                var keysToRemove = s_locks
                    .Where(kvp => kvp.Value.SessionId == sessionId)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                    s_locks.Remove(key);

                anyReleased = keysToRemove.Count > 0;
            }

            if (anyReleased)
                OnLocksChanged?.Invoke();
        }

        /// <summary>
        /// Releases only auto-acquired locks for a session, preserving manual locks.
        /// Called after destructive tool execution completes.
        /// </summary>
        public static void ReleaseAutoLocks(string sessionId)
        {
            bool anyReleased = false;

            lock (s_lock)
            {
                var keysToRemove = s_locks
                    .Where(kvp => kvp.Value.SessionId == sessionId && kvp.Value.IsAutoLock == true)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                    s_locks.Remove(key);

                anyReleased = keysToRemove.Count > 0;
            }

            if (anyReleased)
                OnLocksChanged?.Invoke();
        }

        /// <summary>
        /// Returns a snapshot of locks, optionally filtered by session.
        /// </summary>
        /// <param name="sessionId">If provided, only return locks held by this session.</param>
        /// <returns>A list copy of matching <see cref="LockInfo"/> entries.</returns>
        public static List<LockInfo> QueryLocks(string sessionId = null)
        {
            lock (s_lock)
            {
                if (sessionId != null)
                    return s_locks.Values.Where(l => l.SessionId == sessionId).ToList();

                return s_locks.Values.ToList();
            }
        }

        /// <summary>
        /// Checks whether a resource is locked by a session other than the given one.
        /// </summary>
        /// <param name="resourceKey">The resource to check.</param>
        /// <param name="sessionId">The session to exclude from the check.</param>
        /// <returns>True if the resource is locked by a different session.</returns>
        public static bool IsLockedByOther(string resourceKey, string sessionId)
        {
            lock (s_lock)
            {
                if (s_locks.TryGetValue(resourceKey, out var lockInfo))
                    return lockInfo.SessionId != sessionId;

                return false;
            }
        }

        /// <summary>
        /// Returns the <see cref="LockInfo"/> for a resource, or null if not locked.
        /// </summary>
        /// <param name="resourceKey">The resource to look up.</param>
        public static LockInfo GetLockHolder(string resourceKey)
        {
            lock (s_lock)
            {
                s_locks.TryGetValue(resourceKey, out var lockInfo);
                return lockInfo;
            }
        }

        /// <summary>
        /// No-op for now. Locks persist for the lifetime of their session.
        /// Reserved for future time-based lock expiry.
        /// </summary>
        public static void PruneExpiredLocks()
        {
            // No-op: locks persist for session lifetime.
            // Future: add time-based expiry if needed.
        }

        /// <summary>
        /// Returns true if the resource key is too broad and should be rejected.
        /// Rejects keys in the blocked set and keys that are just a prefix type with no value
        /// (e.g., "gameobject:", "file:").
        /// </summary>
        /// <param name="resourceKey">The resource key to check.</param>
        public static bool IsBlockedKey(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey))
                return true;

            if (s_blockedKeys.Contains(resourceKey))
                return true;

            // Reject keys that are just a type prefix with no value (e.g., "gameobject:", "file:")
            int colonIndex = resourceKey.IndexOf(':');
            if (colonIndex >= 0 && colonIndex == resourceKey.Length - 1)
                return true;

            return false;
        }

        /// <summary>
        /// Extracts resource keys from tool arguments based on known parameter names.
        /// </summary>
        /// <param name="toolName">The tool being invoked.</param>
        /// <param name="arguments">The tool's arguments dictionary.</param>
        /// <returns>A list of resource keys (e.g., "file:Assets/Scripts/Foo.cs").</returns>
        public static List<string> ExtractResourceKeys(string toolName, Dictionary<string, object> arguments)
        {
            var resourceKeys = new List<string>();

            if (arguments == null)
                return resourceKeys;

            // file path arguments
            if (arguments.TryGetValue("path", out var pathValue) && pathValue != null)
            {
                var pathString = pathValue.ToString();
                if (!string.IsNullOrEmpty(pathString))
                    resourceKeys.Add($"file:{pathString}");
            }

            if (arguments.TryGetValue("file_path", out var filePathValue) && filePathValue != null)
            {
                var filePathString = filePathValue.ToString();
                if (!string.IsNullOrEmpty(filePathString))
                    resourceKeys.Add($"file:{filePathString}");
            }

            // instance_id argument (comes through as long from JSON deserialization)
            if (arguments.TryGetValue("instance_id", out var instanceIdValue) && instanceIdValue != null)
            {
                resourceKeys.Add($"gameobject:{instanceIdValue}");
            }

            // guid argument
            if (arguments.TryGetValue("guid", out var guidValue) && guidValue != null)
            {
                var guidString = guidValue.ToString();
                if (!string.IsNullOrEmpty(guidString))
                    resourceKeys.Add($"asset:{guidString}");
            }

            return resourceKeys;
        }

        /// <summary>
        /// Checks for lock conflicts and auto-acquires locks for a tool invocation.
        /// Returns null if all resources are available (or no recognizable resources),
        /// or an error string describing the conflict.
        /// </summary>
        /// <param name="toolName">The tool being invoked.</param>
        /// <param name="arguments">The tool's arguments dictionary.</param>
        /// <param name="sessionId">The session invoking the tool.</param>
        /// <returns>Null if OK, or an error message string if blocked.</returns>
        public static string CheckAndAutoLock(string toolName, Dictionary<string, object> arguments, string sessionId)
        {
            var resourceKeys = ExtractResourceKeys(toolName, arguments);

            if (resourceKeys.Count == 0)
                return null;

            bool lockChanged = false;
            var autoReason = $"auto:{toolName}";

            // Atomic check-and-acquire under a single lock to prevent race conditions
            lock (s_lock)
            {
                // Check all keys for conflicts first
                foreach (var resourceKey in resourceKeys)
                {
                    if (s_locks.TryGetValue(resourceKey, out var existingLock) && existingLock.SessionId != sessionId)
                    {
                        var holderSession = SessionManager.GetSession(existingLock.SessionId);
                        var holderName = holderSession?.FriendlyName ?? "unknown";

                        return $"Resource '{resourceKey}' is locked by {holderName} ({existingLock.SessionId}). " +
                               $"Reason: {existingLock.Reason}. Use agent_lock_query for details.";
                    }
                }

                // No conflicts — auto-acquire all keys
                foreach (var resourceKey in resourceKeys)
                {
                    if (!s_locks.ContainsKey(resourceKey))
                    {
                        s_locks[resourceKey] = new LockInfo
                        {
                            ResourceKey = resourceKey,
                            SessionId = sessionId,
                            AcquiredAt = DateTime.Now,
                            Reason = autoReason,
                            IsAutoLock = true
                        };
                        lockChanged = true;
                    }
                }
            }

            if (lockChanged)
                OnLocksChanged?.Invoke();

            return null;
        }
    }
}
