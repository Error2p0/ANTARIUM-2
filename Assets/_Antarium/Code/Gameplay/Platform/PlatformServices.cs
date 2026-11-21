using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Antarium.Gameplay
{
    public static class PlatformServices
    {
        /// <summary>Antarium 2 on Steam. Also written to <c>steam_appid.txt</c> and the SteamPipe scripts.</summary>
        public const uint SteamAppId = 4453940;
        /// <summary>Editor preference (Antarium → Steam → Use Steam in Editor). Off by default, so play-testing in the
        /// editor never unlocks achievements on the developer's Steam account.</summary>
        public const string SteamInEditorPref = "Antarium.SteamInEditor";
        /// <summary>Player Settings → Company Name before Milestone 9; its folder is moved over once.</summary>
        public const string OldCompanyName = "DefaultCompany";

        /// <summary>
        /// The platform for this run: Steam when the game runs under Steam (builds; the editor only when opted in),
        /// otherwise <see cref="LocalPlatform"/>. When a release build was started outside Steam, Steam restarts it
        /// and this run quits.
        /// </summary>
        /// <param name="achievements">API names of every achievement, merged with the store on the first frames.</param>
        /// <param name="stats">API names of every stat.</param>
        public static IPlatformServices Create(string dataDirectory, IReadOnlyList<string> achievements, IReadOnlyList<string> stats)
        {
            MoveOldFolder(dataDirectory, OldCompanyName);
            var local = new LocalPlatform(dataDirectory);
#if ANTARIUM_STEAM && UNITY_STANDALONE
            if (UseSteam)
            {
                var steam = SteamClient.TryCreate(SteamAppId, out bool restarting);
                if (restarting)
                {
                    Application.Quit();
                    return local;
                }
                if (steam != null) return new StorePlatform(steam, local, achievements, stats);
            }
#endif
            return local;
        }

        static bool UseSteam
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(SteamInEditorPref, false);
#else
                return !Environment.GetCommandLineArgs().Contains("-nosteam");
#endif
            }
        }

        static bool Contains(this string[] args, string arg) => Array.IndexOf(args, arg) >= 0;

        /// <summary>
        /// Copies saves, settings and the profile from the folder of an older Company Name
        /// (<c>LocalLow/DefaultCompany/ANTARIUM 2</c>) once, when the current folder has none of them yet.
        /// The old folder is left as it is. Returns true when something was copied.
        /// </summary>
        public static bool MoveOldFolder(string dataDirectory, string oldCompany)
        {
            try
            {
                var product = Path.GetFileName(dataDirectory.TrimEnd('/', '\\'));
                var company = Path.GetDirectoryName(dataDirectory.TrimEnd('/', '\\'));
                var root = company == null ? null : Path.GetDirectoryName(company);
                if (root == null || string.Equals(Path.GetFileName(company), oldCompany, StringComparison.OrdinalIgnoreCase)) return false;
                var old = Path.Combine(root, oldCompany, product);
                if (!Directory.Exists(old)) return false;
                if (Directory.Exists(Path.Combine(dataDirectory, "Saves")) || File.Exists(Path.Combine(dataDirectory, "settings.json"))
                    || File.Exists(Path.Combine(dataDirectory, "profile.json"))) return false;

                bool any = false;
                foreach (var name in new[] { "settings.json", "profile.json" })
                {
                    var from = Path.Combine(old, name);
                    if (!File.Exists(from)) continue;
                    Directory.CreateDirectory(dataDirectory);
                    File.Copy(from, Path.Combine(dataDirectory, name));
                    any = true;
                }
                var oldSaves = Path.Combine(old, "Saves");
                if (Directory.Exists(oldSaves))
                {
                    var saves = Path.Combine(dataDirectory, "Saves");
                    Directory.CreateDirectory(saves);
                    foreach (var file in Directory.GetFiles(oldSaves))
                    {
                        File.Copy(file, Path.Combine(saves, Path.GetFileName(file)));
                        any = true;
                    }
                }
                if (any) Debug.Log($"Copied saves and settings from {old} to {dataDirectory}.");
                return any;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not copy the old saves: {e.Message}");
                return false;
            }
        }
    }
}
