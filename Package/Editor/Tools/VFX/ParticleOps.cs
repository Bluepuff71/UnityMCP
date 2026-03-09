using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;


namespace UnityMCP.Editor.Tools.VFX
{
    /// <summary>
    /// Particle system operations for manage_vfx tool.
    /// </summary>
    public static class ParticleOps
    {
        /// <summary>
        /// Plays a particle system.
        /// </summary>
        public static object Play(string target, bool withChildren = true)
        {
            ParticleSystem particleSystem = GetParticleSystem(target, out object errorResult);
            if (particleSystem == null)
            {
                return errorResult;
            }

            particleSystem.Play(withChildren);

            return new
            {
                success = true,
                message = $"Particle system '{particleSystem.name}' started playing.",
                gameObject = particleSystem.name,
                instanceID = particleSystem.gameObject.GetInstanceID(),
                isPlaying = particleSystem.isPlaying,
                withChildren
            };
        }

        /// <summary>
        /// Pauses a particle system.
        /// </summary>
        public static object Pause(string target, bool withChildren = true)
        {
            ParticleSystem particleSystem = GetParticleSystem(target, out object errorResult);
            if (particleSystem == null)
            {
                return errorResult;
            }

            particleSystem.Pause(withChildren);

            return new
            {
                success = true,
                message = $"Particle system '{particleSystem.name}' paused.",
                gameObject = particleSystem.name,
                instanceID = particleSystem.gameObject.GetInstanceID(),
                isPaused = particleSystem.isPaused,
                withChildren
            };
        }

        /// <summary>
        /// Stops a particle system.
        /// </summary>
        public static object Stop(string target, bool withChildren = true, bool clearParticles = true)
        {
            ParticleSystem particleSystem = GetParticleSystem(target, out object errorResult);
            if (particleSystem == null)
            {
                return errorResult;
            }

            var stopBehavior = clearParticles
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting;

            particleSystem.Stop(withChildren, stopBehavior);

            return new
            {
                success = true,
                message = $"Particle system '{particleSystem.name}' stopped.",
                gameObject = particleSystem.name,
                instanceID = particleSystem.gameObject.GetInstanceID(),
                isStopped = particleSystem.isStopped,
                cleared = clearParticles,
                withChildren
            };
        }

        /// <summary>
        /// Restarts a particle system (stop + clear + play).
        /// </summary>
        public static object Restart(string target, bool withChildren = true)
        {
            ParticleSystem particleSystem = GetParticleSystem(target, out object errorResult);
            if (particleSystem == null)
            {
                return errorResult;
            }

            particleSystem.Stop(withChildren, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystem.Clear(withChildren);
            particleSystem.Play(withChildren);

            return new
            {
                success = true,
                message = $"Particle system '{particleSystem.name}' restarted.",
                gameObject = particleSystem.name,
                instanceID = particleSystem.gameObject.GetInstanceID(),
                isPlaying = particleSystem.isPlaying,
                withChildren
            };
        }

        /// <summary>
        /// Gets information about a particle system.
        /// </summary>
        public static object Get(string target)
        {
            ParticleSystem particleSystem = GetParticleSystem(target, out object errorResult);
            if (particleSystem == null)
            {
                return errorResult;
            }

            return new
            {
                success = true,
                gameObject = particleSystem.name,
                path = VFXCommon.GetGameObjectPath(particleSystem.gameObject),
                instanceID = particleSystem.gameObject.GetInstanceID(),
                state = new
                {
                    isPlaying = particleSystem.isPlaying,
                    isPaused = particleSystem.isPaused,
                    isStopped = particleSystem.isStopped,
                    isEmitting = particleSystem.isEmitting,
                    particleCount = particleSystem.particleCount,
                    time = particleSystem.time
                },
                main = BuildMainModuleInfo(particleSystem.main),
                emission = BuildEmissionModuleInfo(particleSystem.emission),
                shape = BuildShapeModuleInfo(particleSystem.shape),
                renderer = BuildRendererInfo(particleSystem.GetComponent<ParticleSystemRenderer>())
            };
        }

