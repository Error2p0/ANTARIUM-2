using System;
using System.IO;
using Antarium.Gameplay;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Antarium.Editor
{
    /// <summary>
    /// Windows builds for Steam. The version lives in one place: Player Settings → Version (<c>Application.version</c>,
    /// shown on the title screen). Builds go to <c>Builds/Windows/&lt;Release|Development&gt;</c> (git-ignored); the
    /// SteamPipe upload script (<c>steam/upload.ps1</c>) uploads the release folder. Files that must not ship (Burst
    /// and IL2CPP debug information) are moved to <c>Builds/Symbols/&lt;version&gt;</c> for crash analysis.
    /// </summary>
    public static class ReleaseBuild
    {
        public const string CompanyName = "Elevate Studios";
        public const string ProductName = "ANTARIUM 2";
        public const string BundleId = "com.elevatestudios.antarium2";
        public const string ExeName = "Antarium2.exe";
        public const string MainScene = "Assets/_Antarium/Scenes/Main.unity";
        public const string BuildRoot = "Builds";

        public static string ReleaseFolder => Path.Combine(BuildRoot, "Windows", "Release");
        public static string DevelopmentFolder => Path.Combine(BuildRoot, "Windows", "Development");

        /// <summary>
        /// Company and product name (these pick the save folder: never change them after a public build), bundle id,
        /// window defaults, one instance at a time (two would write the same save), no Unity splash.
        /// </summary>
        [MenuItem("Antarium/Build/Apply Release Player Settings")]
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.forceSingleInstance = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Minimal);
            AssetDatabase.SaveAssets();
            Debug.Log($"Player Settings: {CompanyName} / {ProductName} {PlayerSettings.bundleVersion}, {BundleId}. " +
                      $"Saves go to %USERPROFILE%/AppData/LocalLow/{CompanyName}/{ProductName}.");
        }

        [MenuItem("Antarium/Build/Windows Release")]
        public static void BuildRelease() => Build(development: false);

        [MenuItem("Antarium/Build/Windows Development")]
        public static void BuildDevelopment() => Build(development: true);

        /// <summary>IL2CPP when the editor has the Windows IL2CPP module, otherwise Mono.</summary>
        public static bool Il2CppInstalled
        {
            get
            {
                string engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.StandaloneWindows64, BuildOptions.None);
                return Directory.Exists(Path.Combine(engine, "Variations", "win64_player_nondevelopment_il2cpp"));
            }
        }

        /// <summary>Builds the game; returns the build report (also used by the command line / CI).</summary>
        public static BuildReport Build(bool development)
        {
            if (PlayerSettings.companyName != CompanyName || PlayerSettings.productName != ProductName) ApplyPlayerSettings();
            bool il2cpp = Il2CppInstalled;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            if (!il2cpp) Debug.LogWarning("The Windows IL2CPP module is not installed: building with Mono.");

            string folder = development ? DevelopmentFolder : ReleaseFolder;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = Path.Combine(folder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Build failed ({summary.result}, {summary.totalErrors} errors).");
                return report;
            }

            MoveDoNotShip(folder, development);
            string appId = Path.Combine(folder, "steam_appid.txt");
            // Development builds run without being started by Steam; release builds must not carry the file.
            if (development) File.WriteAllText(appId, PlatformServices.SteamAppId.ToString());
            else if (File.Exists(appId)) File.Delete(appId);

            long bytes = FolderSize(folder);
            Debug.Log($"Built {(development ? "development" : "release")} {PlayerSettings.bundleVersion} ({(il2cpp ? "IL2CPP" : "Mono")}) " +
                      $"to {Path.GetFullPath(folder)}: {bytes / (1024f * 1024f):0.0} MB in {summary.totalTime.TotalSeconds:0} s.");
            return report;
        }

        /// <summary>Moves the Burst / IL2CPP debug folders out of the build so SteamPipe never uploads them.</summary>
        static void MoveDoNotShip(string folder, bool development)
        {
            string symbols = Path.Combine(BuildRoot, "Symbols", PlayerSettings.bundleVersion + (development ? "-dev" : ""));
            foreach (var dir in Directory.GetDirectories(folder))
            {
                string name = Path.GetFileName(dir);
                if (!name.Contains("DoNotShip", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("DontShip", StringComparison.OrdinalIgnoreCase)) continue;
                string target = Path.Combine(symbols, name);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(symbols);
                Directory.Move(dir, target);
            }
        }

        static long FolderSize(string folder)
        {
            long total = 0;
            foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) total += new FileInfo(f).Length;
            return total;
        }
    }
}
