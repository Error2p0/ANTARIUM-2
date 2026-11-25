using System;
using System.Collections;
using Antarium.Content;
using Antarium.Sim;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Store media for Milestones 10 and 11 and the story (all on the showcase colony): the surface by day, at dusk, on a
    /// starfall night and in a thunderstorm; the Elder Tree, glow ants in the ruins, the Ancient Engine waking and the Codex;
    /// the giant worm, fire ants, a water wheel, the ore overlay and the Queen panel; and the opening intro. Clips feed the
    /// trailer and the GIFs (<c>tools/media</c>), stills are Steam screenshots (numbering in <c>GameBootstrap.Shots.cs</c>).
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>The surface clock to <paramref name="days"/> (whole days count up, so night hunters and the weather follow).</summary>
        void SetClock(double days) => _sim.Surface.Clock = days;

        double NextDay => Math.Floor(_sim.Surface.Clock) + 1.0;

        float GroundAt(float x) => _sim.World.SurfaceY[math.clamp((int)x, 0, _sim.World.Width - 1)] + 1f;

        IEnumerator ShotSurface()
        {
            LoadShowcase();
            var q = Queen;
            _sim.StartWeather(WeatherKind.Clear);

            // 01: late afternoon over the hills, trees and mountains, the nest underneath (HUD on).
            SetClock(NextDay + 0.70);
            Hud(true);
            Cam(new float2(q.x - 8f, GroundAt(q.x) - 8f), 2);
            yield return Seconds(1.5f);
            yield return Still("01_surface_and_nest");

            // Sunset over the mountains (before the storm: its clouds linger).
            SetClock(NextDay + 0.835);
            Cam(new float2(q.x + 30f, GroundAt(q.x + 30f) + 10f), 3);
            yield return Seconds(1f);
            yield return Still("25_sunset");

            // Late afternoon to night in seven seconds, the camera drifting along the hills.
            Hud(false);
            double from = NextDay + 0.66, span = 0.37;
            float start = _shotTime;
            SetClock(from);
            _shotTick = (t, _) => SetClock(from + span * math.saturate((t - start) / 7f));
            Pan(new float2(q.x - 10f, GroundAt(q.x) + 9f), new float2(q.x + 14f, GroundAt(q.x) + 9f), 3, 7f);
            yield return Clip("s01_dusk", 7f);
            _shotTick = null;

            // A starfall night (zoom 2, still camera: also a GIF), then its still with the card.
            _sim.SkipToNight();
            yield return Seconds(0.2f);
            _sim.TriggerStarfall();
            Cam(new float2(q.x + 4f, GroundAt(q.x) + 16f), 2);
            yield return Seconds(1.5f);
            yield return Clip("s02_starfall", 6f);
            Hud(true);
            Cam(new float2(q.x + 4f, GroundAt(q.x) + 10f), 3);
            yield return Seconds(1f);
            yield return Still("07_starfall_night");

            // A thunderstorm by day. Lightning comes more often than in play and strikes near the queen (the rain's
            // reach is narrowed for the shot), so the bolts land in view.
            Hud(false);
            SetClock(NextDay + 0.42);
            yield return Seconds(0.2f);
            var interval = config.surface.lightningInterval;
            int reach = config.surface.rainRange;
            config.surface.lightningInterval = new UnityEngine.Vector2(0.7f, 1.5f);
            config.surface.rainRange = 40;
            try
            {
                _sim.StartThunderstorm();
                Cam(new float2(q.x, GroundAt(q.x) + 12f), 2);
                yield return Seconds(3f); // the rain builds up
                yield return Clip("s03_storm", 6f);
                Hud(true);
                config.surface.rainRange = 28;
                Cam(new float2(q.x, GroundAt(q.x) + 8f), 3);
                yield return Seconds(0.5f);
                for (int i = 0; i < 400 && (_surfaceView == null || _surfaceView.Flash < 0.9f); i++) yield return null;
                yield return Still("13_thunderstorm");
            }
            finally
            {
                config.surface.lightningInterval = interval;
                config.surface.rainRange = reach;
            }
            _sim.StartWeather(WeatherKind.Clear);
        }

        /// <summary>Reveals the Elder Tree's cavern and tree (as when the colony breaks in); false if this world has none.</summary>
        bool RevealElderTree(out ElderSite site)
        {
            site = _sim.Surface.ElderTree;
            if (!_sim.Surface.HasElderTree) return false;
            var world = _sim.World;
            var starts = new NativeList<int2>(1024, Allocator.TempJob);
            for (int x = site.Center.x - site.Half.x; x <= site.Center.x + site.Half.x; x++)
            for (int y = site.Center.y - site.Half.y; y <= site.Center.y + site.Half.y; y++)
                if (Reveal.IsUnexploredOpen(world, x, y)) starts.Add(new int2(x, y));
            Reveal.Flood(world, starts.AsArray(), config.dig.revealRadius, config.dig.caveRevealLimit);
            starts.Dispose();
            ElderTree.RevealTree(world, _content, site);
            world.MarkAll(ChunkFlags.TerrainUpload);
            return true;
        }

        IEnumerator ShotStory()
        {
            LoadShowcase();
            Hud(false);

            // The Elder Tree: its glowing cavern, with a few workers on the floor.
            if (RevealElderTree(out var site))
            {
                var world = _sim.World;
                for (int i = 0; i < 10; i++)
                {
                    var c = site.Center + new int2(-30 + i * 6, 0);
                    if (world.InBounds(c.x, c.y) && world[c.x, c.y].IsOpen) _sim.SpawnAntsAt(world.FloorBelow(c), 1, AntCaste.Worker);
                }
                var center = (float2)site.Center + new float2(0.5f, 4f);
                Pan(center + new float2(-14f, -2f), center + new float2(14f, 2f), 3, 7f);
                yield return Seconds(0.5f);
                yield return Clip("s04_elder_tree", 7f);
                Hud(true);
                Cam(center, 2);
                yield return Seconds(1f);
                yield return Still("06_elder_tree");
                Hud(false);
            }
            if (DemoPass) yield break; // the ruins, the Engine and the Codex lie beyond the demo's seal

            // Glow ants carrying their light through the dark ruins (zoom 4, a slow pan; the still camera part feeds a GIF).
            int ruins = math.min(3, _show.Outposts.Length - 1);
            var dark = _show.Outposts[ruins];
            for (int i = 0; i < 14; i++) _sim.Ants.Spawn(dark + new int2(i - 7, 0), (uint)(700 + i), AntCaste.Glow);
            for (int i = 0; i < 6; i++) _sim.Ants.Spawn(dark + new int2(i * 3 - 8, 0), (uint)(720 + i), AntCaste.Worker);
            var view = (float2)dark + new float2(0f, 2f);
            Hud(true);
            Cam(view, 4);
            yield return Seconds(1f);
            yield return Still("05_glow_ants_in_the_ruins");
            Hud(false);
            yield return Clip("s05_glow_ruins", 4f);
            Pan(view, view + new float2(-16f, 1f), 4, 5f);
            yield return Clip("s05b_glow_ruins_pan", 5f);

            // The Ancient Engine waking: placed in the ruins outpost, restored step by step, glow ants lighting it.
            for (int i = 0; i < _sim.Colony.BuildingUnlocked.Length; i++) _sim.Colony.BuildingUnlocked[i] = true;
            _sim.GiveResources(40, 300f);
            int2 engine = FlatSpot(dark, 4, 3);
            _sim.EnqueueBuild(BuildingIndex("ancient_engine"), engine, default);
            yield return Seconds(0.3f);
            _sim.Buildings.CompleteAllBlueprints();
            for (int i = 0; i < 8; i++) _sim.Ants.Spawn(engine + new int2(i - 2, 0), (uint)(740 + i), AntCaste.Glow);
            Cam((float2)engine + new float2(2f, 2.5f), 6);
            yield return Seconds(1f);
            float t0 = _shotTime, done = -1f;
            float[] steps = { 0.8f, 2.4f, 4.0f };
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
            yield return Clip("s06_engine", 6.5f);
            _shotTick = null;
            Hud(true);
            // The glow ants have gone to work by now: fresh ones beside the engine light it for the still.
            for (int i = 0; i < 8; i++) _sim.Ants.Spawn(engine + new int2(i < 4 ? i - 5 : i + 1, 0), (uint)(800 + i), AntCaste.Glow);
            Pointer((float2)engine + new float2(2f, 1.5f), true);
            yield return Seconds(0.6f);
            yield return Still("11_ancient_engine");
            Pointer(new float2(-50f, -50f), false);

            // The Codex: the story so far, the Engine's fragment open, the last ones still locked.
            if (_content.TryGetItemIndex("relic", out ushort relic)) _sim.Colony.Stats.Studied[relic] = 9;
            if (_content.TryGetItemIndex("tablet", out ushort tablet)) _sim.Colony.Stats.Studied[tablet] = 1;
            yield return Seconds(0.3f);
            _hud.ShowModal(_hud.Codex.Root);
            _hud.Codex.Select(5);
            yield return Seconds(0.5f);
            yield return Still("21_codex");
            _hud.HideModal();
        }

        IEnumerator ShotLiving()
        {
            LoadShowcase();
            Hud(false);
            var q = Queen;

            // The giant earthworm tunnelling past the nest (the camera follows its head).
            if (_sim.RandomEvents.TriggerKind(RandomEventKind.GiantWorm))
            {
                _camPath = _ => new CamPose(_sim.Enemies.WormIndex() >= 0 ? _sim.Enemies.Enemies[_sim.Enemies.WormIndex()].Pos : q, 4);
                yield return Seconds(4f);
                yield return Clip("s07_worm", 6f);
                Hud(true);
                yield return Seconds(0.2f);
                yield return Still("15_giant_worm");
                Hud(false);
            }

            // Fire ants at work by the lava of the magma zone (not in the demo: both lie beyond its seal).
            if (!DemoPass)
            {
                int magma = math.min(2, _show.Outposts.Length - 1);
                var hot = _show.Outposts[magma];
                for (int i = 0; i < 8; i++) _sim.Ants.Spawn(hot + new int2(i * 2 - 8, 0), (uint)(760 + i), AntCaste.Fire);
                Cam((float2)hot + new float2(0f, 2f), 4);
                yield return Seconds(2f);
                yield return Clip("s08_fire_ants", 5f);
                Hud(true);
                yield return Seconds(0.2f);
                yield return Still("16_fire_ants");
                Hud(false);
            }

            // A water wheel under a stream, in a chamber under the storage hall (glow ants light it).
            yield return Waterworks(q + new float2(-34f, -21f));

            // The ore overlay after a Seismic Sense pulse, with its legend.
            Hud(true);
            var survey = q + new float2(24f, -34f);
            if (_content.TryGetAbilityIndex("seismic_sense", out int seismic)) _sim.EnqueueAbility(seismic, (int2)survey);
            Cam(survey + new float2(0f, 6f), 2);
            yield return Seconds(2f);
            SetOverlay(OverlayMode.Ores);
            yield return Seconds(1.5f);
            yield return Still("18_ore_overlay");
            SetOverlay(OverlayMode.None);

            // The Queen panel with a few evolutions bought.
            Cam(q + new float2(12f, 2f), 3);
            _sim.GiveResources(20, 200f);
            foreach (var id in new[] { "bigger_broods", "royal_cradles", "hardy_brood", "bigger_broods", "quick_mending" })
                if (_content.TryGetEvolutionIndex(id, out int e)) _sim.EnqueueEvolution(e);
            yield return Seconds(0.5f);
            _hud.ShowModal(_hud.Queen.Root);
            yield return Seconds(0.5f);
            yield return Still("19_queen_panel");
            _hud.HideModal();
        }

        /// <summary>
        /// Carves a chamber with a pool at <paramref name="at"/>, hangs a water wheel in it and pours a stream onto it; records
        /// <paramref name="clip"/> and (unless null) the still <paramref name="still"/>.
        /// </summary>
        IEnumerator Waterworks(float2 at, string clip = "s09_waterworks", string still = "17_waterworks", int zoom = 6)
        {
            var c = (int2)math.floor(at);
            _sim.CarveRectNow(c + new int2(-9, -4), c + new int2(8, 7));
            _sim.CarveEllipseNow((float2)c + new float2(0.5f, -4f), new float2(8f, 3.5f));
            _sim.ClearFluidNow(c + new int2(-10, -9), c + new int2(9, 8));
            _sim.RevealNow(c, 14);
            _sim.ClearPilesNow();
            _sim.World.MarkAll(ChunkFlags.TerrainUpload);
            _sim.GiveResources(40, 300f);
            var wheel = c + new int2(-1, 1);
            _sim.EnqueueBuild(BuildingIndex("water_wheel"), wheel, default);
            yield return Seconds(0.2f);
            _sim.Buildings.CompleteAllBlueprints();
            for (int i = 0; i < 5; i++) _sim.Ants.Spawn(c + new int2(i * 3 - 7, -3), (uint)(780 + i), AntCaste.Glow);
            int top = c.y + 7;
            _shotTick = (_, _) =>
            {
                _sim.Fluids.AddWater(new int2(wheel.x, top), 9);
                _sim.Fluids.AddWater(new int2(wheel.x + 1, top), 9);
            };
            Cam((float2)wheel + new float2(1f, 1f), zoom);
            yield return Seconds(2.5f);
            yield return Clip(clip, 5f);
            if (still == null)
            {
                _shotTick = null;
                yield break;
            }
            Hud(true);
            Pointer((float2)wheel + 1f, true);
            yield return Seconds(0.3f);
            yield return Still(still);
            Pointer(new float2(-50f, -50f), false);
            _shotTick = null;
            Hud(false);
        }

        /// <summary>The opening intro over the title screen: a clip, and stills of the studio logo, the game logo and the title.</summary>
        IEnumerator ShotIntro()
        {
            LoadShowcase();
            Hud(true);
            ShowTitle();
            Cam(Queen + new float2(0f, 6f), 3);
            yield return Seconds(0.5f);
            _hud.Intro.Play();
            _rig.BeginClip(System.IO.Path.Combine(_captureRequest.OutputDirectory, "clips", "intro"));
            int frames = (int)math.round(8.2f * _fps);
            for (int i = 0; i < frames; i++)
            {
                if (i == (int)(1.8f * _fps)) _stillPath = StillPath("intro_studio");
                if (i == (int)(5.6f * _fps)) _stillPath = StillPath("intro_logo");
                yield return null;
            }
            _rig.EndClip();
            yield return Still("intro_title");
            _hud.SetTitle(false);
        }

        string StillPath(string name) => System.IO.Path.Combine(_captureRequest.OutputDirectory, "stills", name + ".png");
    }
}
