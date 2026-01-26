using System;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Services;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for polling the status of asynchronous test jobs.
    /// </summary>
    public static class GetTestJob
    {
        /// <summary>
        /// Gets the status and results of a test job by ID.
        /// </summary>
        [MCPTool("tests_get_job", "Gets the status and results of a test job started by tests_run", Category = "Tests")]
        public static object Get(
            [MCPParam("job_id", "The job ID returned by tests_run", required: true)] string jobId,
            [MCPParam("include_details", "Include full test result details")] bool includeDetails = true,
            [MCPParam("include_failed_tests", "Include failed test information")] bool includeFailedTests = true)
        {
            // Validate job ID
            if (string.IsNullOrEmpty(jobId))
            {
                throw MCPException.InvalidParams("job_id is required");
            }

            // Look up the job
            var job = TestJobManager.GetJob(jobId);
            if (job == null)
            {
                return new
                {
                    success = false,
                    error = $"Job not found: {jobId}"
                };
            }

            // Check for stuck test
            bool isStuck = job.IsStuckSuspected();

            return new
            {
                success = true,
                job = job.ToSerializable(includeDetails, includeFailedTests),
                is_complete = job.status != TestJobStatus.Running,
                stuck_warning = isStuck ? "Test appears stuck - no progress for 60 seconds" : null
            };
        }
    }
}
