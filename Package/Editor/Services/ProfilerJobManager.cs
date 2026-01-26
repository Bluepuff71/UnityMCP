using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;

namespace UnityMCP.Editor.Services
{
    /// <summary>
    /// Status of a profiler job.
    /// </summary>
    public enum ProfilerJobStatus
    {
        Recording,
        Completed,
        Failed
    }

    /// <summary>
    /// Represents a captured profiler frame.
    /// </summary>
    [Serializable]
    public class ProfilerFrameData
    {
        public int frameIndex;
        public float frameTimeMs;
        public float cpuTimeMs;
        public float gpuTimeMs;
        public int drawCalls;
        public int triangles;
        public int vertices;
        public long totalAllocatedMemoryBytes;
        public long gcAllocatedMemoryBytes;

        public object ToSerializable()
        {
            return new
            {
                frame_index = frameIndex,
                frame_time_ms = Math.Round(frameTimeMs, 3),
                cpu_time_ms = Math.Round(cpuTimeMs, 3),
                gpu_time_ms = Math.Round(gpuTimeMs, 3),
                draw_calls = drawCalls,
                triangles,
                vertices,
                total_allocated_memory_bytes = totalAllocatedMemoryBytes,
                gc_allocated_memory_bytes = gcAllocatedMemoryBytes
            };
        }
    }

    /// <summary>
    /// Represents aggregated profiler statistics.
    /// </summary>
    [Serializable]
    public class ProfilerStatistics
    {
        public int frameCount;
        public float averageFrameTimeMs;
        public float minFrameTimeMs;
        public float maxFrameTimeMs;
        public float averageCpuTimeMs;
        public float averageGpuTimeMs;
        public int averageDrawCalls;
        public long peakAllocatedMemoryBytes;
        public long totalGcAllocatedBytes;

        public object ToSerializable()
        {
            return new
            {
                frame_count = frameCount,
                average_frame_time_ms = Math.Round(averageFrameTimeMs, 3),
                min_frame_time_ms = Math.Round(minFrameTimeMs, 3),
                max_frame_time_ms = Math.Round(maxFrameTimeMs, 3),
                average_cpu_time_ms = Math.Round(averageCpuTimeMs, 3),
                average_gpu_time_ms = Math.Round(averageGpuTimeMs, 3),
                average_draw_calls = averageDrawCalls,
                peak_allocated_memory_bytes = peakAllocatedMemoryBytes,
                total_gc_allocated_bytes = totalGcAllocatedBytes
            };
        }
    }

    /// <summary>
    /// Represents a profiler job that tracks the state of an asynchronous profiler recording.
    /// </summary>
    [Serializable]
    public class ProfilerJob
    {
        /// <summary>
        /// Maximum number of frames to store to prevent memory issues.
        /// </summary>
        public const int MaxFramesToStore = 500;

        public string jobId;
        public ProfilerJobStatus status;
        public long startedUnixMs;
        public long finishedUnixMs;
        public long lastUpdateUnixMs;
        public int targetDurationSeconds;
        public int capturedFrameCount;
        public string error;
        public bool includeFrameDetails;

        // Frame data stored separately to avoid serialization overhead
        [NonSerialized]
        public List<ProfilerFrameData> frameData = new List<ProfilerFrameData>();

        // Computed statistics
        [NonSerialized]
        public ProfilerStatistics statistics;

        /// <summary>
        /// Creates a new profiler job.
        /// </summary>
        public static ProfilerJob Create(int durationSeconds, bool includeFrameDetails)
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new ProfilerJob
            {
                jobId = Guid.NewGuid().ToString("N").Substring(0, 12),
                status = ProfilerJobStatus.Recording,
                startedUnixMs = nowMs,
                lastUpdateUnixMs = nowMs,
                targetDurationSeconds = durationSeconds,
                capturedFrameCount = 0,
                includeFrameDetails = includeFrameDetails,
                frameData = new List<ProfilerFrameData>()
            };
        }

