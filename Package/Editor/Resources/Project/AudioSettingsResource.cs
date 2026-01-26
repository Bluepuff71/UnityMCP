using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Resources.Project
{
    /// <summary>
    /// Resource provider for audio settings.
    /// </summary>
    public static class AudioSettingsResource
    {
        /// <summary>
        /// Gets audio configuration settings including speaker mode, DSP buffer size,
        /// sample rate, and virtual voice settings.
        /// </summary>
        /// <returns>Object containing audio settings information.</returns>
        [MCPResource("project://audio", "Audio settings including speaker mode, DSP buffer, and sample rate")]
        public static object Get()
        {
            var audioConfiguration = AudioSettings.GetConfiguration();

            return new
            {
                driver = new
                {
                    speakerMode = AudioSettings.speakerMode.ToString(),
                    driverCapabilities = AudioSettings.driverCapabilities.ToString(),
                    outputSampleRate = AudioSettings.outputSampleRate
                },
                configuration = new
                {
                    speakerMode = audioConfiguration.speakerMode.ToString(),
                    dspBufferSize = audioConfiguration.dspBufferSize,
                    sampleRate = audioConfiguration.sampleRate,
                    numRealVoices = audioConfiguration.numRealVoices,
                    numVirtualVoices = audioConfiguration.numVirtualVoices
                },
                projectSettings = GetProjectAudioSettings(),
                runtime = new
                {
                    dspTime = AudioSettings.dspTime,
                    isAudioDisabled = AudioSettings.GetSpatializerPluginName() == null
                }
            };
        }

        private static object GetProjectAudioSettings()
        {
            // Load the AudioManager asset to read project audio settings
            var audioManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");

            if (audioManagerAssets == null || audioManagerAssets.Length == 0)
            {
                return new
                {
                    error = "Could not load AudioManager.asset"
                };
            }

            var serializedObject = new SerializedObject(audioManagerAssets[0]);

            var defaultSpeakerMode = serializedObject.FindProperty("Default Speaker Mode");
            var systemSampleRate = serializedObject.FindProperty("m_SampleRate");
            var dspBufferSize = serializedObject.FindProperty("m_DSPBufferSize");
            var virtualVoiceCount = serializedObject.FindProperty("m_VirtualVoiceCount");
            var realVoiceCount = serializedObject.FindProperty("m_RealVoiceCount");
            var spatializerPlugin = serializedObject.FindProperty("m_SpatializerPlugin");
            var ambisonicDecoderPlugin = serializedObject.FindProperty("m_AmbisonicDecoderPlugin");
            var disableAudio = serializedObject.FindProperty("m_DisableAudio");
            var virtualizeEffects = serializedObject.FindProperty("m_VirtualizeEffects");
            var requestedDSPBufferSize = serializedObject.FindProperty("m_RequestedDSPBufferSize");

            return new
            {
                defaultSpeakerMode = GetSpeakerModeName(defaultSpeakerMode?.intValue ?? 2),
                systemSampleRate = systemSampleRate?.intValue ?? 0,
                dspBufferSize = GetDSPBufferSizeName(dspBufferSize?.intValue ?? 0),
                requestedDSPBufferSize = requestedDSPBufferSize?.intValue ?? 0,
                virtualVoiceCount = virtualVoiceCount?.intValue ?? 512,
                realVoiceCount = realVoiceCount?.intValue ?? 32,
                spatializerPlugin = spatializerPlugin?.stringValue ?? "",
                ambisonicDecoderPlugin = ambisonicDecoderPlugin?.stringValue ?? "",
                disableAudio = disableAudio?.boolValue ?? false,
                virtualizeEffects = virtualizeEffects?.boolValue ?? true
            };
        }

        private static string GetSpeakerModeName(int mode)
        {
            return mode switch
            {
                0 => "Raw",
                1 => "Mono",
                2 => "Stereo",
                3 => "Quad",
                4 => "Surround",
                5 => "Mode5point1",
                6 => "Mode7point1",
                7 => "Prologic",
                _ => $"Unknown ({mode})"
            };
        }

        private static string GetDSPBufferSizeName(int size)
        {
            return size switch
            {
                0 => "Default",
                1 => "Best latency",
                2 => "Good latency",
                3 => "Best performance",
                _ => $"Custom ({size})"
            };
        }
    }
}
