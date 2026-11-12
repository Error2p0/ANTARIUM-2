using System;
using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public enum WeatherKind : byte
    {
        Clear,
        /// <summary>Water falls on the ground near the nest and runs into open tunnels; it soaks away near the surface.</summary>
        Rain,
        /// <summary>Seeds and leaves appear less often.</summary>
        Drought,
    }

    public enum BirdPhase : byte
    {
        None,
        /// <summary>Its shadow follows an ant on the surface: every ant up there runs for cover until it strikes.</summary>
        Circling,
        /// <summary>Struck (or missed) and flying off; sheltering ants come out when it is gone.</summary>
        Leaving,
    }

    /// <summary>
    /// The surface: time of day, weather, the bird and night spiders, leaves from plants, and the queen's honeydew.
    /// Plain state (saved in section SURF); the logic lives in the simulation.
    /// </summary>
    public sealed class SurfaceState
    {
        /// <summary>Days since the colony was founded plus the time of day: day N (from 1) runs from N - 1 to N.</summary>
        public double Clock;
        /// <summary>0 = midnight, 0.5 = noon.</summary>
        public float TimeOfDay => (float)(Clock - Math.Floor(Clock));
        /// <summary>Day number, from 1.</summary>
        public int Day => (int)Math.Floor(Clock) + 1;
        /// <summary>Sky light 0 (night) .. 1 (day), with smooth sunrises and sunsets.</summary>
        public float Daylight { get; internal set; }
        public bool IsDay { get; internal set; }

        public WeatherKind Weather { get; internal set; }
        /// <summary>Seconds left of the current weather, and how long it lasts in all.</summary>
        public float WeatherLeft { get; internal set; }
        public float WeatherLength { get; internal set; }
        /// <summary>Seconds until the next weather (counts while it is clear).</summary>
        internal float WeatherTimer;
        /// <summary>The current rain is a thunderstorm: heavier rain and lightning.</summary>
        public bool Thunderstorm { get; internal set; }
        /// <summary>Seconds until the next lightning strike (thunderstorms).</summary>
        internal float LightningTimer;
        /// <summary>Lightning strikes so far (the view starts a bolt whenever it changes); not saved.</summary>
        public int LightningStrikes { get; internal set; }
        /// <summary>Where the last lightning struck (the ground, cell space).</summary>
        public int2 LightningCell { get; internal set; }

        /// <summary>Tonight is a starfall: shooting stars all night, no night hunters, and forage keeps coming.</summary>
        public bool Starfall { get; internal set; }
        /// <summary>Seeds and leaves appear: by day, and all through a starfall night.</summary>
        public bool ForageGrows => IsDay || Starfall;

        public BirdPhase Bird { get; internal set; }
        /// <summary>Seconds until the bird strikes (circling) or is gone (leaving).</summary>
        public float BirdTimer { get; internal set; }
        /// <summary>Where its shadow is (cell space, on the ground).</summary>
        public float2 BirdPos { get; internal set; }
        /// <summary>Seconds until the next bird may come.</summary>
        internal float BirdCooldown;

        internal int SpidersToSpawn;
        internal float SpiderTimer;
        internal float LeafTimer;
        /// <summary>Seconds left of the queen's last honeydew (she lays faster and eats nothing else meanwhile).</summary>
        public float RoyalFeast { get; internal set; }

        internal Unity.Mathematics.Random Rng;

        /// <summary>Plants along the ground surface (from the seed; see <see cref="SurfacePlants"/>).</summary>
        public readonly List<SurfacePlant> Plants = new();

        /// <summary>Daylight at time of day <paramref name="t"/>: 1 from sunrise to sunset, 0 at night, smooth in between.</summary>
        public static float DaylightAt(float t, SurfaceConfig config)
        {
            float half = math.saturate(config.dayShare) * 0.5f, tw = math.max(1e-4f, config.twilight) * 0.5f;
            float rise = math.smoothstep(0.5f - half - tw, 0.5f - half + tw, t);
            float set = 1f - math.smoothstep(0.5f + half - tw, 0.5f + half + tw, t);
            return math.min(rise, set);
        }
    }

    public enum SurfaceEventKind : byte
    {
        Dawn,
        Dusk,
        RainStarted,
        RainEnded,
        DroughtStarted,
        DroughtEnded,
        /// <summary>A bird circles over <see cref="SurfaceEvent.Cell"/>; it strikes in <c>birdWarningSeconds</c>.</summary>
        BirdCircling,
        /// <summary>The bird struck; <see cref="SurfaceEvent.Count"/> ants were taken (0 = it missed).</summary>
        BirdStruck,
        BirdGone,
        /// <summary><see cref="SurfaceEvent.Count"/> spiders come out on the surface tonight.</summary>
        SpidersOut,
        /// <summary>Dawn: <see cref="SurfaceEvent.Count"/> spiders slunk off.</summary>
        SpidersLeft,
        /// <summary>The queen ate a honeydew from the stock.</summary>
        QueenFed,
        /// <summary>A rain that is a thunderstorm started (published after <see cref="RainStarted"/>).</summary>
        ThunderstormStarted,
        /// <summary>Lightning struck the ground at <see cref="SurfaceEvent.Cell"/>.</summary>
        Lightning,
        /// <summary>Dusk: tonight is a starfall (no night hunters, forage keeps coming).</summary>
        StarfallStarted,
        /// <summary>Dawn ended the starfall.</summary>
        StarfallEnded,
    }

    /// <summary>Something happened on the surface (day / night, weather, birds, spiders, honeydew).</summary>
    public readonly struct SurfaceEvent
    {
        public readonly SurfaceEventKind Kind;
        public readonly int2 Cell;
        /// <summary>Day number (dawn / dusk), ants taken (strike), spiders (spiders).</summary>
        public readonly int Count;
        public SurfaceEvent(SurfaceEventKind kind, int2 cell, int count = 0) { Kind = kind; Cell = cell; Count = count; }
    }
}
