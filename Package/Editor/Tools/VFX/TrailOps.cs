using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;


namespace UnityMCP.Editor.Tools.VFX
{
    /// <summary>
    /// TrailRenderer operations for manage_vfx tool.
    /// </summary>
    public static class TrailOps
    {
        /// <summary>
        /// Gets information about a TrailRenderer.
        /// </summary>
        public static object Get(string target)
        {
            TrailRenderer trailRenderer = GetTrailRenderer(target, out object errorResult);
            if (trailRenderer == null)
            {
                return errorResult;
            }

            return new
            {
                success = true,
                gameObject = trailRenderer.name,
                path = VFXCommon.GetGameObjectPath(trailRenderer.gameObject),
                instanceID = trailRenderer.gameObject.GetInstanceID(),
                componentInstanceID = trailRenderer.GetInstanceID(),
                time = trailRenderer.time,
                startWidth = trailRenderer.startWidth,
                endWidth = trailRenderer.endWidth,
                widthMultiplier = trailRenderer.widthMultiplier,
                widthCurve = VFXCommon.SerializeAnimationCurve(trailRenderer.widthCurve),
                startColor = VFXCommon.SerializeColor(trailRenderer.startColor),
                endColor = VFXCommon.SerializeColor(trailRenderer.endColor),
                colorGradient = VFXCommon.SerializeGradient(trailRenderer.colorGradient),
                minVertexDistance = trailRenderer.minVertexDistance,
                autodestruct = trailRenderer.autodestruct,
                emitting = trailRenderer.emitting,
                numCornerVertices = trailRenderer.numCornerVertices,
                numCapVertices = trailRenderer.numCapVertices,
                alignment = trailRenderer.alignment.ToString(),
                textureMode = trailRenderer.textureMode.ToString(),
                generateLightingData = trailRenderer.generateLightingData,
                shadowBias = trailRenderer.shadowBias,
                positionCount = trailRenderer.positionCount,
                material = trailRenderer.sharedMaterial != null ? trailRenderer.sharedMaterial.name : null
            };
        }

        /// <summary>
        /// Sets properties on a TrailRenderer.
        /// </summary>
        public static object Set(
            string target,
            float? time = null,
            object width = null,
            object color = null,
            float? minVertexDistance = null,
            bool? autodestruct = null,
            bool? emitting = null,
            string materialPath = null,
            int? cornerVertices = null,
            int? capVertices = null,
            string alignment = null,
            string textureMode = null,
            bool? generateLightingData = null,
            float? shadowBias = null)
        {
            TrailRenderer trailRenderer = GetTrailRenderer(target, out object errorResult);
            if (trailRenderer == null)
            {
                return errorResult;
            }

            Undo.RecordObject(trailRenderer, "Modify TrailRenderer");

            var modifiedProperties = new List<string>();

            // Set time
            if (time.HasValue)
            {
                trailRenderer.time = time.Value;
                modifiedProperties.Add("time");
            }

            // Set width
            if (width != null)
            {
                ApplyWidth(trailRenderer, width);
                modifiedProperties.Add("width");
            }

            // Set color
            if (color != null)
            {
                ApplyColor(trailRenderer, color);
                modifiedProperties.Add("color");
            }

            // Set minVertexDistance
            if (minVertexDistance.HasValue)
            {
                trailRenderer.minVertexDistance = minVertexDistance.Value;
                modifiedProperties.Add("minVertexDistance");
            }

            // Set autodestruct
            if (autodestruct.HasValue)
            {
                trailRenderer.autodestruct = autodestruct.Value;
                modifiedProperties.Add("autodestruct");
            }

            // Set emitting
            if (emitting.HasValue)
            {
                trailRenderer.emitting = emitting.Value;
                modifiedProperties.Add("emitting");
            }

            // Set material
            if (!string.IsNullOrEmpty(materialPath))
            {
                if (ApplyMaterial(trailRenderer, materialPath))
                {
                    modifiedProperties.Add("material");
                }
            }

            // Set corner vertices
            if (cornerVertices.HasValue)
            {
                trailRenderer.numCornerVertices = cornerVertices.Value;
                modifiedProperties.Add("numCornerVertices");
            }

            // Set cap vertices
            if (capVertices.HasValue)
            {
                trailRenderer.numCapVertices = capVertices.Value;
                modifiedProperties.Add("numCapVertices");
            }

            // Set alignment
            if (!string.IsNullOrEmpty(alignment))
            {
                trailRenderer.alignment = alignment.ToLowerInvariant() switch
                {
                    "view" => LineAlignment.View,
                    "transformz" or "transform_z" => LineAlignment.TransformZ,
                    _ => trailRenderer.alignment
                };
                modifiedProperties.Add("alignment");
            }

            // Set texture mode
            if (!string.IsNullOrEmpty(textureMode))
            {
                trailRenderer.textureMode = textureMode.ToLowerInvariant() switch
                {
                    "stretch" => LineTextureMode.Stretch,
                    "tile" => LineTextureMode.Tile,
                    "distributepersegment" or "distribute_per_segment" => LineTextureMode.DistributePerSegment,
                    "repeatpersegment" or "repeat_per_segment" => LineTextureMode.RepeatPerSegment,
                    "static" => LineTextureMode.Static,
                    _ => trailRenderer.textureMode
                };
                modifiedProperties.Add("textureMode");
            }

            // Set generateLightingData
            if (generateLightingData.HasValue)
            {
                trailRenderer.generateLightingData = generateLightingData.Value;
                modifiedProperties.Add("generateLightingData");
            }

            // Set shadowBias
            if (shadowBias.HasValue)
            {
                trailRenderer.shadowBias = shadowBias.Value;
                modifiedProperties.Add("shadowBias");
            }

            EditorUtility.SetDirty(trailRenderer);

            return new
            {
                success = true,
                message = $"Modified {modifiedProperties.Count} property(ies) on TrailRenderer.",
                gameObject = trailRenderer.name,
                instanceID = trailRenderer.gameObject.GetInstanceID(),
                modifiedProperties
            };
        }

