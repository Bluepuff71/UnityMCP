using System;
using System.Collections.Generic;
using System.Linq;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Services;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Run Unity tests and poll job status.
    /// </summary>
    [MCPTool("tests", "Run Unity Test Runner or poll test job status", Category = "Tests")]
    public static class RunTests
    {
        private const int RetryAfterMs = 5000;

        [MCPAction("run", Description = "Start Unity Test Runner asynchronously, returns job_id for polling")]
        public static object Run(
            [MCPParam("mode", "Test mode: EditMode or PlayMode (default: EditMode)")] string mode = "EditMode",
            [MCPParam("test_names", "Comma-separated list of specific test names to run")] string testNames = null,
            [MCPParam("group_names", "Comma-separated regex patterns to match test names")] string groupNames = null,
            [MCPParam("category_names", "Comma-separated NUnit category names to filter by")] string categoryNames = null,
            [MCPParam("assembly_names", "Comma-separated assembly names to filter by")] string assemblyNames = null,
            [MCPParam("include_details", "Include full test result details in job result")] bool includeDetails = true,
            [MCPParam("include_failed_tests", "Include failed test information in job result")] bool includeFailedTests = true)
        {
            TestMode testMode;
            if (!TryParseTestMode(mode, out testMode))
            {
                throw MCPException.InvalidParams($"Invalid test mode: '{mode}'. Must be 'EditMode' or 'PlayMode'.");
            }

            if (TestJobManager.IsRunning)
            {
                var currentJob = TestJobManager.CurrentJob;
                return new
                {
                    success = false,
                    error = "A test run is already in progress",
                    job_id = currentJob?.jobId,
                    retry_after_ms = RetryAfterMs
                };
            }

            var job = TestJobManager.StartJob(testMode.ToString());
            if (job == null)
            {
                return new
                {
                    success = false,
                    error = "Failed to start test job - another job may be running",
                    retry_after_ms = RetryAfterMs
                };
            }

            try
            {
                var filter = new TestFilter
                {
                    Mode = testMode,
                    TestNames = ParseCommaSeparatedList(testNames),
                    GroupPatterns = ParseCommaSeparatedList(groupNames),
                    Categories = ParseCommaSeparatedList(categoryNames),
                    Assemblies = ParseCommaSeparatedList(assemblyNames)
                };

                TestRunnerService.Instance.StartTestRunAsync(filter, includeDetails, includeFailedTests);

                return new
                {
                    success = true,
                    message = $"Test run started in {testMode} mode",
                    job_id = job.jobId,
                    status = "running",
                    mode = testMode.ToString()
                };
            }
            catch (Exception exception)
            {
                TestJobManager.SetCurrentJobError($"Failed to start tests: {exception.Message}");

                return new
                {
                    success = false,
                    error = $"Failed to start test run: {exception.Message}",
                    job_id = job.jobId
                };
            }
        }

        [MCPAction("get_job", Description = "Get test job status and results", ReadOnlyHint = true)]
        public static object GetJob(
            [MCPParam("job_id", "The job ID returned by tests run action", required: true)] string jobId,
            [MCPParam("include_details", "Include full test result details")] bool includeDetails = true,
            [MCPParam("include_failed_tests", "Include failed test information")] bool includeFailedTests = true)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                throw MCPException.InvalidParams("job_id is required");
            }

            var job = TestJobManager.GetJob(jobId);
            if (job == null)
            {
                return new
                {
                    success = false,
                    error = $"Job not found: {jobId}"
                };
            }

            bool isStuck = job.IsStuckSuspected();

            return new
            {
                success = true,
                job = job.ToSerializable(includeDetails, includeFailedTests),
                is_complete = job.status != TestJobStatus.Running,
                stuck_warning = isStuck ? "Test appears stuck - no progress for 60 seconds" : null
            };
        }

        #region Helper Methods

        private static bool TryParseTestMode(string modeString, out TestMode mode)
        {
            mode = TestMode.EditMode;

            if (string.IsNullOrEmpty(modeString)) return true;

            string normalizedMode = modeString.Trim().ToLowerInvariant();

            if (normalizedMode == "editmode" || normalizedMode == "edit")
            {
                mode = TestMode.EditMode;
                return true;
            }

            if (normalizedMode == "playmode" || normalizedMode == "play")
            {
                mode = TestMode.PlayMode;
                return true;
            }

            return false;
        }

        private static List<string> ParseCommaSeparatedList(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return new List<string>();
            }

            return input
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        #endregion
    }
}