        /// <summary>
        /// Sets properties on a particle system's main module.
        /// </summary>
        public static object Set(string target, Dictionary<string, object> settings)
        {
            ParticleSystem particleSystem = GetParticleSystem(target, out object errorResult);
            if (particleSystem == null)
            {
                return errorResult;
            }

            if (settings == null || settings.Count == 0)
            {
                throw MCPException.InvalidParams("The 'particle_settings' parameter is required for particle_set action.");
            }

            Undo.RecordObject(particleSystem, "Modify Particle System");

            var mainModule = particleSystem.main;
            var results = new List<object>();

            foreach (var kvp in settings)
            {
                string propertyName = kvp.Key.ToLowerInvariant();
                object value = kvp.Value;

                try
                {
                    bool success = SetMainModuleProperty(ref mainModule, propertyName, value, out string resultMessage);
                    results.Add(new
                    {
                        property = kvp.Key,
                        success,
                        message = resultMessage
                    });
                }
                catch (Exception exception)
                {
                    results.Add(new
                    {
                        property = kvp.Key,
                        success = false,
                        error = exception.Message
                    });
                }
            }

            EditorUtility.SetDirty(particleSystem);

            int successCount = 0;
            int failCount = 0;
            foreach (var result in results)
            {
                if (result is { } r && r.GetType().GetProperty("success")?.GetValue(r) is bool s && s)
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                }
            }

            return new
            {
                success = failCount == 0,
                message = failCount == 0
                    ? $"Successfully set {successCount} property(ies) on particle system."
                    : $"Set {successCount} property(ies), {failCount} failed.",
                gameObject = particleSystem.name,
                instanceID = particleSystem.gameObject.GetInstanceID(),
                results
            };
        }

        #region Helper Methods

        private static ParticleSystem GetParticleSystem(string target, out object errorResult)
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

            ParticleSystem particleSystem = gameObject.GetComponent<ParticleSystem>();
            if (particleSystem == null)
            {
                errorResult = new
                {
                    success = false,
                    error = $"No ParticleSystem component found on '{gameObject.name}'."
                };
                return null;
            }

            return particleSystem;
        }

        private static bool SetMainModuleProperty(ref ParticleSystem.MainModule main, string propertyName, object value, out string message)
        {
            message = "Property set successfully.";

            switch (propertyName)
            {
                case "duration":
                    main.duration = Convert.ToSingle(value);
                    break;

                case "looping":
                    main.loop = Convert.ToBoolean(value);
                    break;

                case "loop":
                    main.loop = Convert.ToBoolean(value);
                    break;

                case "prewarm":
                    main.prewarm = Convert.ToBoolean(value);
                    break;

                case "startdelay":
                case "start_delay":
                    main.startDelay = Convert.ToSingle(value);
                    break;

                case "startlifetime":
                case "start_lifetime":
                    main.startLifetime = Convert.ToSingle(value);
                    break;

                case "startspeed":
                case "start_speed":
                    main.startSpeed = Convert.ToSingle(value);
                    break;

                case "startsize":
                case "start_size":
                    main.startSize = Convert.ToSingle(value);
                    break;

                case "startrotation":
                case "start_rotation":
                    main.startRotation = Convert.ToSingle(value) * Mathf.Deg2Rad;
                    break;

                case "startcolor":
                case "start_color":
                    Color? color = VFXCommon.ParseColor(value);
                    if (color.HasValue)
                    {
                        main.startColor = color.Value;
                    }
                    else
                    {
                        message = "Failed to parse color value.";
                        return false;
                    }
                    break;

                case "gravitymodifier":
                case "gravity_modifier":
                case "gravity":
                    main.gravityModifier = Convert.ToSingle(value);
                    break;

                case "simulationspace":
                case "simulation_space":
                    if (value is string spaceString)
                    {
                        main.simulationSpace = spaceString.ToLowerInvariant() switch
                        {
                            "local" => ParticleSystemSimulationSpace.Local,
                            "world" => ParticleSystemSimulationSpace.World,
                            "custom" => ParticleSystemSimulationSpace.Custom,
                            _ => main.simulationSpace
                        };
                    }
                    else
                    {
                        main.simulationSpace = (ParticleSystemSimulationSpace)Convert.ToInt32(value);
                    }
                    break;

                case "simulationspeed":
                case "simulation_speed":
                    main.simulationSpeed = Convert.ToSingle(value);
                    break;

                case "scalingmode":
                case "scaling_mode":
                    if (value is string scalingString)
                    {
                        main.scalingMode = scalingString.ToLowerInvariant() switch
                        {
                            "hierarchy" => ParticleSystemScalingMode.Hierarchy,
                            "local" => ParticleSystemScalingMode.Local,
                            "shape" => ParticleSystemScalingMode.Shape,
                            _ => main.scalingMode
                        };
                    }
                    else
                    {
                        main.scalingMode = (ParticleSystemScalingMode)Convert.ToInt32(value);
                    }
                    break;

                case "playonawake":
                case "play_on_awake":
                    main.playOnAwake = Convert.ToBoolean(value);
                    break;

                case "maxparticles":
                case "max_particles":
                    main.maxParticles = Convert.ToInt32(value);
                    break;

                case "emittervelocitymode":
                case "emitter_velocity_mode":
                    if (value is string velocityString)
                    {
                        main.emitterVelocityMode = velocityString.ToLowerInvariant() switch
                        {
                            "transform" => ParticleSystemEmitterVelocityMode.Transform,
                            "rigidbody" => ParticleSystemEmitterVelocityMode.Rigidbody,
                            "custom" => ParticleSystemEmitterVelocityMode.Custom,
                            _ => main.emitterVelocityMode
                        };
                    }
                    break;

                case "stopaction":
                case "stop_action":
                    if (value is string stopString)
                    {
                        main.stopAction = stopString.ToLowerInvariant() switch
                        {
                            "none" => ParticleSystemStopAction.None,
                            "disable" => ParticleSystemStopAction.Disable,
                            "destroy" => ParticleSystemStopAction.Destroy,
                            "callback" => ParticleSystemStopAction.Callback,
                            _ => main.stopAction
                        };
                    }
                    break;

                default:
                    message = $"Unknown property: '{propertyName}'";
                    return false;
            }

            return true;
        }

