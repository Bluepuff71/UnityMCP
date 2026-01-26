using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityMCP.Editor.Resources.Animation
{
    /// <summary>
    /// Resource provider for AnimatorController assets.
    /// </summary>
    public static class AnimatorResource
    {
        /// <summary>
        /// Gets detailed information about an AnimatorController asset.
        /// </summary>
        /// <param name="assetPath">The asset path of the AnimatorController.</param>
        /// <returns>Object containing layers, parameters, and state machine structure.</returns>
        [MCPResource("animation://controller/{path}", "AnimatorController details including layers, parameters, and state machines")]
        public static object GetAnimatorController([MCPParam("path", "Asset path of the AnimatorController (e.g., Assets/Animations/Player.controller)")] string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return new
                {
                    error = true,
                    message = "Asset path is required"
                };
            }

            // Ensure the path starts with Assets/ if not already
            if (!assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Packages/"))
            {
                assetPath = "Assets/" + assetPath;
            }

            var animatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);

            if (animatorController == null)
            {
                // Try to find by name if exact path doesn't work
                var guids = AssetDatabase.FindAssets($"t:AnimatorController {System.IO.Path.GetFileNameWithoutExtension(assetPath)}");
                if (guids.Length > 0)
                {
                    var foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    animatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(foundPath);
                    if (animatorController != null)
                    {
                        assetPath = foundPath;
                    }
                }
            }

            if (animatorController == null)
            {
                return new
                {
                    error = true,
                    message = $"AnimatorController not found at path: {assetPath}",
                    hint = "Provide the full asset path (e.g., Assets/Animations/Player.controller)"
                };
            }

            return BuildAnimatorControllerInfo(animatorController, assetPath);
        }

        /// <summary>
        /// Builds detailed information about an AnimatorController.
        /// </summary>
        private static object BuildAnimatorControllerInfo(AnimatorController animatorController, string assetPath)
        {
            var layers = new List<object>();
            foreach (var layer in animatorController.layers)
            {
                layers.Add(BuildLayerInfo(layer));
            }

            var parameters = new List<object>();
            foreach (var parameter in animatorController.parameters)
            {
                parameters.Add(BuildParameterInfo(parameter));
            }

            return new
            {
                name = animatorController.name,
                assetPath = assetPath,
                instanceId = animatorController.GetInstanceID(),
                layerCount = animatorController.layers.Length,
                layers = layers.ToArray(),
                parameterCount = animatorController.parameters.Length,
                parameters = parameters.ToArray(),
                animationClips = GetAnimationClipsInfo(animatorController)
            };
        }

        /// <summary>
        /// Builds information about an animator layer.
        /// </summary>
        private static object BuildLayerInfo(AnimatorControllerLayer layer)
        {
            var stateMachine = layer.stateMachine;

            return new
            {
                name = layer.name,
                defaultWeight = layer.defaultWeight,
                blendingMode = layer.blendingMode.ToString(),
                syncedLayerIndex = layer.syncedLayerIndex,
                iKPass = layer.iKPass,
                stateMachine = stateMachine != null ? BuildStateMachineInfo(stateMachine, 0) : null
            };
        }

        /// <summary>
        /// Builds information about a state machine.
        /// </summary>
        private static object BuildStateMachineInfo(AnimatorStateMachine stateMachine, int depth)
        {
            // Limit depth to prevent stack overflow on deeply nested state machines
            if (depth > 10)
            {
                return new
                {
                    name = stateMachine.name,
                    truncated = true,
                    reason = "Maximum depth reached"
                };
            }

            var states = new List<object>();
            foreach (var childState in stateMachine.states)
            {
                states.Add(BuildStateInfo(childState));
            }

            var subStateMachines = new List<object>();
            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                subStateMachines.Add(new
                {
                    name = childStateMachine.stateMachine.name,
                    position = new { x = childStateMachine.position.x, y = childStateMachine.position.y },
                    stateMachine = BuildStateMachineInfo(childStateMachine.stateMachine, depth + 1)
                });
            }

            var anyStateTransitions = new List<object>();
            foreach (var transition in stateMachine.anyStateTransitions)
            {
                anyStateTransitions.Add(BuildTransitionInfo(transition));
            }

            var entryTransitions = new List<object>();
            foreach (var transition in stateMachine.entryTransitions)
            {
                entryTransitions.Add(new
                {
                    destinationState = transition.destinationState != null ? transition.destinationState.name : null,
                    destinationStateMachine = transition.destinationStateMachine != null ? transition.destinationStateMachine.name : null,
                    conditionCount = transition.conditions.Length,
                    conditions = transition.conditions.Select(condition => new
                    {
                        parameter = condition.parameter,
                        mode = condition.mode.ToString(),
                        threshold = condition.threshold
                    }).ToArray()
                });
            }

            return new
            {
                name = stateMachine.name,
                defaultState = stateMachine.defaultState != null ? stateMachine.defaultState.name : null,
                stateCount = stateMachine.states.Length,
                states = states.ToArray(),
                subStateMachineCount = stateMachine.stateMachines.Length,
                subStateMachines = subStateMachines.ToArray(),
                anyStateTransitions = anyStateTransitions.ToArray(),
                entryTransitions = entryTransitions.ToArray(),
                behaviours = stateMachine.behaviours.Select(behaviour => new
                {
                    type = behaviour.GetType().Name,
                    fullType = behaviour.GetType().FullName
                }).ToArray()
            };
        }

        /// <summary>
        /// Builds information about a state.
        /// </summary>
        private static object BuildStateInfo(ChildAnimatorState childState)
        {
            var state = childState.state;

            var transitions = new List<object>();
            foreach (var transition in state.transitions)
            {
                transitions.Add(BuildTransitionInfo(transition));
            }

            return new
            {
                name = state.name,
                nameHash = state.nameHash,
                tag = state.tag,
                position = new { x = childState.position.x, y = childState.position.y },
                speed = state.speed,
                speedParameterActive = state.speedParameterActive,
                speedParameter = state.speedParameter,
                cycleOffset = state.cycleOffset,
                cycleOffsetParameterActive = state.cycleOffsetParameterActive,
                cycleOffsetParameter = state.cycleOffsetParameter,
                mirror = state.mirror,
                mirrorParameterActive = state.mirrorParameterActive,
                mirrorParameter = state.mirrorParameter,
                iKOnFeet = state.iKOnFeet,
                writeDefaultValues = state.writeDefaultValues,
                motion = GetMotionInfo(state.motion),
                transitionCount = state.transitions.Length,
                transitions = transitions.ToArray(),
                behaviours = state.behaviours.Select(behaviour => new
                {
                    type = behaviour.GetType().Name,
                    fullType = behaviour.GetType().FullName
                }).ToArray()
            };
        }

        /// <summary>
        /// Builds information about a transition.
        /// </summary>
        private static object BuildTransitionInfo(AnimatorStateTransition transition)
        {
            return new
            {
                name = transition.name,
                destinationState = transition.destinationState != null ? transition.destinationState.name : null,
                destinationStateMachine = transition.destinationStateMachine != null ? transition.destinationStateMachine.name : null,
                isExit = transition.isExit,
                mute = transition.mute,
                solo = transition.solo,
                hasExitTime = transition.hasExitTime,
                exitTime = transition.exitTime,
                hasFixedDuration = transition.hasFixedDuration,
                duration = transition.duration,
                offset = transition.offset,
                orderedInterruption = transition.orderedInterruption,
                interruptionSource = transition.interruptionSource.ToString(),
                canTransitionToSelf = transition.canTransitionToSelf,
                conditionCount = transition.conditions.Length,
                conditions = transition.conditions.Select(condition => new
                {
                    parameter = condition.parameter,
                    mode = condition.mode.ToString(),
                    threshold = condition.threshold
                }).ToArray()
            };
        }

        /// <summary>
        /// Builds information about an animator parameter.
        /// </summary>
        private static object BuildParameterInfo(AnimatorControllerParameter parameter)
        {
            object defaultValue = parameter.type switch
            {
                AnimatorControllerParameterType.Float => parameter.defaultFloat,
                AnimatorControllerParameterType.Int => parameter.defaultInt,
                AnimatorControllerParameterType.Bool => parameter.defaultBool,
                AnimatorControllerParameterType.Trigger => false,
                _ => null
            };

            return new
            {
                name = parameter.name,
                nameHash = parameter.nameHash,
                type = parameter.type.ToString(),
                defaultValue = defaultValue
            };
        }

        /// <summary>
        /// Gets information about a motion (animation clip or blend tree).
        /// </summary>
        private static object GetMotionInfo(Motion motion)
        {
            if (motion == null)
            {
                return null;
            }

            if (motion is AnimationClip clip)
            {
                return new
                {
                    type = "AnimationClip",
                    name = clip.name,
                    length = clip.length,
                    frameRate = clip.frameRate,
                    isLooping = clip.isLooping,
                    isHumanMotion = clip.isHumanMotion,
                    legacy = clip.legacy,
                    hasGenericRootTransform = clip.hasGenericRootTransform,
                    hasMotionCurves = clip.hasMotionCurves,
                    hasMotionFloatCurves = clip.hasMotionFloatCurves,
                    hasRootCurves = clip.hasRootCurves,
                    assetPath = AssetDatabase.GetAssetPath(clip)
                };
            }

            if (motion is BlendTree blendTree)
            {
                var children = new List<object>();
                foreach (var child in blendTree.children)
                {
                    children.Add(new
                    {
                        motion = GetMotionInfo(child.motion),
                        threshold = child.threshold,
                        position = new { x = child.position.x, y = child.position.y },
                        timeScale = child.timeScale,
                        cycleOffset = child.cycleOffset,
                        directBlendParameter = child.directBlendParameter,
                        mirror = child.mirror
                    });
                }

                return new
                {
                    type = "BlendTree",
                    name = blendTree.name,
                    blendType = blendTree.blendType.ToString(),
                    blendParameter = blendTree.blendParameter,
                    blendParameterY = blendTree.blendParameterY,
                    minThreshold = blendTree.minThreshold,
                    maxThreshold = blendTree.maxThreshold,
                    useAutomaticThresholds = blendTree.useAutomaticThresholds,
                    childCount = blendTree.children.Length,
                    children = children.ToArray()
                };
            }

            return new
            {
                type = motion.GetType().Name,
                name = motion.name
            };
        }

        /// <summary>
        /// Gets information about all animation clips used in the controller.
        /// </summary>
        private static object GetAnimationClipsInfo(AnimatorController animatorController)
        {
            var clips = animatorController.animationClips;
            var clipInfoList = new List<object>();

            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }

                clipInfoList.Add(new
                {
                    name = clip.name,
                    length = clip.length,
                    frameRate = clip.frameRate,
                    isLooping = clip.isLooping,
                    assetPath = AssetDatabase.GetAssetPath(clip)
                });
            }

            return new
            {
                totalClips = clips.Length,
                clips = clipInfoList.ToArray()
            };
        }
    }
}
