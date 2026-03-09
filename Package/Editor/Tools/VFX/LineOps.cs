using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;


namespace UnityMCP.Editor.Tools.VFX
{
    /// <summary>
    /// LineRenderer operations for manage_vfx tool.
    /// </summary>
    public static class LineOps
    {
        /// <summary>
        /// Creates a LineRenderer on a GameObject.
        /// </summary>
        public static object Create(
            string target,
            List<Vector3> positions = null,
            object width = null,
            object color = null,
            string materialPath = null)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw MCPException.InvalidParams("The 'target' parameter is required for line_create action.");
            }

            GameObject gameObject = VFXCommon.FindGameObject(target);
            if (gameObject == null)
            {
                return new
                {
                    success = false,
                    error = $"GameObject '{target}' not found."
                };
            }

            // Check if LineRenderer already exists
            LineRenderer existingLine = gameObject.GetComponent<LineRenderer>();
            if (existingLine != null)
            {
                return new
                {
                    success = false,
                    error = $"LineRenderer already exists on '{gameObject.name}'. Use line_set to modify it."
                };
            }

            LineRenderer lineRenderer = Undo.AddComponent<LineRenderer>(gameObject);
            if (lineRenderer == null)
            {
                return new
                {
                    success = false,
                    error = $"Failed to add LineRenderer to '{gameObject.name}'."
                };
            }

            // Set default values
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 0;

            // Set positions if provided
            if (positions != null && positions.Count > 0)
            {
                lineRenderer.positionCount = positions.Count;
                lineRenderer.SetPositions(positions.ToArray());
            }

            // Set width if provided
            if (width != null)
            {
                ApplyWidth(lineRenderer, width);
            }

            // Set color if provided
            if (color != null)
            {
                ApplyColor(lineRenderer, color);
            }

            // Set material if provided
            if (!string.IsNullOrEmpty(materialPath))
            {
                ApplyMaterial(lineRenderer, materialPath);
            }

            EditorUtility.SetDirty(gameObject);

