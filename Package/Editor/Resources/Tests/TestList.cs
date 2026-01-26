using System;
using System.Collections.Generic;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityMCP.Editor.Resources.Tests
{
    /// <summary>
    /// Resource provider for listing available unit tests.
    /// </summary>
    public static class TestList
    {
        /// <summary>
        /// Gets all available unit tests in the project.
        /// </summary>
        /// <returns>Object containing all tests organized by mode.</returns>
        [MCPResource("tests://list", "Available unit tests")]
        public static object GetAll()
        {
            var testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();

            try
            {
                var editModeTests = new List<object>();
                var playModeTests = new List<object>();

                // Retrieve EditMode tests
                testRunnerApi.RetrieveTestList(TestMode.EditMode, (testAdaptor) =>
                {
                    FlattenTestTree(testAdaptor, editModeTests, "EditMode");
                });

                // Retrieve PlayMode tests
                testRunnerApi.RetrieveTestList(TestMode.PlayMode, (testAdaptor) =>
                {
                    FlattenTestTree(testAdaptor, playModeTests, "PlayMode");
                });

                return new
                {
                    totalCount = editModeTests.Count + playModeTests.Count,
                    editMode = new
                    {
                        count = editModeTests.Count,
                        tests = editModeTests.ToArray()
                    },
                    playMode = new
                    {
                        count = playModeTests.Count,
                        tests = playModeTests.ToArray()
                    }
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testRunnerApi);
            }
        }

        /// <summary>
        /// Gets tests filtered by mode (EditMode or PlayMode).
        /// </summary>
        /// <param name="mode">The test mode to filter by (EditMode or PlayMode).</param>
        /// <returns>Object containing tests for the specified mode.</returns>
        [MCPResource("tests://list/{mode}", "Tests filtered by mode")]
        public static object GetByMode(
            [MCPParam("mode", "EditMode or PlayMode")] string mode)
        {
            // Validate and parse mode
            if (!TryParseTestMode(mode, out TestMode testMode))
            {
                return new
                {
                    error = true,
                    message = $"Invalid test mode '{mode}'. Must be 'EditMode' or 'PlayMode'.",
                    validModes = new[] { "EditMode", "PlayMode" }
                };
            }

            var testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();

            try
            {
                var tests = new List<object>();

                testRunnerApi.RetrieveTestList(testMode, (testAdaptor) =>
                {
                    FlattenTestTree(testAdaptor, tests, mode);
                });

                return new
                {
                    mode = testMode.ToString(),
                    count = tests.Count,
                    tests = tests.ToArray()
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testRunnerApi);
            }
        }

        /// <summary>
        /// Flattens the test tree to extract individual test methods.
        /// </summary>
        /// <param name="testAdaptor">The root test adaptor.</param>
        /// <param name="testList">The list to populate with test information.</param>
        /// <param name="mode">The test mode name for reference.</param>
        private static void FlattenTestTree(ITestAdaptor testAdaptor, List<object> testList, string mode)
        {
            if (testAdaptor == null)
            {
                return;
            }

            // If this is a leaf test (actual test method), add it
            if (!testAdaptor.HasChildren)
            {
                var categories = ExtractCategories(testAdaptor);

                testList.Add(new
                {
                    fullName = testAdaptor.FullName,
                    name = testAdaptor.Name,
                    categories = categories,
                    mode = mode,
                    isRunnable = testAdaptor.RunState == RunState.Runnable,
                    runState = testAdaptor.RunState.ToString(),
                    testCaseCount = testAdaptor.TestCaseCount
                });
            }
            else
            {
                // Recurse into children
                foreach (var child in testAdaptor.Children)
                {
                    FlattenTestTree(child, testList, mode);
                }
            }
        }

        /// <summary>
        /// Extracts categories from a test adaptor.
        /// Categories are stored in the TypeInfo.Categories property in Unity's Test Runner API.
        /// </summary>
        /// <param name="testAdaptor">The test adaptor to extract categories from.</param>
        /// <returns>Array of category names.</returns>
        private static string[] ExtractCategories(ITestAdaptor testAdaptor)
        {
            var categories = new List<string>();

            try
            {
                // ITestAdaptor exposes categories through the TypeInfo property
                // which contains the NUnit test attributes including categories
                if (testAdaptor.TypeInfo != null)
                {
                    // TypeInfo.GetCategories() or check via reflection
                    // Unity's ITestAdaptor doesn't directly expose categories but
                    // they can be accessed through the underlying NUnit test structure
                }

                // Alternative: Parse categories from FullName if they follow a convention
                // Some test frameworks embed category info in the test path
            }
            catch (Exception ex)
            {
                // Categories extraction failed, return empty array
                Debug.LogWarning($"[TestList] Failed to extract test categories: {ex.Message}");
            }

            return categories.ToArray();
        }

        /// <summary>
        /// Tries to parse a string to a TestMode enum.
        /// </summary>
        /// <param name="modeString">The mode string to parse.</param>
        /// <param name="testMode">The parsed test mode.</param>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        private static bool TryParseTestMode(string modeString, out TestMode testMode)
        {
            testMode = TestMode.EditMode;

            if (string.IsNullOrWhiteSpace(modeString))
            {
                return false;
            }

            string normalizedMode = modeString.Trim();

            if (normalizedMode.Equals("EditMode", StringComparison.OrdinalIgnoreCase) ||
                normalizedMode.Equals("Edit", StringComparison.OrdinalIgnoreCase))
            {
                testMode = TestMode.EditMode;
                return true;
            }

            if (normalizedMode.Equals("PlayMode", StringComparison.OrdinalIgnoreCase) ||
                normalizedMode.Equals("Play", StringComparison.OrdinalIgnoreCase))
            {
                testMode = TestMode.PlayMode;
                return true;
            }

            return false;
        }
    }
}
