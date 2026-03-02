using System;
using UnityEngine;
using UnityMCP.Editor.Services;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Profile Unity Editor and runtime performance.
    /// </summary>
    [MCPTool("profiler", "Manage profiler recording: start, stop, or poll job status", Category = "Profiler")]
    public static class ProfilerTools
    {
        [MCPAction("start", Description = "Start profiler recording, returns job_id for polling")]
        public static object Start(
            [MCPParam("duration_seconds", "Maximum recording duration in seconds (1-60)")] int durationSeconds = 10,
            [MCPParam("include_frame_details", "Include per-frame data in results")] bool includeFrameDetails = false)
        {
            try
            {
                if (durationSeconds < 1) durationSeconds = 1;
                else if (durationSeconds > 60) durationSeconds = 60;

                if (ProfilerJobManager.IsRecording)
                {
                    var currentJob = ProfilerJobManager.CurrentJob;
                    return new
                    {
                        success = false,
                        error = "A profiler recording is already in progress.",
                        existing_job_id = currentJob?.jobId,
                        elapsed_seconds = currentJob?.GetElapsedSeconds()
                    };
                }

                var job = ProfilerJobManager.StartJob(durationSeconds, includeFrameDetails);
                if (job == null)
                {
                    return new
                    {
                        success = false,
                        error = "Failed to start profiler recording."
                    };
                }

                return new
                {
                    success = true,
                    job_id = job.jobId,
                    status = "recording",
                    target_duration_seconds = durationSeconds,
                    include_frame_details = includeFrameDetails,
                    message = $"Profiler recording started. Use profiler with action='get_job' and job_id '{job.jobId}' to poll status, or action='stop' to end early."
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ProfilerTools] Error starting profiler: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error starting profiler: {exception.Message}"
                };
            }
        }

        [MCPAction("stop", Description = "Stop profiler recording and finalize job")]
        public static object Stop(
            [MCPParam("job_id", "Job ID to stop (optional, verifies correct recording)")] string jobId = null)
        {
            try
            {
                if (!ProfilerJobManager.IsRecording)
                {
                    return new
                    {
                        success = false,
                        error = "No profiler recording is in progress."
                    };
                }

                var currentJob = ProfilerJobManager.CurrentJob;

                if (!string.IsNullOrEmpty(jobId) && currentJob != null && currentJob.jobId != jobId)
                {
                    return new
                    {
                        success = false,
                        error = $"Job ID mismatch. Current recording is '{currentJob.jobId}', not '{jobId}'.",
                        current_job_id = currentJob.jobId
                    };
                }

                var completedJob = ProfilerJobManager.StopRecording();
                if (completedJob == null)
                {
                    return new
                    {
                        success = false,
                        error = "Failed to stop profiler recording."
                    };
                }

                return new
                {
                    success = true,
                    job_id = completedJob.jobId,
                    status = "completed",
                    job = completedJob.ToSerializable(includeDetails: true)
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ProfilerTools] Error stopping profiler: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error stopping profiler: {exception.Message}"
                };
            }
        }

        [MCPAction("get_job", Description = "Poll job status and get captured data", ReadOnlyHint = true)]
        public static object GetJob(
            [MCPParam("job_id", "Job ID to query", required: true)] string jobId,
            [MCPParam("include_details", "Include detailed frame data if available")] bool includeDetails = true)
        {
            try
            {
                if (string.IsNullOrEmpty(jobId))
                {
                    return new
                    {
                        success = false,
                        error = "job_id is required."
                    };
                }

                var job = ProfilerJobManager.GetJob(jobId);
                if (job == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Job '{jobId}' not found. It may have expired or never existed."
                    };
                }

                return new
                {
                    success = true,
                    job = job.ToSerializable(includeDetails)
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ProfilerTools] Error getting job: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error getting job: {exception.Message}"
                };
            }
        }
    }
}
