using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Captures Game View or Scene View screenshots for AI vision analysis.
    /// Returns base64-encoded image data by default, or saves to disk via output_path.
    /// </summary>
    public static class VisionCapture
    {
        [MCPTool("vision_capture",
            "Capture Game/Scene View screenshot as base64 image for AI vision analysis. " +
            "Returns JPEG by default (compact). Use output_path to save to disk instead. " +
            "For file-based screenshots with target framing and camera angles, use scene_screenshot.",
            Category = "Scene", ReadOnlyHint = true, IdempotentHint = true)]
        public static object Capture(
            [MCPParam("view", "Which view to capture",
                Enum = new[] { "game", "scene" })] string view = "game",
            [MCPParam("width", "Capture width in pixels (height is proportional). Lower values produce smaller responses.",
                Minimum = 64, Maximum = 3840)] int width = 800,
            [MCPParam("format", "Image format: jpeg (smaller, default) or png (lossless, larger)",
                Enum = new[] { "jpeg", "png" })] string format = "jpeg",
            [MCPParam("quality", "JPEG quality 1-100. Ignored for PNG.",
                Minimum = 1, Maximum = 100)] int quality = 75,
            [MCPParam("output_path", "Save to this path instead of returning base64. Relative paths resolve from project root.")] string outputPath = null)
        {
            try
            {
                string resolvedView = (view ?? "game").ToLowerInvariant();
                string resolvedFormat = (format ?? "jpeg").ToLowerInvariant();
                int resolvedWidth = Mathf.Clamp(width, 64, 3840);
                int resolvedQuality = Mathf.Clamp(quality, 1, 100);

                if (resolvedView != "game" && resolvedView != "scene")
                    return new { success = false, error = $"Invalid view '{view}'. Must be 'game' or 'scene'." };

                if (resolvedFormat != "jpeg" && resolvedFormat != "png")
                    return new { success = false, error = $"Invalid format '{format}'. Must be 'jpeg' or 'png'." };

                byte[] imageBytes;
                int capturedWidth;
                int capturedHeight;

                if (resolvedView == "scene")
                {
                    var result = CaptureSceneView(resolvedWidth, resolvedFormat, resolvedQuality);
                    imageBytes = result.bytes;
                    capturedWidth = result.width;
                    capturedHeight = result.height;
                }
                else
                {
                    var result = CaptureGameView(resolvedWidth, resolvedFormat, resolvedQuality);
                    imageBytes = result.bytes;
                    capturedWidth = result.width;
                    capturedHeight = result.height;
                }

                if (imageBytes == null || imageBytes.Length == 0)
                    return new { success = false, error = $"Failed to capture {resolvedView} view. Ensure the view window is open." };

                // Save to disk if output_path provided
                if (!string.IsNullOrEmpty(outputPath))
                    return SaveToDisk(imageBytes, outputPath, resolvedFormat, resolvedView, capturedWidth, capturedHeight);

                // Check response size before encoding to base64
                // Base64 encoding expands data by ~33%, plus JSON wrapper overhead
                int estimatedResponseSize = (imageBytes.Length * 4 / 3) + 512;
                if (estimatedResponseSize >= MCPProxy.MaxResponseSize)
                {
                    return new
                    {
                        success = false,
                        error = $"Captured image ({imageBytes.Length} bytes, ~{estimatedResponseSize} base64) exceeds max response size ({MCPProxy.MaxResponseSize} bytes). " +
                                "Reduce 'width', switch to 'jpeg' format, or use 'output_path' to save to disk."
                    };
                }

                string base64Data = Convert.ToBase64String(imageBytes);
                string mimeType = resolvedFormat == "png" ? "image/png" : "image/jpeg";

                return new
                {
                    success = true,
                    view = resolvedView,
                    width = capturedWidth,
                    height = capturedHeight,
                    format = resolvedFormat,
                    mimeType,
                    sizeBytes = imageBytes.Length,
                    base64 = base64Data
                };
            }
            catch (Exception exception)
            {
                return new { success = false, error = $"Vision capture failed: {exception.Message}" };
            }
        }

        private static (byte[] bytes, int width, int height) CaptureGameView(int targetWidth, string format, int quality)
        {
            // Determine target dimensions from Game View or camera aspect ratio
            int targetHeight;
            var gameViewDimensions = GameViewCapture.GetGameViewDimensions();
            if (gameViewDimensions.width > 0 && gameViewDimensions.height > 0)
            {
                float aspectRatio = (float)gameViewDimensions.width / gameViewDimensions.height;
                targetHeight = Mathf.RoundToInt(targetWidth / aspectRatio);
            }
            else
            {
                Camera camera = Camera.main ?? (Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null);
                float aspectRatio = camera != null && camera.aspect > 0f ? camera.aspect : 16f / 9f;
                targetHeight = Mathf.RoundToInt(targetWidth / aspectRatio);
            }

            // Tier 1: Play Mode — ScreenCapture (full composite including Canvas/UITK)
            if (Application.isPlaying)
            {
                try
                {
                    Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                    if (screenshot != null)
                    {
                        try
                        {
                            // Resize if needed
                            if (screenshot.width != targetWidth || screenshot.height != targetHeight)
                            {
                                var resizedRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
                                Graphics.Blit(screenshot, resizedRT);
                                var resizedTex = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                                var previousActive = RenderTexture.active;
                                RenderTexture.active = resizedRT;
                                resizedTex.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                                resizedTex.Apply();
                                RenderTexture.active = previousActive;
                                RenderTexture.ReleaseTemporary(resizedRT);
                                UnityEngine.Object.DestroyImmediate(screenshot);
                                screenshot = resizedTex;
                            }

                            byte[] encodedBytes = EncodeTexture(screenshot, format, quality);
                            int finalWidth = screenshot.width;
                            int finalHeight = screenshot.height;
                            return (encodedBytes, finalWidth, finalHeight);
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(screenshot);
                        }
                    }
                }
                catch
                {
                    // Fall through to next tier
                }
            }

            // Tier 2: GameViewCapture composited RT (includes Canvas, UITK overlays)
            if (GameViewCapture.TryCaptureComposited(targetWidth, targetHeight,
                    out byte[] compositedPng, out int cw, out int ch, out string _diagnostics))
            {
                // TryCaptureComposited returns PNG; re-encode if JPEG requested
                if (format == "jpeg")
                {
                    var tempTex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                    tempTex.LoadImage(compositedPng);
                    byte[] encodedBytes = tempTex.EncodeToJPG(quality);
                    UnityEngine.Object.DestroyImmediate(tempTex);
                    return (encodedBytes, cw, ch);
                }
                return (compositedPng, cw, ch);
            }

            // Tier 3: Camera.Render() fallback (3D only, no UI overlays)
            Camera fallbackCamera = Camera.main ?? (Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null);
            if (fallbackCamera == null)
                return (null, 0, 0);

            RenderTexture previousTargetTexture = fallbackCamera.targetTexture;
            RenderTexture previousActiveTexture = RenderTexture.active;
            RenderTexture renderTexture = null;

            try
            {
                renderTexture = new RenderTexture(targetWidth, targetHeight, 24);
                fallbackCamera.targetTexture = renderTexture;
                fallbackCamera.Render();

                RenderTexture.active = renderTexture;
                var texture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                texture.Apply();

                byte[] encodedBytes = EncodeTexture(texture, format, quality);
                UnityEngine.Object.DestroyImmediate(texture);

                return (encodedBytes, targetWidth, targetHeight);
            }
            finally
            {
                fallbackCamera.targetTexture = previousTargetTexture;
                RenderTexture.active = previousActiveTexture;

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
            }
        }

        private static (byte[] bytes, int width, int height) CaptureSceneView(int targetWidth, string format, int quality)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return (null, 0, 0);

            Camera sceneCamera = sceneView.camera;
            if (sceneCamera == null)
                return (null, 0, 0);

            float aspectRatio = sceneView.position.width / sceneView.position.height;
            if (aspectRatio <= 0f) aspectRatio = 16f / 9f;
            int targetHeight = Mathf.RoundToInt(targetWidth / aspectRatio);

            RenderTexture previousTargetTexture = sceneCamera.targetTexture;
            RenderTexture previousActiveTexture = RenderTexture.active;
            RenderTexture renderTexture = null;

            try
            {
                renderTexture = new RenderTexture(targetWidth, targetHeight, 24);
                sceneCamera.targetTexture = renderTexture;
                sceneCamera.Render();

                RenderTexture.active = renderTexture;
                var texture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                texture.Apply();

                byte[] encodedBytes = EncodeTexture(texture, format, quality);
                UnityEngine.Object.DestroyImmediate(texture);

                return (encodedBytes, targetWidth, targetHeight);
            }
            finally
            {
                sceneCamera.targetTexture = previousTargetTexture;
                RenderTexture.active = previousActiveTexture;

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
            }
        }

        private static byte[] EncodeTexture(Texture2D texture, string format, int quality)
        {
            return format == "png" ? texture.EncodeToPNG() : texture.EncodeToJPG(quality);
        }

        private static object SaveToDisk(byte[] imageBytes, string outputPath, string format, string view, int capturedWidth, int capturedHeight)
        {
            string extension = format == "png" ? ".png" : ".jpg";
            if (!outputPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                outputPath += extension;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullPath = Path.IsPathRooted(outputPath)
                ? outputPath
                : Path.Combine(projectRoot, outputPath);

            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(fullPath, imageBytes);

            // If the file is under Assets/, schedule an import
            string relativePath = null;
            if (fullPath.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = "Assets" + fullPath.Substring(Application.dataPath.Length).Replace('\\', '/');
                string capturedRelativePath = relativePath;
                EditorApplication.delayCall += () =>
                {
                    if (File.Exists(fullPath))
                        AssetDatabase.ImportAsset(capturedRelativePath);
                };
            }

            return new
            {
                success = true,
                view,
                width = capturedWidth,
                height = capturedHeight,
                format,
                sizeBytes = imageBytes.Length,
                path = relativePath ?? fullPath
            };
        }
    }
}
