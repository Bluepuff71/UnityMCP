using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityMCP.Editor.Services
{
    /// <summary>
    /// Test mode for running tests.
    /// </summary>
    public enum TestMode
    {
        EditMode,
        PlayMode
    }

    /// <summary>
    /// Filter configuration for test runs.
    /// </summary>
    public class TestFilter
    {
        public TestMode Mode { get; set; } = TestMode.EditMode;
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> GroupPatterns { get; set; } = new List<string>();
        public List<string> Categories { get; set; } = new List<string>();
        public List<string> Assemblies { get; set; } = new List<string>();
    }

    /// <summary>
    /// Service for executing Unity tests using the Test Runner API.
    /// Implements ICallbacks to receive test progress events.
    /// </summary>
    public class TestRunnerService : ICallbacks
    {
        private static TestRunnerService _instance;
        private TestRunnerApi _testRunnerApi;
        private bool _includeDetails;
        private bool _includeFailedTests;

        // Tracking for result aggregation
        private int _passedCount;
        private int _failedCount;
        private int _skippedCount;
        private int _inconclusiveCount;
        private double _totalDuration;
        private List<TestFailure> _failures = new List<TestFailure>();
        private DateTime _runStartTime;

        /// <summary>
        /// Gets the singleton instance of the TestRunnerService.
        /// </summary>
        public static TestRunnerService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TestRunnerService();
                }
                return _instance;
            }
        }

        private TestRunnerService()
        {
            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
        }

        /// <summary>
        /// Starts an async test run with the given filter.
        /// Returns immediately after starting. Use TestJobManager to track progress.
        /// </summary>
        public void StartTestRunAsync(TestFilter filter, bool includeDetails, bool includeFailedTests)
        {
            _includeDetails = includeDetails;
            _includeFailedTests = includeFailedTests;

            // Reset tracking
            _passedCount = 0;
            _failedCount = 0;
            _skippedCount = 0;
            _inconclusiveCount = 0;
            _totalDuration = 0;
            _failures.Clear();
            _runStartTime = DateTime.UtcNow;

            // Save dirty scenes before running tests
            SaveDirtyScenesIfNeeded();

            // Build the filter
            var executionFilter = BuildFilter(filter);

            // Configure domain reload settings for PlayMode tests
            if (filter.Mode == TestMode.PlayMode)
            {
                ConfigurePlayModeSettings();
            }

            // Register callbacks
            _testRunnerApi.RegisterCallbacks(this);

            // Execute tests
            var executionSettings = new ExecutionSettings(executionFilter);
            _testRunnerApi.Execute(executionSettings);
        }

        private void SaveDirtyScenesIfNeeded()
        {
            try
            {
                var activeScene = EditorSceneManager.GetActiveScene();
                if (activeScene.isDirty)
                {
                    EditorSceneManager.SaveScene(activeScene);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TestRunnerService] Failed to save dirty scene: {exception.Message}");
            }
        }

        private void ConfigurePlayModeSettings()
        {
            // For PlayMode tests, we may want to disable domain reload to keep MCP connection alive.
            // This is done via EditorSettings.enterPlayModeOptions but requires careful handling.
            // For now, we just log a warning about potential disconnection.
            Debug.Log("[TestRunnerService] Starting PlayMode tests. MCP connection may be interrupted if domain reload occurs.");
        }

        private Filter BuildFilter(TestFilter testFilter)
        {
            var filter = new Filter
            {
                testMode = testFilter.Mode == TestMode.PlayMode ? TestMode.PlayMode.ToUnityTestMode() : TestMode.EditMode.ToUnityTestMode()
            };

            // Add specific test names
            if (testFilter.TestNames.Count > 0)
            {
                filter.testNames = testFilter.TestNames.ToArray();
            }

            // Add group patterns (regex patterns for test names)
            if (testFilter.GroupPatterns.Count > 0)
            {
                // Unity's filter supports regex patterns via testNames
                // We'll combine exact names with patterns
                var allNames = new List<string>(filter.testNames ?? Array.Empty<string>());
                allNames.AddRange(testFilter.GroupPatterns);
                filter.testNames = allNames.ToArray();
            }

            // Add category filters
            if (testFilter.Categories.Count > 0)
            {
                filter.categoryNames = testFilter.Categories.ToArray();
            }

            // Add assembly filters
            if (testFilter.Assemblies.Count > 0)
            {
                filter.assemblyNames = testFilter.Assemblies.ToArray();
            }

            return filter;
        }

        #region ICallbacks Implementation

        public void RunStarted(ITestAdaptor testsToRun)
        {
            int testCount = CountLeafTests(testsToRun);
            TestJobManager.OnRunStarted(testCount);
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            try
            {
                // Unregister callbacks
                _testRunnerApi.UnregisterCallbacks(this);

                // Build the final result
                var runResult = new TestRunResult
                {
                    passed = _passedCount,
                    failed = _failedCount,
                    skipped = _skippedCount,
                    inconclusive = _inconclusiveCount,
                    durationSeconds = (DateTime.UtcNow - _runStartTime).TotalSeconds,
                    failures = _includeFailedTests ? _failures : new List<TestFailure>()
                };

                // Finalize the job
                TestJobManager.FinalizeCurrentJobFromRunFinished(runResult);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestRunnerService] Error in RunFinished: {exception.Message}");
                TestJobManager.SetCurrentJobError($"Error finalizing test run: {exception.Message}");
            }
        }

        public void TestStarted(ITestAdaptor test)
        {
            // Only track leaf tests (actual test methods, not fixtures)
            if (!test.HasChildren)
            {
                TestJobManager.OnTestStarted(test.FullName);
            }
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            // Only track leaf test results
            if (!result.Test.HasChildren)
            {
                bool passed = result.TestStatus == TestStatus.Passed;
                string message = result.Message ?? "";
                string stackTrace = result.StackTrace ?? "";

                // Update counts based on status
                switch (result.TestStatus)
                {
                    case TestStatus.Passed:
                        _passedCount++;
                        break;
                    case TestStatus.Failed:
                        _failedCount++;
                        if (_failures.Count < 25) // Cap at 25 failures
                        {
                            _failures.Add(new TestFailure
                            {
                                testFullName = result.Test.FullName,
                                message = message,
                                stackTrace = stackTrace
                            });
                        }
                        break;
                    case TestStatus.Skipped:
                        _skippedCount++;
                        break;
                    case TestStatus.Inconclusive:
                        _inconclusiveCount++;
                        break;
                }

                _totalDuration += result.Duration;

                TestJobManager.OnLeafTestFinished(result.Test.FullName, passed, message, stackTrace);
            }
        }

        #endregion

        #region Helper Methods

        private int CountLeafTests(ITestAdaptor test)
        {
            if (test == null)
            {
                return 0;
            }

            if (!test.HasChildren)
            {
                return 1;
            }

            int count = 0;
            foreach (var child in test.Children)
            {
                count += CountLeafTests(child);
            }
            return count;
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for TestMode conversion.
    /// </summary>
    internal static class TestModeExtensions
    {
        public static UnityEditor.TestTools.TestRunner.Api.TestMode ToUnityTestMode(this TestMode mode)
        {
            return mode == TestMode.PlayMode
                ? UnityEditor.TestTools.TestRunner.Api.TestMode.PlayMode
                : UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode;
        }
    }
}
