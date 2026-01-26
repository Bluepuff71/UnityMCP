using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Services
{
    /// <summary>
    /// Status of a test job.
    /// </summary>
    public enum TestJobStatus
    {
        Running,
        Succeeded,
        Failed
    }

    /// <summary>
    /// Represents a single test failure.
    /// </summary>
    [Serializable]
    public class TestFailure
    {
        public string testFullName;
        public string message;
        public string stackTrace;

        public object ToSerializable()
        {
            return new
            {
                test_full_name = testFullName,
                message,
                stack_trace = stackTrace
            };
        }
    }

    /// <summary>
    /// Represents the result of a completed test run.
    /// </summary>
    [Serializable]
    public class TestRunResult
    {
        public int passed;
        public int failed;
        public int skipped;
        public int inconclusive;
        public double durationSeconds;
        public List<TestFailure> failures = new List<TestFailure>();

        public object ToSerializable(bool includeFailedTests)
        {
            var result = new Dictionary<string, object>
            {
                { "passed", passed },
                { "failed", failed },
                { "skipped", skipped },
                { "inconclusive", inconclusive },
                { "duration_seconds", Math.Round(durationSeconds, 3) }
            };

            if (includeFailedTests && failures.Count > 0)
            {
                result["failures"] = failures.Select(f => f.ToSerializable()).ToList();
            }

            return result;
        }
    }

    /// <summary>
    /// Represents a test job that tracks the state of an asynchronous test run.
    /// </summary>
    [Serializable]
    public class TestJob
    {
        /// <summary>
        /// Maximum number of failures to track in a job to prevent memory issues.
        /// </summary>
        public const int MaxFailures = 25;

        public string jobId;
        public TestJobStatus status;
        public string mode;
        public long startedUnixMs;
        public long finishedUnixMs;
        public long lastUpdateUnixMs;
        public int totalTests;
        public int completedTests;
        public string currentTestFullName;
        public List<TestFailure> failuresSoFar = new List<TestFailure>();
        public string error;
        public TestRunResult result;

        /// <summary>
        /// Creates a new test job.
        /// </summary>
        public static TestJob Create(string mode)
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new TestJob
            {
                jobId = Guid.NewGuid().ToString("N").Substring(0, 12),
                status = TestJobStatus.Running,
                mode = mode,
                startedUnixMs = nowMs,
                lastUpdateUnixMs = nowMs
            };
        }

        /// <summary>
        /// Called when a test run starts.
        /// </summary>
        public void OnRunStarted(int testCount)
        {
            totalTests = testCount;
            lastUpdateUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Called when an individual test starts.
        /// </summary>
        public void OnTestStarted(string testFullName)
        {
            currentTestFullName = testFullName;
            lastUpdateUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Called when a leaf test finishes (not a fixture or suite).
        /// </summary>
        public void OnLeafTestFinished(string testFullName, bool passed, string message, string stackTrace)
        {
            completedTests++;
            lastUpdateUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (!passed && failuresSoFar.Count < MaxFailures)
            {
                failuresSoFar.Add(new TestFailure
                {
                    testFullName = testFullName,
                    message = message,
                    stackTrace = stackTrace
                });
            }
        }

        /// <summary>
        /// Called when the entire test run finishes.
        /// </summary>
        public void OnRunFinished(TestRunResult runResult)
        {
            result = runResult;
            finishedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lastUpdateUnixMs = finishedUnixMs;
            status = runResult.failed > 0 ? TestJobStatus.Failed : TestJobStatus.Succeeded;
            currentTestFullName = null;
        }

        /// <summary>
        /// Marks the job as failed with an error message.
        /// </summary>
        public void SetError(string errorMessage)
        {
            error = errorMessage;
            status = TestJobStatus.Failed;
            finishedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lastUpdateUnixMs = finishedUnixMs;
        }

        /// <summary>
        /// Checks if a test appears to be stuck (no progress for 60 seconds).
        /// </summary>
        public bool IsStuckSuspected()
        {
            if (status != TestJobStatus.Running)
            {
                return false;
            }

            const long stuckThresholdMs = 60000; // 60 seconds
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return (nowMs - lastUpdateUnixMs) > stuckThresholdMs;
        }

        /// <summary>
        /// Checks if the job is orphaned (started but no updates for 5 minutes).
        /// </summary>
        public bool IsOrphaned()
        {
            if (status != TestJobStatus.Running)
            {
                return false;
            }

            const long orphanThresholdMs = 300000; // 5 minutes
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return (nowMs - lastUpdateUnixMs) > orphanThresholdMs;
        }

        /// <summary>
        /// Converts the job to a serializable format for JSON output.
        /// </summary>
        public object ToSerializable(bool includeDetails, bool includeFailedTests)
        {
            var jobData = new Dictionary<string, object>
            {
                { "job_id", jobId },
                { "status", status.ToString().ToLowerInvariant() },
                { "mode", mode },
                { "started_unix_ms", startedUnixMs }
            };

            if (finishedUnixMs > 0)
            {
                jobData["finished_unix_ms"] = finishedUnixMs;
            }

            // Progress information
            var progressData = new Dictionary<string, object>
            {
                { "completed", completedTests },
                { "total", totalTests }
            };

            if (!string.IsNullOrEmpty(currentTestFullName))
            {
                progressData["current_test_full_name"] = currentTestFullName;
            }

            progressData["stuck_suspected"] = IsStuckSuspected();

            if (includeFailedTests && failuresSoFar.Count > 0)
            {
                progressData["failures_so_far"] = failuresSoFar.Select(f => f.ToSerializable()).ToList();
            }

            jobData["progress"] = progressData;

            // Error information
            if (!string.IsNullOrEmpty(error))
            {
                jobData["error"] = error;
            }

            // Result information (only when complete)
            if (result != null && includeDetails)
            {
                jobData["result"] = result.ToSerializable(includeFailedTests);
            }

            return jobData;
        }
    }

    /// <summary>
    /// Serializable container for jobs stored in SessionState.
    /// </summary>
    [Serializable]
    internal class TestJobStorage
    {
        public List<TestJob> jobs = new List<TestJob>();
    }

    /// <summary>
    /// Manages asynchronous test job state, persisting across domain reloads via SessionState.
    /// </summary>
    public static class TestJobManager
    {
        private const string SessionStateKey = "UnityMCP.TestJobManager.Jobs";
        private const int MaxJobsToKeep = 10;
        private static readonly object LockObject = new object();

        private static TestJob _currentJob;
        private static TestJobStorage _storage;

        /// <summary>
        /// Gets the currently running job, if any.
        /// </summary>
        public static TestJob CurrentJob
        {
            get
            {
                EnsureInitialized();
                return _currentJob;
            }
        }

        /// <summary>
        /// Checks if a test run is currently in progress.
        /// </summary>
        public static bool IsRunning
        {
            get
            {
                EnsureInitialized();
                return _currentJob != null && _currentJob.status == TestJobStatus.Running;
            }
        }

        /// <summary>
        /// Initialize from SessionState when domain reloads.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            LoadFromSessionState();
            CleanupOrphanedJobs();
        }

        private static void EnsureInitialized()
        {
            if (_storage == null)
            {
                LoadFromSessionState();
            }
        }

        private static void LoadFromSessionState()
        {
            lock (LockObject)
            {
                string json = SessionState.GetString(SessionStateKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        _storage = JsonUtility.FromJson<TestJobStorage>(json);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[TestJobManager] Failed to load jobs from SessionState: {exception.Message}");
                        _storage = new TestJobStorage();
                    }
                }
                else
                {
                    _storage = new TestJobStorage();
                }

                // Find the current running job
                _currentJob = _storage.jobs.FirstOrDefault(j => j.status == TestJobStatus.Running);
            }
        }

        private static void SaveToSessionState()
        {
            lock (LockObject)
            {
                if (_storage == null)
                {
                    _storage = new TestJobStorage();
                }

                // Enforce max jobs limit
                while (_storage.jobs.Count > MaxJobsToKeep)
                {
                    // Remove oldest completed job
                    var oldestCompleted = _storage.jobs
                        .Where(j => j.status != TestJobStatus.Running)
                        .OrderBy(j => j.startedUnixMs)
                        .FirstOrDefault();

                    if (oldestCompleted != null)
                    {
                        _storage.jobs.Remove(oldestCompleted);
                    }
                    else
                    {
                        break;
                    }
                }

                string json = JsonUtility.ToJson(_storage);
                SessionState.SetString(SessionStateKey, json);
            }
        }

        private static void CleanupOrphanedJobs()
        {
            lock (LockObject)
            {
                if (_storage == null)
                {
                    return;
                }

                foreach (var job in _storage.jobs.Where(j => j.status == TestJobStatus.Running))
                {
                    if (job.IsOrphaned())
                    {
                        job.SetError("Job was orphaned after domain reload (no progress for 5 minutes)");
                        Debug.LogWarning($"[TestJobManager] Marked orphaned job {job.jobId} as failed");
                    }
                }

                // Update current job reference after cleanup
                _currentJob = _storage.jobs.FirstOrDefault(j => j.status == TestJobStatus.Running);
                SaveToSessionState();
            }
        }

        /// <summary>
        /// Starts a new test job. Returns null if a job is already running.
        /// </summary>
        public static TestJob StartJob(string mode)
        {
            lock (LockObject)
            {
                EnsureInitialized();

                // Check for running job
                if (_currentJob != null && _currentJob.status == TestJobStatus.Running)
                {
                    // Check if it's orphaned
                    if (_currentJob.IsOrphaned())
                    {
                        _currentJob.SetError("Job was orphaned (no progress for 5 minutes)");
                    }
                    else
                    {
                        return null; // Another job is still running
                    }
                }

                // Create new job
                _currentJob = TestJob.Create(mode);
                _storage.jobs.Add(_currentJob);
                SaveToSessionState();

                return _currentJob;
            }
        }

        /// <summary>
        /// Gets a job by ID.
        /// </summary>
        public static TestJob GetJob(string jobId)
        {
            lock (LockObject)
            {
                EnsureInitialized();
                return _storage.jobs.FirstOrDefault(j => j.jobId == jobId);
            }
        }

        /// <summary>
        /// Called when a test run starts.
        /// </summary>
        public static void OnRunStarted(int testCount)
        {
            lock (LockObject)
            {
                if (_currentJob != null)
                {
                    _currentJob.OnRunStarted(testCount);
                    SaveToSessionState();
                }
            }
        }

        /// <summary>
        /// Called when an individual test starts.
        /// </summary>
        public static void OnTestStarted(string testFullName)
        {
            lock (LockObject)
            {
                if (_currentJob != null)
                {
                    _currentJob.OnTestStarted(testFullName);
                    SaveToSessionState();
                }
            }
        }

        /// <summary>
        /// Called when a leaf test finishes.
        /// </summary>
        public static void OnLeafTestFinished(string testFullName, bool passed, string message, string stackTrace)
        {
            lock (LockObject)
            {
                if (_currentJob != null)
                {
                    _currentJob.OnLeafTestFinished(testFullName, passed, message, stackTrace);
                    SaveToSessionState();
                }
            }
        }

        /// <summary>
        /// Called when the entire test run finishes.
        /// </summary>
        public static void OnRunFinished(TestRunResult runResult)
        {
            lock (LockObject)
            {
                if (_currentJob != null)
                {
                    _currentJob.OnRunFinished(runResult);
                    SaveToSessionState();
                    _currentJob = null;
                }
            }
        }

        /// <summary>
        /// Finalizes the current job from a run finished callback.
        /// </summary>
        public static void FinalizeCurrentJobFromRunFinished(TestRunResult runResult)
        {
            OnRunFinished(runResult);
        }

        /// <summary>
        /// Marks the current job as failed with an error.
        /// </summary>
        public static void SetCurrentJobError(string errorMessage)
        {
            lock (LockObject)
            {
                if (_currentJob != null)
                {
                    _currentJob.SetError(errorMessage);
                    SaveToSessionState();
                    _currentJob = null;
                }
            }
        }
    }
}
