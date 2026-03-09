using System;
using System.Collections.Generic;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Tools.VFX;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for managing VFX components: ParticleSystem, LineRenderer, and TrailRenderer.
    /// </summary>
    [MCPTool("manage_vfx", "Manage VFX: particles (play/pause/stop/restart/get/set), lines (create/get/set), trails (get/set/clear)", Category = "VFX")]
    public static class ManageVFX
    {
        #region Particle Actions

        /// <summary>
        /// Plays a particle system.
        /// </summary>
        [MCPAction("particle_play", Description = "Play a particle system")]
        public static object ParticlePlay(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target,
            [MCPParam("with_children", "Apply to child particle systems (default: true)")] bool withChildren = true)
        {
            try
            {
                return ParticleOps.Play(target, withChildren);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'particle_play': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'particle_play': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Pauses a particle system.
        /// </summary>
        [MCPAction("particle_pause", Description = "Pause a particle system")]
        public static object ParticlePause(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target,
            [MCPParam("with_children", "Apply to child particle systems (default: true)")] bool withChildren = true)
        {
            try
            {
                return ParticleOps.Pause(target, withChildren);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'particle_pause': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'particle_pause': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Stops a particle system.
        /// </summary>
        [MCPAction("particle_stop", Description = "Stop a particle system")]
        public static object ParticleStop(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target,
            [MCPParam("with_children", "Apply to child particle systems (default: true)")] bool withChildren = true,
            [MCPParam("clear_on_play", "Clear particles when stopping (default: true)")] bool clearOnPlay = true)
        {
            try
            {
                return ParticleOps.Stop(target, withChildren, clearOnPlay);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'particle_stop': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'particle_stop': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Restarts a particle system (stop + clear + play).
        /// </summary>
        [MCPAction("particle_restart", Description = "Restart a particle system (stop + clear + play)")]
        public static object ParticleRestart(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target,
            [MCPParam("with_children", "Apply to child particle systems (default: true)")] bool withChildren = true)
        {
            try
            {
                return ParticleOps.Restart(target, withChildren);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'particle_restart': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'particle_restart': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Gets information about a particle system.
        /// </summary>
        [MCPAction("particle_get", Description = "Get particle system information", ReadOnlyHint = true)]
        public static object ParticleGet(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target)
        {
            try
            {
                return ParticleOps.Get(target);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'particle_get': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'particle_get': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Sets properties on a particle system's main module.
        /// </summary>
        [MCPAction("particle_set", Description = "Set particle system main module properties")]
        public static object ParticleSet(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target,
            [MCPParam("particle_settings", "Dict of main module settings for particle_set", required: true)] object particleSettings)
        {
            try
            {
                Dictionary<string, object> parsedSettings = VFXCommon.ConvertToDictionary(particleSettings);
                return ParticleOps.Set(target, parsedSettings);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'particle_set': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'particle_set': {exception.Message}"
                };
            }
        }

        #endregion

        #region Line Actions

        /// <summary>
        /// Creates a LineRenderer on a GameObject.
        /// </summary>
        [MCPAction("line_create", Description = "Create a LineRenderer on a GameObject")]
        public static object LineCreate(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target,
            [MCPParam("positions", "Array of Vector3 positions for line_create")] List<object> positions = null,
            [MCPParam("material_path", "Path to material asset")] string materialPath = null,
            [MCPParam("width", "Width curve or single value for line")] object width = null,
            [MCPParam("color", "Color gradient or single color for line")] object color = null,
            [MCPParam("use_world_space", "Use world space for line positions")] bool? useWorldSpace = null,
            [MCPParam("loop", "Whether line should loop")] bool? loop = null)
        {
            try
            {
                List<Vector3> parsedPositions = null;
                if (positions != null && positions.Count > 0)
                {
                    parsedPositions = VFXCommon.ParsePositions(positions);
                }

                return LineOps.Create(target, parsedPositions, width, color, materialPath);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'line_create': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'line_create': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Gets information about a LineRenderer.
        /// </summary>
        [MCPAction("line_get", Description = "Get LineRenderer information", ReadOnlyHint = true)]
        public static object LineGet(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target)
        {
            try
            {
                return LineOps.Get(target);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'line_get': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'line_get': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Sets properties on a LineRenderer.
        /// </summary>
        [MCPAction("line_set", Description = "Set LineRenderer properties")]
        public static object LineSet(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target,
            [MCPParam("positions", "Array of Vector3 positions for line_set")] List<object> positions = null,
            [MCPParam("width", "Width curve or single value for line")] object width = null,
            [MCPParam("color", "Color gradient or single color for line")] object color = null,
            [MCPParam("use_world_space", "Use world space for line positions")] bool? useWorldSpace = null,
            [MCPParam("loop", "Whether line should loop")] bool? loop = null,
            [MCPParam("material_path", "Path to material asset")] string materialPath = null,
            [MCPParam("alignment", "Line alignment: view, transformz")] string alignment = null,
            [MCPParam("texture_mode", "Line texture mode: stretch, tile, distribute_per_segment, repeat_per_segment, static")] string textureMode = null,
            [MCPParam("corner_vertices", "Number of corner vertices for line")] int? cornerVertices = null,
            [MCPParam("cap_vertices", "Number of cap vertices for line")] int? capVertices = null)
        {
            try
            {
                List<Vector3> parsedPositions = null;
                if (positions != null && positions.Count > 0)
                {
                    parsedPositions = VFXCommon.ParsePositions(positions);
                }

                return LineOps.Set(target, parsedPositions, width, color, useWorldSpace, loop, materialPath, cornerVertices, capVertices, alignment, textureMode);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'line_set': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'line_set': {exception.Message}"
                };
            }
        }

        #endregion

        #region Trail Actions

        /// <summary>
        /// Gets information about a TrailRenderer.
        /// </summary>
        [MCPAction("trail_get", Description = "Get TrailRenderer information", ReadOnlyHint = true)]
        public static object TrailGet(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target)
        {
            try
            {
                return TrailOps.Get(target);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'trail_get': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'trail_get': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Sets properties on a TrailRenderer.
        /// </summary>
        [MCPAction("trail_set", Description = "Set TrailRenderer properties")]
        public static object TrailSet(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target,
            [MCPParam("time", "Trail time for trail_set")] float? time = null,
            [MCPParam("width", "Width curve or single value for trail")] object width = null,
            [MCPParam("color", "Color gradient or single color for trail")] object color = null,
            [MCPParam("min_vertex_distance", "Min distance between trail vertices")] float? minVertexDistance = null,
            [MCPParam("autodestruct", "Whether trail should autodestruct")] bool? autodestruct = null,
            [MCPParam("emitting", "Whether trail is emitting")] bool? emitting = null,
            [MCPParam("generate_lighting_data", "Whether trail generates lighting data")] bool? generateLightingData = null,
            [MCPParam("material_path", "Path to material asset")] string materialPath = null,
            [MCPParam("alignment", "Trail alignment: view, transformz")] string alignment = null,
            [MCPParam("texture_mode", "Trail texture mode: stretch, tile, distribute_per_segment, repeat_per_segment, static")] string textureMode = null,
            [MCPParam("shadow_bias", "Trail shadow bias")] float? shadowBias = null,
            [MCPParam("corner_vertices", "Number of corner vertices for trail")] int? cornerVertices = null,
            [MCPParam("cap_vertices", "Number of cap vertices for trail")] int? capVertices = null)
        {
            try
            {
                return TrailOps.Set(target, time, width, color, minVertexDistance, autodestruct, emitting, materialPath, cornerVertices, capVertices, alignment, textureMode, generateLightingData, shadowBias);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'trail_set': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'trail_set': {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Clears the trail path.
        /// </summary>
        [MCPAction("trail_clear", Description = "Clear the trail path")]
        public static object TrailClear(
            [MCPParam("target", "GameObject path or instance ID", required: true)] string target)
        {
            try
            {
                return TrailOps.Clear(target);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action 'trail_clear': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action 'trail_clear': {exception.Message}"
                };
            }
        }

        #endregion
    }
}
