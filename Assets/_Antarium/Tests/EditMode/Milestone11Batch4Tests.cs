using System;
using System.Collections.Generic;
using System.IO;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Tests
{
    /// <summary>
    /// Milestone 11, batch 4: lakes and rivers on the surface, berry bushes, toadstools, cave mushrooms and glowing mushrooms
    /// (growing back, lighting their caves, saved), and light updates that relight far-apart changes separately.
    /// </summary>
    public class Milestone11Batch4Tests
    {
        TestContent _c;
        ContentDB _db;
        Simulation _sim;

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            if (_db != null && _db != _c?.DB) _db.Dispose();
            _c?.Dispose();
            _sim = null;
            _db = null;
            _c = null;
        }

        /// <summary>A 512 x 128 world without caves; lakes and rivers within reach of the nest (30 columns clear of it).</summary>
        void MakeWaterWorld()
        {
            _c = new TestContent(16, 4, caves: false);
            var g = _c.WorldGen;
            g.waterClearance = 30;
            g.waterSpacing = 10;
            g.lakeCount = new Vector2Int(1, 1);
            g.riverCount = new Vector2Int(1, 1);
            g.lakeWidth = new Vector2Int(12, 12);
            g.lakeDepth = new Vector2Int(5, 5);
            g.riverWidth = new Vector2Int(10, 10);
            g.riverDepth = new Vector2Int(2, 2);
            g.waterBedMaterial = _c.Sand;
            var c = _c.Config.colony;
            c.forageInterval = 1000f;
            c.eggInterval = 1000f;
            c.fieldRefreshTicks = 1;
            _db = _c.DB;
        }

        /// <summary>A seed whose lake or river lies within <paramref name="within"/> columns of the nest.</summary>
        uint SeedWithWaterNear(int within)
        {
            int width = _c.WorldGen.widthInChunks * 32, nest = width / 2;
            for (uint seed = 1; seed < 200; seed++)
                foreach (var b in SurfaceWater.Place(width, _c.WorldGen, seed))
                    if (math.min(math.abs(b.X0 - nest), math.abs(b.X1 - nest)) < within) return seed;
            Assert.Fail("no seed puts water near the nest");
            return 0;
        }

        void Run(float seconds)
        {
            int ticks = (int)math.round(seconds / _sim.TickDuration);
            for (int i = 0; i < ticks; i++) _sim.Tick();
        }

        int Count(ushort item)
        {
            int n = 0;
            for (int i = 0; i < _sim.Colony.Piles.Count; i++)
                if (_sim.Colony.Piles[i].Item == item) n += _sim.Colony.Piles[i].Count;
            return n;
        }

        long LakeWater()
        {
            long total = 0;
            var w = _sim.World;
            for (int i = 0; i < w.Cells.Length; i++)
                if ((w.Cells[i].Flags & CellFlags.Lake) != 0) total += w.Fluid[i].Level;
            return total;
        }

        Simulation Reload(Simulation sim)
        {
            using var ms = new MemoryStream();
            SaveGame.Save(sim, ms, DateTime.UtcNow);
            ms.Position = 0;
            return SaveGame.Load(ms, _db, _c.Config, out _);
        }

        // ---------------------------------------------------------------- lakes and rivers

        [Test]
        public void LakesAndRivers_AreDugIntoTheSurface_FullOfWater_AwayFromTheNest()
        {
            MakeWaterWorld();
            _sim = new Simulation(_db, _c.Config, 7);
            var water = _sim.Surface.Water;
            var world = _sim.World;
            Assert.AreEqual(2, water.Count, "a lake and a river");
            Assert.AreEqual(1, water.FindAll(b => b.River).Count);
            int nest = world.Width / 2;
            foreach (var b in water)
            {
                Assert.IsTrue(b.X1 < nest - 30 || b.X0 > nest + 30, "kept clear of the nest");
                int bank = math.min(world.SurfaceY[b.X0 - 1], world.SurfaceY[b.X1 + 1]);
                int mid = (b.X0 + b.X1) / 2;
                Assert.AreEqual(bank - b.Depth, world.SurfaceY[mid], "the bed lies Depth below the lower bank in the middle");
                for (int y = world.SurfaceY[mid] + 1; y <= bank; y++)
                {
                    var cell = world[mid, y];
                    Assert.IsTrue((cell.Flags & CellFlags.Lake) != 0, "basin cells are flagged");
                    Assert.AreEqual(FluidCell.Full, world.Fluid[world.Index(mid, y)].Level, "full of water up to the bank");
                }
                Assert.AreEqual(_db.GetMaterialIndex(_c.Sand), world[mid, world.SurfaceY[mid]].Material, "a sandy bed");
                foreach (var p in _sim.Surface.Plants)
                    Assert.IsFalse(p.X >= b.X0 - 2 && p.X <= b.X1 + 2, "no plants in the water or on its banks");
            }
        }

        [Test]
        public void LakeWater_NeverSoaksAway_ButRainWaterNearTheSurfaceStillDoes()
        {
            MakeWaterWorld();
            _sim = new Simulation(_db, _c.Config, 7);
            long before = LakeWater();
            Assert.Greater(before, 255 * 20);
            Run(60f);
            Assert.AreEqual(before, LakeWater(), "lakes keep their water");

            // A puddle on dry ground by the nest soaks in as before.
            var ground = new int2(_sim.World.Width / 2 + 12, _sim.World.SurfaceY[_sim.World.Width / 2 + 12] + 1);
            _sim.Fluids.AddWater(ground, 200);
            Run(60f);
            Assert.Less(_sim.World.Fluid[_sim.World.Index(ground.x, ground.y)].Level, 50, "rain water soaks away");
        }

        [Test]
        public void SameSeed_SameWater_AndPlantsKeepTheirPlaces()
        {
            MakeWaterWorld();
            var a = SurfaceWater.Place(512, _c.WorldGen, 99);
            var b = SurfaceWater.Place(512, _c.WorldGen, 99);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i].X0, b[i].X0);

            // Plants between the water bodies are where they were before lakes existed (same positions, by the same random steps).
            _c.WorldGen.lakeCount = _c.WorldGen.riverCount = Vector2Int.zero;
            var dry = SurfacePlants.Place(512, _c.WorldGen, 99);
            _c.WorldGen.lakeCount = _c.WorldGen.riverCount = Vector2Int.one;
            var wet = SurfacePlants.Place(512, _c.WorldGen, 99);
            var dryX = new HashSet<int>();
            foreach (var p in dry) dryX.Add(p.X);
            foreach (var p in wet) Assert.IsTrue(dryX.Contains(p.X));
            Assert.Less(wet.Count, dry.Count, "plants in the water are left out");
        }

        [Test]
        public void AnOldSave_HasNoLakes_AndKeepsAllItsPlants()
        {
            MakeWaterWorld();
            _c.WorldGen.lakeCount = _c.WorldGen.riverCount = Vector2Int.zero; // a world made before lakes existed
            _sim = new Simulation(_db, _c.Config, 7);
            int plants = _sim.Surface.Plants.Count;
            Assert.AreEqual(0, _sim.Surface.Water.Count);

            _c.WorldGen.lakeCount = _c.WorldGen.riverCount = Vector2Int.one; // this build would put water there
            using var loaded = Reload(_sim);
            Assert.AreEqual(0, loaded.Surface.Water.Count, "the saved world has no water, so neither does the list");
            Assert.AreEqual(plants, loaded.Surface.Plants.Count, "no plants left out for water that isn't there");
        }

        [Test]
        public void Forage_IsOnlyPutWhereAntsCanWalkFromTheNest()
        {
            MakeWaterWorld();
            uint seed = SeedWithWaterNear(80);
            _c.Config.colony.forageRange = 200;
            _c.Config.wild.toadstoolInterval = 0.2f;
            _c.Config.wild.maxSurfaceToadstools = 500;
            _c.Config.surface.leafInterval = 0.2f;
            _c.Config.surface.maxSurfaceLeaves = 500;
            _sim = new Simulation(_db, _c.Config, seed);
            Run(1f);

            // The far bank of the nearest water body is cut off.
            int nest = _sim.Colony.QueenCell.x;
            WaterBody near = default;
            int best = int.MaxValue;
            foreach (var b in _sim.Surface.Water)
            {
                int d = math.min(math.abs(b.X0 - nest), math.abs(b.X1 - nest));
                if (d < best) { best = d; near = b; }
            }
            int farX = near.X0 > nest ? near.X1 + 4 : near.X0 - 4;
            var far = new int2(farX, _sim.World.SurfaceY[farX] + 1);
            Assert.AreEqual(FlowFields.Unreachable, _sim.Fields.Get(FieldKind.Home, _sim.World, far), "the far bank can't be reached");

            Run(40f);
            ushort toad = _c.Item(_c.Toadstool), leaf = _c.Item(_c.Leaf);
            int toadstools = 0, leaves = 0;
            for (int i = 0; i < _sim.Colony.Piles.Count; i++)
            {
                var p = _sim.Colony.Piles[i];
                if (p.Item == toad) toadstools += p.Count;
                else if (p.Item == leaf) leaves += p.Count;
                else continue;
                Assert.AreNotEqual(FlowFields.Unreachable, _sim.Fields.Get(FieldKind.Home, _sim.World, p.Cell), "nothing across the water");
            }
            Assert.Greater(toadstools, 20);
            Assert.Greater(leaves, 20);
        }

        // ---------------------------------------------------------------- berries and toadstools

        [Test]
        public void BerryBushes_DropBerries_ByDayOnly()
        {
            _c = new TestContent(8, 4, caves: false);
            _db = _c.DB;
            _c.WorldGen.berryBushShare = 1f;
            _c.Config.wild.berryInterval = 0.5f;
            _c.Config.colony.fieldRefreshTicks = 1;
            _sim = new Simulation(_db, _c.Config, 5);
            Assert.IsTrue(_sim.Surface.Plants.TrueForAll(p => p.Kind == SurfacePlants.BerryBush));
            ushort berry = _c.Item(_c.Berry);

            _sim.SkipToNight();
            Run(8f);
            Assert.AreEqual(0, Count(berry), "none at night");

            _sim.Surface.Clock = Math.Floor(_sim.Surface.Clock) + 1.4;
            Run(8f);
            int n = Count(berry);
            Assert.Greater(n, 0, "berries fall by day");
            Assert.LessOrEqual(n, _c.Config.wild.maxSurfaceBerries);
        }

        [Test]
        public void Toadstools_PopUpNearTheNest_MostOftenInTheRain()
        {
            _c = new TestContent(8, 4, caves: false);
            _db = _c.DB;
            var w = _c.Config.wild;
            w.toadstoolInterval = 10f;
            w.rainToadstoolFactor = 4f;
            w.maxSurfaceToadstools = 10;
            _c.Config.colony.fieldRefreshTicks = 1;
            _sim = new Simulation(_db, _c.Config, 5);
            ushort toad = _c.Item(_c.Toadstool);

            Run(9f);
            Assert.AreEqual(0, Count(toad), "not yet in dry weather");
            _sim.StartWeather(WeatherKind.Rain);
            Run(9f);
            Assert.GreaterOrEqual(Count(toad), 3, "four times as often in the rain");
            for (int i = 0; i < _sim.Colony.Piles.Count; i++)
            {
                var p = _sim.Colony.Piles[i];
                if (p.Item != toad) continue;
                Assert.Greater(p.Cell.y, _sim.World.SurfaceY[p.Cell.x] - 2, "on the surface");
                Assert.LessOrEqual(math.abs(p.Cell.x - _sim.Colony.QueenCell.x), _c.Config.colony.forageRange);
            }
        }

        // ---------------------------------------------------------------- cave mushrooms

        /// <summary>A 128 x 128 world with caves: mushrooms grow in the topsoil's caves, glowing ones in the deep.</summary>
        void MakeMushroomWorld()
        {
            _c = new TestContent(4, 4, caves: true);
            _c.Deep.deposits.Clear(); // no glow crystals: the mushrooms' light is the only light down there
            _c.WorldGen.caveGrowths = new List<GrowthRule>
            {
                new() { item = _c.Mushroom, biome = _c.Topsoil, chance = 1f },
                new() { item = _c.GlowMushroom, biome = _c.Deep, chance = 1f },
            };
            _c.Config.wild.regrowSeconds = new Vector2(3f, 3f);
            _db = _c.Rebuild();
        }

        [Test]
        public void CaveMushrooms_GrowOnUnexploredCaveFloors_AndGlowingOnesLightTheCave()
        {
            MakeMushroomWorld();
            _sim = new Simulation(_db, _c.Config, 3);
            var growths = _sim.Wild.Growths;
            Assert.Greater(growths.Count, 5, "mushrooms grow in the caves");
            ushort glow = _db.GetItemIndex(_c.GlowMushroom), plain = _db.GetItemIndex(_c.Mushroom);
            int glowing = 0;
            foreach (var g in growths)
            {
                var cell = _sim.World[g.Cell.x, g.Cell.y];
                Assert.IsTrue(cell.IsOpen && !_sim.World[g.Cell.x, g.Cell.y - 1].IsOpen, "on a floor");
                Assert.AreEqual(0, (int)(cell.Flags & CellFlags.Explored), "in a cave nobody found yet");
                Assert.IsTrue(g.Grown);
                Assert.AreEqual(1, _sim.Colony.Piles.CountAt(g.Cell, g.Item), "a grown mushroom is a pile");
                Assert.IsTrue(g.Item == glow || g.Item == plain);
                if (g.Item == glow) glowing++;
            }
            Assert.Greater(glowing, 0);
            Assert.AreEqual(glowing, _sim.Light.EmitterCount, "every glowing mushroom gives off light");

            _sim.UpdateDerived();
            var lit = growths.Find(g => g.Item == glow);
            Assert.Greater(_sim.Light.Sample(_sim.World, lit.Cell.x, lit.Cell.y, 0f).y, 0.3f, "its cave is lit");
        }

        [Test]
        public void PickedMushroom_GrowsBack_AndItsLightGoesOutMeanwhile()
        {
            MakeMushroomWorld();
            _sim = new Simulation(_db, _c.Config, 3);
            ushort glow = _db.GetItemIndex(_c.GlowMushroom);
            int index = _sim.Wild.Growths.FindIndex(g => g.Item == glow);
            Assert.GreaterOrEqual(index, 0);
            var cell = _sim.Wild.Growths[index].Cell;
            _sim.UpdateDerived();
            float lit = _sim.Light.Sample(_sim.World, cell.x, cell.y, 0f).y;

            Assert.IsTrue(_sim.Colony.Piles.TryTakeAny(cell, out ushort taken));
            Assert.AreEqual(glow, taken);
            Run(1.2f);
            var g = _sim.Wild.Growths[index];
            Assert.IsFalse(g.Grown, "picked");
            Assert.AreEqual(3f, g.Timer, 1.2f, "grows back after regrowSeconds");
            _sim.UpdateDerived();
            Assert.Less(_sim.Light.Sample(_sim.World, cell.x, cell.y, 0f).y, lit - 0.01f, "its own light went out");

            Run(4f);
            Assert.IsTrue(_sim.Wild.Growths[index].Grown, "grown back");
            Assert.AreEqual(1, _sim.Colony.Piles.CountAt(cell, glow));
            _sim.UpdateDerived();
            Assert.AreEqual(lit, _sim.Light.Sample(_sim.World, cell.x, cell.y, 0f).y, 1e-4f, "lit again");
        }

        [Test]
        public void PickedMushroom_WaitsWhileItsFloorIsUnderWater()
        {
            MakeMushroomWorld();
            _sim = new Simulation(_db, _c.Config, 3);
            var g = _sim.Wild.Growths[0];
            _sim.Colony.Piles.TryTakeAny(g.Cell, out _);
            Run(1.2f);
            int i = _sim.World.Index(g.Cell.x, g.Cell.y);
            _sim.World.Fluid[i] = new FluidCell(FluidKind.Water, FluidCell.Full);
            Run(4f);
            Assume.That(_sim.World.Fluid[i].Level >= _c.Config.wild.maxWater, "the water stayed (the cave's chunk sleeps)");
            Assert.IsFalse(_sim.Wild.Growths[0].Grown, "nothing grows under deep water");
            Assert.AreEqual(0, _sim.Colony.Piles.CountAt(g.Cell, g.Item));
        }

        [Test]
        public void Mushrooms_AreSaved_AndAnOldSaveGetsThemInItsUnfoundCaves()
        {
            MakeMushroomWorld();
            _sim = new Simulation(_db, _c.Config, 3);
            var first = _sim.Wild.Growths[0];
            _sim.Colony.Piles.TryTakeAny(first.Cell, out _);
            Run(1.2f);
            int count = _sim.Wild.Growths.Count;
            int emitters = _sim.Light.EmitterCount;

            using (var loaded = Reload(_sim))
            {
                Assert.AreEqual(count, loaded.Wild.Growths.Count);
                var g = loaded.Wild.Growths[0];
                Assert.AreEqual(first.Cell, g.Cell);
                Assert.IsFalse(g.Grown, "still picked");
                Assert.AreEqual(_sim.Wild.Growths[0].Timer, g.Timer, 1e-4f);
                Assert.AreEqual(emitters, loaded.Light.EmitterCount, "glowing mushrooms shine again after loading");
            }

            // A save from before Milestone 11 (no spots placed): the load puts them in the caves not found yet.
            foreach (var g in _sim.Wild.Growths)
                while (_sim.Colony.Piles.TryTakeAny(g.Cell, out _)) { }
            _sim.Wild.Clear();
            _sim.Wild.Placed = false;
            using (var old = Reload(_sim))
            {
                Assert.IsTrue(old.Wild.Placed);
                Assert.AreEqual(count, old.Wild.Growths.Count, "the same caves, still unexplored");
            }
        }

        [Test]
        public void Mushrooms_AreHauledHomeAsFood_OnceTheTunnelsReachThem()
        {
            MakeMushroomWorld();
            var colony = _c.Config.colony;
            colony.fieldRefreshTicks = 1;
            colony.eggInterval = 1000f;
            colony.queenUpkeep = 0f;
            colony.startingFood = 10f;
            _c.WorkerCaste.upkeep = 0f;
            _sim = new Simulation(_db, _c.Config, 3);
            ushort plain = _db.GetItemIndex(_c.Mushroom);
            // A short tunnel off the royal chamber, with a mushroom spot at its end (as if it had reached a cave).
            var q = _sim.Colony.QueenCell;
            for (int x = q.x; x <= q.x + 12; x++)
            for (int y = q.y; y <= q.y + 1; y++)
                if (!_sim.World[x, y].IsOpen)
                    _sim.SetCell(new int2(x, y), new Cell(_sim.World[x, y].Material, 0, CellFlags.Revealed | CellFlags.Explored));
            var spot = _sim.World.FloorBelow(new int2(q.x + 11, q.y + 1));
            // Reaching the cave explores it (breaking in does; this tunnel is carved by hand): piles in unfound caves are no jobs.
            for (int y = spot.y; y <= q.y; y++)
                if (_sim.World[spot.x, y].IsOpen)
                    _sim.SetCell(new int2(spot.x, y), new Cell(_sim.World[spot.x, y].Material, 0, _sim.World[spot.x, y].Flags | CellFlags.Revealed | CellFlags.Explored));
            _sim.Wild.Add(new Growth { Cell = spot, Item = plain, Grown = true });
            _sim.Colony.Piles.Add(spot, plain, 1);
            float food = _sim.Colony.Inventory.Food;
            Run(40f);
            // Delivered (the test world regrows mushrooms after 3 s, so the spot may hold a new one by now).
            Assert.Greater(_sim.Colony.Stats.Delivered[plain], 0, "picked up and brought home");
            Assert.Greater(_sim.Colony.Inventory.Food, food, "eaten as food");
        }

        // ---------------------------------------------------------------- light

        [Test]
        public void Light_FarApartChanges_AreRelitSeparately_AndExactly()
        {
            MakeWaterWorld();
            _sim = new Simulation(_db, _c.Config, 7);
            _sim.UpdateDerived();
            var w = _sim.World;
            var a = new int2(40, w.SurfaceY[40] - 30);
            var b = new int2(w.Width - 40, w.SurfaceY[w.Width - 40] - 40);
            _sim.SetCell(a, new Cell(w[a.x, a.y].Material, 0, CellFlags.Revealed));
            _sim.SetCell(b, new Cell(w[b.x, b.y].Material, 0, CellFlags.Revealed));
            _sim.Light.SetEmitter(w, a, new float3(1f, 0.5f, 0f));
            _sim.Light.SetEmitter(w, b, new float3(0f, 0.5f, 1f));
            _sim.UpdateDerived();
            var partial = _sim.Light.Light.ToArray();
            Assert.Greater(_sim.Light.Sample(w, a.x, a.y, 0f).x, 0.9f);
            Assert.Greater(_sim.Light.Sample(w, b.x, b.y, 0f).z, 0.9f);

            _sim.Light.RecomputeAll(w, _db.MaterialSim);
            var full = _sim.Light.Light.ToArray();
            CollectionAssert.AreEqual(full, partial, "relighting clusters gives the same light as relighting everything");
        }

        // ---------------------------------------------------------------- content

        [Test]
        public void Content_GlowingMushroomsGiveHalfAgainTheFood_AndCavesHaveRules()
        {
#if UNITY_EDITOR
            var db = UnityEditor.AssetDatabase.LoadAssetAtPath<ContentDatabase>("Assets/_Antarium/Content/ContentDatabase.asset");
            Assert.IsNotNull(db);
            var glow = db.items.Find(i => i != null && i.Id == "glow_mushroom");
            var plain = db.items.Find(i => i != null && i.Id == "mushroom");
            Assert.IsNotNull(glow);
            Assert.IsNotNull(plain);
            Assert.AreEqual(plain.foodValue * 1.5f, glow.foodValue, 1e-4f);
            Assert.Greater(glow.growthLight.a, 0f, "it glows");
            Assert.IsTrue(db.bushForage.Exists(i => i != null && i.pileArt == PileArt.Berries));
            Assert.IsTrue(db.surfaceMushrooms.Exists(i => i != null && i.pileArt == PileArt.Toadstool));
            Assert.IsTrue(db.worldGen.caveGrowths.Exists(r => r.item == glow));
            Assert.IsTrue(db.worldGen.caveGrowths.Exists(r => r.item == plain));
            Assert.IsNotNull(db.worldGen.waterBedMaterial);
#endif
        }
    }
}
