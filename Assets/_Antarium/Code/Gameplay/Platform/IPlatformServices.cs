using System;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Store / platform features the game uses (GDD 3.7): achievements, stats, where saves live and the overlay.
    /// The game only talks to this interface; <see cref="LocalPlatform"/> works without any store, and the Steam
    /// implementation (Milestone 9) plugs in behind <see cref="PlatformServices.Create"/>.
    /// </summary>
    public interface IPlatformServices : IDisposable
    {
        /// <summary>"Local", "Steam", ...</summary>
        string Name { get; }
        /// <summary>True when connected to a store client (achievements reach the store).</summary>
        bool Online { get; }
        /// <summary>Folder for save files. Steam Auto-Cloud syncs this folder, so it must not depend on the store.</summary>
        string SaveDirectory { get; }

        /// <summary>Call once per frame (store callbacks).</summary>
        void Tick();

        bool IsAchievementUnlocked(string apiName);
        void UnlockAchievement(string apiName);
        /// <summary>Sets an integer stat (kept as the highest value seen).</summary>
        void SetStat(string apiName, int value);
        /// <summary>Writes stats and achievements to the store (or to the local profile).</summary>
        void Flush();

        /// <summary>The store overlay opened (true) or closed (false). The game pauses while it is open.</summary>
        event Action<bool> OverlayChanged;
    }
}
