using System;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Test tools for verifying MCP connectivity and functionality.
    /// These tools can be used to test the end-to-end MCP flow.
    /// </summary>
    public static class TestTools
    {
        /// <summary>
        /// Echoes back the input message - useful for testing basic connectivity.
        /// </summary>
        [MCPTool("test_echo", "Echoes back the input message - for testing connectivity", Category = "Debug")]
        public static object Echo(
            [MCPParam("message", "The message to echo back", required: true)] string message)
        {
            return new
            {
                success = true,
                echo = message,
                timestamp = DateTime.UtcNow.ToString("o")
            };
        }

        /// <summary>
        /// Adds two numbers together - useful for testing parameter handling.
        /// </summary>
        [MCPTool("test_add", "Adds two numbers - for testing parameter handling", Category = "Debug")]
        public static object Add(
            [MCPParam("a", "First number", required: true)] int a,
            [MCPParam("b", "Second number", required: true)] int b)
        {
            return new
            {
                success = true,
                result = a + b
            };
        }

        /// <summary>
        /// Returns basic Unity editor information.
        /// </summary>
        [MCPTool("test_unity_info", "Returns basic Unity editor information", Category = "Debug")]
        public static object GetUnityInfo()
        {
            return new
            {
                success = true,
                unityVersion = Application.unityVersion,
                productName = Application.productName,
                isPlaying = EditorApplication.isPlaying,
                platform = Application.platform.ToString()
            };
        }

        /// <summary>
        /// Lists all scenes in the build settings.
        /// </summary>
        [MCPTool("test_list_scenes", "Lists all scenes in build settings", Category = "Debug")]
        public static object ListScenes()
        {
            var scenes = EditorBuildSettings.scenes
                .Select(scene => new
                {
                    path = scene.path,
                    enabled = scene.enabled
                })
                .ToList();

            return new
            {
                success = true,
                count = scenes.Count,
                scenes
            };
        }
    }
}