            return new
            {
                success = true,
                message = $"LineRenderer created on '{gameObject.name}'.",
                gameObject = gameObject.name,
                path = VFXCommon.GetGameObjectPath(gameObject),
                instanceID = gameObject.GetInstanceID(),
                componentInstanceID = lineRenderer.GetInstanceID(),
                positionCount = lineRenderer.positionCount
            };
        }

        /// <summary>
        /// Gets information about a LineRenderer.
        /// </summary>
        public static object Get(string target)
        {
            LineRenderer lineRenderer = GetLineRenderer(target, out object errorResult);
            if (lineRenderer == null)
            {
                return errorResult;
            }

            // Get positions
            Vector3[] positions = new Vector3[lineRenderer.positionCount];
            lineRenderer.GetPositions(positions);

            var positionsList = positions.Select(p => VFXCommon.SerializeVector3(p)).ToList();

            return new
            {
                success = true,
                gameObject = lineRenderer.name,
                path = VFXCommon.GetGameObjectPath(lineRenderer.gameObject),
                instanceID = lineRenderer.gameObject.GetInstanceID(),
                componentInstanceID = lineRenderer.GetInstanceID(),
                positionCount = lineRenderer.positionCount,
                positions = positionsList,
                useWorldSpace = lineRenderer.useWorldSpace,
                loop = lineRenderer.loop,
                startWidth = lineRenderer.startWidth,
                endWidth = lineRenderer.endWidth,
                widthMultiplier = lineRenderer.widthMultiplier,
                widthCurve = VFXCommon.SerializeAnimationCurve(lineRenderer.widthCurve),
                startColor = VFXCommon.SerializeColor(lineRenderer.startColor),
                endColor = VFXCommon.SerializeColor(lineRenderer.endColor),
                colorGradient = VFXCommon.SerializeGradient(lineRenderer.colorGradient),
                numCornerVertices = lineRenderer.numCornerVertices,
                numCapVertices = lineRenderer.numCapVertices,
                alignment = lineRenderer.alignment.ToString(),
                textureMode = lineRenderer.textureMode.ToString(),
                material = lineRenderer.sharedMaterial != null ? lineRenderer.sharedMaterial.name : null
            };
        }

        /// <summary>
        /// Sets properties on a LineRenderer.
        /// </summary>
        public static object Set(
            string target,
            List<Vector3> positions = null,
            object width = null,
            object color = null,
            bool? useWorldSpace = null,
            bool? loop = null,
            string materialPath = null,
            int? cornerVertices = null,
            int? capVertices = null,
            string alignment = null,
            string textureMode = null)
        {
            LineRenderer lineRenderer = GetLineRenderer(target, out object errorResult);
            if (lineRenderer == null)
            {
                return errorResult;
            }

            Undo.RecordObject(lineRenderer, "Modify LineRenderer");

            var modifiedProperties = new List<string>();

            // Set positions
            if (positions != null && positions.Count > 0)
            {
                lineRenderer.positionCount = positions.Count;
                lineRenderer.SetPositions(positions.ToArray());
                modifiedProperties.Add("positions");
            }

            // Set width
            if (width != null)
            {
                ApplyWidth(lineRenderer, width);
                modifiedProperties.Add("width");
            }

            // Set color
            if (color != null)
            {
                ApplyColor(lineRenderer, color);
                modifiedProperties.Add("color");
            }

            // Set useWorldSpace
            if (useWorldSpace.HasValue)
            {
                lineRenderer.useWorldSpace = useWorldSpace.Value;
                modifiedProperties.Add("useWorldSpace");
            }

            // Set loop
            if (loop.HasValue)
            {
                lineRenderer.loop = loop.Value;
                modifiedProperties.Add("loop");
            }

            // Set material
            if (!string.IsNullOrEmpty(materialPath))
            {
                if (ApplyMaterial(lineRenderer, materialPath))
                {
                    modifiedProperties.Add("material");
                }
            }

            // Set corner vertices
            if (cornerVertices.HasValue)
            {
                lineRenderer.numCornerVertices = cornerVertices.Value;
                modifiedProperties.Add("numCornerVertices");
            }

            // Set cap vertices
            if (capVertices.HasValue)
            {
                lineRenderer.numCapVertices = capVertices.Value;
                modifiedProperties.Add("numCapVertices");
            }

            // Set alignment
            if (!string.IsNullOrEmpty(alignment))
            {
                lineRenderer.alignment = alignment.ToLowerInvariant() switch
                {
                    "view" => LineAlignment.View,
                    "transformz" or "transform_z" => LineAlignment.TransformZ,
                    _ => lineRenderer.alignment
                };
                modifiedProperties.Add("alignment");
            }

            // Set texture mode
            if (!string.IsNullOrEmpty(textureMode))
            {
                lineRenderer.textureMode = textureMode.ToLowerInvariant() switch
                {
                    "stretch" => LineTextureMode.Stretch,
                    "tile" => LineTextureMode.Tile,
                    "distributepersegment" or "distribute_per_segment" => LineTextureMode.DistributePerSegment,
                    "repeatpersegment" or "repeat_per_segment" => LineTextureMode.RepeatPerSegment,
                    "static" => LineTextureMode.Static,
                    _ => lineRenderer.textureMode
                };
                modifiedProperties.Add("textureMode");
            }

            EditorUtility.SetDirty(lineRenderer);

            return new
            {
                success = true,
                message = $"Modified {modifiedProperties.Count} property(ies) on LineRenderer.",
                gameObject = lineRenderer.name,
                instanceID = lineRenderer.gameObject.GetInstanceID(),
                modifiedProperties,
                positionCount = lineRenderer.positionCount
            };
        }

        #region Helper Methods

        private static LineRenderer GetLineRenderer(string target, out object errorResult)
        {
            errorResult = null;

            if (string.IsNullOrEmpty(target))
            {
                errorResult = new
                {
                    success = false,
                    error = "The 'target' parameter is required."
                };
                return null;
            }

            GameObject gameObject = VFXCommon.FindGameObject(target);
            if (gameObject == null)
            {
                errorResult = new
                {
                    success = false,
                    error = $"GameObject '{target}' not found."
                };
                return null;
            }

            LineRenderer lineRenderer = gameObject.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                errorResult = new
                {
                    success = false,
                    error = $"No LineRenderer component found on '{gameObject.name}'."
                };
                return null;
            }

            return lineRenderer;
        }

        private static void ApplyWidth(LineRenderer lineRenderer, object width)
        {
            // Handle single value
            if (width is double doubleWidth)
            {
                lineRenderer.startWidth = (float)doubleWidth;
                lineRenderer.endWidth = (float)doubleWidth;
                return;
            }
            if (width is float floatWidth)
            {
                lineRenderer.startWidth = floatWidth;
                lineRenderer.endWidth = floatWidth;
                return;
            }
            if (width is int intWidth)
            {
                lineRenderer.startWidth = intWidth;
                lineRenderer.endWidth = intWidth;
                return;
            }
            if (width is long longWidth)
            {
                lineRenderer.startWidth = longWidth;
                lineRenderer.endWidth = longWidth;
                return;
            }

            // Handle dictionary with start/end
            if (width is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("start", out object startValue))
                {
                    lineRenderer.startWidth = Convert.ToSingle(startValue);
                }
                if (dict.TryGetValue("end", out object endValue))
                {
                    lineRenderer.endWidth = Convert.ToSingle(endValue);
                }
                if (dict.TryGetValue("curve", out object curveValue))
                {
                    AnimationCurve curve = VFXCommon.ParseAnimationCurve(curveValue);
                    if (curve != null)
                    {
                        lineRenderer.widthCurve = curve;
                    }
                }
                if (dict.TryGetValue("multiplier", out object multiplierValue))
                {
                    lineRenderer.widthMultiplier = Convert.ToSingle(multiplierValue);
                }
                return;
            }

            // Handle animation curve
            AnimationCurve parsedCurve = VFXCommon.ParseAnimationCurve(width);
            if (parsedCurve != null)
            {
                lineRenderer.widthCurve = parsedCurve;
            }
        }

        private static void ApplyColor(LineRenderer lineRenderer, object color)
        {
            // Try single color first
            Color? parsedColor = VFXCommon.ParseColor(color);
            if (parsedColor.HasValue)
            {
                lineRenderer.startColor = parsedColor.Value;
                lineRenderer.endColor = parsedColor.Value;
                return;
            }

            // Handle dictionary with start/end colors
            if (color is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("start", out object startValue))
                {
                    Color? startColor = VFXCommon.ParseColor(startValue);
                    if (startColor.HasValue)
                    {
                        lineRenderer.startColor = startColor.Value;
                    }
                }
                if (dict.TryGetValue("end", out object endValue))
                {
                    Color? endColor = VFXCommon.ParseColor(endValue);
                    if (endColor.HasValue)
                    {
                        lineRenderer.endColor = endColor.Value;
                    }
                }
                if (dict.TryGetValue("gradient", out object gradientValue))
                {
                    Gradient gradient = VFXCommon.ParseGradient(gradientValue);
                    if (gradient != null)
                    {
                        lineRenderer.colorGradient = gradient;
                    }
                }
                return;
            }

            // Try gradient
            Gradient parsedGradient = VFXCommon.ParseGradient(color);
            if (parsedGradient != null)
            {
                lineRenderer.colorGradient = parsedGradient;
            }
        }

        private static bool ApplyMaterial(LineRenderer lineRenderer, string materialPath)
        {
            // Normalize path
            string normalizedPath = materialPath.Replace('\\', '/').Trim();
            if (!normalizedPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = "Assets/" + normalizedPath;
            }
            if (!normalizedPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath += ".mat";
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(normalizedPath);
            if (material == null)
            {
                // Try to find by name
                string[] guids = AssetDatabase.FindAssets($"t:Material {System.IO.Path.GetFileNameWithoutExtension(materialPath)}");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    material = AssetDatabase.LoadAssetAtPath<Material>(path);
                }
            }

            if (material != null)
            {
                lineRenderer.sharedMaterial = material;
                return true;
            }

            Debug.LogWarning($"[LineOps] Material not found: {materialPath}");
            return false;
        }

        #endregion
    }
}