        private static object BuildMainModuleInfo(ParticleSystem.MainModule main)
        {
            return new
            {
                duration = main.duration,
                looping = main.loop,
                prewarm = main.prewarm,
                startDelay = main.startDelay.constant,
                startLifetime = main.startLifetime.constant,
                startSpeed = main.startSpeed.constant,
                startSize = main.startSize.constant,
                startRotation = main.startRotation.constant * Mathf.Rad2Deg,
                startColor = VFXCommon.SerializeColor(main.startColor.color),
                gravityModifier = main.gravityModifier.constant,
                simulationSpace = main.simulationSpace.ToString(),
                simulationSpeed = main.simulationSpeed,
                scalingMode = main.scalingMode.ToString(),
                playOnAwake = main.playOnAwake,
                maxParticles = main.maxParticles,
                emitterVelocityMode = main.emitterVelocityMode.ToString(),
                stopAction = main.stopAction.ToString()
            };
        }

        private static object BuildEmissionModuleInfo(ParticleSystem.EmissionModule emission)
        {
            return new
            {
                enabled = emission.enabled,
                rateOverTime = emission.rateOverTime.constant,
                rateOverDistance = emission.rateOverDistance.constant,
                burstCount = emission.burstCount
            };
        }

        private static object BuildShapeModuleInfo(ParticleSystem.ShapeModule shape)
        {
            return new
            {
                enabled = shape.enabled,
                shapeType = shape.shapeType.ToString(),
                radius = shape.radius,
                angle = shape.angle,
                arc = shape.arc,
                position = VFXCommon.SerializeVector3(shape.position),
                rotation = VFXCommon.SerializeVector3(shape.rotation),
                scale = VFXCommon.SerializeVector3(shape.scale)
            };
        }

        private static object BuildRendererInfo(ParticleSystemRenderer renderer)
        {
            if (renderer == null)
            {
                return null;
            }

            return new
            {
                renderMode = renderer.renderMode.ToString(),
                sortMode = renderer.sortMode.ToString(),
                minParticleSize = renderer.minParticleSize,
                maxParticleSize = renderer.maxParticleSize,
                material = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : null,
                trailMaterial = renderer.trailMaterial != null ? renderer.trailMaterial.name : null
            };
        }

        #endregion
    }
}