        /// <summary>
        /// Clears the trail path.
        /// </summary>
        public static object Clear(string target)
        {
            TrailRenderer trailRenderer = GetTrailRenderer(target, out object errorResult);
            if (trailRenderer == null)
            {
                return errorResult;
            }

            int previousPositionCount = trailRenderer.positionCount;
            trailRenderer.Clear();

            return new
            {
                success = true,
                message = $"Trail cleared on '{trailRenderer.name}'.",
                gameObject = trailRenderer.name,
                instanceID = trailRenderer.gameObject.GetInstanceID(),
                previousPositionCount,
                currentPositionCount = trailRenderer.positionCount
            };
        }

        #region Helper Methods

        private static TrailRenderer GetTrailRenderer(string target, out object errorResult)
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

            TrailRenderer trailRenderer = gameObject.GetComponent<TrailRenderer>();
            if (trailRenderer == null)
            {
                errorResult = new
                {
                    success = false,
                    error = $"No TrailRenderer component found on '{gameObject.name}'."
                };
                return null;
            }

            return trailRenderer;
        }

        private static void ApplyWidth(TrailRenderer trailRenderer, object width)
        {
            // Handle single value
            if (width is double doubleWidth)
            {
                trailRenderer.startWidth = (float)doubleWidth;
                trailRenderer.endWidth = (float)doubleWidth;
                return;
            }
            if (width is float floatWidth)
            {
                trailRenderer.startWidth = floatWidth;
                trailRenderer.endWidth = floatWidth;
                return;
            }
            if (width is int intWidth)
            {
                trailRenderer.startWidth = intWidth;
                trailRenderer.endWidth = intWidth;
                return;
            }
            if (width is long longWidth)
            {
                trailRenderer.startWidth = longWidth;
                trailRenderer.endWidth = longWidth;
                return;
            }

            // Handle dictionary with start/end
            if (width is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("start", out object startValue))
                {
                    trailRenderer.startWidth = Convert.ToSingle(startValue);
                }
                if (dict.TryGetValue("end", out object endValue))
                {
                    trailRenderer.endWidth = Convert.ToSingle(endValue);
                }
                if (dict.TryGetValue("curve", out object curveValue))
                {
                    AnimationCurve curve = VFXCommon.ParseAnimationCurve(curveValue);
                    if (curve != null)
                    {
                        trailRenderer.widthCurve = curve;
                    }
                }
                if (dict.TryGetValue("multiplier", out object multiplierValue))
                {
                    trailRenderer.widthMultiplier = Convert.ToSingle(multiplierValue);
                }
                return;
            }

            // Handle animation curve
            AnimationCurve parsedCurve = VFXCommon.ParseAnimationCurve(width);
            if (parsedCurve != null)
            {
                trailRenderer.widthCurve = parsedCurve;
            }
        }

        private static void ApplyColor(TrailRenderer trailRenderer, object color)
        {
            // Try single color first
            Color? parsedColor = VFXCommon.ParseColor(color);
            if (parsedColor.HasValue)
            {
                trailRenderer.startColor = parsedColor.Value;
                trailRenderer.endColor = parsedColor.Value;
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
                        trailRenderer.startColor = startColor.Value;
                    }
                }
                if (dict.TryGetValue("end", out object endValue))
                {
                    Color? endColor = VFXCommon.ParseColor(endValue);
                    if (endColor.HasValue)
                    {
                        trailRenderer.endColor = endColor.Value;
                    }
                }
                if (dict.TryGetValue("gradient", out object gradientValue))
                {
                    Gradient gradient = VFXCommon.ParseGradient(gradientValue);
                    if (gradient != null)
                    {
                        trailRenderer.colorGradient = gradient;
                    }
                }
                return;
            }

            // Try gradient
            Gradient parsedGradient = VFXCommon.ParseGradient(color);
            if (parsedGradient != null)
            {
                trailRenderer.colorGradient = parsedGradient;
            }
        }

        private static bool ApplyMaterial(TrailRenderer trailRenderer, string materialPath)
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
                trailRenderer.sharedMaterial = material;
                return true;
            }

            Debug.LogWarning($"[TrailOps] Material not found: {materialPath}");
            return false;
        }

        #endregion
    }
}
