using System;
using System.Collections.Generic;
using System.Linq;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tools for acquiring, releasing, and querying resource locks across agents.
    /// </summary>
    public static class LockTools
    {
        /// <summary>
        /// Acquires an exclusive lock on a resource for the calling agent.
        /// </summary>
        [MCPTool("agent_lock_acquire", "Acquire an exclusive lock on a resource for the calling agent",
            IdempotentHint = true, DestructiveHint = false, Category = "Agent")]
        public static object Acquire(
            [MCPParam("resource", "Resource key to lock (e.g. 'gameobject:14220', 'file:Assets/Scripts/Foo.cs')", required: true)]
            string resource,
            [MCPParam("reason", "Reason for acquiring the lock")]
            string reason = null)
        {
            string currentSessionId = RequestContext.CurrentSessionId;
            if (string.IsNullOrEmpty(currentSessionId))
            {
                throw new MCPException("No active session. Cannot acquire lock.");
            }

            if (string.IsNullOrWhiteSpace(resource))
            {
                throw new MCPException("Resource key cannot be empty.");
            }

            if (LockManager.IsBlockedKey(resource))
            {
                throw new MCPException($"Resource key '{resource}' is too broad and cannot be locked. Use a specific key (e.g. 'file:Assets/Scripts/Foo.cs' instead of 'file:').");
            }

            bool lockAcquired = LockManager.AcquireLock(resource, currentSessionId, reason, isAutoLock: false);

            if (lockAcquired)
            {
                return new
                {
                    locked = true,
                    resource,
                    session_id = currentSessionId,
                    reason,
                    message = $"Lock acquired on '{resource}'"
                };
            }

            // Lock is held by another session — find the holder
            var existingLocks = LockManager.QueryLocks();
            var holdingLock = existingLocks.FirstOrDefault(lockInfo => lockInfo.ResourceKey == resource);

            if (holdingLock != null)
            {
                var holderSession = SessionManager.GetSession(holdingLock.SessionId);
                string holderName = holderSession?.FriendlyName ?? holdingLock.SessionId;

                return new
                {
                    locked = false,
                    resource,
                    held_by = new
                    {
                        session_id = holdingLock.SessionId,
                        name = holderName,
                        reason = holdingLock.Reason,
                        since = holdingLock.AcquiredAt.ToString("o")
                    }
                };
            }

            // Lock failed but no holder found (race condition or internal error)
            return new
            {
                locked = false,
                resource,
                message = "Failed to acquire lock. The resource may be temporarily unavailable."
            };
        }

        /// <summary>
        /// Releases a lock held by the calling agent on the specified resource.
        /// </summary>
        [MCPTool("agent_lock_release", "Release a lock held by the calling agent",
            IdempotentHint = true, DestructiveHint = false, Category = "Agent")]
        public static object Release(
            [MCPParam("resource", "Resource key to unlock", required: true)]
            string resource)
        {
            string currentSessionId = RequestContext.CurrentSessionId;
            if (string.IsNullOrEmpty(currentSessionId))
            {
                throw new MCPException("No active session. Cannot release lock.");
            }

            if (string.IsNullOrWhiteSpace(resource))
            {
                throw new MCPException("Resource key cannot be empty.");
            }

            bool wasReleased = LockManager.ReleaseLock(resource, currentSessionId);

            return new
            {
                released = wasReleased,
                resource,
                message = wasReleased
                    ? $"Lock released on '{resource}'"
                    : $"No lock held on '{resource}' by this session"
            };
        }

        /// <summary>
        /// Queries active locks, optionally filtered by resource key.
        /// </summary>
        [MCPTool("agent_lock_query", "Query active locks across all agents",
            ReadOnlyHint = true, IdempotentHint = true, Category = "Agent")]
        public static object Query(
            [MCPParam("resource", "Filter to a specific resource key. If omitted, returns all active locks.")]
            string resource = null)
        {
            if (!string.IsNullOrEmpty(resource))
            {
                // Query for a specific resource
                var allLocks = LockManager.QueryLocks();
                var matchingLock = allLocks.FirstOrDefault(lockInfo => lockInfo.ResourceKey == resource);

                if (matchingLock == null)
                {
                    return new
                    {
                        resource,
                        locked = false,
                        message = "Resource is not locked"
                    };
                }

                var holderSession = SessionManager.GetSession(matchingLock.SessionId);
                string holderName = holderSession?.FriendlyName ?? matchingLock.SessionId;

                return new
                {
                    resource,
                    locked = true,
                    held_by = new
                    {
                        session_id = matchingLock.SessionId,
                        name = holderName,
                        reason = matchingLock.Reason,
                        is_auto = matchingLock.IsAutoLock,
                        since = matchingLock.AcquiredAt.ToString("o")
                    }
                };
            }

            // Return all active locks grouped by session
            var activeLocks = LockManager.QueryLocks();
            var locksBySession = activeLocks
                .GroupBy(lockInfo => lockInfo.SessionId)
                .Select(sessionGroup =>
                {
                    var session = SessionManager.GetSession(sessionGroup.Key);
                    string sessionName = session?.FriendlyName ?? sessionGroup.Key;

                    var sessionLockEntries = sessionGroup.Select(lockInfo => new
                    {
                        resource = lockInfo.ResourceKey,
                        reason = lockInfo.Reason,
                        is_auto = lockInfo.IsAutoLock,
                        since = lockInfo.AcquiredAt.ToString("o")
                    }).ToList();

                    return new
                    {
                        session_id = sessionGroup.Key,
                        name = sessionName,
                        locks = sessionLockEntries
                    };
                }).ToList();

            return new
            {
                total_locks = activeLocks.Count,
                sessions = locksBySession
            };
        }
    }
}
