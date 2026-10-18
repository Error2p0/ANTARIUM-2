using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Tests
{
    public class DiggingTests
    {
        TestContent _content;
        WorldGrid _world;
        EventBus _events;
        NativeList<int2> _broken;

        [SetUp]
        public void SetUp()
        {
            _content = new TestContent(2, 2);
            _world = new WorldGrid(2, 2);
            ushort dirt = _content.Index(_content.Dirt);
            for (int i = 0; i < _world.Cells.Length; i++)
                _world.Cells[i] = new Cell(dirt, Cell.Full, CellFlags.Revealed);
            _events = new EventBus();
            _broken = new NativeList<int2>(16, Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            _broken.Dispose();
            _world.Dispose();
            _content.Dispose();
        }

        int Dig(float2 pos, float radius, float power) =>
            Digging.Apply(_world, _content.DB.MaterialSim, new DigCommand(pos, radius, power), 1f, _events, _broken);

        [Test]
        public void Dig_ReducesDensityByPowerOverHardness()
        {
            Dig(new float2(10.5f, 10.5f), 0.5f, 0.4f);
            Assert.AreEqual(153, _world[10, 10].Density); // 255 - ceil(0.4 * 255)
            Assert.AreEqual(Cell.Full, _world[11, 10].Density);
            Assert.AreEqual(0, _broken.Length);
        }

        [Test]
        public void Dig_BelowOpenThresholdBreaksTheCell()
        {
            Dig(new float2(10.5f, 10.5f), 0.5f, 0.6f); // 255 - 153 = 102 < 128
            Assert.IsTrue(_world[10, 10].IsEmpty, "no half-open cells");
            Assert.AreEqual(1, _broken.Length);
        }

        [Test]
        public void Dig_BreaksCellAndPublishesEventsAndMarksChunk()
        {
            _world.Chunks[0] = ChunkFlags.None;
            Dig(new float2(5.5f, 5.5f), 0.5f, 1f);

            Assert.IsTrue(_world[5, 5].IsEmpty);
            Assert.AreEqual(1, _broken.Length);
            Assert.AreEqual(new int2(5, 5), _broken[0]);
            Assert.AreNotEqual(0, (int)(_world[5, 5].Flags & CellFlags.Explored));
            var events = _events.Read<CellDamagedEvent>();
            Assert.AreEqual(1, events.Length);
            Assert.IsTrue(events[0].Broken);
            Assert.AreEqual(ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute, _world.Chunks[0]);
        }

        [Test]
        public void Dig_HarderMaterialTakesMoreHits()
        {
            _world[3, 3] = new Cell(_content.Index(_content.Stone), Cell.Full, CellFlags.Revealed);
            Dig(new float2(3.5f, 3.5f), 0.5f, 1f);
            Assert.AreEqual(255 - 64, _world[3, 3].Density); // ceil(255 / 4) = 64
        }

        [Test]
        public void Dig_IgnoresBedrockAndUnrevealedCells()
        {
            _world[3, 3] = new Cell(_content.Index(_content.Bedrock), Cell.Full, CellFlags.Revealed);
            _world[6, 6] = new Cell(_content.Index(_content.Dirt), Cell.Full);
            Assert.AreEqual(0, Dig(new float2(3.5f, 3.5f), 0.5f, 100f));
            Assert.AreEqual(0, Dig(new float2(6.5f, 6.5f), 0.5f, 100f));
            Assert.AreEqual(Cell.Full, _world[3, 3].Density);
            Assert.AreEqual(Cell.Full, _world[6, 6].Density);
        }

        [Test]
        public void Dig_OutOfBoundsIsSafe()
        {
            Assert.DoesNotThrow(() => Dig(new float2(-5f, -5f), 3f, 1f));
            Assert.DoesNotThrow(() => Dig(new float2(_world.Width + 1f, 2f), 3f, 1f));
        }

        [Test]
        public void Combo_BuildsWithinWindowAndResetsAfter()
        {
            var config = new DigConfig { comboWindow = 0.5f, comboPerHit = 0.1f, maxCombo = 1.25f };
            var combo = new DigCombo(config);
            combo.RegisterHit(0.0);
            Assert.AreEqual(1f, combo.Multiplier, 1e-5f);
            combo.RegisterHit(0.2);
            combo.RegisterHit(0.4);
            combo.RegisterHit(0.6);
            Assert.AreEqual(1.25f, combo.Multiplier, 1e-5f, "capped at maxCombo");
            combo.Update(2.0);
            Assert.AreEqual(1f, combo.Multiplier, 1e-5f);
            Assert.AreEqual(0, combo.Streak);
        }
    }
}
