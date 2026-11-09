using System;
using System.IO;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>The surface (10.2): day and night, weather, leaves, honeydew, the golden aphid, roots, birds and night spiders.</summary>
    public class SurfaceTests : SimulationFixture
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

        /// <summary>The open cell on the ground surface of column <paramref name="x"/>.</summary>
        int2 Ground(int x) => new(x, Sim.World.SurfaceY[x] + 1);

        [Test]
        public void DayAndNight_CycleOverTheConfiguredLength_AndLeavesFallOnlyByDay()
        {
            var s = Sim.Surface;
            Assert.IsTrue(s.IsDay, "a colony starts in the morning");
            Assert.AreEqual(1f, s.Daylight, 1e-3f);
            Assert.AreEqual(0f, SurfaceState.DaylightAt(0f, Cfg), 1e-3f, "midnight");
            Assert.AreEqual(1f, SurfaceState.DaylightAt(0.5f, Cfg), 1e-3f, "noon");
            Assert.Greater(Sim.Surface.Plants.Count, 2, "plants grow along the surface");

            double before = s.Clock;
            Run(Cfg.dayLengthSeconds / 10f);
            Assert.AreEqual(before + 0.1, s.Clock, 1e-3, "a tenth of a day");

            Cfg.leafInterval = 0.5f;
            Cfg.maxSurfaceLeaves = 50;
            Sim.SkipToNight();
            Sim.Tick();
            Assert.IsFalse(s.IsDay);
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.Dusk));
            Run(6f);
            Assert.AreEqual(0, LeavesOnSurface(), "no leaves fall at night");

            s.Clock = Math.Floor(s.Clock) + 1.4; // next morning
            Sim.Tick();
            Assert.IsTrue(s.IsDay);
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.Dawn));
            Run(6f);
            Assert.Greater(LeavesOnSurface(), 3, "leaves fall under the plants by day");
        }

        [Test]
        public void LightMap_KeepsTheSkyApart_SoNightNeedsNoRelight()
        {
            int x = Sim.World.Width / 4;
            int sky = Sim.World.Height - 2;
            Assert.AreEqual(1f, Sim.Light.SkyAt(Sim.World, x, sky), 1e-3f);
            Assert.AreEqual(1f, Sim.Light.Sample(Sim.World, x, sky).x, 1e-3f, "full daylight");
            Assert.AreEqual(0.3f, Sim.Light.Sample(Sim.World, x, sky, 0.3f).x, 1e-2f, "the sky scales with the daylight");
            Assert.AreEqual(0f, Sim.Light.SkyAt(Sim.World, x, 10), 1e-3f, "deep rock gets no sky");
        }

        [Test]
        public void Rain_PutsWaterNearTheSurface_ThatSoaksAwayAfterwards()
        {
            Sim.StartWeather(WeatherKind.Rain);
            Sim.Tick();
            Assert.AreEqual(WeatherKind.Rain, Sim.Surface.Weather);
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.RainStarted));
            Run(20f);
            long peak = Sim.Fluids.Total(FluidKind.Water);
            Assert.Greater(peak, 2000, "rain fell");

            Sim.StartWeather(WeatherKind.Clear);
            Sim.Tick();
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.RainEnded));
            Run(240f);
            Assert.Less(Sim.Fluids.Total(FluidKind.Water), peak / 10, "near the surface the water soaks into the ground");
        }

        [Test]
        public void Drought_EndsByItself()
        {
            Sim.StartWeather(WeatherKind.Drought);
            Sim.Tick();
            Assert.AreEqual(WeatherKind.Drought, Sim.Surface.Weather);
            Run(Cfg.droughtSeconds.y + 1f);
            Assert.AreEqual(WeatherKind.Clear, Sim.Surface.Weather);
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.DroughtEnded));
        }

        [Test]
        public void Bird_ScaresAntsUnderground_AndTakesThoseStillInTheOpen()
        {
            int nest = Queen.x;
            var far = Ground(nest + 40);
            var near = Ground(nest + 4);
            Sim.Ants.Spawn(far, 3);
            Sim.Ants.Spawn(near, 4);
            int farAnt = Sim.Ants.Count - 2, nearAnt = Sim.Ants.Count - 1;

            Assert.IsTrue(Sim.TriggerBird());
            Assert.AreEqual(BirdPhase.Circling, Sim.Surface.Bird);
            Assert.AreEqual(AntState.Sheltering, Sim.Ants.Ants[farAnt].State);
            Assert.AreEqual(AntState.Sheltering, Sim.Ants.Ants[nearAnt].State);
            Assert.Less(math.distance(Sim.Surface.BirdPos, new float2(far) + 0.5f), 2f, "it circles over the ant farthest out");

            Run(Cfg.birdWarningSeconds + 0.5f);
            int died = 0;
            foreach (var e in Sim.Events.Read<AntDiedEvent>()) if (e.Cause == DeathCause.Bird) died++;
            Assert.AreEqual(1, died, "the far forager could not get under ground in time");
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.BirdStruck));
            Assert.AreEqual(BirdPhase.Leaving, Sim.Surface.Bird);

            // The near one is safe underground and comes out once the bird is gone.
            bool safe = false;
            foreach (var a in Sim.Ants.Ants) safe |= a.State == AntState.Sheltering && !Sim.IsExposed(a.Cell);
            Assert.IsTrue(safe, "the near ant reached cover");
            Run(Cfg.birdShelterSeconds + 0.5f);
            Assert.AreEqual(BirdPhase.None, Sim.Surface.Bird);
            foreach (var a in Sim.Ants.Ants) Assert.AreNotEqual(AntState.Sheltering, a.State);
        }

        [Test]
        public void Bird_DoesNotComeWhenNobodyIsOutside()
        {
            foreach (var a in Sim.Ants.Ants) Assume.That(!Sim.IsExposed(a.Cell));
            Assert.IsFalse(Sim.TriggerBird());
            Assert.AreEqual(BirdPhase.None, Sim.Surface.Bird);
        }

        [Test]
        public void NightSpiders_ComeOutAtNight_AndSlinkOffAtDawn()
        {
            Cfg.firstSpiderSeconds = 0f;
            Cfg.spiderMinAnts = 0;
            Cfg.spidersBase = 2f;
            Cfg.spidersPerAnt = 0f;
            Cfg.spiderSpawnWindow = 1f;
            Sim.SkipToNight();
            Sim.Tick();
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.SpidersOut));
            Run(3f);
            int spider = C.DB.TryGetEnemyIndex(C.WolfSpider.Id, out int i) ? i : -1;
            int count = 0;
            foreach (var e in Sim.Enemies.Enemies)
            {
                if (e.Def != spider) continue;
                count++;
                Assert.Less(Sim.World.Depth(e.Cell.x, e.Cell.y), Simulation.ShelterDepth, "on the surface");
            }
            Assert.AreEqual(2, count);

            Sim.Surface.Clock = Math.Floor(Sim.Surface.Clock) + 1.4;
            Sim.Tick();
            Assert.AreEqual(1, CountEvents(SurfaceEventKind.SpidersLeft));
            Assert.AreEqual(0, Sim.Enemies.Count);
        }

        [Test]
        public void Honeydew_TheQueenEatsIt_AndLaysFaster()
        {
            float plain = Sim.Colony.EggInterval;
            ushort honeydew = C.Item(C.Honeydew);
            Inv.Add(honeydew, 2, Queen);
            Sim.Tick();
            Assert.IsTrue(Sim.Colony.QueenFed);
            Assert.AreEqual(1, Inv.Items[honeydew], "she ate one");
            Assert.AreEqual(plain / (1f + Cfg.honeydewLaying), Sim.Colony.EggInterval, 1e-3f);
            Run(Cfg.honeydewSeconds + 0.5f);
            Assert.AreEqual(0, Inv.Items[honeydew], "and the next one when the first is used up");
            Run(Cfg.honeydewSeconds + 0.5f);
            Assert.IsFalse(Sim.Colony.QueenFed, "none left");
            Assert.AreEqual(plain, Sim.Colony.EggInterval, 1e-3f);
        }

        [Test]
        public void GoldenAphid_SitsInARanch_AndPaysWhenCaught()
        {
            int aphid = EventIndex(C.AphidEvent);
            Assert.IsFalse(Sim.RandomEvents.Trigger(aphid), "no ranch, no aphid");
            Assert.IsTrue(FindMachineSpot(C.Ranch, out var origin));
            Sim.EnqueueBuild(C.BuildingIndex(C.Ranch), origin, default);
            Sim.Tick();
            Sim.Buildings.CompleteAllBlueprints();
            Sim.Tick();

            Assert.IsTrue(Sim.RandomEvents.Trigger(aphid));
            Assert.AreEqual(1, Sim.RandomEvents.Critters.Count);
            var cell = Sim.RandomEvents.Critters[0].Cell;
            Assert.AreEqual(origin.y, cell.y, "on the ranch floor");
            Run(3f);
            Assert.AreEqual(cell, Sim.RandomEvents.Critters[0].Cell, "it stays put");

            Sim.EnqueueCatch(new float2(cell) + 0.5f);
            Sim.Tick();
            Assert.AreEqual(0, Sim.RandomEvents.Active.Count);
            Assert.AreEqual(3, Inv.Items[C.Item(C.Honeydew)] + (Sim.Colony.QueenFed ? 1 : 0), "the reward (the queen may have eaten one)");
        }

        [Test]
        public void Roots_GrowDownFromPlants_ThroughTheTopsoilOnly()
        {
            C.WorldGen.rootMaterial = C.Root;
            C.WorldGen.rootMaxDepth = 20;
            using var world = WorldGenerator.Create(C.DB, 77);
            ushort root = C.Index(C.Root);
            int count = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (world[x, y].Material != root) continue;
                count++;
                int depth = world.Depth(x, y);
                Assert.GreaterOrEqual(depth, C.WorldGen.grassDepth);
                Assert.LessOrEqual(depth, C.WorldGen.rootMaxDepth);
                Assert.IsFalse(world[x, y].IsOpen);
            }
            Assert.Greater(count, 20, "every plant has a root");
            var plants = SurfacePlants.Place(world.Width, C.WorldGen, 77);
            foreach (var p in plants)
                Assert.AreEqual(root, world[p.X, world.SurfaceY[p.X] - C.WorldGen.grassDepth].Material, $"a root starts under the plant at {p.X}");
            Assert.AreEqual(C.Item(C.Fibre), C.DB.MaterialSim[root].DropItem);
        }

        [Test]
        public void Save_KeepsTimeOfDayWeatherAndTheQueensHoneydew()
        {
            Sim.Surface.Clock = 3.62;
            Sim.StartWeather(WeatherKind.Rain);
            Inv.Add(C.Item(C.Honeydew), 1, Queen);
            Run(1f);
            var s = Sim.Surface;
            using var ms = new MemoryStream();
            SaveGame.Save(Sim, ms, DateTime.UtcNow);
            ms.Position = 0;
            using var loaded = SaveGame.Load(ms, C.DB, C.Config, out _);
            var l = loaded.Surface;
            Assert.AreEqual(s.Clock, l.Clock, 1e-9);
            Assert.AreEqual(s.Day, l.Day);
            Assert.AreEqual(s.IsDay, l.IsDay);
            Assert.AreEqual(WeatherKind.Rain, l.Weather);
            Assert.AreEqual(s.WeatherLeft, l.WeatherLeft, 1e-4f);
            Assert.AreEqual(s.RoyalFeast, l.RoyalFeast, 1e-4f);
            Assert.Greater(l.RoyalFeast, 0f);
            Assert.AreEqual(s.Plants.Count, l.Plants.Count, "plants come from the seed");
        }

        [Test]
        public void Offline_TheClockRunsOn_WithoutWeather()
        {
            Sim.StartWeather(WeatherKind.Rain);
            Sim.Tick();
            double before = Sim.Surface.Clock;
            OfflineProgress.Apply(Sim, 3600);
            Assert.AreEqual(before + 3600 / Cfg.dayLengthSeconds, Sim.Surface.Clock, 1e-6);
            Assert.AreEqual(WeatherKind.Clear, Sim.Surface.Weather);
        }
    }
}
