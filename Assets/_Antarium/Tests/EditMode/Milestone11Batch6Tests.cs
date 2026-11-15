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
    /// Milestone 11, batch 6: hills and terraces on the surface (flat by the nest, level under lakes), rare surface structures,
    /// trees with big roots, apples, and the Elder Tree in its cavern with its golden apples (found once, saved).
    /// </summary>
    public class Milestone11Batch6Tests
    {
        TestContent _c;
        ContentDB _db;
        Simulation _sim;
        WorldGrid _world;

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _world?.Dispose();
            if (_db != null && _db != _c?.DB) _db.Dispose();
            _c?.Dispose();
            _sim = null;
            _world = null;
            _db = null;
            _c = null;
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

        Simulation Reload(Simulation sim)
        {
            using var ms = new MemoryStream();
            SaveGame.Save(sim, ms, DateTime.UtcNow);
            ms.Position = 0;
            return SaveGame.Load(ms, _db, _c.Config, out _);
        }

        // ---------------------------------------------------------------- the surface line

        [Test]
        public void Hills_RiseAndFall_ButTheNestStaysFlat()
        {
            _c = new TestContent(16, 4, caves: false);
            var g = _c.WorldGen;
            g.hillAmplitude = 12f;
            g.hillFrequency = 0.02f;
            g.nestFlatRadius = 10;
            g.nestFlatBlend = 10;
            _db = _c.Rebuild();
            _world = WorldGenerator.Create(_db, 11);
            int lo = int.MaxValue, hi = int.MinValue, nest = _world.Width / 2;
            for (int x = 0; x < _world.Width; x++)
            {
                lo = math.min(lo, _world.SurfaceY[x]);
                hi = math.max(hi, _world.SurfaceY[x]);
            }
            Assert.Greater(hi - lo, 10, "hills");
            for (int x = nest - 10; x <= nest + 10; x++)
                Assert.LessOrEqual(math.abs(_world.SurfaceY[x] - _world.SurfaceY[nest]), 0, "flat by the nest");
        }

        [Test]
        public void Terraces_AreFlatSteps_WithCliffsBetween()
        {
            _c = new TestContent(16, 4, caves: false);
            var g = _c.WorldGen;
            g.hillAmplitude = 12f;
            g.hillFrequency = 0.02f;
            g.terraceThreshold = -1f; // terraced everywhere
            g.terraceStep = 4;
            g.lakeCount = g.riverCount = Vector2Int.zero; // lakes level the ground under them
            _db = _c.Rebuild();
            _world = WorldGenerator.Create(_db, 11);
            int baseSurface = _world.Height - 1 - g.skyHeight, cliffs = 0;
            for (int x = 0; x < _world.Width; x++)
            {
                Assert.AreEqual(0, ((_world.SurfaceY[x] - baseSurface) % 4 + 4) % 4, "every column on a step");
                if (x > 0 && math.abs(_world.SurfaceY[x] - _world.SurfaceY[x - 1]) >= 4) cliffs++;
            }
            Assert.Greater(cliffs, 2, "steps with cliffs between them");
        }

        [Test]
        public void Lakes_LieOnLevelGround_EvenAmongHills()
        {
            _c = new TestContent(16, 4, caves: false);
            var g = _c.WorldGen;
            g.hillAmplitude = 14f;
            g.hillFrequency = 0.03f;
            g.waterClearance = 30;
            g.lakeCount = new Vector2Int(2, 2);
            g.riverCount = new Vector2Int(0, 0);
            g.lakeWidth = new Vector2Int(12, 12);
            g.lakeDepth = new Vector2Int(4, 4);
            g.waterFlatBlend = 8;
            _db = _c.Rebuild();
            const uint seed = 5;
            _world = WorldGenerator.Create(_db, seed);
            var water = SurfaceWater.Place(_world.Width, g, seed);
            Assert.Greater(water.Count, 0);
            foreach (var b in water)
                Assert.AreEqual(_world.SurfaceY[b.X0 - 1], _world.SurfaceY[b.X1 + 1], "both banks at the same height");
        }

        // ---------------------------------------------------------------- structures

        void MakeStructureWorld()
        {
            _c = new TestContent(16, 4, caves: false);
            var g = _c.WorldGen;
            g.featureClearance = 16;
            g.lakeCount = g.riverCount = Vector2Int.zero;
            SurfaceFeatureRule Rule(SurfaceFeatureKind kind, int width, TerrainMaterialDef material, TerrainMaterialDef find = null, int finds = 0) =>
                new() { kind = kind, count = new Vector2Int(1, 1), width = new Vector2Int(width, width), material = material, find = find, finds = finds };
            g.surfaceFeatures = new List<SurfaceFeatureRule>
            {
                Rule(SurfaceFeatureKind.Boulder, 6, _c.Stone),
                Rule(SurfaceFeatureKind.Overhang, 15, _c.Obsidian),
                Rule(SurfaceFeatureKind.HollowLog, 14, _c.DeadWood, _c.Root, 2),
                Rule(SurfaceFeatureKind.Anthill, 18, _c.Packed, _c.OreRock, 3),
                Rule(SurfaceFeatureKind.StandingStones, 12, _c.Magma, _c.Crystal, 1),
            };
            _c.Config.colony.fieldRefreshTicks = 1;
            _db = _c.Rebuild();
        }

        int CountMaterial(int x0, int x1, int y0, int y1, TerrainMaterialDef m)
        {
            ushort index = _db.GetMaterialIndex(m);
            int n = 0;
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (_sim.World.InBounds(x, y) && _sim.World[x, y].Material == index && !_sim.World[x, y].IsOpen) n++;
            return n;
        }

        [Test]
        public void Structures_StandOnTheSurface_AndTheSurfaceLineIsTheirTop()
        {
            MakeStructureWorld();
            _sim = new Simulation(_db, _c.Config, 3);
            var world = _sim.World;
            var features = _sim.Surface.Features;
            Assert.AreEqual(5, features.Count, "one of each");
            foreach (var f in features)
            {
                int nest = world.Width / 2;
                Assert.Greater(math.min(math.abs(f.X0 - nest), math.abs(f.X1 - nest)), 16, "clear of the nest");
                for (int x = f.X0; x <= f.X1; x++)
                {
                    int top = world.SurfaceY[x];
                    Assert.IsFalse(world[x, top].IsOpen, $"{f.Kind}: the surface line is solid");
                    for (int y = top + 1; y < world.Height; y++) Assert.IsTrue(world[x, y].IsOpen, $"{f.Kind}: nothing above it");
                }
                var rule = _c.WorldGen.surfaceFeatures[f.Rule];
                Assert.Greater(CountMaterial(f.X0, f.X1, world.SurfaceY[f.Mid] - 12, world.Height - 1, rule.material), 4, $"{f.Kind} is built");
                if (rule.find != null)
                    Assert.AreEqual(rule.finds, CountMaterial(f.X0 - 8, f.X1 + 8, world.SurfaceY[f.Mid] - 14, world.Height - 1, rule.find), $"{f.Kind}: its finds");
            }

            // Under the overhang's ledge and inside the log ants are under cover; the space under the ledge is open to the air.
            var ledge = features.Find(f => f.Kind == SurfaceFeatureKind.Overhang);
            int sheltered = 0;
            for (int x = ledge.X0; x <= ledge.X1; x++)
            for (int y = world.SurfaceY[x] - 1; y > world.SurfaceY[x] - 12; y--)
            {
                if (!world[x, y].IsOpen || world[x, y - 1].IsOpen) continue;
                Assert.AreNotEqual(0, (int)(world[x, y].Flags & CellFlags.Explored), "reachable from the open air");
                if (!_sim.IsExposed(new int2(x, y))) sheltered++;
                break;
            }
            Assert.GreaterOrEqual(sheltered, 4, "ground under the ledge, under cover");

            var log = features.Find(f => f.Kind == SurfaceFeatureKind.HollowLog);
            ushort dead = _db.GetMaterialIndex(_c.DeadWood);
            int2 inside = new int2(-1, -1);
            for (int y = world.SurfaceY[log.Mid]; y > 0; y--)
                if (world[log.Mid, y].IsOpen && world[log.Mid, y].Material == dead) inside = new int2(log.Mid, y); // its floor
            Assert.GreaterOrEqual(inside.x, 0, "hollow inside, its wood as the back wall");
            Assert.IsTrue(new NavGrid(world).IsWalkable(inside), "ants can crawl in");
            Assert.IsFalse(_sim.IsExposed(inside), "sheltered inside the log");
            Assert.IsTrue(world[log.X0, inside.y].IsOpen || world[log.X0, inside.y + 1].IsOpen, "open at its end");

            // The old anthill's tunnels reach the open air, so they are explored from the start.
            var hill = features.Find(f => f.Kind == SurfaceFeatureKind.Anthill);
            bool explored = false;
            ushort packed = _db.GetMaterialIndex(_c.Packed);
            for (int x = hill.X0; x <= hill.X1; x++)
            for (int y = world.SurfaceY[x] - 10; y < world.SurfaceY[x]; y++)
                if (world[x, y].IsOpen && world[x, y].Material == packed && (world[x, y].Flags & CellFlags.Explored) != 0) explored = true;
            Assert.IsTrue(explored);

            // Plants keep off the structures.
            foreach (var p in _sim.Surface.Plants)
                Assert.Less(SurfaceFeatures.At(features, p.X), 0, "no plant on a structure");
        }

        [Test]
        public void AnOldWorld_WithoutStructures_DropsThem_AndPlantsGrowThere()
        {
            MakeStructureWorld();
            _sim = new Simulation(_db, _c.Config, 3);
            int plants = _sim.Surface.Plants.Count;
            // An old save's world: the same seed, but none of the structures were built (made before batch 6).
            foreach (var f in _sim.Surface.Features)
            {
                var rule = _c.WorldGen.surfaceFeatures[f.Rule];
                ushort m = _db.GetMaterialIndex(rule.material);
                for (int x = f.X0 - 1; x <= f.X1 + 1; x++)
                for (int y = 0; y < _sim.World.Height; y++)
                    if (_sim.World[x, y].Material == m) _sim.SetCell(new int2(x, y), new Cell(_db.GetMaterialIndex(_c.Dirt), _sim.World[x, y].Density, _sim.World[x, y].Flags));
            }
            using var old = Reload(_sim);
            Assert.AreEqual(0, old.Surface.Features.Count, "the structures it doesn't have are dropped");
            Assert.Greater(old.Surface.Plants.Count, plants, "plants grow where they would have stood");
        }

        // ---------------------------------------------------------------- trees

        void MakeTreeWorld(float appleShare = 0.5f)
        {
            _c = new TestContent(16, 4, caves: false);
            var g = _c.WorldGen;
            g.treeGap = new Vector2Int(20, 30);
            g.treeClearance = 6;
            g.appleTreeShare = appleShare;
            g.treeRootMaterial = _c.TreeRoot;
            g.rootMaterial = _c.Root;
            g.treeRootMaxDepth = 40;
            g.lakeCount = g.riverCount = Vector2Int.zero;
            _c.Config.colony.fieldRefreshTicks = 1;
            _db = _c.Rebuild();
        }

        [Test]
        public void Trees_StandAmongThePlants_WithBigRootsBelow()
        {
            MakeTreeWorld();
            _sim = new Simulation(_db, _c.Config, 9);
            var plants = _sim.Surface.Plants;
            var trees = plants.FindAll(p => p.IsTree);
            Assert.Greater(trees.Count, 8);
            Assert.IsTrue(trees.Exists(t => t.Kind == SurfacePlants.AppleTree));
            Assert.IsTrue(trees.Exists(t => t.Kind != SurfacePlants.AppleTree));
            for (int i = 1; i < plants.Count; i++) Assert.LessOrEqual(plants[i - 1].X, plants[i].X, "sorted by column");
            foreach (var p in plants)
            {
                if (p.IsTree) continue;
                foreach (var t in trees) Assert.Greater(math.abs(p.X - t.X), SurfacePlants.TreeSpread, "plants keep clear of trunks");
            }
            foreach (var t in trees)
            {
                int ground = _sim.World.SurfaceY[t.X];
                Assert.Greater(CountMaterial(t.X - 20, t.X + 20, ground - 40, ground, _c.TreeRoot), 25, "big roots under every tree");
            }
            // The same seed puts the same trees in the same places.
            CollectionAssert.AreEqual(trees, SurfacePlants.Place(_sim.World.Width, _c.WorldGen, 9).FindAll(p => p.IsTree));
        }

        [Test]
        public void AppleTrees_DropApples_ByDayOnly_UnderTheirCrowns()
        {
            MakeTreeWorld(appleShare: 1f);
            _c.Config.wild.appleInterval = 0.5f;
            _sim = new Simulation(_db, _c.Config, 9);
            ushort apple = _c.Item(_c.Apple);

            _sim.SkipToNight();
            Run(8f);
            Assert.AreEqual(0, Count(apple), "none at night");

            _sim.Surface.Clock = Math.Floor(_sim.Surface.Clock) + 1.4;
            Run(8f);
            int n = Count(apple);
            Assert.Greater(n, 0, "apples fall by day");
            Assert.LessOrEqual(n, _c.Config.wild.maxSurfaceApples);
            var trees = _sim.Surface.Plants.FindAll(p => p.Kind == SurfacePlants.AppleTree);
            for (int i = 0; i < _sim.Colony.Piles.Count; i++)
            {
                var pile = _sim.Colony.Piles[i];
                if (pile.Item != apple) continue;
                Assert.IsTrue(trees.Exists(t => math.abs(t.X - pile.Cell.x) <= _c.Config.wild.appleSpread), "under an apple tree");
                Assert.Greater(pile.Cell.y, _sim.World.SurfaceY[pile.Cell.x] - 2, "on the ground");
            }
        }

        // ---------------------------------------------------------------- the Elder Tree

        void MakeElderWorld()
        {
            _c = new TestContent(16, 8, caves: false);
            var g = _c.WorldGen;
            _c.Deep.deposits.Clear(); // no glowing crystals: the tree's light is the only light down there
            _c.Deep.baseMaterial = _c.Obsidian; // plain rock (the test stone drops ore, and roots never replace finds)
            _c.Deep.thickness = 150;
            g.lakeCount = g.riverCount = Vector2Int.zero;
            g.elderBiome = _c.Deep;
            g.elderCaveSize = new Vector2Int(64, 44);
            g.elderDepthShare = 0.5f;
            g.elderMinDistance = 60;
            g.elderWood = _c.ElderWood;
            g.elderLeaves = _c.ElderLeaves;
            g.treeRootMaterial = _c.TreeRoot;
            g.elderFruit = _c.GoldenApple;
            g.elderFruitChance = 1f;
            g.elderFruitMax = 10;
            _db = _c.Rebuild();
        }

        [Test]
        public void ElderTree_GrowsInAnUnfoundCavern_WithGlowingGoldenApples()
        {
            MakeElderWorld();
            _sim = new Simulation(_db, _c.Config, 4);
            var world = _sim.World;
            Assert.IsTrue(_sim.Surface.HasElderTree);
            var site = _sim.Surface.ElderTree;
            Assert.GreaterOrEqual(math.abs(site.Center.x - world.Width / 2), 60, "away from the nest");
            Assert.IsTrue(ElderTree.IsBuilt(world, _db, site));
            Assert.Greater(CountMaterial(site.Center.x - 4, site.Center.x + 4, site.Center.y - site.Half.y, site.Center.y, _c.ElderWood), 40, "a trunk");
            Assert.Greater(CountMaterial(site.Center.x - site.Half.x, site.Center.x + site.Half.x, site.Center.y - site.Half.y, site.Center.y + site.Half.y, _c.ElderLeaves), 60, "a canopy");
            Assert.Greater(CountMaterial(site.Center.x - site.Half.x - 20, site.Center.x + site.Half.x + 20, site.Center.y - site.Half.y - 30, site.Center.y, _c.TreeRoot), 30, "big roots");
            Assert.IsFalse(world[site.Center.x, site.Center.y + site.Half.y - 4].IsRevealed, "hidden in the fog");

            ushort fruit = _db.GetItemIndex(_c.GoldenApple);
            ushort wood = _db.GetMaterialIndex(_c.ElderWood), leaves = _db.GetMaterialIndex(_c.ElderLeaves);
            var apples = _sim.Wild.Growths.FindAll(g => g.Item == fruit);
            Assert.AreEqual(10, apples.Count, "fruit up to the most a tree has");
            foreach (var g in apples)
            {
                Assert.IsTrue(world[g.Cell.x, g.Cell.y].IsOpen);
                ushort below = world[g.Cell.x, g.Cell.y - 1].Material;
                Assert.IsTrue(below == wood || below == leaves, "on the canopy or a branch");
                Assert.AreEqual(1, _sim.Colony.Piles.CountAt(g.Cell, fruit), "grown at the start");
            }
            Assert.AreEqual(apples.Count, _sim.Light.EmitterCount, "every golden apple shines");
            _sim.UpdateDerived();
            Assert.Greater(_sim.Light.Sample(world, apples[0].Cell.x, apples[0].Cell.y, 0f).x, 0.2f, "the cavern is lit");
        }

        [Test]
        public void ElderTree_IsFoundWhenTheColonyBreaksIn_OnlyOnce_AndThatIsSaved()
        {
            MakeElderWorld();
            _sim = new Simulation(_db, _c.Config, 4);
            var world = _sim.World;
            var site = _sim.Surface.ElderTree;
            // A cavern wall cell beside an open cavern cell, at a third of the cavern's height.
            int2 wall = default;
            int y = site.Center.y;
            for (int x = site.Center.x - site.Half.x - 2; x < site.Center.x; x++)
                if (!world[x, y].IsOpen && world[x + 1, y].IsOpen) { wall = new int2(x, y); break; }
            Assert.AreNotEqual(default(int2), wall);
            Assert.IsFalse(_sim.Wild.ElderFound);
            _sim.SetCell(wall, new Cell(world[wall.x, wall.y].Material, world[wall.x, wall.y].Density, world[wall.x, wall.y].Flags | CellFlags.Revealed));
            _sim.Enqueue(new DigCommand(new float2(wall) + 0.5f, 0.4f, 10000f));
            _sim.Tick();
            Assert.IsTrue(world[wall.x, wall.y].IsOpen, "dug through");
            Assert.IsTrue(_sim.Wild.ElderFound, "found");
            int revealed = 0;
            for (int x = site.Center.x - site.Half.x; x <= site.Center.x + site.Half.x; x++)
            for (int yy = site.Center.y - site.Half.y; yy <= site.Center.y + site.Half.y; yy++)
                if (world[x, yy].IsOpen && world[x, yy].IsRevealed) revealed++;
            Assert.Greater(revealed, 800, "the whole cavern is revealed");
            ushort wood = _db.GetMaterialIndex(_c.ElderWood), leaves = _db.GetMaterialIndex(_c.ElderLeaves);
            for (int x = site.Center.x - site.Half.x; x <= site.Center.x + site.Half.x; x++)
            for (int yy = site.Center.y - site.Half.y; yy <= site.Center.y + site.Half.y; yy++)
                if (world[x, yy].Material == wood || world[x, yy].Material == leaves)
                    Assert.IsTrue(world[x, yy].IsRevealed, "the whole tree shows, not just its outline");

            _sim.Wild.AppleTimer = 3.5f;
            using var loaded = Reload(_sim);
            Assert.IsTrue(loaded.Wild.ElderFound, "saved");
            Assert.AreEqual(3.5f, loaded.Wild.AppleTimer, 1e-4f);
            Assert.IsTrue(loaded.Surface.HasElderTree);
        }

        [Test]
        public void ElderTree_NoneInAWorldMadeWithoutIt_EvenAfterLoading()
        {
            MakeElderWorld();
            _c.WorldGen.elderWood = null; // made before batch 6: no tree in the world
            var noTree = _c.Rebuild();
            _sim = new Simulation(noTree, _c.Config, 4);
            Assert.IsFalse(_sim.Surface.HasElderTree);
            Assert.AreEqual(0, _sim.Wild.Growths.Count);
            using var ms = new MemoryStream();
            SaveGame.Save(_sim, ms, DateTime.UtcNow);
            ms.Position = 0;
            _c.WorldGen.elderWood = _c.ElderWood; // ... loaded by a build that has it
            using var loaded = SaveGame.Load(ms, _db, _c.Config, out _);
            Assert.IsFalse(loaded.Surface.HasElderTree, "no tree is made up for an old world");
            noTree.Dispose();
        }

        // ---------------------------------------------------------------- content

        [Test]
        public void Content_TreesApplesAndTheElderTreeAreSetUp()
        {
#if UNITY_EDITOR
            var db = UnityEditor.AssetDatabase.LoadAssetAtPath<ContentDatabase>("Assets/_Antarium/Content/ContentDatabase.asset");
            Assert.IsNotNull(db);
            var g = db.worldGen;
            var apple = db.items.Find(i => i != null && i.Id == "apple");
            Assert.IsNotNull(apple);
            Assert.IsTrue(db.treeForage.Contains(apple));
            Assert.AreEqual(PileArt.GoldenApple, g.elderFruit.pileArt);
            Assert.Greater(g.elderFruit.foodValue, apple.foodValue, "golden apples are worth more");
            Assert.Greater(g.elderFruit.growthLight.a, 0f, "and glow");
            Assert.IsTrue((g.elderLeaves.flags & MaterialFlags.LightEmitting) != 0, "the canopy glows");
            Assert.IsNotNull(g.treeRootMaterial);
            Assert.Greater(g.treeGap.x, 0);
            Assert.Greater(g.hillAmplitude, 0f);
            Assert.Greater(g.nestFlatRadius, 0);
            Assert.AreEqual(5, g.surfaceFeatures.Count);
            Assert.IsTrue(g.biomes.Contains(g.elderBiome));
            // The cavern fits inside the world, clear of the bedrock, and the world makes a tree.
            Assert.IsTrue(ElderTree.Locate(g.widthInChunks * 32, g.heightInChunks * 32, g, 1337, 1f, out var site));
            Assert.Greater(site.Center.y - site.Half.y, g.bedrockThickness + 4);
#endif
        }
    }
}
