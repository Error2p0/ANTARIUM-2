using System;
using System.IO;
using System.Threading;
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

        static readonly TimeSpan MoveTimeout = TimeSpan.FromMinutes(2);
        const int MoveRetryMilliseconds = 500;

        public static string ReleaseFolder => Path.Combine(BuildRoot, "Windows", "Release");
        public static string DevelopmentFolder => Path.Combine(BuildRoot, "Windows", "Development");

        /// <summary>The demo (GDD 2.16): the same project built with <c>ANTARIUM_DEMO</c>, its own product name (save folder) and exe.</summary>
        public const string DemoDefine = "ANTARIUM_DEMO";
        public const string DemoProductName = GameBootstrap.DemoProductName;
        public const string DemoBundleId = BundleId + ".demo";
        public const string DemoExeName = "Antarium2Demo.exe";
        public static string DemoFolder => Path.Combine(BuildRoot, "Windows", "Demo");
        public static string DemoDevelopmentFolder => Path.Combine(BuildRoot, "Windows", "DemoDevelopment");

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

        [MenuItem("Antarium/Build/Windows Demo")]
        public static void BuildDemo() => Build(development: false, demo: true);

        [MenuItem("Antarium/Build/Windows Demo (Development)")]
        public static void BuildDemoDevelopment() => Build(development: true, demo: true);

        /// <summary>IL2CPP when the editor has the Windows IL2CPP module, otherwise Mono.</summary>
        public static bool Il2CppInstalled
        {
            get
            {
                string engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.StandaloneWindows64, BuildOptions.None);
                return Directory.Exists(Path.Combine(engine, "Variations", "win64_player_nondevelopment_il2cpp"));
            }
        }

        /// <summary>
        /// Builds the game (or with <paramref name="demo"/> the demo: <c>ANTARIUM_DEMO</c>, product name <see cref="DemoProductName"/> and
        /// its bundle id for the duration of the build, so it keeps its own save folder); returns the build report (also used by the
        /// command line / CI).
        /// </summary>
        public static BuildReport Build(bool development, bool demo = false)
        {
            if (PlayerSettings.companyName != CompanyName || PlayerSettings.productName != ProductName) ApplyPlayerSettings();
            bool il2cpp = Il2CppInstalled;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            if (!il2cpp) Debug.LogWarning("The Windows IL2CPP module is not installed: building with Mono.");

            string folder = demo ? development ? DemoDevelopmentFolder : DemoFolder : development ? DevelopmentFolder : ReleaseFolder;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = Path.Combine(folder, demo ? DemoExeName : ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = demo ? new[] { DemoDefine } : null,
            };
            BuildReport report;
            try
            {
                if (demo)
                {
                    PlayerSettings.productName = DemoProductName;
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, DemoBundleId);
                }
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                if (demo)
                {
                    // Back to the full game's names, saved at once: the build wrote the demo's into ProjectSettings.asset.
                    PlayerSettings.productName = ProductName;
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
                    AssetDatabase.SaveAssets();
                }
            }
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Build failed ({summary.result}, {summary.totalErrors} errors).");
                return report;
            }

            MoveDoNotShip(folder, development, demo);
            string appId = Path.Combine(folder, "steam_appid.txt");
            uint id = demo ? DemoAppId() : PlatformServices.SteamAppId;
            // Development builds run without being started by Steam; release builds must not carry the file.
            if (development && id != 0) File.WriteAllText(appId, id.ToString());
            else if (File.Exists(appId)) File.Delete(appId);
            if (demo && id == 0) Debug.LogWarning("DemoConfig.demoAppId is 0: the demo runs without Steam. Create the demo app in Steamworks and enter its App ID.");

            long bytes = FolderSize(folder);
            Debug.Log($"Built {(demo ? "demo " : "")}{(development ? "development" : "release")} {PlayerSettings.bundleVersion} ({(il2cpp ? "IL2CPP" : "Mono")}) " +
                      $"to {Path.GetFullPath(folder)}: {bytes / (1024f * 1024f):0.0} MB in {summary.totalTime.TotalSeconds:0} s.");
            return report;
        }

        /// <summary>Moves the Burst / IL2CPP debug folders out of the build so SteamPipe never uploads them.</summary>
        /// <summary>The demo's Steam App ID from the DemoConfig (0 = none yet).</summary>
        static uint DemoAppId()
        {
            var config = AssetDatabase.LoadAssetAtPath<Antarium.Content.DemoConfig>(ContentSetup.DemoConfigPath);
            return config != null ? config.demoAppId : 0;
        }

        static void MoveDoNotShip(string folder, bool development, bool demo = false)
        {
            string symbols = Path.Combine(BuildRoot, "Symbols", PlayerSettings.bundleVersion + (demo ? "-demo" : "") + (development ? "-dev" : ""));
            foreach (var dir in Directory.GetDirectories(folder))
            {
                string name = Path.GetFileName(dir);
                if (!name.Contains("DoNotShip", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("DontShip", StringComparison.OrdinalIgnoreCase)) continue;
                string target = Path.Combine(symbols, name);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(symbols);
                MoveWhenFree(dir, target);
            }
        }

        /// <summary>
        /// Unity's symbol uploader (usymtool) keeps reading the IL2CPP backup folder after BuildPlayer returns, and
        /// Windows refuses the move until it lets go, so keep trying. A failed move is not fatal: the upload script
        /// excludes these folders anyway.
        /// </summary>
        static void MoveWhenFree(string from, string to)
        {
            var deadline = DateTime.UtcNow + MoveTimeout;
            EditorUtility.DisplayProgressBar("Antarium build", $"Moving {Path.GetFileName(from)} to {to}", 1f);
            try
            {
                while (true)
                {
                    try
                    {
                        Directory.Move(from, to);
                        return;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        if (DateTime.UtcNow >= deadline)
                        {
                            Debug.LogWarning($"Could not move {from} to {to}, it stays in the build folder ({e.Message}). " +
                                             "Copy it by hand to keep the symbols: the next build deletes it.");
                            return;
                        }
                        Thread.Sleep(MoveRetryMilliseconds);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
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
