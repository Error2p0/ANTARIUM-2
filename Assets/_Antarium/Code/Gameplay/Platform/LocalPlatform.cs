using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Platform without a store: achievements and stats are kept in a small profile file next to the saves
    /// (<c>profile.json</c>), so the in-game list works in the editor, in dev builds and in DRM-free copies.
    /// </summary>
    public sealed class LocalPlatform : IPlatformServices
    {
        [Serializable]
        sealed class Profile
        {
            public List<string> achievements = new();
            public List<string> statNames = new();
            public List<int> statValues = new();
        }

        readonly string _path;
        readonly Profile _profile;
        readonly HashSet<string> _unlocked = new();
        bool _dirty;

        public string Name => "Local";
        public bool Online => false;
        public string SaveDirectory { get; }
        public event Action<bool> OverlayChanged { add { } remove { } }

        /// <param name="dataDirectory">Folder for the profile; saves go into its "Saves" subfolder.</param>
        public LocalPlatform(string dataDirectory)
        {
            SaveDirectory = Path.Combine(dataDirectory, "Saves");
            _path = Path.Combine(dataDirectory, "profile.json");
            _profile = Read(_path) ?? new Profile();
            foreach (var a in _profile.achievements) _unlocked.Add(a);
        }

        static Profile Read(string path)
        {
            try
            {
                return File.Exists(path) ? JsonUtility.FromJson<Profile>(File.ReadAllText(path)) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not read the profile ({e.Message}); starting a new one.");
                return null;
            }
        }

        public void Tick() { }

        public bool IsAchievementUnlocked(string apiName) => _unlocked.Contains(apiName);

        public void UnlockAchievement(string apiName)
        {
            if (!_unlocked.Add(apiName)) return;
            _profile.achievements.Add(apiName);
            _dirty = true;
        }

        public void SetStat(string apiName, int value)
        {
            int i = _profile.statNames.IndexOf(apiName);
            if (i < 0)
            {
                _profile.statNames.Add(apiName);
                _profile.statValues.Add(value);
                _dirty = true;
            }
            else if (value > _profile.statValues[i])
            {
                _profile.statValues[i] = value;
                _dirty = true;
            }
        }

        public int GetStat(string apiName)
        {
            int i = _profile.statNames.IndexOf(apiName);
            return i < 0 ? 0 : _profile.statValues[i];
        }

        public void Flush()
        {
            if (!_dirty) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonUtility.ToJson(_profile, true));
                _dirty = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not write the profile: {e.Message}");
            }
        }

        /// <summary>Forgets every achievement and stat (Settings → Erase all progress).</summary>
        public void Reset()
        {
            _unlocked.Clear();
            _profile.achievements.Clear();
            _profile.statNames.Clear();
            _profile.statValues.Clear();
            _dirty = true;
            Flush();
        }

        public void Dispose() => Flush();
    }

    public static class PlatformServices
    {
        /// <summary>
        /// The platform for this run. Milestone 9 adds the Steam implementation here (behind a scripting define), with
        /// <see cref="LocalPlatform"/> as the fallback when Steam is not running.
        /// </summary>
        public static IPlatformServices Create(string dataDirectory) => new LocalPlatform(dataDirectory);
    }
}
