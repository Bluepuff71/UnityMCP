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
    public static class ManageVFX
    {
        /// <summary>
        /// Manages VFX components: particles, lines, and trails.
        /// </summary>
        /// <param name="action">The action to perform (see description for valid actions)</param>
        /// <param name="target">GameObject path or instance ID for finding the target</param>
        /// <param name="particleSettings">Dict of main module settings for particle_set</param>
        /// <param name="positions">Array of Vector3 positions for line_set</param>
        /// <param name="width">Width curve or single value for line/trail</param>
        /// <param name="color">Color gradient or single color for line/trail</param>
        /// <param name="time">Trail time for trail_set</param>
        /// <param name="withChildren">Apply to child particle systems (default: true)</param>
        /// <param name="clearOnPlay">Clear particles when stopping (default: true)</param>
        /// <param name="useWorldSpace">Use world space for line positions</param>
        /// <param name="loop">Whether line should loop</param>
        /// <param name="materialPath">Path to material asset</param>
        /// <param name="minVertexDistance">Min distance between trail vertices</param>
        /// <param name="emitting">Whether trail is emitting</param>
        /// <returns>Result object indicating success or failure with appropriate data.</returns>
        [MCPTool("manage_vfx", "Manage VFX: particles (play/pause/stop/restart/get/set), lines (create/get/set), trails (get/set/clear)", Category = "VFX", DestructiveHint = true)]
        public static object Execute(
            [MCPParam("action", "Action: particle_play, particle_pause, particle_stop, particle_restart, particle_get, particle_set, line_create, line_get, line_set, trail_get, trail_set, trail_clear", required: true, Enum = new[] { "particle_play", "particle_pause", "particle_stop", "particle_restart", "particle_get", "particle_set", "line_create", "line_get", "line_set", "trail_get", "trail_set", "trail_clear" })] string action,
            [MCPParam("target", "GameObject path or instance ID")] string target = null,
            [MCPParam("particle_settings", "Dict of main module settings for particle_set")] object particleSettings = null,
            [MCPParam("positions", "Array of Vector3 positions for line_set/line_create")] List<object> positions = null,
            [MCPParam("width", "Width curve or single value for line/trail")] object width = null,
            [MCPParam("color", "Color gradient or single color for line/trail")] object color = null,
            [MCPParam("time", "Trail time for trail_set")] float? time = null,
            [MCPParam("with_children", "Apply to child particle systems (default: true)")] bool withChildren = true,
            [MCPParam("clear_on_play", "Clear particles when stopping (default: true)")] bool clearOnPlay = true,
            [MCPParam("use_world_space", "Use world space for line positions")] bool? useWorldSpace = null,
            [MCPParam("loop", "Whether line should loop")] bool? loop = null,
            [MCPParam("material_path", "Path to material asset")] string materialPath = null,
            [MCPParam("min_vertex_distance", "Min distance between trail vertices")] float? minVertexDistance = null,
            [MCPParam("emitting", "Whether trail is emitting")] bool? emitting = null,
            [MCPParam("corner_vertices", "Number of corner vertices for line/trail")] int? cornerVertices = null,
            [MCPParam("cap_vertices", "Number of cap vertices for line/trail")] int? capVertices = null,
            [MCPParam("alignment", "Line/trail alignment: view, transformz")] string alignment = null,
            [MCPParam("texture_mode", "Line/trail texture mode: stretch, tile, distribute_per_segment, repeat_per_segment, static")] string textureMode = null,
            [MCPParam("autodestruct", "Whether trail should autodestruct")] bool? autodestruct = null,
            [MCPParam("generate_lighting_data", "Whether trail generates lighting data")] bool? generateLightingData = null,
            [MCPParam("shadow_bias", "Trail shadow bias")] float? shadowBias = null)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw MCPException.InvalidParams("The 'action' parameter is required.");
            }

            string normalizedAction = action.Trim().ToLowerInvariant();

            try
            {
                // Parse positions if provided
                List<Vector3> parsedPositions = null;
                if (positions != null && positions.Count > 0)
                {
                    parsedPositions = VFXCommon.ParsePositions(positions);
                }

                // Parse particle settings if provided
                Dictionary<string, object> parsedSettings = null;
                if (particleSettings != null)
                {
                    parsedSettings = VFXCommon.ConvertToDictionary(particleSettings);
                }

                return normalizedAction switch
                {
                    // Particle actions
                    "particle_play" => ParticleOps.Play(target, withChildren),
                    "particle_pause" => ParticleOps.Pause(target, withChildren),
                    "particle_stop" => ParticleOps.Stop(target, withChildren, clearOnPlay),
                    "particle_restart" => ParticleOps.Restart(target, withChildren),
                    "particle_get" => ParticleOps.Get(target),
                    "particle_set" => ParticleOps.Set(target, parsedSettings),

                    // Line actions
                    "line_create" => LineOps.Create(target, parsedPositions, width, color, materialPath),
                    "line_get" => LineOps.Get(target),
                    "line_set" => LineOps.Set(target, parsedPositions, width, color, useWorldSpace, loop, materialPath, cornerVertices, capVertices, alignment, textureMode),

                    // Trail actions
                    "trail_get" => TrailOps.Get(target),
                    "trail_set" => TrailOps.Set(target, time, width, color, minVertexDistance, autodestruct, emitting, materialPath, cornerVertices, capVertices, alignment, textureMode, generateLightingData, shadowBias),
                    "trail_clear" => TrailOps.Clear(target),

                    _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: particle_play, particle_pause, particle_stop, particle_restart, particle_get, particle_set, line_create, line_get, line_set, trail_get, trail_set, trail_clear")
                };
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageVFX] Error executing action '{action}': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action '{action}': {exception.Message}"
                };
            }
        }
    }
}
