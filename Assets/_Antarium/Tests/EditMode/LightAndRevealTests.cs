using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Tests
{
    public class LightAndRevealTests
    {
        TestContent _content;
        WorldGrid _world;

        [SetUp]
        public void SetUp()
        {
            _content = new TestContent(4, 4);
            _world = new WorldGrid(4, 4);
            Fill(_content.Index(_content.Stone));
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            _content.Dispose();
        }

        void Fill(ushort material)
        {
            for (int i = 0; i < _world.Cells.Length; i++)
                _world.Cells[i] = new Cell(material, Cell.Full);
        }

        void Open(int x, int y) => _world[x, y] = new Cell(_world[x, y].Material, 0);

        LightSystem MakeLight(int maxLevel = 64, int openCost = 4, int solidCost = 16) =>
            new(_world, new LightConfig { maxLevel = maxLevel, openCost = openCost, solidCost = solidCost, skyLight = UnityEngine.Color.white });

        [Test]
        public void Light_FallsOffDownAShaftFromTheSky()
        {
            ushort air = _content.Index(_content.Air);
            for (int x = 0; x < _world.Width; x++)
            for (int y = 100; y < _world.Height; y++)
                _world[x, y] = new Cell(air, 0);
            for (int y = 60; y < 100; y++) Open(50, y);

            using var light = MakeLight();
            light.RecomputeAll(_world, _content.DB.MaterialSim);

            float top = light.Sample(_world, 50, 99).x;
            float mid = light.Sample(_world, 50, 92).x;
            float deep = light.Sample(_world, 50, 70).x;
            Assert.AreEqual(1f, light.Sample(_world, 50, 110).x, 1e-3f);
            Assert.Greater(top, mid);
            Assert.Greater(mid, deep);
            Assert.AreEqual(0f, deep, 1e-3f, "light reach is maxLevel / openCost = 16 cells");
        }

        [Test]
        public void Light_EmitterLightsSealedCaveButNotBeyondReach()
        {
            for (int y = 40; y < 50; y++)
            for (int x = 40; x < 80; x++)
                Open(x, y);
            _world[41, 45] = new Cell(_content.Index(_content.Crystal), Cell.Full);

            using var light = MakeLight();
            light.RecomputeAll(_world, _content.DB.MaterialSim);

            Assert.Greater(light.Sample(_world, 43, 45).x, 0.5f);
            Assert.AreEqual(0f, light.Sample(_world, 75, 45).x, 1e-3f);
        }

        [Test]
        public void Light_LocalUpdateMatchesFullRecompute()
        {
            ushort air = _content.Index(_content.Air);
            for (int x = 0; x < _world.Width; x++)
            for (int y = 110; y < _world.Height; y++)
                _world[x, y] = new Cell(air, 0);
            for (int y = 80; y < 110; y++) Open(60, y);

            using var incremental = MakeLight();
            incremental.RecomputeAll(_world, _content.DB.MaterialSim);

            // Dig a side tunnel, then update only the dirty region.
            for (int x = 61; x < 75; x++)
            {
                Open(x, 95);
                _world.MarkChanged(x, 95, ChunkFlags.LightRecompute);
            }
            Assert.IsTrue(incremental.UpdateDirty(_world, _content.DB.MaterialSim));

            using var full = MakeLight();
            full.RecomputeAll(_world, _content.DB.MaterialSim);
            for (int i = 0; i < full.Light.Length; i++)
                Assert.AreEqual(full.Light[i], incremental.Light[i], $"mismatch at cell {GridIndex.ToCell(i, _world.ChunksX)}");
        }

        [Test]
        public void Reveal_FloodExploresConnectedCaveOnly()
        {
            for (int x = 20; x < 30; x++) Open(x, 20);   // cave A
            for (int x = 40; x < 45; x++) Open(x, 20);   // cave B, not connected

            using var starts = new NativeArray<int2>(new[] { new int2(20, 20) }, Allocator.TempJob);
            int explored = Reveal.Flood(_world, starts, 2, int.MaxValue);

            Assert.AreEqual(10, explored);
            Assert.IsTrue(_world[29, 20].IsRevealed);
            Assert.IsTrue(_world[25, 22].IsRevealed, "walls within the radius are revealed");
            Assert.IsFalse(_world[42, 20].IsRevealed);
            Assert.IsTrue(Reveal.IsUnexploredOpen(_world, 42, 20));
            Assert.IsFalse(Reveal.IsUnexploredOpen(_world, 25, 20));
        }

        [Test]
        public void Reveal_FloodRespectsLimit()
        {
            for (int x = 10; x < 100; x++) Open(x, 30);
            using var starts = new NativeArray<int2>(new[] { new int2(10, 30) }, Allocator.TempJob);
            Assert.AreEqual(25, Reveal.Flood(_world, starts, 1, 25));
        }

        [Test]
        public void Simulation_DiggingIntoCaveRevealsIt()
        {
            using var content = new TestContent(4, 4, caves: false);
            var sim = new Simulation(content.DB, content.Config, 5);
            try
            {
                var world = sim.World;
                int cx = world.Width / 2;
                int nestY = world.SurfaceY[cx] - content.WorldGen.nestDepth;

                // A hidden cave just right of the nest, 1 cell of stone away from the nest wall.
                int wallX = cx + (int)content.WorldGen.nestRadius.x + 1;
                int caveStart = wallX + 6;
                for (int x = caveStart; x < caveStart + 20; x++)
                    world[x, nestY] = new Cell(world[x, nestY].Material, 0);
                Assert.IsTrue(Reveal.IsUnexploredOpen(world, caveStart + 10, nestY));

                // Dig a horizontal tunnel from the nest into the cave.
                for (int x = cx; x <= caveStart; x++)
                {
                    for (int hit = 0; hit < 8; hit++)
                        sim.Enqueue(new DigCommand(new float2(x + 0.5f, nestY + 0.5f), 1.2f, 1f));
                    sim.Tick();
                }

                Assert.IsTrue(world[caveStart + 19, nestY].IsRevealed, "far end of the breached cave is revealed");
                Assert.IsFalse(Reveal.IsUnexploredOpen(world, caveStart + 10, nestY));
            }
            finally
            {
                sim.Dispose();
            }
        }
    }
}
