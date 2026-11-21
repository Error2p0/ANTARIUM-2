using System;
using System.IO;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>Milestone 11 tweaks: starfall nights, thunderstorms, scents kept out of walls, stopping the queen laying.</summary>
    public class Milestone11Tests : TunnelFixture
    {
        SurfaceConfig Cfg => C.Config.surface;

        int CountEvents(SurfaceEventKind kind)
        {
            int n = 0;
            foreach (var e in Sim.Events.Read<SurfaceEvent>()) if (e.Kind == kind) n++;
            return n;
        }

        int LeavesOnSurface()
        {
            int n = 0;
            ushort leaf = C.Item(C.Leaf);
            for (int i = 0; i < Sim.Colony.Piles.Count; i++)
            {
                var p = Sim.Colony.Piles[i];
                if (p.Item == leaf && p.Cell.y >= Sim.World.SurfaceY[p.Cell.x] - 1) n += p.Count;
            }
            return n;
        }

        void NightHuntersEveryNight()
        {
            Cfg.firstSpiderSeconds = 0f;
            Cfg.spiderMinAnts = 0;
            Cfg.spidersBase = 2f;
            Cfg.spidersPerAnt = 0f;
            Cfg.spiderSpawnWindow = 1f;
        }

        // ---------------------------------------------------------------- starfall

        [Test]
        public void Starfall_ComesAtDusk_KeepsHuntersAway_AndLetsLeavesFall_UntilDawn()
        {
            NightHuntersEveryNight();
            Cfg.starfallChance = 1f;
            Cfg.firstStarfallSeconds = 0f;
            Cfg.leafInterval = 0.5f;
            Cfg.maxSurfaceLeaves = 50;
            var s = Sim.Surface;
            Sim.SkipToNight();
            Sim.Tick();
            Assert.IsFalse(s.IsDay);
            Assert.IsTrue(s.Starfall);
            Assert.IsTrue(s.ForageGrows);
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.StarfallStarted));
            Assert.AreEqual(0, CountEvents(SurfaceEventKind.SpidersOut), "no hunters on a starfall night");

            Run(6f);
            Assert.AreEqual(0, Sim.Enemies.Count, "no hunters came");
            Assert.Greater(LeavesOnSurface(), 3, "leaves keep falling through a starfall night");

            s.Clock = Math.Floor(s.Clock) + 1.4; // next morning
            Sim.Tick();
            Assert.IsTrue(s.IsDay);
            Assert.IsFalse(s.Starfall);
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.StarfallEnded));
        }

        [Test]
        public void Starfall_NeverOnARainyNight_NorBeforeItsTime()
        {
            Cfg.starfallChance = 1f;
            Cfg.firstStarfallSeconds = 0f;
            Sim.StartWeather(WeatherKind.Rain);
            Sim.SkipToNight();
            Sim.Tick();
            Assert.IsFalse(Sim.Surface.Starfall, "rain hides the stars");

            Sim.StartWeather(WeatherKind.Clear);
            Sim.Surface.Clock = Math.Floor(Sim.Surface.Clock) + 1.4;
            Sim.Tick();
            Cfg.firstStarfallSeconds = 1e9f;
            Sim.SkipToNight();
            Sim.Tick();
            Assert.IsFalse(Sim.Surface.Starfall, "not this early in a colony");
        }

        [Test]
        public void TriggeredStarfall_SendsHuntersOff_AndHoldsTheWeather()
        {
            NightHuntersEveryNight();
            Assert.IsFalse(Sim.TriggerStarfall(), "only at night");
            Sim.SkipToNight();
            Sim.Tick();
            Run(3f);
            Assert.Greater(Sim.Enemies.Count, 0, "hunters are out");
            Sim.StartWeather(WeatherKind.Rain);

            Assert.IsTrue(Sim.TriggerStarfall());
            Assert.AreEqual(0, Sim.Enemies.Count, "the hunters left");
            Assert.AreEqual(WeatherKind.Clear, Sim.Surface.Weather, "the sky clears for it");
            Run(2f);
            Assert.AreEqual(0, Sim.Enemies.Count, "and stay away");
        }

        // ---------------------------------------------------------------- thunderstorms

        [Test]
        public void Thunderstorm_RainsHarder_AndLightningStrikesTheGround()
        {
            Cfg.lightningInterval = new UnityEngine.Vector2(0.5f, 1f);
            Sim.StartThunderstorm();
            var s = Sim.Surface;
            Assert.AreEqual(WeatherKind.Rain, s.Weather);
            Assert.IsTrue(s.Thunderstorm);
            Sim.Tick();
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.RainStarted));
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.ThunderstormStarted));

            int strikes = 0;
            for (int i = 0; i < (int)(6f / Sim.TickDuration); i++)
            {
                Sim.Tick();
                foreach (var e in Sim.Events.Read<SurfaceEvent>())
                {
                    if (e.Kind != SurfaceEventKind.Lightning) continue;
                    strikes++;
                    Assert.IsTrue(Sim.World[e.Cell.x, e.Cell.y].IsOpen, "strikes an open cell");
                    Assert.IsFalse(Sim.World[e.Cell.x, e.Cell.y - 1].IsOpen, "on the ground");
                }
                Sim.Events.Flush();
            }
            Assert.GreaterOrEqual(strikes, 3);
            Assert.AreEqual(strikes, s.LightningStrikes);

            Sim.StartWeather(WeatherKind.Clear);
            Assert.IsFalse(s.Thunderstorm);
            int before = s.LightningStrikes;
            Run(3f);
            Assert.AreEqual(before, s.LightningStrikes, "no lightning without the storm");
        }

        [Test]
        public void Rain_IsAThunderstormByChance()
        {
            Cfg.thunderstormChance = 0f;
            Sim.StartWeather(WeatherKind.Rain);
            Assert.IsFalse(Sim.Surface.Thunderstorm);
            Cfg.thunderstormChance = 1f;
            Sim.StartWeather(WeatherKind.Rain);
            Assert.IsTrue(Sim.Surface.Thunderstorm);
            Sim.StartWeather(WeatherKind.Drought);
            Assert.IsFalse(Sim.Surface.Thunderstorm, "droughts have no storms");
        }

        // ---------------------------------------------------------------- scents

        [Test]
        public void Trail_GatherAndDefend_StayOutOfWalls_DigAndAvoidDoNot()
        {
            CarveTunnel(6);
            int2 at = Queen + new int2(8, 0);
            foreach (var kind in new[] { PheromoneKind.Trail, PheromoneKind.Gather, PheromoneKind.Defend, PheromoneKind.Dig, PheromoneKind.Avoid })
            {
                Paint(kind, at, 3f);
                int open = 0, solid = 0;
                for (int y = at.y - 3; y <= at.y + 3; y++)
                for (int x = at.x - 3; x <= at.x + 3; x++)
                {
                    if (Sim.Pheromones.Get(kind, new int2(x, y)) == 0) continue;
                    if (Sim.World[x, y].IsOpen) open++;
                    else solid++;
                }
                Assert.Greater(open, 0, kind + " on the tunnel");
                if (Pheromones.OpenCellsOnly(kind)) Assert.AreEqual(0, solid, kind + " not in the walls");
                else Assert.Greater(solid, 0, kind + " also on solid ground");
            }
        }

        // ---------------------------------------------------------------- laying

        [Test]
        public void Laying_CanBeStopped_AndResumed()
        {
            var c = C.Config.colony;
            c.eggInterval = 0.5f;
            c.maxEggs = 10;
            Sim.EnqueueLaying(false);
            Run(3f);
            Assert.IsTrue(Sim.Colony.LayingPaused);
            Assert.AreEqual(0, Sim.Colony.Eggs.Count, "she rests");

            Sim.EnqueueLaying(true);
            Run(1.2f);
            Assert.IsFalse(Sim.Colony.LayingPaused);
            Assert.Greater(Sim.Colony.Eggs.Count, 0, "she lays again");
        }

        [Test]
        public void Laying_StoppedStaysStoppedOffline()
        {
            C.Config.colony.eggInterval = 1f;
            Sim.EnqueueLaying(false);
            Sim.Tick();
            int ants = Sim.Ants.Count;
            OfflineProgress.Apply(Sim, 1800);
            Assert.AreEqual(ants, Sim.Ants.Count, "no eggs while the game was closed");
        }

        // ---------------------------------------------------------------- saving

        [Test]
        public void Save_KeepsTheStormTheStarfallAndTheRestingQueen()
        {
            Sim.StartThunderstorm();
            Sim.EnqueueLaying(false);
            Run(1f);
            using (var ms = new MemoryStream())
            {
                SaveGame.Save(Sim, ms, DateTime.UtcNow);
                ms.Position = 0;
                using var loaded = SaveGame.Load(ms, C.DB, C.Config, out _);
                Assert.AreEqual(WeatherKind.Rain, loaded.Surface.Weather);
                Assert.IsTrue(loaded.Surface.Thunderstorm);
                Assert.IsTrue(loaded.Colony.LayingPaused);
                Assert.IsFalse(loaded.Surface.Starfall);
            }

            Sim.StartWeather(WeatherKind.Clear);
            Sim.SkipToNight();
            Sim.Tick();
            Assert.IsTrue(Sim.TriggerStarfall());
            using (var ms = new MemoryStream())
            {
                SaveGame.Save(Sim, ms, DateTime.UtcNow);
                ms.Position = 0;
                using var loaded = SaveGame.Load(ms, C.DB, C.Config, out _);
                Assert.IsTrue(loaded.Surface.Starfall);
                Assert.IsFalse(loaded.Surface.Thunderstorm);
            }
        }
    }
}
