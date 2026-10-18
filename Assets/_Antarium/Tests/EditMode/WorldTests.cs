using System;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    public class WorldTests
    {
        [Test]
        public void GridIndex_RoundTripsAndChunksAreContiguous()
        {
            const int chunksX = 3;
            var seen = new bool[chunksX * 32 * 2 * 32];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < chunksX * 32; x++)
            {
                int i = GridIndex.Of(x, y, chunksX);
                Assert.IsFalse(seen[i], "index collision");
                seen[i] = true;
                Assert.AreEqual(new int2(x, y), GridIndex.ToCell(i, chunksX));
            }
            // Chunk (1,0) occupies indices 1024..2047.
            Assert.AreEqual(1024, GridIndex.Of(32, 0, chunksX));
            Assert.AreEqual(2047, GridIndex.Of(63, 31, chunksX));
        }

        [Test]
        public void ContentDB_PutsAirFirstAndRejectsDuplicates()
        {
            using var content = new TestContent();
            Assert.AreEqual(ContentDB.AirIndex, content.Index(content.Air));
            Assert.IsTrue(content.DB.MaterialSim[0].IsAir);
            Assert.Throws<ArgumentException>(() =>
                ContentDB.Build(new[] { content.Air, content.Dirt, content.Dirt }, content.WorldGen));
        }

        [Test]
        public void WorldGen_IsDeterministicPerSeed()
        {
            using var content = new TestContent();
            using var a = WorldGenerator.Create(content.DB, 42);
            using var b = WorldGenerator.Create(content.DB, 42);
            using var c = WorldGenerator.Create(content.DB, 43);

            Assert.IsTrue(SameCells(a, b), "same seed should produce identical worlds");
            Assert.IsFalse(SameCells(a, c), "different seeds should differ");
        }

        [Test]
        public void WorldGen_HasSkyAboveGroundAndBedrockBorder()
        {
            using var content = new TestContent();
            using var world = WorldGenerator.Create(content.DB, 7);
            ushort air = content.Index(content.Air), bedrock = content.Index(content.Bedrock);

            for (int x = 3; x < world.Width - 3; x++)
            {
                Assert.AreEqual(air, world[x, world.Height - 1].Material, $"top row at x={x}");
                Assert.AreEqual(bedrock, world[x, 0].Material, $"bottom row at x={x}");
                Assert.Less(world.SurfaceY[x], world.Height - 1);
            }
            Assert.AreEqual(bedrock, world[0, world.Height / 2].Material);
            Assert.AreEqual(bedrock, world[world.Width - 1, world.Height / 2].Material);
        }

        [Test]
        public void WorldGen_CarvesNestConnectedToSurface()
        {
            using var content = new TestContent(caves: false);
            using var world = WorldGenerator.Create(content.DB, 1);
            int cx = world.Width / 2;
            for (int y = world.SurfaceY[cx] - content.WorldGen.nestDepth; y <= world.SurfaceY[cx]; y++)
                Assert.IsTrue(world[cx, y].IsEmpty, $"nest shaft blocked at y={y}");
        }

        [Test]
        public void WorldGen_UsesBiomeMaterialsByDepth()
        {
            using var content = new TestContent(caves: false);
            using var world = WorldGenerator.Create(content.DB, 3);
            int x = 10;
            var shallow = world[x, world.SurfaceY[x] - 5];
            var deep = world[x, 8];
            Assert.AreEqual(content.Index(content.Dirt), shallow.Material);
            Assert.That(deep.Material, Is.EqualTo(content.Index(content.Stone)).Or.EqualTo(content.Index(content.Crystal)));
        }

        static bool SameCells(WorldGrid a, WorldGrid b)
        {
            for (int i = 0; i < a.Cells.Length; i++)
            {
                var ca = a.Cells[i];
                var cb = b.Cells[i];
                if (ca.Material != cb.Material || ca.Density != cb.Density) return false;
            }
            return true;
        }
    }
}
