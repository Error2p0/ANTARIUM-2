using System;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Store / platform features the game uses (GDD 3.7): achievements, stats, where saves live, rich presence and
    /// the overlay. The game only talks to this interface; <see cref="LocalPlatform"/> works without any store, and
    /// <see cref="StorePlatform"/> adds Steam on top of it (see <see cref="PlatformServices.Create"/>).
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
        /// <summary>Forgets the local achievements and stats (Settings → Erase all progress). A store keeps its own.</summary>
        void ResetProgress();

        /// <summary>What the player is doing, shown to friends (Steam rich presence). Cheap to call every frame.</summary>
        void SetPresence(Presence presence);

        /// <summary>The store overlay opened (true) or closed (false). The game pauses while it is open.</summary>
        event Action<bool> OverlayChanged;
    }

    public enum PresenceState : byte
    {
        None,
        Title,
        Colony,
        Flight,
    }

    /// <summary>Rich presence: "Generation 3, 420 cells deep".</summary>
    public readonly struct Presence : IEquatable<Presence>
    {
        public readonly PresenceState State;
        /// <summary>1 = the first colony of the bloodline.</summary>
        public readonly int Generation;
        /// <summary>Deepest dug cell below the surface.</summary>
        public readonly int Depth;

        public Presence(PresenceState state, int generation = 0, int depth = 0)
        {
            State = state;
            Generation = generation;
            Depth = depth;
        }

        public bool Equals(Presence o) => State == o.State && Generation == o.Generation && Depth == o.Depth;
        public override bool Equals(object obj) => obj is Presence p && Equals(p);
        public override int GetHashCode() => ((int)State * 397 ^ Generation) * 397 ^ Depth;
    }
}
