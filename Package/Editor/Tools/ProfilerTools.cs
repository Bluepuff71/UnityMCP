using System;
using UnityEngine;
using UnityMCP.Editor.Services;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tools for profiling Unity Editor and runtime performance.
    /// Uses an async job pattern for recording profiler data.
    /// </summary>
    public static class ProfilerTools
    {
        /// <summary>
        /// Starts profiler recording, returning a job_id for polling.
        /// </summary>
        /// <param name="durationSeconds">Maximum recording duration in seconds (1-60).</param>
        /// <param name="includeFrameDetails">Whether to include per-frame data in results.</param>
        /// <returns>Result object with job_id and initial status.</returns>
        [MCPTool("profiler_start", "Start profiler recording, returns job_id for polling", Category = "Profiler", DestructiveHint = true)]
        public static object Start(
            [MCPParam("duration_seconds", "Maximum recording duration in seconds (1-60)")] int durationSeconds = 10,
            [MCPParam("include_frame_details", "Include per-frame data in results")] bool includeFrameDetails = false)
        {
            try
            {
                // Validate duration
                if (durationSeconds < 1)
                {
                    durationSeconds = 1;
                }
                else if (durationSeconds > 60)
                {
                    durationSeconds = 60;
                }

                // Check if already recording
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

                // Start new recording
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
                    message = $"Profiler recording started. Use profiler_get_job with job_id '{job.jobId}' to poll status, or profiler_stop to end early."
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

        /// <summary>
        /// Stops the current profiler recording.
        /// </summary>
        /// <param name="jobId">Optional job_id to verify correct recording is stopped.</param>
        /// <returns>Result object with final job status and statistics.</returns>
        [MCPTool("profiler_stop", "Stop profiler recording and finalize job", Category = "Profiler", DestructiveHint = true)]
        public static object Stop(
            [MCPParam("job_id", "Job ID to stop (optional, verifies correct recording)")] string jobId = null)
        {
            try
            {
                // Check if recording
                if (!ProfilerJobManager.IsRecording)
                {
                    return new
                    {
                        success = false,
                        error = "No profiler recording is in progress."
                    };
                }

                var currentJob = ProfilerJobManager.CurrentJob;

                // Verify job_id if provided
                if (!string.IsNullOrEmpty(jobId) && currentJob != null && currentJob.jobId != jobId)
                {
                    return new
                    {
                        success = false,
                        error = $"Job ID mismatch. Current recording is '{currentJob.jobId}', not '{jobId}'.",
                        current_job_id = currentJob.jobId
                    };
                }

                // Stop recording
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

        /// <summary>
        /// Gets the status and data of a profiler job.
        /// </summary>
        /// <param name="jobId">The job ID to query.</param>
        /// <param name="includeDetails">Whether to include detailed frame data if available.</param>
        /// <returns>Result object with job status and optional data.</returns>
        [MCPTool("profiler_get_job", "Poll job status and get captured data", Category = "Profiler", ReadOnlyHint = true)]
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
