using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityMCP.Editor.Services
{
    /// <summary>
    /// Status of a build job.
    /// </summary>
    public enum BuildJobStatus
    {
        Building,
        Succeeded,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Represents a build step from the build report.
    /// </summary>
    [Serializable]
    public class BuildStepInfo
    {
        public string name;
        public int depth;
        public double durationSeconds;
        public int messageCount;
        public int warningCount;
        public int errorCount;

        public object ToSerializable()
        {
            return new
            {
                name,
                depth,
                duration_seconds = Math.Round(durationSeconds, 3),
                message_count = messageCount,
                warning_count = warningCount,
                error_count = errorCount
            };
        }
    }

    /// <summary>
    /// Represents a build message (error or warning).
    /// </summary>
    [Serializable]
    public class BuildMessageInfo
    {
        public string type;
        public string message;
        public string file;
        public int line;

        public object ToSerializable()
        {
            var result = new Dictionary<string, object>
            {
                { "type", type },
                { "message", message }
            };

            if (!string.IsNullOrEmpty(file))
            {
                result["file"] = file;
                if (line > 0)
                {
                    result["line"] = line;
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Represents the result of a completed build.
    /// </summary>
    [Serializable]
    public class BuildResult
    {
        public string summary;
        public string outputPath;
        public long totalSizeBytes;
        public double totalDurationSeconds;
        public int totalErrors;
        public int totalWarnings;
        public List<BuildStepInfo> steps = new List<BuildStepInfo>();
        public List<BuildMessageInfo> errors = new List<BuildMessageInfo>();
        public List<BuildMessageInfo> warnings = new List<BuildMessageInfo>();

        public object ToSerializable(bool includeDetails)
        {
            var result = new Dictionary<string, object>
            {
                { "summary", summary },
                { "output_path", outputPath },
                { "total_size_bytes", totalSizeBytes },
                { "total_duration_seconds", Math.Round(totalDurationSeconds, 3) },
                { "total_errors", totalErrors },
                { "total_warnings", totalWarnings }
            };

            if (includeDetails)
            {
                if (steps.Count > 0)
                {
                    result["steps"] = steps.Select(s => s.ToSerializable()).ToList();
                }

                if (errors.Count > 0)
                {
                    result["errors"] = errors.Select(e => e.ToSerializable()).ToList();
                }

                if (warnings.Count > 0)
                {
                    // Limit warnings to avoid huge responses
                    int warningsToInclude = Math.Min(warnings.Count, 50);
                    result["warnings"] = warnings.Take(warningsToInclude).Select(w => w.ToSerializable()).ToList();

                    if (warnings.Count > warningsToInclude)
                    {
                        result["warnings_truncated"] = true;
                        result["warnings_total"] = warnings.Count;
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Represents a build job that tracks the state of an asynchronous build.
    /// </summary>
    [Serializable]
    public class BuildJob
    {
        /// <summary>
        /// Maximum number of errors/warnings to store to prevent memory issues.
        /// </summary>
        public const int MaxMessagesToStore = 100;

        public string jobId;
        public BuildJobStatus status;
        public string target;
        public string outputPath;
        public bool development;
        public long startedUnixMs;
        public long finishedUnixMs;
        public long lastUpdateUnixMs;
        public string error;
        public List<string> scenes = new List<string>();

        // Result stored separately to avoid serialization overhead
        [NonSerialized]
        public BuildResult result;

        /// <summary>
        /// Creates a new build job.
        /// </summary>
        public static BuildJob Create(string target, string outputPath, List<string> scenes, bool development)
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new BuildJob
            {
                jobId = Guid.NewGuid().ToString("N").Substring(0, 12),
                status = BuildJobStatus.Building,
                target = target,
                outputPath = outputPath,
                scenes = scenes ?? new List<string>(),
                development = development,
                startedUnixMs = nowMs,
                lastUpdateUnixMs = nowMs
            };
        }

        /// <summary>
        /// Marks the job as completed with a build report.
        /// </summary>
        public void Complete(BuildReport report)
        {
            finishedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lastUpdateUnixMs = finishedUnixMs;

            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                status = BuildJobStatus.Succeeded;
            }
            else if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Cancelled)
            {
                status = BuildJobStatus.Cancelled;
            }
            else
            {
                status = BuildJobStatus.Failed;
            }

            result = ExtractBuildResult(report);
        }

        /// <summary>
        /// Marks the job as failed with an error message.
        /// </summary>
        public void SetError(string errorMessage)
        {
            error = errorMessage;
            status = BuildJobStatus.Failed;
            finishedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lastUpdateUnixMs = finishedUnixMs;
        }

        /// <summary>
        /// Gets elapsed build time in seconds.
        /// </summary>
        public double GetElapsedSeconds()
        {
            long endMs = status == BuildJobStatus.Building
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                : finishedUnixMs;
            return (endMs - startedUnixMs) / 1000.0;
        }

        private BuildResult ExtractBuildResult(BuildReport report)
        {
            var buildResult = new BuildResult
            {
                summary = report.summary.result.ToString(),
                outputPath = report.summary.outputPath,
                totalSizeBytes = (long)report.summary.totalSize,
                totalDurationSeconds = report.summary.totalTime.TotalSeconds,
                totalErrors = report.summary.totalErrors,
                totalWarnings = report.summary.totalWarnings
            };

            // Extract build steps
            foreach (var step in report.steps)
            {
                buildResult.steps.Add(new BuildStepInfo
                {
                    name = step.name,
                    depth = step.depth,
                    durationSeconds = step.duration.TotalSeconds,
                    messageCount = step.messages.Length,
                    warningCount = step.messages.Count(m => m.type == LogType.Warning),
                    errorCount = step.messages.Count(m => m.type == LogType.Error)
                });
            }

            // Extract errors and warnings from all steps
            int errorCount = 0;
            int warningCount = 0;

            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error && errorCount < MaxMessagesToStore)
                    {
                        buildResult.errors.Add(new BuildMessageInfo
                        {
                            type = "error",
                            message = message.content
                        });
                        errorCount++;
                    }
                    else if (message.type == LogType.Warning && warningCount < MaxMessagesToStore)
                    {
                        buildResult.warnings.Add(new BuildMessageInfo
                        {
                            type = "warning",
                            message = message.content
                        });
                        warningCount++;
                    }
                }
            }

            return buildResult;
        }

        /// <summary>
        /// Converts the job to a serializable format for JSON output.
        /// </summary>
        public object ToSerializable(bool includeDetails)
        {
            var jobData = new Dictionary<string, object>
            {
                { "job_id", jobId },
                { "status", status.ToString().ToLowerInvariant() },
                { "target", target },
                { "output_path", outputPath },
                { "development", development },
                { "started_unix_ms", startedUnixMs },
                { "elapsed_seconds", Math.Round(GetElapsedSeconds(), 2) }
            };

            if (scenes.Count > 0)
            {
                jobData["scenes"] = scenes;
            }

            if (finishedUnixMs > 0)
            {
                jobData["finished_unix_ms"] = finishedUnixMs;
            }

            if (!string.IsNullOrEmpty(error))
            {
                jobData["error"] = error;
            }

            if (result != null)
            {
                jobData["result"] = result.ToSerializable(includeDetails);
            }

            return jobData;
        }
    }

    /// <summary>
    /// Serializable container for jobs stored in SessionState.
    /// </summary>
    [Serializable]
    internal class BuildJobStorage
    {
        public List<BuildJob> jobs = new List<BuildJob>();
    }

    /// <summary>
    /// Manages asynchronous build job state, persisting across domain reloads via SessionState.
    /// </summary>
    public static class BuildJobManager
    {
        private const string SessionStateKey = "UnityMCP.BuildJobManager.Jobs";
        private const int MaxJobsToKeep = 5;
        private static readonly object LockObject = new object();

        private static BuildJob _currentJob;
        private static BuildJobStorage _storage;

        /// <summary>
        /// Gets the currently building job, if any.
        /// </summary>
        public static BuildJob CurrentJob
        {
            get
            {
                EnsureInitialized();
                return _currentJob;
            }
        }

        /// <summary>
        /// Checks if a build is currently in progress.
        /// </summary>
        public static bool IsBuilding
        {
            get
            {
                EnsureInitialized();
                return _currentJob != null && _currentJob.status == BuildJobStatus.Building;
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
                        _storage = JsonUtility.FromJson<BuildJobStorage>(json);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[BuildJobManager] Failed to load jobs from SessionState: {exception.Message}");
                        _storage = new BuildJobStorage();
                    }
                }
                else
                {
                    _storage = new BuildJobStorage();
                }

                // Find the current building job
                _currentJob = _storage.jobs.FirstOrDefault(j => j.status == BuildJobStatus.Building);
            }
        }

        private static void SaveToSessionState()
        {
            lock (LockObject)
            {
                if (_storage == null)
                {
                    _storage = new BuildJobStorage();
                }

                // Enforce max jobs limit
                while (_storage.jobs.Count > MaxJobsToKeep)
                {
                    // Remove oldest completed job
                    var oldestCompleted = _storage.jobs
                        .Where(j => j.status != BuildJobStatus.Building)
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

                // Mark any building jobs as failed since builds don't survive domain reloads
                foreach (var job in _storage.jobs.Where(j => j.status == BuildJobStatus.Building))
                {
                    job.SetError("Build was interrupted by domain reload");
                    Debug.LogWarning($"[BuildJobManager] Marked interrupted build job {job.jobId} as failed");
                }

                _currentJob = null;
                SaveToSessionState();
            }
        }

        /// <summary>
        /// Starts a new build job.
        /// </summary>
        public static BuildJob StartJob(string target, string outputPath, List<string> scenes, bool development)
        {
            lock (LockObject)
            {
                EnsureInitialized();

                // Check for existing build
                if (_currentJob != null && _currentJob.status == BuildJobStatus.Building)
                {
                    return null; // Another build is in progress
                }

                // Create new job
                _currentJob = BuildJob.Create(target, outputPath, scenes, development);
                _storage.jobs.Add(_currentJob);
                SaveToSessionState();

                return _currentJob;
            }
        }

        /// <summary>
        /// Completes the current build job with a report.
        /// </summary>
        public static void CompleteJob(BuildReport report)
        {
            lock (LockObject)
            {
                if (_currentJob == null)
                {
                    return;
                }

                _currentJob.Complete(report);
                SaveToSessionState();
                _currentJob = null;
            }
        }

        /// <summary>
        /// Marks the current job as failed with an error.
        /// </summary>
        public static void SetCurrentJobError(string errorMessage)
        {
            lock (LockObject)
            {
                if (_currentJob == null)
                {
                    return;
                }

                _currentJob.SetError(errorMessage);
                SaveToSessionState();
                _currentJob = null;
            }
        }

        /// <summary>
        /// Gets a job by ID.
        /// </summary>
        public static BuildJob GetJob(string jobId)
        {
            lock (LockObject)
            {
                EnsureInitialized();
                return _storage.jobs.FirstOrDefault(j => j.jobId == jobId);
            }
        }
    }
}
