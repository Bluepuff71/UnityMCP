using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityMCP.Editor.Resources.Project
{
    /// <summary>
    /// Resource provider for quality settings information.
    /// </summary>
    public static class QualitySettingsResource
    {
        /// <summary>
        /// Gets all quality settings information including quality levels and their configurations.
        /// </summary>
        /// <returns>Object containing quality settings information.</returns>
        [MCPResource("project://quality", "Quality settings - all quality levels and their configurations")]
        public static object GetQualitySettings()
        {
            try
            {
                string[] qualityLevelNames = QualitySettings.names;
                int currentQualityLevel = QualitySettings.GetQualityLevel();
                string currentQualityName = qualityLevelNames.Length > currentQualityLevel
                    ? qualityLevelNames[currentQualityLevel]
                    : "Unknown";

                // Build per-level settings by temporarily switching quality levels
                var qualityLevels = new List<object>();

                for (int levelIndex = 0; levelIndex < qualityLevelNames.Length; levelIndex++)
                {
                    // Temporarily switch to this quality level to read its settings
                    QualitySettings.SetQualityLevel(levelIndex, applyExpensiveChanges: false);

                    qualityLevels.Add(new
                    {
                        index = levelIndex,
                        name = qualityLevelNames[levelIndex],
                        isCurrent = levelIndex == currentQualityLevel,
                        rendering = new
                        {
                            pixelLightCount = QualitySettings.pixelLightCount,
                            textureQuality = GetTextureQualityName(QualitySettings.globalTextureMipmapLimit),
                            textureMipmapLimit = QualitySettings.globalTextureMipmapLimit,
                            anisotropicFiltering = QualitySettings.anisotropicFiltering.ToString(),
                            antiAliasing = QualitySettings.antiAliasing,
                            softParticles = QualitySettings.softParticles,
                            realtimeReflectionProbes = QualitySettings.realtimeReflectionProbes,
                            billboardsFaceCameraPosition = QualitySettings.billboardsFaceCameraPosition,
                            useLegacyDetailDistribution = GetLegacyDetailDistribution()
                        },
                        shadows = new
                        {
                            shadowQuality = QualitySettings.shadows.ToString(),
                            shadowResolution = QualitySettings.shadowResolution.ToString(),
                            shadowProjection = QualitySettings.shadowProjection.ToString(),
                            shadowDistance = QualitySettings.shadowDistance,
                            shadowCascades = QualitySettings.shadowCascades,
                            shadowNearPlaneOffset = QualitySettings.shadowNearPlaneOffset
                        },
                        lod = new
                        {
                            lodBias = QualitySettings.lodBias,
                            maximumLodLevel = QualitySettings.maximumLODLevel,
                            skinWeights = QualitySettings.skinWeights.ToString(),
                            streamingMipmapsActive = QualitySettings.streamingMipmapsActive,
                            streamingMipmapsMemoryBudget = QualitySettings.streamingMipmapsMemoryBudget,
                            streamingMipmapsMaxLevelReduction = QualitySettings.streamingMipmapsMaxLevelReduction,
                            streamingMipmapsAddAllCameras = QualitySettings.streamingMipmapsAddAllCameras
                        },
                        other = new
                        {
                            vSyncCount = QualitySettings.vSyncCount,
                            asyncUploadTimeSlice = QualitySettings.asyncUploadTimeSlice,
                            asyncUploadBufferSize = QualitySettings.asyncUploadBufferSize,
                            asyncUploadPersistentBuffer = QualitySettings.asyncUploadPersistentBuffer,
                            resolutionScalingFixedDPIFactor = QualitySettings.resolutionScalingFixedDPIFactor
                        },
                        renderPipeline = new
                        {
                            renderPipelineAsset = QualitySettings.renderPipeline != null
                                ? new
                                {
                                    name = QualitySettings.renderPipeline.name,
                                    type = QualitySettings.renderPipeline.GetType().Name
                                }
                                : null,
                            usesRenderPipeline = QualitySettings.renderPipeline != null
                        }
                    });
                }

                // Restore the original quality level
                QualitySettings.SetQualityLevel(currentQualityLevel, applyExpensiveChanges: false);

                return new
                {
                    current = new
                    {
                        index = currentQualityLevel,
                        name = currentQualityName
                    },
                    qualityLevelCount = qualityLevelNames.Length,
                    qualityLevelNames = qualityLevelNames,
                    desiredColorSpace = QualitySettings.desiredColorSpace.ToString(),
                    activeColorSpace = QualitySettings.activeColorSpace.ToString(),
                    qualityLevels = qualityLevels.ToArray()
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    error = true,
                    message = $"Failed to retrieve quality settings: {exception.Message}",
                    stackTrace = exception.StackTrace
                };
            }
        }

        /// <summary>
        /// Gets a human-readable name for the texture quality setting.
        /// </summary>
        private static string GetTextureQualityName(int mipmapLimit)
        {
            return mipmapLimit switch
            {
                0 => "Full Resolution",
                1 => "Half Resolution",
                2 => "Quarter Resolution",
                3 => "Eighth Resolution",
                _ => $"1/{1 << mipmapLimit} Resolution"
            };
        }

        /// <summary>
        /// Gets the legacy detail distribution setting using reflection for compatibility.
        /// </summary>
        private static bool GetLegacyDetailDistribution()
        {
            try
            {
                // This property may not exist in all Unity versions
                var property = typeof(QualitySettings).GetProperty("useLegacyDetailDistribution",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (property != null)
                {
                    return (bool)property.GetValue(null);
                }
            }
            catch
            {
                // Property doesn't exist in this Unity version
            }

            return false;
        }
    }
}
