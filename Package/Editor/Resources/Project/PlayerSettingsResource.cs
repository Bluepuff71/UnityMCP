using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityMCP.Editor.Resources.Project
{
    /// <summary>
    /// Resource provider for comprehensive Player Settings.
    /// </summary>
    public static class PlayerSettingsResource
    {
        /// <summary>
        /// Gets comprehensive PlayerSettings including company/product info,
        /// icons, resolution, display, and platform-specific settings.
        /// </summary>
        /// <returns>Object containing complete PlayerSettings information.</returns>
        [MCPResource("project://player_settings", "Comprehensive player settings including icons, resolution, and platform settings")]
        public static object Get()
        {
            var activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(activeBuildTarget);
            var namedBuildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);

            return new
            {
                identity = GetIdentitySettings(),
                icons = GetIconSettings(namedBuildTarget),
                resolution = GetResolutionSettings(),
                splash = GetSplashSettings(),
                rendering = GetRenderingSettings(namedBuildTarget),
                scripting = GetScriptingSettings(namedBuildTarget),
                optimization = GetOptimizationSettings(namedBuildTarget),
                configuration = GetConfigurationSettings(),
                publishing = GetPublishingSettings(),
                platformSpecific = GetPlatformSpecificSettings(activeBuildTarget, namedBuildTarget)
            };
        }

        private static object GetIdentitySettings()
        {
            return new
            {
                companyName = PlayerSettings.companyName,
                productName = PlayerSettings.productName,
                bundleVersion = PlayerSettings.bundleVersion,
                applicationIdentifier = PlayerSettings.applicationIdentifier,
                defaultCursor = PlayerSettings.defaultCursor != null ? PlayerSettings.defaultCursor.name : null,
                cursorHotspot = new { x = PlayerSettings.cursorHotspot.x, y = PlayerSettings.cursorHotspot.y }
            };
        }

        private static object GetIconSettings(UnityEditor.Build.NamedBuildTarget namedBuildTarget)
        {
            var iconKinds = PlayerSettings.GetSupportedIconKinds(namedBuildTarget);
            var iconsByKind = new List<object>();
            var allIconTextures = new List<string>();

            foreach (var iconKind in iconKinds)
            {
                var platformIcons = PlayerSettings.GetPlatformIcons(namedBuildTarget, iconKind);
                var iconInfos = new List<object>();

                foreach (var platformIcon in platformIcons)
                {
                    var textures = platformIcon.GetTextures();
                    var textureNames = textures
                        .Where(texture => texture != null)
                        .Select(texture => texture.name)
                        .ToArray();

                    if (textureNames.Length > 0)
                    {
                        iconInfos.Add(new
                        {
                            width = platformIcon.width,
                            height = platformIcon.height,
                            textures = textureNames
                        });
                        allIconTextures.AddRange(textureNames);
                    }
                }

                if (iconInfos.Count > 0)
                {
                    iconsByKind.Add(new
                    {
                        kind = iconKind.ToString(),
                        icons = iconInfos.ToArray()
                    });
                }
            }

            return new
            {
                platformIconCount = allIconTextures.Count,
                iconKinds = iconsByKind.ToArray(),
                allTextures = allIconTextures.Distinct().ToArray()
            };
        }

        private static object GetResolutionSettings()
        {
            return new
            {
                fullscreen = new
                {
                    defaultIsFullScreen = PlayerSettings.defaultIsNativeResolution,
                    defaultScreenWidth = PlayerSettings.defaultScreenWidth,
                    defaultScreenHeight = PlayerSettings.defaultScreenHeight,
                    fullScreenMode = PlayerSettings.fullScreenMode.ToString(),
                    runInBackground = PlayerSettings.runInBackground,
                    usePlayerLog = PlayerSettings.usePlayerLog,
                    resizableWindow = PlayerSettings.resizableWindow,
                    visibleInBackground = PlayerSettings.visibleInBackground,
                    allowFullscreenSwitch = PlayerSettings.allowFullscreenSwitch,
                    forceSingleInstance = PlayerSettings.forceSingleInstance
                },
                standalone = new
                {
                    defaultIsNativeResolution = PlayerSettings.defaultIsNativeResolution,
                    macRetinaSupport = PlayerSettings.macRetinaSupport
                },
                aspectRatio = new
                {
                    aspectRatioMode = PlayerSettings.allowedAutorotateToPortrait ? "AllowRotation" : "Fixed"
                }
            };
        }

        private static object GetSplashSettings()
        {
            return new
            {
                showSplashScreen = PlayerSettings.SplashScreen.show,
                splashScreenStyle = PlayerSettings.SplashScreen.unityLogoStyle.ToString(),
                animationMode = PlayerSettings.SplashScreen.animationMode.ToString(),
                drawMode = PlayerSettings.SplashScreen.drawMode.ToString(),
                backgroundColor = ColorToHex(PlayerSettings.SplashScreen.backgroundColor),
                showUnityLogo = PlayerSettings.SplashScreen.showUnityLogo,
                overlayOpacity = PlayerSettings.SplashScreen.overlayOpacity
            };
        }

        private static object GetRenderingSettings(UnityEditor.Build.NamedBuildTarget namedBuildTarget)
        {
            return new
            {
                colorSpace = PlayerSettings.colorSpace.ToString(),
                graphicsJobs = PlayerSettings.graphicsJobs,
                graphicsJobMode = PlayerSettings.graphicsJobMode.ToString(),
                useDirect3D11 = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64),
                gpuSkinning = PlayerSettings.gpuSkinning,
                mTRendering = PlayerSettings.MTRendering,
                virtualTexturingSupportEnabled = PlayerSettings.GetVirtualTexturingSupportEnabled(),
                use32BitDisplayBuffer = PlayerSettings.use32BitDisplayBuffer,
                preserveFramebufferAlpha = PlayerSettings.preserveFramebufferAlpha,
                hdrBitDepth = PlayerSettings.hdrBitDepth.ToString(),
                defaultInterfaceOrientation = PlayerSettings.defaultInterfaceOrientation.ToString()
            };
        }

        private static object GetScriptingSettings(UnityEditor.Build.NamedBuildTarget namedBuildTarget)
        {
            PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget, out string[] defines);

            return new
            {
                scriptingBackend = PlayerSettings.GetScriptingBackend(namedBuildTarget).ToString(),
                apiCompatibilityLevel = PlayerSettings.GetApiCompatibilityLevel(namedBuildTarget).ToString(),
                il2CppCodeGeneration = PlayerSettings.GetIl2CppCodeGeneration(namedBuildTarget).ToString(),
                il2CppCompilerConfiguration = PlayerSettings.GetIl2CppCompilerConfiguration(namedBuildTarget).ToString(),
                gcIncrementalTimeSliceMode = PlayerSettings.gcIncremental,
                scriptingDefineSymbols = defines,
                allowUnsafeCode = PlayerSettings.allowUnsafeCode,
                activeInputHandling = PlayerSettings.GetScriptingBackend(namedBuildTarget).ToString()
            };
        }

        private static object GetOptimizationSettings(UnityEditor.Build.NamedBuildTarget namedBuildTarget)
        {
            return new
            {
                prebakeCollisionMeshes = PlayerSettings.bakeCollisionMeshes,
                stripUnusedMeshComponents = PlayerSettings.stripUnusedMeshComponents,
                managedStrippingLevel = PlayerSettings.GetManagedStrippingLevel(namedBuildTarget).ToString(),
                stripEngineCode = PlayerSettings.stripEngineCode,
                enableInternalProfiler = PlayerSettings.enableInternalProfiler
            };
        }

        private static object GetConfigurationSettings()
        {
            return new
            {
                scriptingRuntimeVersion = "Latest",
                apiCompatibilityLevel = PlayerSettings.GetApiCompatibilityLevel(
                    UnityEditor.Build.NamedBuildTarget.Standalone).ToString(),
                logObjCUncaughtExceptions = PlayerSettings.logObjCUncaughtExceptions,
                enableCrashReportAPI = PlayerSettings.enableCrashReportAPI
            };
        }

        private static object GetPublishingSettings()
        {
            return new
            {
                useMacAppStoreValidation = PlayerSettings.useMacAppStoreValidation
            };
        }

        private static object GetPlatformSpecificSettings(BuildTarget activeBuildTarget, UnityEditor.Build.NamedBuildTarget namedBuildTarget)
        {
            var platformSettings = new Dictionary<string, object>();

            // Windows settings - get first available icon from supported icon kinds
            string windowsIconName = null;
            var standaloneTarget = UnityEditor.Build.NamedBuildTarget.Standalone;
            var standaloneIconKinds = PlayerSettings.GetSupportedIconKinds(standaloneTarget);
            foreach (var iconKind in standaloneIconKinds)
            {
                var icons = PlayerSettings.GetPlatformIcons(standaloneTarget, iconKind);
                foreach (var icon in icons)
                {
                    var textures = icon.GetTextures();
                    var firstTexture = textures.FirstOrDefault(t => t != null);
                    if (firstTexture != null)
                    {
                        windowsIconName = firstTexture.name;
                        break;
                    }
                }
                if (windowsIconName != null) break;
            }

            platformSettings["windows"] = new
            {
                applicationIcon = windowsIconName,
                showResolutionDialog = "HiddenByDefault"
            };

            // Android settings
            platformSettings["android"] = new
            {
                packageName = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android),
                minSdkVersion = PlayerSettings.Android.minSdkVersion.ToString(),
                targetSdkVersion = PlayerSettings.Android.targetSdkVersion.ToString(),
                targetArchitectures = PlayerSettings.Android.targetArchitectures.ToString(),
                preferredInstallLocation = PlayerSettings.Android.preferredInstallLocation.ToString(),
                useAPKExpansionFiles = PlayerSettings.Android.splitApplicationBinary,
                useCustomKeystore = PlayerSettings.Android.useCustomKeystore,
                keystoreName = PlayerSettings.Android.keystoreName,
                splitApplicationBinary = PlayerSettings.Android.splitApplicationBinary,
                buildAppBundle = EditorUserBuildSettings.buildAppBundle
            };

            // iOS settings
            platformSettings["ios"] = new
            {
                bundleIdentifier = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS),
                buildNumber = PlayerSettings.iOS.buildNumber,
                targetDevice = PlayerSettings.iOS.targetDevice.ToString(),
                targetOSVersionString = PlayerSettings.iOS.targetOSVersionString,
                sdkVersion = PlayerSettings.iOS.sdkVersion.ToString(),
                appleEnableAutomaticSigning = PlayerSettings.iOS.appleEnableAutomaticSigning,
                appleDeveloperTeamID = PlayerSettings.iOS.appleDeveloperTeamID,
                requiresPersistentWiFi = PlayerSettings.iOS.requiresPersistentWiFi,
                requiresFullScreen = PlayerSettings.iOS.requiresFullScreen,
                insecureHttpOption = PlayerSettings.insecureHttpOption.ToString(),
                cameraUsageDescription = PlayerSettings.iOS.cameraUsageDescription,
                microphoneUsageDescription = PlayerSettings.iOS.microphoneUsageDescription,
                locationUsageDescription = PlayerSettings.iOS.locationUsageDescription
            };

            // WebGL settings
            platformSettings["webgl"] = new
            {
                memorySize = PlayerSettings.WebGL.memorySize,
                exceptionSupport = PlayerSettings.WebGL.exceptionSupport.ToString(),
                compressionFormat = PlayerSettings.WebGL.compressionFormat.ToString(),
                dataCaching = PlayerSettings.WebGL.dataCaching,
                debugSymbolMode = PlayerSettings.WebGL.debugSymbolMode.ToString(),
                template = PlayerSettings.WebGL.template
            };

            return platformSettings;
        }

        private static string ColorToHex(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
        }
    }
}
