#if ANTARIUM_STEAM && UNITY_STANDALONE
using System;
using Steamworks;
using UnityEngine;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Steamworks.NET behind <see cref="IStoreClient"/>. Only thin calls live here; merging, presence and the local
    /// profile are in <see cref="StorePlatform"/>. Compiled only with the Steamworks.NET package (<c>ANTARIUM_STEAM</c>
    /// is defined by the Gameplay assembly when the package is installed).
    /// </summary>
    public sealed class SteamClient : IStoreClient
    {
        Callback<GameOverlayActivated_t> _overlay;
        bool _disposed;

        public string Name => "Steam";
        public event Action<bool> OverlayChanged;

        SteamClient()
        {
            _overlay = Callback<GameOverlayActivated_t>.Create(OnOverlay);
        }

        /// <summary>
        /// Starts Steam. Null when Steam is not running, the DLL is missing or the user doesn't own the app: the game then
        /// runs on the local profile. <paramref name="restarting"/> is true when a build was started outside Steam and
        /// Steam is starting it again (this process should quit).
        /// </summary>
        public static SteamClient TryCreate(uint appId, out bool restarting)
        {
            restarting = false;
            try
            {
                if (!Packsize.Test() || !DllCheck.Test())
                {
                    Debug.LogWarning("Steam: steam_api64.dll does not match Steamworks.NET; running without Steam.");
                    return null;
                }
#if !UNITY_EDITOR
                // Without steam_appid.txt next to the exe (release builds) Steam relaunches the game through the client.
                if (SteamAPI.RestartAppIfNecessary(new AppId_t(appId)))
                {
                    restarting = true;
                    return null;
                }
#endif
                var result = SteamAPI.InitEx(out string error);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    Debug.Log($"Steam is not available ({result}: {error}); running without Steam.");
                    return null;
                }
                Debug.Log($"Steam started for app {SteamUtils.GetAppID()} as {SteamFriends.GetPersonaName()}.");
                return new SteamClient();
            }
            catch (DllNotFoundException e)
            {
                Debug.LogWarning($"Steam: steam_api64.dll was not found ({e.Message}); running without Steam.");
                return null;
            }
        }

        void OnOverlay(GameOverlayActivated_t e) => OverlayChanged?.Invoke(e.m_bActive != 0);

        public void RunCallbacks()
        {
            if (!_disposed) SteamAPI.RunCallbacks();
        }

        public bool TryGetAchievement(string apiName, out bool unlocked) => SteamUserStats.GetAchievement(apiName, out unlocked);
        public bool SetAchievement(string apiName) => SteamUserStats.SetAchievement(apiName);
        public bool TryGetStat(string apiName, out int value) => SteamUserStats.GetStat(apiName, out value);
        public bool SetStat(string apiName, int value) => SteamUserStats.SetStat(apiName, value);
        public bool StoreStats() => SteamUserStats.StoreStats();
        public void SetPresence(string key, string value) => SteamFriends.SetRichPresence(key, value);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _overlay?.Dispose();
            _overlay = null;
            SteamFriends.ClearRichPresence();
            SteamAPI.Shutdown();
        }
    }
}
#endif
