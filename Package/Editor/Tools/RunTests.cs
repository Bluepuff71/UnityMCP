using System;
using System.Collections.Generic;
using System.Linq;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Services;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for starting asynchronous Unity test runs.
    /// </summary>
    public static class RunTests
    {
        private const int RetryAfterMs = 5000;

        /// <summary>
        /// Starts an asynchronous Unity Test Runner job and returns immediately with a job ID for polling.
        /// </summary>
        [MCPTool("tests_run", "Starts Unity Test Runner asynchronously, returns job_id for polling with tests_get_job", Category = "Tests", DestructiveHint = true)]
        public static object Run(
            [MCPParam("mode", "Test mode: EditMode or PlayMode (default: EditMode)")] string mode = "EditMode",
            [MCPParam("test_names", "Comma-separated list of specific test names to run")] string testNames = null,
            [MCPParam("group_names", "Comma-separated regex patterns to match test names")] string groupNames = null,
            [MCPParam("category_names", "Comma-separated NUnit category names to filter by")] string categoryNames = null,
            [MCPParam("assembly_names", "Comma-separated assembly names to filter by")] string assemblyNames = null,
            [MCPParam("include_details", "Include full test result details in job result")] bool includeDetails = true,
            [MCPParam("include_failed_tests", "Include failed test information in job result")] bool includeFailedTests = true)
        {
            // Parse and validate mode
            TestMode testMode;
            if (!TryParseTestMode(mode, out testMode))
            {
                throw MCPException.InvalidParams($"Invalid test mode: '{mode}'. Must be 'EditMode' or 'PlayMode'.");
            }

            // Check if tests are already running
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

            // Start a new job
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
                // Build the filter
                var filter = new TestFilter
                {
                    Mode = testMode,
                    TestNames = ParseCommaSeparatedList(testNames),
                    GroupPatterns = ParseCommaSeparatedList(groupNames),
                    Categories = ParseCommaSeparatedList(categoryNames),
                    Assemblies = ParseCommaSeparatedList(assemblyNames)
                };

                // Start the test run asynchronously
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
                // Mark the job as failed
                TestJobManager.SetCurrentJobError($"Failed to start tests: {exception.Message}");

                return new
                {
                    success = false,
                    error = $"Failed to start test run: {exception.Message}",
                    job_id = job.jobId
                };
            }
        }

        /// <summary>
        /// Tries to parse a test mode string case-insensitively.
        /// </summary>
        private static bool TryParseTestMode(string modeString, out TestMode mode)
        {
            mode = TestMode.EditMode;

            if (string.IsNullOrEmpty(modeString))
            {
                return true; // Default to EditMode
            }

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

        /// <summary>
        /// Parses a comma-separated string into a list of trimmed, non-empty strings.
        /// </summary>
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
    }
}
