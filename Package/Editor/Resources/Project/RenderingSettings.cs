using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityMCP.Editor.Resources.Project
{
    /// <summary>
    /// Resource provider for Unity rendering settings.
    /// </summary>
    public static class RenderingSettings
    {
        /// <summary>
        /// Gets the current rendering settings for the project.
        /// </summary>
        /// <returns>Object containing render pipeline, ambient lighting, fog settings, and more.</returns>
        [MCPResource("project://rendering", "Rendering settings including render pipeline, ambient lighting, and fog")]
        public static object Get()
        {
            return new
            {
                renderPipeline = GetRenderPipelineInfo(),
                ambientLighting = GetAmbientLightingInfo(),
                fog = GetFogSettings(),
                skybox = GetSkyboxInfo(),
                reflections = GetReflectionSettings(),
                haloSettings = GetHaloSettings(),
                lightSettings = GetLightSettings(),
                qualitySettings = GetQualitySettingsInfo()
            };
        }

        /// <summary>
        /// Gets information about the current render pipeline.
        /// </summary>
        private static object GetRenderPipelineInfo()
        {
            var currentPipeline = GraphicsSettings.currentRenderPipeline;
            var defaultPipeline = GraphicsSettings.defaultRenderPipeline;

            string pipelineType = "Built-in";
            string pipelineName = "Built-in Render Pipeline";

            if (currentPipeline != null)
            {
                var pipelineTypeName = currentPipeline.GetType().Name;

                if (pipelineTypeName.Contains("Universal") || pipelineTypeName.Contains("URP"))
                {
                    pipelineType = "URP";
                    pipelineName = "Universal Render Pipeline";
                }
                else if (pipelineTypeName.Contains("HD") || pipelineTypeName.Contains("HDRP"))
                {
                    pipelineType = "HDRP";
                    pipelineName = "High Definition Render Pipeline";
                }
                else
                {
                    pipelineType = "Custom";
                    pipelineName = pipelineTypeName;
                }
            }

            return new
            {
                type = pipelineType,
                name = pipelineName,
                currentPipelineAsset = currentPipeline != null ? new
                {
                    name = currentPipeline.name,
                    typeName = currentPipeline.GetType().Name,
                    assetPath = AssetDatabase.GetAssetPath(currentPipeline)
                } : null,
                defaultPipelineAsset = defaultPipeline != null ? new
                {
                    name = defaultPipeline.name,
                    typeName = defaultPipeline.GetType().Name,
                    assetPath = AssetDatabase.GetAssetPath(defaultPipeline)
                } : null,
                isUsingScriptableRenderPipeline = currentPipeline != null,
                transparencySortMode = GraphicsSettings.transparencySortMode.ToString(),
                lightsUseLinearIntensity = GraphicsSettings.lightsUseLinearIntensity,
                lightsUseColorTemperature = GraphicsSettings.lightsUseColorTemperature,
                useScriptableRenderPipelineBatching = GraphicsSettings.useScriptableRenderPipelineBatching,
                logWhenShaderIsCompiled = GraphicsSettings.logWhenShaderIsCompiled
            };
        }

        /// <summary>
        /// Gets ambient lighting settings.
        /// </summary>
        private static object GetAmbientLightingInfo()
        {
            return new
            {
                ambientMode = RenderSettings.ambientMode.ToString(),
                ambientIntensity = RenderSettings.ambientIntensity,
                ambientLight = FormatColor(RenderSettings.ambientLight),
                ambientSkyColor = FormatColor(RenderSettings.ambientSkyColor),
                ambientEquatorColor = FormatColor(RenderSettings.ambientEquatorColor),
                ambientGroundColor = FormatColor(RenderSettings.ambientGroundColor),
                ambientProbe = GetSphericalHarmonicsInfo(RenderSettings.ambientProbe),
                subtractiveShadowColor = FormatColor(RenderSettings.subtractiveShadowColor)
            };
        }

        /// <summary>
        /// Gets fog settings.
        /// </summary>
        private static object GetFogSettings()
        {
            return new
            {
                enabled = RenderSettings.fog,
                mode = RenderSettings.fogMode.ToString(),
                color = FormatColor(RenderSettings.fogColor),
                density = RenderSettings.fogDensity,
                startDistance = RenderSettings.fogStartDistance,
                endDistance = RenderSettings.fogEndDistance
            };
        }

        /// <summary>
        /// Gets skybox information.
        /// </summary>
        private static object GetSkyboxInfo()
        {
            var skybox = RenderSettings.skybox;
            if (skybox == null)
            {
                return new { hasSkybox = false };
            }

            var shaderPropertyNames = new List<string>();
            var shader = skybox.shader;
            if (shader != null)
            {
                int propertyCount = shader.GetPropertyCount();
                for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
                {
                    shaderPropertyNames.Add(shader.GetPropertyName(propertyIndex));
                }
            }

            return new
            {
                hasSkybox = true,
                materialName = skybox.name,
                shaderName = shader != null ? shader.name : null,
                assetPath = AssetDatabase.GetAssetPath(skybox),
                properties = shaderPropertyNames.ToArray()
            };
        }

        /// <summary>
        /// Gets reflection settings.
        /// </summary>
        private static object GetReflectionSettings()
        {
            return new
            {
                defaultReflectionMode = RenderSettings.defaultReflectionMode.ToString(),
                defaultReflectionResolution = RenderSettings.defaultReflectionResolution,
                reflectionIntensity = RenderSettings.reflectionIntensity,
                reflectionBounces = RenderSettings.reflectionBounces,
                customReflectionTexture = RenderSettings.customReflectionTexture != null ? new
                {
                    name = RenderSettings.customReflectionTexture.name,
                    assetPath = AssetDatabase.GetAssetPath(RenderSettings.customReflectionTexture)
                } : null
            };
        }

        /// <summary>
        /// Gets halo settings.
        /// </summary>
        private static object GetHaloSettings()
        {
            return new
            {
                haloStrength = RenderSettings.haloStrength,
                flareStrength = RenderSettings.flareStrength,
                flareFadeSpeed = RenderSettings.flareFadeSpeed
            };
        }

        /// <summary>
        /// Gets light settings.
        /// </summary>
        private static object GetLightSettings()
        {
            var sun = RenderSettings.sun;
            return new
            {
                sun = sun != null ? new
                {
                    name = sun.name,
                    instanceId = sun.GetInstanceID(),
                    lightType = sun.type.ToString(),
                    color = FormatColor(sun.color),
                    intensity = sun.intensity
                } : null
            };
        }

        /// <summary>
        /// Gets relevant quality settings.
        /// </summary>
        private static object GetQualitySettingsInfo()
        {
            var qualityLevelNames = QualitySettings.names;

            return new
            {
                currentQualityLevel = QualitySettings.GetQualityLevel(),
                currentQualityName = qualityLevelNames.Length > 0 ? qualityLevelNames[QualitySettings.GetQualityLevel()] : "Unknown",
                qualityLevels = qualityLevelNames,
                pixelLightCount = QualitySettings.pixelLightCount,
                anisotropicFiltering = QualitySettings.anisotropicFiltering.ToString(),
                antiAliasing = QualitySettings.antiAliasing,
                softParticles = QualitySettings.softParticles,
                softVegetation = QualitySettings.softVegetation,
                realtimeReflectionProbes = QualitySettings.realtimeReflectionProbes,
                billboardsFaceCameraPosition = QualitySettings.billboardsFaceCameraPosition,
                resolutionScalingFixedDPIFactor = QualitySettings.resolutionScalingFixedDPIFactor,
                shadows = new
                {
                    shadowQuality = QualitySettings.shadows.ToString(),
                    shadowResolution = QualitySettings.shadowResolution.ToString(),
                    shadowProjection = QualitySettings.shadowProjection.ToString(),
                    shadowDistance = QualitySettings.shadowDistance,
                    shadowNearPlaneOffset = QualitySettings.shadowNearPlaneOffset,
                    shadowCascades = QualitySettings.shadowCascades
                },
                lod = new
                {
                    lodBias = QualitySettings.lodBias,
                    maximumLODLevel = QualitySettings.maximumLODLevel
                },
                textures = new
                {
                    globalTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit,
                    streamingMipmapsActive = QualitySettings.streamingMipmapsActive
                },
                vSync = new
                {
                    vSyncCount = QualitySettings.vSyncCount,
                    maxQueuedFrames = QualitySettings.maxQueuedFrames
                }
            };
        }

        /// <summary>
        /// Formats a Color for JSON output.
        /// </summary>
        private static object FormatColor(Color color)
        {
            return new
            {
                r = color.r,
                g = color.g,
                b = color.b,
                a = color.a,
                hex = ColorUtility.ToHtmlStringRGBA(color)
            };
        }

        /// <summary>
        /// Gets basic info about spherical harmonics (ambient probe).
        /// </summary>
        private static object GetSphericalHarmonicsInfo(SphericalHarmonicsL2 sphericalHarmonics)
        {
            // Extract approximate dominant color from SH
            // The first coefficient (L0) represents the average color
            return new
            {
                description = "Spherical harmonics ambient probe data",
                hasData = true
            };
        }
    }
}
