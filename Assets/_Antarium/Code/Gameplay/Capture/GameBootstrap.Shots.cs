using System;
using System.Collections;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The scripted shots of the store media capture. Stills go to <c>Captures/stills</c> (Steam screenshots, HUD on),
    /// clips to <c>Captures/clips/&lt;name&gt;</c> (1080p, 60 fps; the trailer, and the GIFs are cut from them at
    /// half size, so clips that feed a GIF use an even zoom). Shot names are the capture filter
    /// (<c>CaptureMenu.Run("stills,raid")</c>).
    /// </summary>
    public sealed partial class GameBootstrap
    {
        IEnumerable<(string Name, Func<IEnumerator> Run)> Shots()
        {
            yield return ("stills", ShotStills);
            yield return ("descent", ShotDescent);
            yield return ("dig", ShotDig);
            yield return ("factory", ShotFactory);
            yield return ("deep", ShotDeep);
            yield return ("raid", ShotRaid);
            yield return ("possess", ShotPossess);
            yield return ("abilities", ShotAbilities);
            yield return ("panels", ShotPanels);
            yield return ("flight", ShotFlight);
            yield return ("gifs", ShotGifs);
            yield return ("survey", ShotSurvey);
            yield return ("art", ShotArt);
        }

        // ---------------------------------------------------------------- stills

        IEnumerator ShotStills()
        {
            LoadShowcase();
            Hud(true);
            var q = Queen;

            Cam(q + new float2(18f, 4f), 3);
            yield return Seconds(2f);
            yield return Still("01_colony");

            Cam((float2)_show.Factory + new float2(18f, 0f), 4);
            yield return Seconds(1f);
            yield return Still("02_factory");
            ToggleOverlay();
            yield return Seconds(0.5f);
            yield return Still("03_factory_flow_overlay");
            ToggleOverlay();

            Cam((float2)_show.OreRoom + new float2(-6f, 4f), 4);
            yield return Seconds(1f);
            yield return Still("04_ore_room");

            Cam(q + new float2(-22f, 2f), 4);
            yield return Seconds(1f);
            yield return Still("05_queen_and_storage");

            string[] biomes = { "06_crystal_caverns", "07_fungal_depths", "08_magma_zone", "09_ancient_ruins" };
            for (int i = 0; i < _show.Outposts.Length; i++)
            {
                Cam(OutpostView(i), 3);
                yield return Seconds(1f);
                yield return Still(biomes[i]);
            }

            Cam(q + new float2(14f, -4f), 2);
            yield return Seconds(1f);
            yield return Still("10_nest_wide");
        }

        float2 OutpostView(int i)
        {
            var o = (float2)_show.Outposts[i];
            return new float2(math.lerp(o.x, _show.ShaftX, 0.35f), o.y + 2f);
        }

        // ---------------------------------------------------------------- trailer clips

        /// <summary>From the sky down into the nest, then a dive down the shaft through every biome.</summary>
        IEnumerator ShotDescent()
        {
            LoadShowcase();
            Hud(false);
            var q = Queen;
            var sky = new float2(q.x + 10f, _show.Surface + 40f);
            Pan(sky, q + new float2(10f, -2f), 3, 7f);
            yield return Clip("t01_sky_to_nest", 7f);

            // The dive: zoom 2, straight down the shaft from the nursery to the ruins (~1350 cells in 10 s).
            float2 top = new float2(_show.ShaftX, q.y - 4f), bottom = new float2(_show.ShaftX, _show.Outposts[^1].y + 4f);
            Pan(top, bottom, 2, 10f);
            yield return Clip("t06_dive", 10f);
        }

        /// <summary>The player digging by hand: cursor, debris, combo, screen shake; then ants hauling the drops.</summary>
        IEnumerator ShotDig()
        {
            LoadShowcase();
            Hud(false);
            var q = Queen;
            // Fresh ground left of the nurseries: a stroke from the grass down into the soil, so daylight follows it in.
            int x0 = (int)q.x - 112;
            float surface = _sim.World.SurfaceY[x0];
            float2 a = new float2(x0 + 0.5f, surface - 0.5f), b = new float2(x0 + 20.5f, surface - 15.5f);
            var center = (a + b) * 0.5f + new float2(0f, 2f);
            Cam(center, 4);
            yield return Seconds(0.3f);
            _shotTick = (t, dt) =>
            {
                float k = math.saturate(t / 5.5f);
                var p = math.lerp(a, b, k) + new float2(math.sin(k * 7f) * 2.5f, 0f);
                Pointer(p, true);
                _tools.Dig.Update(_sim, true, false, p * WorldConstants.CellSize, dt, UnityEngine.Time.timeAsDouble);
            };
            yield return Clip("t03_dig", 6f);
            _shotTick = null;
            Pointer(center, false);
            yield return Clip("t03b_dig_haul", 3f);
        }

        IEnumerator ShotFactory()
        {
            LoadShowcase();
            Hud(false);
            var f = (float2)_show.Factory;
            // Along the line: ore room → lift → smelter → archive → princess chamber.
            Pan((float2)_show.OreRoom + new float2(4f, 2f), new float2(_show.Lift.x - 8f, _show.Lift.y + 3f), 4, 5f);
            yield return Clip("t04_ore_room", 5f);
            Pan(f + new float2(32f, 2f), f + new float2(-4f, 2f), 4, 7f);
            yield return Clip("t05_factory", 7f);
            Cam(f + new float2(20f, 0f), 4);
            ToggleOverlay();
            yield return Seconds(0.2f);
            yield return Clip("t05b_flow_overlay", 3f);
            ToggleOverlay();
            // The royal chamber: the queen, eggs and the nursery traffic.
            Cam(Queen + new float2(0f, 1f), 6);
            yield return Clip("t02_queen", 4f);
        }

        IEnumerator ShotDeep()
        {
            LoadShowcase();
            Hud(false);
            string[] names = { "t07_crystal", "t08_fungal", "t09_magma", "t09b_ruins" };
            for (int i = 0; i < _show.Outposts.Length; i++)
            {
                var v = OutpostView(i);
                Pan(v + new float2(-10f, 2f), v + new float2(10f, -1f), 2, 5f, ease: false);
                yield return Clip(names[i], 5f);
            }
        }

        /// <summary>A termite raid meets the soldiers.</summary>
        IEnumerator ShotRaid()
        {
            LoadShowcase();
            Hud(false);
            // Soldiers off duty gather in the royal chamber; the raid is met on the grass above the nest.
            _sim.SpawnAntsAt(_sim.Colony.QueenCell, 18, AntCaste.Soldier);
            _sim.EnqueueTriggerRaid();
            _sim.FastForward(0.2f);
            for (int i = 0; i < 120 && RaidCenter(out var c) && math.abs(c.x - Queen.x) > 52f; i++) _sim.FastForward(0.25f);
            float2 cam = RaidCenter(out var start) ? start : Queen;
            _camPath = t =>
            {
                if (RaidCenter(out var target)) cam = math.lerp(cam, target + new float2(0f, -3f), 0.025f);
                return new CamPose(cam, 4);
            };
            yield return Seconds(0.5f);
            yield return Clip("t10_raid", 11f);
            Hud(true);
            yield return Seconds(0.2f);
            yield return Still("11_raid");
        }

        bool RaidCenter(out float2 center)
        {
            center = default;
            int n = 0;
            foreach (var e in _sim.Enemies.Enemies)
            {
                if (e.IsDead || e.Raid == 0) continue;
                center += e.Pos;
                n++;
            }
            if (n == 0) return false;
            center /= n;
            return true;
        }

        /// <summary>The player takes control of an ant and tunnels through the dark with it.</summary>
        IEnumerator ShotPossess()
        {
            LoadShowcase();
            Hud(false);
            // A soldier in the nursery room, then left into the rock (and a sand pocket that caves in).
            var q = Queen;
            var spot = q + new float2(-60f, -5f);
            _sim.SpawnAntsAt((int2)spot, 1, AntCaste.Soldier); // a soldier lives through the sand cave-in ahead
            _sim.EnqueuePossess(spot + new float2(0f, 0f));
            _sim.FastForward(0.1f);
            float2 cam = spot;
            float bite = 0f;
            _shotTick = (t, dt) =>
            {
                int i = _sim.PossessedAnt;
                if (i < 0) return;
                var pos = _sim.Ants.Ants[i].Pos;
                // Walk left along the floor and bite the rock ahead (slightly down-left, so the tunnel slopes).
                _sim.PossessInput = new int2(-1, 0);
                bite -= dt;
                if (bite <= 0f)
                {
                    bite += 1f / 6f;
                    _sim.EnqueuePossessAct(pos + new float2(-2f, -0.3f), _tools.Dig.ComboMultiplier);
                }
            };
            _camPath = t =>
            {
                int i = _sim.PossessedAnt;
                if (i >= 0) cam = math.lerp(cam, _sim.Ants.Ants[i].Pos, 0.08f);
                return new CamPose(cam, 6);
            };
            yield return Seconds(0.3f);
            yield return Clip("t11_possess", 7f);
            Hud(true);
            yield return Still("12_possession");
            _sim.EnqueueRelease();
        }

        /// <summary>Seismic Sense lighting up ore through the fog, and a Pheromone Burst rallying the colony.</summary>
        IEnumerator ShotAbilities()
        {
            LoadShowcase();
            Hud(false);
            var q = Queen;
            var target = q + new float2(30f, -30f);
            Cam(target + new float2(0f, 8f), 2);
            yield return Seconds(0.5f);
            if (_content.TryGetAbilityIndex("seismic_sense", out int seismic))
                _sim.EnqueueAbility(seismic, (int2)target);
            yield return Clip("t12_seismic", 4f);

            var rally = q + new float2(52f, 6f);
            Cam(rally + new float2(0f, 2f), 3);
            yield return Seconds(0.3f);
            if (_content.TryGetAbilityIndex("pheromone_burst", out int burst))
                _sim.EnqueueAbility(burst, (int2)rally);
            yield return Clip("t13_rally", 5f);
        }

        /// <summary>The game's screens over the running colony: research, castes, contracts.</summary>
        IEnumerator ShotPanels()
        {
            LoadShowcase();
            Hud(true);
            Cam(Queen + new float2(20f, 2f), 3);
            yield return Seconds(1f);
            _hud.ShowModal(_hud.Research.Root);
            yield return Seconds(0.5f);
            yield return Still("13_research");
            yield return Clip("t14_research", 3f);
            _hud.HideModal();
            _hud.Castes?.Toggle();
            yield return Seconds(0.5f);
            yield return Still("14_castes");
            _hud.Castes?.Toggle();
        }

        /// <summary>The Nuptial Flight: princesses leave the nest, the next colony begins, the genetic tree opens.</summary>
        IEnumerator ShotFlight()
        {
            LoadShowcase();
            Hud(true);
            _sim.DebugGivePrestige(3, 0);
            Cam(Queen + new float2(0f, 8f), 3);
            yield return Seconds(0.5f);
            ToggleFlight();
            yield return Seconds(0.5f);
            yield return Still("15_flight_screen");
            _camPath = null; // the flight moves the camera itself
            Hud(false);
            BeginFlight(Array.Empty<int>());
            yield return Clip("t15_flight", config.prestige.flightSeconds + 0.2f);
            Hud(true);
            yield return Seconds(1f);
            yield return Still("16_genetic_tree");
            yield return Clip("t16_genetics", 3f);
        }

        /// <summary>
        /// Footage for the store page GIFs: a still camera (GIFs only store what changes between frames, so a moving
        /// camera makes them several times bigger), zoom 4 so the halved GIF keeps 2 × 2 art pixels.
        /// </summary>
        IEnumerator ShotGifs()
        {
            LoadShowcase();
            Hud(false);
            var q = Queen;
            Cam(q + new float2(-12f, 2f), 4);
            yield return Seconds(0.5f);
            yield return Clip("g_colony", 4.5f);

            Cam(new float2(_show.Smelter.x + 2f, _show.Smelter.y + 1f), 4);
            yield return Seconds(0.5f);
            yield return Clip("g_factory", 4.5f);

            Cam(OutpostView(0) + new float2(8f, 0f), 4);
            yield return Seconds(0.5f);
            yield return Clip("g_crystal", 3f);
            Cam(OutpostView(2) + new float2(10f, 0f), 4);
            yield return Seconds(0.5f);
            yield return Clip("g_magma", 3f);
        }

        // ---------------------------------------------------------------- planning aid

        /// <summary>Not media: stills of every place in the showcase colony, to plan the other shots.</summary>
        IEnumerator ShotSurvey()
        {
            LoadShowcase();
            Hud(false);
            var q = Queen;
            var views = new List<(string, float2, int)>
            {
                ("nest_z2", q + new float2(10f, 0f), 2),
                ("factory_z3", (float2)_show.Factory + new float2(10f, 0f), 3),
                ("ore_z3", (float2)_show.OreRoom + new float2(-5f, 3f), 3),
                ("storage_z4", (float2)_show.StorageHall, 4),
                ("nursery_z4", (float2)_show.Nursery, 4),
            };
            for (int i = 0; i < _show.Outposts.Length; i++)
                views.Add(($"outpost{i}_z2", OutpostView(i), 2));
            foreach (var (name, cell, zoom) in views)
            {
                Cam(cell, zoom);
                yield return Seconds(0.5f);
                yield return Still("survey_" + name);
            }
        }

        /// <summary>
        /// Not media: stills for reviewing the art (10.7a), HUD on. Few frames between them, so the run stays short
        /// when frames are stepped by hand (<c>CaptureMenu.Run("art", false)</c>).
        /// </summary>
        IEnumerator ShotArt()
        {
            LoadShowcase();
            Hud(true);
            var q = Queen;
            var views = new List<(string, float2, int)>
            {
                ("queen_z6", q + new float2(2f, 1f), 6),
                ("colony_z4", q + new float2(-10f, 2f), 4),
                ("smelter_z5", new float2(_show.Smelter.x + 1f, _show.Smelter.y + 2f), 5),
                ("factory_z3", (float2)_show.Factory + new float2(18f, 0f), 3),
                ("stone_z4", new float2(_show.ShaftX + 8f, _show.Surface - 380f), 4),
                ("nest_z2", q + new float2(14f, -4f), 2),
            };
            yield return Frames(30);
            foreach (var (name, cell, zoom) in views)
            {
                // Deep rock is dark until something lights it: raise the ambient light to review its texture.
                if (globalLight != null) globalLight.intensity = name.StartsWith("stone") ? 0.8f : config.render.ambient;
                Cam(cell, zoom);
                yield return Frames(6);
                yield return Still("art_" + name);
            }
            if (globalLight != null) globalLight.intensity = config.render.ambient;
        }
    }
}
