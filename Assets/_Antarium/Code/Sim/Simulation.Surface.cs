using System;
using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// The surface (Milestone 10.2): day and night, weather (rain floods the tunnels near the surface for a while, some rains
    /// are thunderstorms with lightning (11); droughts halve forage), leaves falling from plants, birds that scare ants
    /// underground and take those left in the open, spiders that come out at night (except on a starfall night, 11), and the
    /// queen eating honeydew.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Ants less than this many cells below the surface are in the open (birds see them).</summary>
        public const int ShelterDepth = 2;

        public readonly SurfaceState Surface = new();
        bool _shelterDirty;
        bool _anySheltering;
        readonly List<(int ant, float dist)> _prey = new();

        void InitSurface()
        {
            var c = Config.surface;
            Surface.Plants.Clear();
            Surface.Plants.AddRange(SurfacePlants.Place(World.Width, Content.WorldGen, Seed));
            Surface.Rng = new Unity.Mathematics.Random(math.hash(new uint2(Seed, 0x5CEA7u)) | 1u);
            Surface.Clock = c.startTime;
            Surface.WeatherTimer = c.firstWeatherSeconds;
            Surface.BirdCooldown = c.firstBirdSeconds;
            Fluids.SoakDepth = c.soakDepth;
            Fluids.SoakChance = c.soakChance;
            UpdateDaylight();
        }

        internal void UpdateDaylight()
        {
            Surface.Daylight = SurfaceState.DaylightAt(Surface.TimeOfDay, Config.surface);
            Surface.IsDay = Surface.Daylight >= 0.5f;
        }

        /// <summary>True if an ant in cell <paramref name="c"/> is out in the open (on the surface or just under it).</summary>
        public bool IsExposed(int2 c) => World.Depth(c.x, c.y) < ShelterDepth;

        void UpdateSurface(float dt)
        {
            var s = Surface;
            bool wasDay = s.IsDay;
            s.Clock += dt / math.max(1f, Config.surface.dayLengthSeconds);
            UpdateDaylight();
            if (s.IsDay != wasDay)
            {
                Events.Publish(new SurfaceEvent(s.IsDay ? SurfaceEventKind.Dawn : SurfaceEventKind.Dusk, Colony.QueenCell, s.Day));
                if (s.IsDay) Dawn();
                else Nightfall();
            }
            UpdateWeather(dt);
            UpdateBird(dt);
            UpdateSpiders(dt);
            UpdateHoneydew(dt);
            UpdateLeaves(dt);
            if (_anySheltering && s.Bird == BirdPhase.None) ReleaseShelter();
        }

        /// <summary>Offline: the clock runs on; weather, birds and spiders do not happen while the game is closed.</summary>
        internal void SkipSurfaceTime(float seconds)
        {
            var s = Surface;
            var c = Config.surface;
            s.Clock += seconds / math.max(1f, c.dayLengthSeconds);
            UpdateDaylight();
            if (s.Weather != WeatherKind.Clear) EndWeather();
            if (s.IsDay || seconds >= c.dayLengthSeconds * (1f - c.dayShare)) s.Starfall = false;
            s.WeatherTimer = math.max(60f, s.WeatherTimer - seconds);
            s.Bird = BirdPhase.None;
            s.BirdCooldown = math.max(30f, s.BirdCooldown - seconds);
            s.SpidersToSpawn = 0;
            if (s.IsDay) Enemies.DismissNightHunters();
            s.RoyalFeast = 0f;
            _shelterDirty = true;
            ReleaseShelter();
        }

        // ---------------------------------------------------------------- weather

        void UpdateWeather(float dt)
        {
            var s = Surface;
            if (s.Weather != WeatherKind.Clear)
            {
                s.WeatherLeft -= dt;
                if (s.Thunderstorm) UpdateLightning(dt);
                if (s.WeatherLeft <= 0f) EndWeather();
                return;
            }
            s.WeatherTimer -= dt;
            if (s.WeatherTimer > 0f || s.Starfall) return; // a starfall night stays clear; the weather waits for dawn
            var c = Config.surface;
            s.WeatherTimer = s.Rng.NextFloat(c.weatherIntervalMin, math.max(c.weatherIntervalMin, c.weatherIntervalMax));
            StartWeather(s.Rng.NextFloat() < c.rainChance ? WeatherKind.Rain : WeatherKind.Drought);
        }

        /// <summary>Starts rain (a thunderstorm by <c>thunderstormChance</c>) or a drought now (also debug and tests).</summary>
        public void StartWeather(WeatherKind kind) =>
            StartWeather(kind, kind == WeatherKind.Rain && Surface.Rng.NextFloat() < Config.surface.thunderstormChance);

        /// <summary>Starts a thunderstorm now (debug and tests).</summary>
        public void StartThunderstorm() => StartWeather(WeatherKind.Rain, true);

        void StartWeather(WeatherKind kind, bool storm)
        {
            var s = Surface;
            var c = Config.surface;
            if (s.Weather != WeatherKind.Clear) EndWeather();
            if (kind == WeatherKind.Clear) return;
            var range = kind == WeatherKind.Rain ? c.rainSeconds : c.droughtSeconds;
            s.Weather = kind;
            s.Thunderstorm = storm && kind == WeatherKind.Rain;
            s.LightningTimer = s.Rng.NextFloat(1f, math.max(1f, c.lightningInterval.x) + 1e-3f);
            s.WeatherLength = s.WeatherLeft = s.Rng.NextFloat(math.max(1f, range.x), math.max(range.x, range.y) + 1e-3f);
            Events.Publish(new SurfaceEvent(kind == WeatherKind.Rain ? SurfaceEventKind.RainStarted : SurfaceEventKind.DroughtStarted, Colony.QueenCell));
            if (s.Thunderstorm) Events.Publish(new SurfaceEvent(SurfaceEventKind.ThunderstormStarted, Colony.QueenCell));
        }

        /// <summary>
        /// Thunderstorms: every few seconds lightning strikes the ground somewhere within the rain's reach (the ground, or the
        /// bottom of an open shaft). It is a spectacle only: it lights up the sky and rumbles, but harms nothing.
        /// </summary>
        void UpdateLightning(float dt)
        {
            var s = Surface;
            var c = Config.surface;
            s.LightningTimer -= dt;
            if (s.LightningTimer > 0f) return;
            float min = math.max(0.5f, c.lightningInterval.x);
            s.LightningTimer = s.Rng.NextFloat(min, math.max(min, c.lightningInterval.y) + 1e-3f);
            int range = math.max(1, c.rainRange);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int x = math.clamp(Colony.QueenCell.x + s.Rng.NextInt(-range, range + 1), 1, World.Width - 2);
                int y = World.Height - 1;
                if (!World[x, y].IsOpen) continue; // no sky over this column (the world's edge)
                while (y > 0 && World[x, y - 1].IsOpen) y--;
                s.LightningCell = new int2(x, y);
                s.LightningStrikes++;
                Events.Publish(new SurfaceEvent(SurfaceEventKind.Lightning, s.LightningCell));
                return;
            }
        }

        void EndWeather()
        {
            var s = Surface;
            var kind = s.Weather;
            s.Weather = WeatherKind.Clear;
            s.Thunderstorm = false;
            s.WeatherLeft = 0f;
            Events.Publish(new SurfaceEvent(kind == WeatherKind.Rain ? SurfaceEventKind.RainEnded : SurfaceEventKind.DroughtEnded, Colony.QueenCell));
        }

        /// <summary>Forage (seeds, leaves) appears this much as often under the current weather.</summary>
        float WeatherForage => Surface.Weather == WeatherKind.Drought ? Config.surface.droughtForage : 1f;

        /// <summary>One fluid step of rain: drops of water land on the ground (or the bottom of an open shaft) near the nest.</summary>
        void RainStep()
        {
            var c = Config.surface;
            int range = math.max(1, c.rainRange);
            int drops = Surface.Thunderstorm ? (int)math.round(c.rainDropsPerStep * c.stormRainFactor) : c.rainDropsPerStep;
            for (int k = 0; k < drops; k++)
            {
                int x = math.clamp(Colony.QueenCell.x + Surface.Rng.NextInt(-range, range + 1), 1, World.Width - 2);
                int y = World.Height - 1;
                while (y > 0 && World[x, y - 1].IsOpen) y--;
                if (World[x, y].IsOpen) Fluids.AddWater(new int2(x, y), c.rainDropLevel);
            }
        }

        // ---------------------------------------------------------------- leaves

        /// <summary>By day (and on a starfall night), leaves fall from living plants next to them, up to <c>maxSurfaceLeaves</c> on the ground.</summary>
        void UpdateLeaves(float dt)
        {
            var s = Surface;
            var c = Config.surface;
            if (Content.PlantForage.Length == 0 || s.Plants.Count == 0 || !s.ForageGrows) return;
            float rate = Colony.Mods.Mult(StatKind.ForageRate) * WeatherForage;
            if (rate <= 0f) return;
            s.LeafTimer += dt;
            if (s.LeafTimer < c.leafInterval / rate) return;
            s.LeafTimer = 0f;

            int onSurface = 0;
            for (int i = 0; i < Colony.Piles.Count; i++)
            {
                var p = Colony.Piles[i];
                if (p.Cell.y > World.SurfaceY[p.Cell.x] - 2 && Array.IndexOf(Content.PlantForage, p.Item) >= 0) onSurface += p.Count;
            }
            if (onSurface >= c.maxSurfaceLeaves) return;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var plant = s.Plants[s.Rng.NextInt(s.Plants.Count)];
                if (!SurfacePlants.IsAlive(World, plant)) continue;
                int x = math.clamp(plant.X + s.Rng.NextInt(-1, 2), 1, World.Width - 2);
                int2 top = new int2(x, math.min(World.Height - 1, World.SurfaceY[x] + 4));
                if (!World[top.x, top.y].IsOpen) continue;
                DropItem(top, Content.PlantForage[s.Rng.NextInt(Content.PlantForage.Length)], 1);
                return;
            }
        }

        // ---------------------------------------------------------------- honeydew

        /// <summary>The queen eats a honeydew from the stock whenever the last one is used up: she lays faster meanwhile.</summary>
        void UpdateHoneydew(float dt)
        {
            var s = Surface;
            ushort treat = Content.RoyalTreat;
            s.RoyalFeast = math.max(0f, s.RoyalFeast - dt);
            if (treat != ContentDB.NoItem && s.RoyalFeast <= 0f && Colony.Inventory.Available(treat) > 0 && Colony.Inventory.Consume(treat))
            {
                s.RoyalFeast = Config.surface.honeydewSeconds;
                Events.Publish(new SurfaceEvent(SurfaceEventKind.QueenFed, Colony.QueenCell));
            }
            Colony.QueenFed = s.RoyalFeast > 0f;
            Colony.FedLayingBonus = Config.surface.honeydewLaying;
        }

        // ---------------------------------------------------------------- birds

        void UpdateBird(float dt)
        {
            var s = Surface;
            var c = Config.surface;
            switch (s.Bird)
            {
                case BirdPhase.None:
                    s.BirdCooldown -= dt;
                    if (s.BirdCooldown > 0f || !s.IsDay) return;
                    if (!TriggerBird()) s.BirdCooldown = 20f; // nobody out there: look again soon
                    return;

                case BirdPhase.Circling:
                    s.BirdTimer -= dt;
                    ShelterExposedAnts();
                    if (FindPrey(out float2 prey))
                    {
                        float2 d = prey - s.BirdPos;
                        float len = math.length(d), step = c.birdSpeed * dt;
                        s.BirdPos = len <= step ? prey : s.BirdPos + d / len * step;
                    }
                    if (s.BirdTimer <= 0f) BirdStrike();
                    return;

                case BirdPhase.Leaving:
                    s.BirdTimer -= dt;
                    if (s.BirdTimer > 0f) return;
                    s.Bird = BirdPhase.None;
                    s.BirdCooldown = s.Rng.NextFloat(c.birdIntervalMin, math.max(c.birdIntervalMin, c.birdIntervalMax));
                    _shelterDirty = true;
                    ReleaseShelter();
                    Events.Publish(new SurfaceEvent(SurfaceEventKind.BirdGone, (int2)s.BirdPos));
                    return;
            }
        }

        /// <summary>A bird starts circling over the ant out in the open farthest from the nest (also debug and tests). False if no ant is out.</summary>
        public bool TriggerBird()
        {
            var s = Surface;
            if (s.Bird != BirdPhase.None || !FindPrey(out float2 prey)) return false;
            s.Bird = BirdPhase.Circling;
            s.BirdTimer = Config.surface.birdWarningSeconds;
            s.BirdPos = prey;
            ShelterExposedAnts();
            _shelterDirty = true;
            _forceFields = true;
            Events.Publish(new SurfaceEvent(SurfaceEventKind.BirdCircling, (int2)prey));
            return true;
        }

        /// <summary>
        /// The bird's prey: the ant in the open that is farthest from cover (along the shelter field once it is built,
        /// else farthest from the nest).
        /// </summary>
        bool FindPrey(out float2 pos)
        {
            pos = default;
            float best = -1f;
            var ants = Ants.Ants;
            bool field = Surface.Bird == BirdPhase.Circling;
            for (int i = 0; i < ants.Length; i++)
            {
                var a = ants[i];
                if (a.IsDead || !IsExposed(a.Cell)) continue;
                float score;
                if (field)
                {
                    ushort d = Fields.Get(FieldKind.Shelter, World, a.Cell);
                    score = d == FlowFields.Unreachable ? 60000f : d;
                }
                else score = math.abs(a.Cell.x - Colony.QueenCell.x);
                if (score <= best) continue;
                best = score;
                pos = a.Pos;
            }
            return best >= 0f;
        }

        /// <summary>Every ant in the open (except the one the player controls) drops its job and runs underground.</summary>
        void ShelterExposedAnts()
        {
            var ants = Ants.Ants;
            for (int i = 0; i < ants.Length; i++)
            {
                var a = ants[i];
                if (a.IsDead || a.Possessed || a.State == AntState.Sheltering || !IsExposed(a.Cell)) continue;
                a.State = AntState.Sheltering;
                ants[i] = a;
                _anySheltering = true;
            }
        }

        void ReleaseShelter()
        {
            _anySheltering = false;
            var ants = Ants.Ants;
            for (int i = 0; i < ants.Length; i++)
            {
                var a = ants[i];
                if (a.State != AntState.Sheltering) continue;
                a.State = AntState.Idle;
                ants[i] = a;
            }
        }

        /// <summary>The bird strikes: ants still in the open near its shadow are taken (the nearest first, at most <c>birdMaxKills</c>).</summary>
        void BirdStrike()
        {
            var s = Surface;
            var c = Config.surface;
            _prey.Clear();
            var ants = Ants.Ants;
            for (int i = 0; i < ants.Length; i++)
            {
                var a = ants[i];
                if (a.IsDead || !IsExposed(a.Cell)) continue;
                float d = math.distance(a.Pos, s.BirdPos);
                if (d <= c.birdStrikeRadius) _prey.Add((i, d));
            }
            _prey.Sort((a, b) => a.dist.CompareTo(b.dist));
            int taken = math.min(_prey.Count, c.birdMaxKills);
            for (int k = 0; k < taken; k++)
            {
                var a = ants[_prey[k].ant];
                a.State = AntState.Dead;
                a.Cause = DeathCause.Bird;
                ants[_prey[k].ant] = a;
            }
            s.Bird = BirdPhase.Leaving;
            s.BirdTimer = c.birdShelterSeconds;
            Events.Publish(new SurfaceEvent(SurfaceEventKind.BirdStruck, (int2)s.BirdPos, taken));
        }

        // ---------------------------------------------------------------- night spiders

        void Nightfall()
        {
            var s = Surface;
            var c = Config.surface;
            s.SpidersToSpawn = 0;
            if (s.Weather != WeatherKind.Rain && PlayTime >= c.firstStarfallSeconds && s.Rng.NextFloat() < c.starfallChance)
            {
                s.Starfall = true;
                Events.Publish(new SurfaceEvent(SurfaceEventKind.StarfallStarted, Colony.QueenCell));
                return; // no hunters tonight
            }
            if (Content.NightHunters.Length == 0 || PlayTime < c.firstSpiderSeconds || Ants.Count < c.spiderMinAnts) return;
            float more = math.max(0f, Colony.Mods.Mult(StatKind.CreatureCount));
            int max = (int)math.round(c.spidersMax * more);
            s.SpidersToSpawn = math.clamp((int)math.round((c.spidersBase + Ants.Count * c.spidersPerAnt) * more), 0, max);
            s.SpiderTimer = s.Rng.NextFloat(c.spiderSpawnWindow * 0.25f);
            if (s.SpidersToSpawn > 0)
                Events.Publish(new SurfaceEvent(SurfaceEventKind.SpidersOut, Colony.QueenCell, s.SpidersToSpawn));
        }

        void UpdateSpiders(float dt)
        {
            var s = Surface;
            if (s.SpidersToSpawn <= 0 || s.IsDay || s.Starfall) return;
            s.SpiderTimer -= dt;
            if (s.SpiderTimer > 0f) return;
            var c = Config.surface;
            int def = Content.NightHunters[s.Rng.NextInt(Content.NightHunters.Length)];
            Enemies.SpawnOnSurface(def, new int2(c.spiderDistance.x, c.spiderDistance.y));
            s.SpidersToSpawn--;
            s.SpiderTimer = c.spiderSpawnWindow / math.max(1, s.SpidersToSpawn + 1);
        }

        void Dawn()
        {
            Surface.SpidersToSpawn = 0;
            if (Surface.Starfall)
            {
                Surface.Starfall = false;
                Events.Publish(new SurfaceEvent(SurfaceEventKind.StarfallEnded, Colony.QueenCell));
            }
            int left = Enemies.DismissNightHunters();
            if (left > 0) Events.Publish(new SurfaceEvent(SurfaceEventKind.SpidersLeft, Colony.QueenCell, left));
        }

        /// <summary>
        /// Debug / tests: tonight becomes a starfall (clears the weather, sends the night hunters off). False by day.
        /// </summary>
        public bool TriggerStarfall()
        {
            var s = Surface;
            if (s.IsDay || s.Starfall) return false;
            if (s.Weather != WeatherKind.Clear) EndWeather();
            s.Starfall = true;
            s.SpidersToSpawn = 0;
            Enemies.DismissNightHunters();
            Events.Publish(new SurfaceEvent(SurfaceEventKind.StarfallStarted, Colony.QueenCell));
            return true;
        }

        /// <summary>Debug / tests: night falls now (the clock jumps to just after sunset).</summary>
        public void SkipToNight()
        {
            var c = Config.surface;
            double sunset = 0.5 + c.dayShare * 0.5 + c.twilight * 0.5 + 0.005;
            Surface.Clock = Math.Floor(Surface.Clock) + sunset + (Surface.TimeOfDay > sunset ? 1 : 0);
        }

        // ---------------------------------------------------------------- shelter field

        void MarkSurfaceFieldsDirty()
        {
            if (!_shelterDirty) return;
            Fields.MarkDirty(FieldKind.Shelter);
            _shelterDirty = false;
        }

        void FillSurfaceSources()
        {
            if (!Fields.IsDirty(FieldKind.Shelter)) return;
            var sources = Fields.Sources(FieldKind.Shelter);
            sources.Clear();
            if (Surface.Bird == BirdPhase.None) return;
            var nav = new NavGrid(World);
            for (int x = 0; x < World.Width; x++)
            for (int d = ShelterDepth; d <= ShelterDepth + 1; d++)
            {
                var c = new int2(x, World.SurfaceY[x] - d);
                if (nav.IsWalkable(c)) sources.Add(c);
            }
        }
    }
}
