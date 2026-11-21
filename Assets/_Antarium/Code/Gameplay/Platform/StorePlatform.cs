using System;
using System.Collections.Generic;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The calls a store SDK has to answer (Steam: <c>SteamClient</c>). Kept tiny so <see cref="StorePlatform"/>,
    /// which holds the logic, can be tested without the store running.
    /// </summary>
    public interface IStoreClient : IDisposable
    {
        string Name { get; }
        void RunCallbacks();
        /// <summary>False when the store does not know the achievement (not set up) or has not loaded the user's stats yet.</summary>
        bool TryGetAchievement(string apiName, out bool unlocked);
        bool SetAchievement(string apiName);
        bool TryGetStat(string apiName, out int value);
        bool SetStat(string apiName, int value);
        bool StoreStats();
        void SetPresence(string key, string value);
        event Action<bool> OverlayChanged;
    }

    /// <summary>
    /// A store (Steam) on top of the local profile. The profile stays the game's record: every unlock and stat goes to
    /// both, and once the store has the user's stats the two are merged both ways (an achievement unlocked in either
    /// is unlocked in both, stats keep the higher value). So achievements earned before the first Steam launch, or
    /// while Steam was not running, reach Steam on the next launch, and the in-game list matches Steam.
    /// </summary>
    public sealed class StorePlatform : IPlatformServices
    {
        /// <summary>Rich presence keys (localized by the tokens in <c>steam/rich_presence_english.vdf</c>).</summary>
        public const string DisplayKey = "steam_display", GenerationKey = "gen", DepthKey = "depth";
        public const string TitleToken = "#Title", ColonyToken = "#Colony", FirstColonyToken = "#FirstColony", FlightToken = "#Flight";

        const int SyncAttempts = 10;
        const float SyncRetrySeconds = 1f;

        readonly IStoreClient _store;
        readonly LocalPlatform _local;
        readonly IReadOnlyList<string> _achievements, _stats;
        bool _synced, _storeDirty;
        int _syncAttempts;
        float _syncTimer;
        Presence _presence;

        public string Name => _store.Name;
        public bool Online => true;
        public string SaveDirectory => _local.SaveDirectory;
        /// <summary>The merge with the store has run (it waits until the store has loaded the user's stats).</summary>
        public bool Synced => _synced;
        /// <summary>Achievements and stats the store did not know during the merge (not set up in Steamworks).</summary>
        public readonly List<string> Unknown = new();

        public event Action<bool> OverlayChanged
        {
            add => _store.OverlayChanged += value;
            remove => _store.OverlayChanged -= value;
        }

        /// <param name="achievements">API names of every achievement in the content.</param>
        /// <param name="stats">API names of every stat in the content.</param>
        public StorePlatform(IStoreClient store, LocalPlatform local, IReadOnlyList<string> achievements, IReadOnlyList<string> stats)
        {
            _store = store;
            _local = local;
            _achievements = achievements;
            _stats = stats;
        }

        /// <param name="dt">Seconds since the last tick (paces the merge retries).</param>
        public void Tick(float dt)
        {
            _store.RunCallbacks();
            if (_synced) return;
            _syncTimer -= dt;
            if (_syncTimer > 0f) return;
            _syncTimer = SyncRetrySeconds;
            TrySync();
        }

        public void Tick() => Tick(UnityEngine.Time.unscaledDeltaTime);

        /// <summary>
        /// Merges the profile and the store. Retried while the store answers nothing (its stats are still loading);
        /// after <see cref="SyncAttempts"/> tries whatever answered is merged and the rest is reported as unknown.
        /// </summary>
        void TrySync()
        {
            _syncAttempts++;
            int known = 0, total = _achievements.Count + _stats.Count;
            foreach (var name in _achievements)
                if (_store.TryGetAchievement(name, out _)) known++;
            foreach (var name in _stats)
                if (_store.TryGetStat(name, out _)) known++;
            if (known == 0 && total > 0 && _syncAttempts < SyncAttempts) return;

            _synced = true;
            Unknown.Clear();
            foreach (var name in _achievements)
            {
                if (!_store.TryGetAchievement(name, out bool onStore))
                {
                    Unknown.Add(name);
                    continue;
                }
                bool local = _local.IsAchievementUnlocked(name);
                if (local && !onStore) _storeDirty |= _store.SetAchievement(name);
                else if (onStore && !local) _local.UnlockAchievement(name);
            }
            foreach (var name in _stats)
            {
                if (!_store.TryGetStat(name, out int onStore))
                {
                    Unknown.Add(name);
                    continue;
                }
                int local = _local.GetStat(name);
                if (local > onStore) _storeDirty |= _store.SetStat(name, local);
                else if (onStore > local) _local.SetStat(name, onStore);
            }
            if (Unknown.Count > 0)
                UnityEngine.Debug.LogWarning($"{_store.Name} does not know these achievements / stats (set them up in Steamworks and publish): {string.Join(", ", Unknown)}");
            Flush();
        }

        public bool IsAchievementUnlocked(string apiName) => _local.IsAchievementUnlocked(apiName);

        public void UnlockAchievement(string apiName)
        {
            _local.UnlockAchievement(apiName);
            _storeDirty |= _store.SetAchievement(apiName);
        }

        public void SetStat(string apiName, int value)
        {
            if (value <= _local.GetStat(apiName) && _local.HasStat(apiName)) return;
            _local.SetStat(apiName, value);
            // Before the merge the store may hold a higher value (another machine): the merge pushes ours if it is higher.
            if (_synced) _storeDirty |= _store.SetStat(apiName, value);
        }

        public void Flush()
        {
            _local.Flush();
            if (!_storeDirty) return;
            _store.StoreStats();
            _storeDirty = false;
        }

        /// <summary>Erases the local profile; the store keeps its achievements, and the next merge brings them back.</summary>
        public void ResetProgress()
        {
            _local.ResetProgress();
            _synced = false;
            _syncAttempts = 0;
            _syncTimer = 0f;
        }

        public void SetPresence(Presence presence)
        {
            if (presence.Equals(_presence)) return;
            _presence = presence;
            switch (presence.State)
            {
                case PresenceState.Title:
                    _store.SetPresence(DisplayKey, TitleToken);
                    break;
                case PresenceState.Flight:
                    _store.SetPresence(DisplayKey, FlightToken);
                    break;
                case PresenceState.Colony:
                    _store.SetPresence(GenerationKey, presence.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _store.SetPresence(DepthKey, presence.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _store.SetPresence(DisplayKey, presence.Generation > 1 ? ColonyToken : FirstColonyToken);
                    break;
                default:
                    _store.SetPresence(DisplayKey, "");
                    break;
            }
        }

        public void Dispose()
        {
            Flush();
            _store.Dispose();
            _local.Dispose();
        }
    }
}