        /// <summary>
        /// Adds frame data to the job.
        /// </summary>
        public void AddFrameData(ProfilerFrameData frame)
        {
            if (frameData == null)
            {
                frameData = new List<ProfilerFrameData>();
            }

            if (frameData.Count < MaxFramesToStore)
            {
                frameData.Add(frame);
            }
            capturedFrameCount++;
            lastUpdateUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Completes the job and computes statistics.
        /// </summary>
        public void Complete()
        {
            status = ProfilerJobStatus.Completed;
            finishedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lastUpdateUnixMs = finishedUnixMs;
            ComputeStatistics();
        }

        /// <summary>
        /// Marks the job as failed with an error message.
        /// </summary>
        public void SetError(string errorMessage)
        {
            error = errorMessage;
            status = ProfilerJobStatus.Failed;
            finishedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lastUpdateUnixMs = finishedUnixMs;
        }

        /// <summary>
        /// Checks if the recording duration has exceeded the target.
        /// </summary>
        public bool HasExceededDuration()
        {
            if (status != ProfilerJobStatus.Recording)
            {
                return false;
            }

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long elapsedMs = nowMs - startedUnixMs;
            return elapsedMs >= (targetDurationSeconds * 1000);
        }

        /// <summary>
        /// Gets elapsed recording time in seconds.
        /// </summary>
        public double GetElapsedSeconds()
        {
            long endMs = status == ProfilerJobStatus.Recording
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                : finishedUnixMs;
            return (endMs - startedUnixMs) / 1000.0;
        }

        private void ComputeStatistics()
        {
            if (frameData == null || frameData.Count == 0)
            {
                statistics = new ProfilerStatistics { frameCount = capturedFrameCount };
                return;
            }

            statistics = new ProfilerStatistics
            {
                frameCount = capturedFrameCount,
                averageFrameTimeMs = frameData.Average(f => f.frameTimeMs),
                minFrameTimeMs = frameData.Min(f => f.frameTimeMs),
                maxFrameTimeMs = frameData.Max(f => f.frameTimeMs),
                averageCpuTimeMs = frameData.Average(f => f.cpuTimeMs),
                averageGpuTimeMs = frameData.Average(f => f.gpuTimeMs),
                averageDrawCalls = (int)frameData.Average(f => f.drawCalls),
                peakAllocatedMemoryBytes = frameData.Max(f => f.totalAllocatedMemoryBytes),
                totalGcAllocatedBytes = frameData.Sum(f => f.gcAllocatedMemoryBytes)
            };
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
                { "started_unix_ms", startedUnixMs },
                { "target_duration_seconds", targetDurationSeconds },
                { "elapsed_seconds", Math.Round(GetElapsedSeconds(), 2) },
                { "captured_frame_count", capturedFrameCount }
            };

            if (finishedUnixMs > 0)
            {
                jobData["finished_unix_ms"] = finishedUnixMs;
            }

            if (!string.IsNullOrEmpty(error))
            {
                jobData["error"] = error;
            }

            if (status == ProfilerJobStatus.Completed && statistics != null)
            {
                jobData["statistics"] = statistics.ToSerializable();
            }

            if (includeDetails && includeFrameDetails && frameData != null && frameData.Count > 0)
            {
                // Limit frame details to avoid huge responses
                int framesToInclude = Math.Min(frameData.Count, 100);
                jobData["frame_data"] = frameData
                    .Take(framesToInclude)
                    .Select(f => f.ToSerializable())
                    .ToList();

                if (frameData.Count > framesToInclude)
                {
                    jobData["frame_data_truncated"] = true;
                    jobData["frame_data_total"] = frameData.Count;
                }
            }

            return jobData;
        }
    }

    /// <summary>
    /// Serializable container for jobs stored in SessionState.
    /// </summary>
    [Serializable]
    internal class ProfilerJobStorage
    {
        public List<ProfilerJob> jobs = new List<ProfilerJob>();
    }

    /// <summary>
    /// Manages asynchronous profiler job state, persisting across domain reloads via SessionState.
    /// </summary>
    public static class ProfilerJobManager
    {
        private const string SessionStateKey = "UnityMCP.ProfilerJobManager.Jobs";
        private const int MaxJobsToKeep = 5;
        private static readonly object LockObject = new object();

        private static ProfilerJob _currentJob;
        private static ProfilerJobStorage _storage;
        private static bool _isRecording;

        // ProfilerRecorders for capturing data
        private static ProfilerRecorder _frameTimeRecorder;
        private static ProfilerRecorder _cpuTimeRecorder;
        private static ProfilerRecorder _drawCallsRecorder;
        private static ProfilerRecorder _trianglesRecorder;
        private static ProfilerRecorder _verticesRecorder;
        private static ProfilerRecorder _totalMemoryRecorder;
        private static ProfilerRecorder _gcMemoryRecorder;

        /// <summary>
        /// Gets the currently recording job, if any.
        /// </summary>
        public static ProfilerJob CurrentJob
        {
            get
            {
                EnsureInitialized();
                return _currentJob;
            }
        }

        /// <summary>
        /// Checks if a profiler recording is currently in progress.
        /// </summary>
        public static bool IsRecording
        {
            get
            {
                EnsureInitialized();
                return _isRecording && _currentJob != null && _currentJob.status == ProfilerJobStatus.Recording;
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
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
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
                        _storage = JsonUtility.FromJson<ProfilerJobStorage>(json);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[ProfilerJobManager] Failed to load jobs from SessionState: {exception.Message}");
                        _storage = new ProfilerJobStorage();
                    }
                }
                else
                {
                    _storage = new ProfilerJobStorage();
                }

                // Find the current recording job
                _currentJob = _storage.jobs.FirstOrDefault(j => j.status == ProfilerJobStatus.Recording);
                _isRecording = _currentJob != null;
            }
        }

        private static void SaveToSessionState()
        {
            lock (LockObject)
            {
                if (_storage == null)
                {
                    _storage = new ProfilerJobStorage();
                }

                // Enforce max jobs limit
                while (_storage.jobs.Count > MaxJobsToKeep)
                {
                    var oldestCompleted = _storage.jobs
                        .Where(j => j.status != ProfilerJobStatus.Recording)
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

                foreach (var job in _storage.jobs.Where(j => j.status == ProfilerJobStatus.Recording))
                {
                    // If we have a recording job but _isRecording is false, it was orphaned
                    if (!_isRecording)
                    {
                        job.SetError("Job was orphaned after domain reload");
                        Debug.LogWarning($"[ProfilerJobManager] Marked orphaned job {job.jobId} as failed");
                    }
                }

                _currentJob = _storage.jobs.FirstOrDefault(j => j.status == ProfilerJobStatus.Recording);
                SaveToSessionState();
            }
        }

        private static void OnEditorUpdate()
        {
            if (!_isRecording || _currentJob == null)
            {
                return;
            }

            // Check if duration exceeded
            if (_currentJob.HasExceededDuration())
            {
                StopRecording();
                return;
            }

            // Capture frame data
            CaptureFrameData();
        }

        private static void CaptureFrameData()
        {
            if (_currentJob == null || !_isRecording)
            {
                return;
            }

            try
            {
                var frameData = new ProfilerFrameData
                {
                    frameIndex = _currentJob.capturedFrameCount,
                    frameTimeMs = _frameTimeRecorder.Valid ? _frameTimeRecorder.LastValue / 1000000f : 0f,
                    cpuTimeMs = _cpuTimeRecorder.Valid ? _cpuTimeRecorder.LastValue / 1000000f : 0f,
                    gpuTimeMs = 0f, // GPU time requires different approach
                    drawCalls = _drawCallsRecorder.Valid ? (int)_drawCallsRecorder.LastValue : 0,
                    triangles = _trianglesRecorder.Valid ? (int)_trianglesRecorder.LastValue : 0,
                    vertices = _verticesRecorder.Valid ? (int)_verticesRecorder.LastValue : 0,
                    totalAllocatedMemoryBytes = _totalMemoryRecorder.Valid ? _totalMemoryRecorder.LastValue : 0,
                    gcAllocatedMemoryBytes = _gcMemoryRecorder.Valid ? _gcMemoryRecorder.LastValue : 0
                };

                _currentJob.AddFrameData(frameData);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ProfilerJobManager] Error capturing frame data: {exception.Message}");
            }
        }

        private static void StartRecorders()
        {
            try
            {
                _frameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
                _cpuTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Total Frame Time", 1);
                _drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
                _trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
                _verticesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count", 1);
                _totalMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);
                _gcMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory", 1);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ProfilerJobManager] Error starting recorders: {exception.Message}");
            }
        }

        private static void StopRecorders()
        {
            try
            {
                _frameTimeRecorder.Dispose();
                _cpuTimeRecorder.Dispose();
                _drawCallsRecorder.Dispose();
                _trianglesRecorder.Dispose();
                _verticesRecorder.Dispose();
                _totalMemoryRecorder.Dispose();
                _gcMemoryRecorder.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ProfilerJobManager] Error stopping recorders: {exception.Message}");
            }
        }

        /// <summary>
        /// Starts a new profiler recording job.
        /// </summary>
        public static ProfilerJob StartJob(int durationSeconds, bool includeFrameDetails)
        {
            lock (LockObject)
            {
                EnsureInitialized();

                // Check for existing recording
                if (_isRecording && _currentJob != null)
                {
                    return null; // Another recording is in progress
                }

                // Create new job
                _currentJob = ProfilerJob.Create(durationSeconds, includeFrameDetails);
                _storage.jobs.Add(_currentJob);
                SaveToSessionState();

                // Start recording
                _isRecording = true;
                StartRecorders();
                UnityEngine.Profiling.Profiler.enabled = true;

                return _currentJob;
            }
        }

        /// <summary>
        /// Stops the current profiler recording.
        /// </summary>
        public static ProfilerJob StopRecording()
        {
            lock (LockObject)
            {
                if (!_isRecording || _currentJob == null)
                {
                    return null;
                }

                // Stop recording
                StopRecorders();
                UnityEngine.Profiling.Profiler.enabled = false;
                _isRecording = false;

                // Finalize job
                _currentJob.Complete();
                SaveToSessionState();

                var completedJob = _currentJob;
                _currentJob = null;

                return completedJob;
            }
        }

        /// <summary>
        /// Gets a job by ID.
        /// </summary>
        public static ProfilerJob GetJob(string jobId)
        {
            lock (LockObject)
            {
                EnsureInitialized();
                return _storage.jobs.FirstOrDefault(j => j.jobId == jobId);
            }
        }

        /// <summary>
        /// Cancels the current recording with an error.
        /// </summary>
        public static void CancelRecording(string reason)
        {
            lock (LockObject)
            {
                if (!_isRecording || _currentJob == null)
                {
                    return;
                }

                StopRecorders();
                UnityEngine.Profiling.Profiler.enabled = false;
                _isRecording = false;

                _currentJob.SetError(reason);
                SaveToSessionState();
                _currentJob = null;
            }
        }
    }
}
