using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Vertical footage for short-form video (TikTok, YouTube Shorts, Instagram Reels): every clip is rendered natively at
    /// 1080 × 1920 (9:16, no cropping or scaling of the pixel art), HUD off, framed for a phone (the sky over the nest, the
    /// shaft straight down). Clips are <c>clips/v_*</c>; the "shorts" capture filter runs these shots only, and
    /// <c>tools/media/make_shorts.py</c> cuts the videos from them. A portrait frame at zoom z shows 135 / z cells across
    /// and 240 / z cells down.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        const int ShortWidth = 1080, ShortHeight = 1920;

        void Vertical()
        {
            _rig.SetSize(ShortWidth, ShortHeight);
            Hud(false);
        }

        /// <summary>The sky, the meadow and the nest: a storm, a starfall, day turning to night, the drop from the sky.</summary>
        IEnumerator ShotShortsSurface()
        {
            LoadShowcase();
            Vertical();
            var q = Queen;
            float ground = GroundAt(q.x);
            _sim.StartWeather(WeatherKind.Clear);

            // From high in the sky down onto the meadow and into the nest.
            SetClock(NextDay + 0.45);
            Cam(new float2(q.x + 6f, ground + 70f), 3);
            yield return Seconds(0.5f); // the first frames after loading are still black
            Pan(new float2(q.x + 6f, ground + 70f), new float2(q.x + 6f, ground - 4f), 3, 6f);
            yield return Clip("v_sky_to_nest", 6.5f);

            // Day to night in eight seconds: the sky over the top half, the colony under the grass below.
            double from = NextDay + 0.64, span = 0.40;
            float start = _shotTime;
            SetClock(from);
            _shotTick = (t, _) => SetClock(from + span * math.saturate((t - start) / 8f));
            Pan(new float2(q.x - 4f, ground + 2f), new float2(q.x + 4f, ground + 2f), 3, 8f);
            yield return Clip("v_dusk", 8f);
            _shotTick = null;

            // A starfall night.
            _sim.SkipToNight();
            yield return Seconds(0.2f);
            _sim.TriggerStarfall();
            Cam(new float2(q.x + 4f, ground - 2f), 4);
            yield return Seconds(1.2f);
            yield return Clip("v_starfall", 7f);

            // A thunderstorm, lightning close by, the colony safe underground.
            SetClock(NextDay + 0.42);
            yield return Seconds(0.2f);
            var interval = config.surface.lightningInterval;
            int reach = config.surface.rainRange;
            config.surface.lightningInterval = new UnityEngine.Vector2(0.6f, 1.3f);
            config.surface.rainRange = 30;
            try
            {
                _sim.StartThunderstorm();
                Cam(new float2(q.x, ground - 2f), 4);
                yield return Seconds(3f);
                yield return Clip("v_storm", 8f);
            }
            finally
            {
                config.surface.lightningInterval = interval;
                config.surface.rainRange = reach;
            }
            _sim.StartWeather(WeatherKind.Clear);
        }

        /// <summary>The nest up close: the zoom-out reveal, the queen, digging by hand, possessing an ant, the busy halls.</summary>
        IEnumerator ShotShortsColony()
        {
            LoadShowcase();
            Vertical();
            var q = Queen;
            float ground = GroundAt(q.x);
            SetClock(NextDay + 0.45);

            // One ant to the whole colony: integer zoom steps out from the queen, the view sinking to show the depths.
            var deep = new float2(q.x, ground - 80f);
            _sim.RevealNow((int2)deep, 140); // the zoom-1 view (135 x 240 cells) would be unexplored fog below the nest
            _sim.World.MarkAll(ChunkFlags.TerrainUpload);
            int[] zooms = { 8, 6, 4, 3, 2, 1 };
            float[] at = { 0f, 1.3f, 2.4f, 3.3f, 4.1f, 4.9f };
            float s0 = _shotTime + 0.3f;
            _camPath = t =>
            {
                int i = 0;
                while (i + 1 < at.Length && t - s0 >= at[i + 1]) i++;
                int z = zooms[i];
                // How far the view has opened up (0 at the queen, 1 fully out) decides how far it has sunk.
                float open = math.saturate((8f / z - 1f) / 7f);
                return new CamPose(math.lerp(q + new float2(0f, 0.5f), deep, open), z);
            };
            yield return Seconds(0.3f);
            yield return Clip("v_zoom_out", 8f);

            // The royal chamber: the queen, eggs, nurses.
            Cam(q + new float2(0f, 3f), 6);
            yield return Seconds(0.3f);
            yield return Clip("v_queen", 5f);

            // The busy storage hall and nurseries.
            Cam((float2)_show.StorageHall + new float2(-4f, 6f), 4);
            yield return Seconds(0.3f);
            yield return Clip("v_halls", 5f);

            // Digging by hand straight down from the grass, then the ants hauling what fell.
            int x0 = (int)q.x - 112;
            float surface = _sim.World.SurfaceY[x0];
            float2 a = new float2(x0 + 0.5f, surface - 0.5f), b = new float2(x0 + 6.5f, surface - 22.5f);
            Cam(new float2(x0 + 3f, surface - 9f), 4);
            yield return Seconds(0.3f);
            float d0 = _shotTime;
            _shotTick = (t, dt) =>
            {
                float k = math.saturate((t - d0) / 6.5f);
                var p = math.lerp(a, b, k) + new float2(math.sin(k * 9f) * 3f, 0f);
                Pointer(p, true);
                _tools.Dig.Update(_sim, true, false, p * WorldConstants.CellSize, dt, UnityEngine.Time.timeAsDouble);
            };
            yield return Clip("v_dig", 7f);
            _shotTick = null;
            Pointer(b, false);
            yield return Clip("v_dig_haul", 4f);

            // Possessing a soldier and tunnelling down through the dark with it.
            var spot = q + new float2(-60f, -5f);
            _sim.SpawnAntsAt((int2)spot, 1, AntCaste.Soldier);
            _sim.EnqueuePossess(spot);
            _sim.FastForward(0.1f);
            float2 cam = spot;
            float bite = 0f;
            _shotTick = (t, dt) =>
            {
                int i = _sim.PossessedAnt;
                if (i < 0) return;
                var pos = _sim.Ants.Ants[i].Pos;
                _sim.PossessInput = new int2(-1, 0);
                bite -= dt;
                if (bite <= 0f)
                {
                    bite += 1f / 6f;
                    _sim.EnqueuePossessAct(pos + new float2(-1.8f, -1.1f), _tools.Dig.ComboMultiplier);
                }
            };
            _camPath = t =>
            {
                int i = _sim.PossessedAnt;
                if (i >= 0) cam = math.lerp(cam, _sim.Ants.Ants[i].Pos, 0.08f);
                return new CamPose(cam, 6);
            };
            yield return Seconds(0.3f);
            yield return Clip("v_possess", 7f);
            _shotTick = null;
            _sim.EnqueueRelease();
        }

        /// <summary>The living machine: along the factory line, the flow overlay, a water wheel.</summary>
        IEnumerator ShotShortsFactory()
        {
            LoadShowcase();
            Vertical();
            var f = (float2)_show.Factory;
            Pan((float2)_show.OreRoom + new float2(2f, 2f), f + new float2(-2f, 2f), 5, 9f);
            yield return Clip("v_factory", 9f);
            Cam(new float2(_show.Smelter.x + 2f, _show.Smelter.y + 3f), 4);
            ToggleOverlay();
            yield return Seconds(0.2f);
            yield return Clip("v_flow", 4f);
            ToggleOverlay();
            yield return Waterworks(Queen + new float2(-34f, -21f), "v_waterworks", null, 5);
        }

        /// <summary>The depths: the dive down the shaft with the depth logged per frame, each deep biome, the Elder Tree.</summary>
        IEnumerator ShotShortsDeep()
        {
            LoadShowcase();
            Vertical();
            var q = Queen;
            SetClock(NextDay + 0.45);

            // The dive: from the sky straight down the shaft to the ruins (zoom 2: 120 cells tall), at a steady speed so
            // the depth counter reads well. depth.tsv: frame, depth in cells at the frame's centre, biome.
            float2 top = new float2(_show.ShaftX, GroundAt(_show.ShaftX) + 50f), bottom = new float2(_show.ShaftX, _show.Outposts[^1].y + 10f);
            Pan(top, bottom, 2, 15f);
            var depths = new List<string>();
            _shotTick = (_, _) =>
            {
                if (!_rig.Recording) return;
                var cell = (int2)math.floor(_camera.Position / WorldConstants.CellSize);
                int depth = _sim.World.Depth(cell.x, cell.y);
                string biome = depth < 0 ? "Surface" : _content.Biomes[_sim.BiomeAtDepth(depth)].Def.DisplayName;
                depths.Add($"{_rig.FrameIndex}\t{math.max(0, depth).ToString(CultureInfo.InvariantCulture)}\t{biome}");
            };
            yield return Seconds(0.2f);
            yield return Clip("v_dive", 15.5f);
            _shotTick = null;
            File.WriteAllLines(Path.Combine(_captureRequest.OutputDirectory, "clips", "v_dive", "depth.tsv"), depths);

            // Every deep biome, a slow drift down through each.
            string[] names = { "v_crystal", "v_fungal", "v_magma", "v_ruins" };
            for (int i = 0; i < _show.Outposts.Length && i < names.Length; i++)
            {
                var v = OutpostView(i);
                Pan(v + new float2(-7f, 0f), v + new float2(7f, 0f), 5, 4f, ease: false);
                yield return Clip(names[i], 4f);
            }

            // Glow ants carrying their light through the dark ruins.
            int ruins = math.min(3, _show.Outposts.Length - 1);
            var dark = _show.Outposts[ruins];
            for (int i = 0; i < 14; i++) _sim.Ants.Spawn(dark + new int2(i - 7, 0), (uint)(700 + i), AntCaste.Glow);
            for (int i = 0; i < 6; i++) _sim.Ants.Spawn(dark + new int2(i * 3 - 8, 0), (uint)(720 + i), AntCaste.Worker);
            Pan((float2)dark + new float2(4f, 6f), (float2)dark + new float2(-8f, 6f), 4, 6f);
            yield return Clip("v_glow", 6f);

            // Fire ants at work by the lava.
            int magma = math.min(2, _show.Outposts.Length - 1);
            var hot = _show.Outposts[magma];
            for (int i = 0; i < 8; i++) _sim.Ants.Spawn(hot + new int2(i * 2 - 8, 0), (uint)(760 + i), AntCaste.Fire);
            Cam((float2)hot + new float2(0f, 6f), 4);
            yield return Seconds(2f);
            yield return Clip("v_fire", 5f);

            // The Elder Tree, from its roots up to the crown.
            if (RevealElderTree(out var site))
            {
                var world = _sim.World;
                for (int i = 0; i < 10; i++)
                {
                    var c = site.Center + new int2(-30 + i * 6, 0);
                    if (world.InBounds(c.x, c.y) && world[c.x, c.y].IsOpen) _sim.SpawnAntsAt(world.FloorBelow(c), 1, AntCaste.Worker);
                }
                var center = (float2)site.Center + new float2(0.5f, 4f);
                Pan(center + new float2(0f, -site.Half.y * 0.6f), center + new float2(0f, site.Half.y * 0.6f), 3, 7f);
                yield return Seconds(0.5f);
                yield return Clip("v_elder_tree", 7f);
            }
        }

        /// <summary>The Old Colony's Ancient Engine waking (its own shot: it needs every building unlocked).</summary>
        IEnumerator ShotShortsEngine()
        {
            LoadShowcase();
            Vertical();
            int ruins = math.min(3, _show.Outposts.Length - 1);
            var dark = _show.Outposts[ruins];
            for (int i = 0; i < _sim.Colony.BuildingUnlocked.Length; i++) _sim.Colony.BuildingUnlocked[i] = true;
            _sim.GiveResources(40, 300f);
            int2 engine = FlatSpot(dark, 4, 3);
            _sim.EnqueueBuild(BuildingIndex("ancient_engine"), engine, default);
            yield return Seconds(0.3f);
            _sim.Buildings.CompleteAllBlueprints();
            for (int i = 0; i < 10; i++) _sim.Ants.Spawn(engine + new int2(i - 3, 0), (uint)(740 + i), AntCaste.Glow);
            Cam((float2)engine + new float2(2f, 2f), 6);
            yield return Seconds(1f);
            float t0 = _shotTime, done = -1f;
            float[] steps = { 1.0f, 2.8f, 4.6f };
            int next = 0;
            _shotTick = (t, _) =>
            {
                if (next < steps.Length && t - t0 >= steps[next])
                {
                    _sim.GiveResources(40, 300f);
                    _sim.EnqueueStage(engine);
                    done = t + 0.3f;
                    next++;
                }
                if (done > 0f && t >= done)
                {
                    _sim.Buildings.CompleteAllBlueprints();
                    done = -1f;
                }
            };
            yield return Clip("v_engine", 7.5f);
            _shotTick = null;
        }

        /// <summary>Danger and power: the giant worm, a raid, Seismic Sense, the Nuptial Flight.</summary>
        IEnumerator ShotShortsAction()
        {
            LoadShowcase();
            Vertical();
            var q = Queen;
            SetClock(NextDay + 0.45);

            if (_sim.RandomEvents.TriggerKind(RandomEventKind.GiantWorm))
            {
                _camPath = _ => new CamPose(_sim.Enemies.WormIndex() >= 0 ? _sim.Enemies.Enemies[_sim.Enemies.WormIndex()].Pos : q, 4);
                yield return Seconds(4f);
                yield return Clip("v_worm", 7f);
            }

            // Seismic Sense lighting the ore up through the dark rock.
            var target = q + new float2(30f, -40f);
            Cam(target + new float2(0f, 4f), 3);
            yield return Seconds(0.5f);
            if (_content.TryGetAbilityIndex("seismic_sense", out int seismic)) _sim.EnqueueAbility(seismic, (int2)target);
            float pulse = _shotTime;
            _shotTick = (t, _) =>
            {
                if (t - pulse >= 1.8f && _overlayMode != OverlayMode.Ores) SetOverlay(OverlayMode.Ores);
            };
            yield return Clip("v_seismic", 5f);
            _shotTick = null;
            SetOverlay(OverlayMode.None);
        }

        IEnumerator ShotShortsRaid()
        {
            LoadShowcase();
            Vertical();
            SetClock(NextDay + 0.45);
            _sim.SpawnAntsAt(_sim.Colony.QueenCell, 18, AntCaste.Soldier);
            _sim.EnqueueTriggerRaid();
            _sim.FastForward(0.2f);
            for (int i = 0; i < 120 && RaidCenter(out var c) && math.abs(c.x - Queen.x) > 52f; i++) _sim.FastForward(0.25f);
            float2 cam = RaidCenter(out var start) ? start : Queen;
            _camPath = t =>
            {
                if (RaidCenter(out var target)) cam = math.lerp(cam, target + new float2(0f, -1f), 0.03f);
                return new CamPose(cam, 6); // close: termites and soldiers read on a phone
            };
            yield return Seconds(0.5f);
            yield return Clip("v_raid", 11f);
        }

        IEnumerator ShotShortsFlight()
        {
            LoadShowcase();
            Vertical();
            Hud(true);
            _sim.DebugGivePrestige(3, 0);
            Cam(Queen + new float2(0f, 8f), 3);
            yield return Seconds(0.5f);
            ToggleFlight();
            yield return Seconds(0.5f);
            _camPath = null; // the flight moves the camera itself
            Hud(false);
            BeginFlight(Array.Empty<int>());
            yield return Clip("v_flight", config.prestige.flightSeconds + 0.2f);
        }
    }
}
