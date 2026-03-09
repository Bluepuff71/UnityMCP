using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Utilities
{
    /// <summary>
    /// Captures the Game View's composited output including Canvas UI and UI Toolkit panels.
    /// Camera.Render() alone misses these overlays because they're composited after camera
    /// rendering in Unity's pipeline. This utility reads from the Game View's internal
    /// RenderTexture which contains the full composited frame.
    /// </summary>
    public static class GameViewCapture
    {
        private static Type s_gameViewType;
        private static FieldInfo s_rtField;
        private static bool s_discovered;

        /// <summary>
        /// Attempts to capture the Game View's composited output (including Canvas UI and UITK panels).
        /// Returns PNG bytes at the requested dimensions.
        /// </summary>
        /// <param name="width">Target width in pixels</param>
        /// <param name="height">Target height in pixels</param>
        /// <param name="png">PNG-encoded bytes of the capture</param>
        /// <param name="captureWidth">Actual width of the captured image</param>
        /// <param name="captureHeight">Actual height of the captured image</param>
        /// <param name="diagnostics">Diagnostic info if capture fails</param>
        /// <returns>True if composited capture succeeded</returns>
        public static bool TryCaptureComposited(int width, int height,
            out byte[] png, out int captureWidth, out int captureHeight, out string diagnostics)
        {
            png = null;
            captureWidth = 0;
            captureHeight = 0;
            diagnostics = null;

            try
            {
                var gameView = GetGameView();
                if (gameView == null)
                {
                    diagnostics = "GameView window not found.";
                    return false;
                }

                // Force repaint to ensure the RT has the latest frame
                gameView.Repaint();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                var sourceRT = GetGameViewRT(gameView);
                if (sourceRT == null || !sourceRT.IsCreated())
                {
                    diagnostics = s_discovered
                        ? "GameView RT field found but RT is null or not created."
                        : "Could not discover GameView internal RenderTexture field.";
                    return false;
                }

                // Read from the source RT, then resize if needed
                int srcW = sourceRT.width;
                int srcH = sourceRT.height;

                bool needsResize = width != srcW || height != srcH;
                RenderTexture readTarget = sourceRT;

                if (needsResize && width > 0 && height > 0)
                {
                    readTarget = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(sourceRT, readTarget);
                    captureWidth = width;
                    captureHeight = height;
                }
                else
                {
                    captureWidth = srcW;
                    captureHeight = srcH;
                }

                // Read pixels
                var tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
                var previousActive = RenderTexture.active;
                RenderTexture.active = readTarget;
                tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                tex.Apply();
                RenderTexture.active = previousActive;

                if (needsResize && readTarget != sourceRT)
                    RenderTexture.ReleaseTemporary(readTarget);

                // Flip Y (Game View RT is stored bottom-up)
                FlipTextureY(tex);

                png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);

                return true;
            }
            catch (Exception ex)
            {
                diagnostics = $"GameViewCapture exception: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Gets the Game View window dimensions (base resolution before superSize).
        /// Returns (width, height) or (0, 0) if Game View is not available.
        /// </summary>
        public static (int width, int height) GetGameViewDimensions()
        {
            try
            {
                var gameView = GetGameView();
                if (gameView == null) return (0, 0);

                var position = gameView.position;
                int viewWidth = Mathf.Max(320, (int)position.width);
                int viewHeight = Mathf.Max(240, (int)position.height);
                return (viewWidth, viewHeight);
            }
            catch
            {
                return (0, 0);
            }
        }

        private static EditorWindow GetGameView()
        {
            if (s_gameViewType == null)
                s_gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (s_gameViewType == null) return null;
            return EditorWindow.GetWindow(s_gameViewType, false, null, false);
        }

        private static RenderTexture GetGameViewRT(EditorWindow gameView)
        {
            if (!s_discovered)
            {
                DiscoverRTField(gameView);
                s_discovered = true;
            }

            if (s_rtField == null) return null;

            try
            {
                return s_rtField.GetValue(gameView) as RenderTexture;
            }
            catch
            {
                return null;
            }
        }

        private static void DiscoverRTField(EditorWindow gameView)
        {
            // Try known field names first (in priority order)
            string[] knownNames = { "m_RenderTexture", "m_TargetTexture" };
            var type = gameView.GetType();

            while (type != null && type != typeof(object))
            {
                foreach (var name in knownNames)
                {
                    var field = type.GetField(name,
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (field != null && field.FieldType == typeof(RenderTexture))
                    {
                        var rt = field.GetValue(gameView) as RenderTexture;
                        if (rt != null && rt.IsCreated())
                        {
                            s_rtField = field;
                            return;
                        }
                    }
                }
                type = type.BaseType;
            }

            // Fallback: search for any RenderTexture field that is created
            type = gameView.GetType();
            while (type != null && type != typeof(object))
            {
                var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    if (field.FieldType != typeof(RenderTexture)) continue;
                    var rt = field.GetValue(gameView) as RenderTexture;
                    if (rt != null && rt.IsCreated())
                    {
                        s_rtField = field;
                        return;
                    }
                }
                type = type.BaseType;
            }
        }

        private static void FlipTextureY(Texture2D tex)
        {
            int w = tex.width;
            int h = tex.height;
            var pixels = tex.GetPixels32();
            var flipped = new Color32[pixels.Length];

            for (int y = 0; y < h; y++)
            {
                int srcRow = (h - 1 - y) * w;
                int dstRow = y * w;
                Array.Copy(pixels, srcRow, flipped, dstRow, w);
            }

            tex.SetPixels32(flipped);
            tex.Apply();
        }
    }
}
