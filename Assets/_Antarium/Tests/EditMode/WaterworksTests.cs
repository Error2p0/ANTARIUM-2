using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>
    /// Milestone 10.3 on a hand-made 64×64 grid of stone: pumps, water wheels, irrigation and magma heat, with the buildings and
    /// the fluid automaton stepped together (a fluid step every 2 ticks, as in the game).
    /// </summary>
    public class WaterworksTests
    {
        const float Dt = 0.05f;
        const float StepSeconds = Dt * 2f;

        TestContent _c;
        WorldGrid _world;
        Colony _colony;
        BuildingSystem _b;
        FluidSystem _fluids;
        int _tick;

        [SetUp]
        public void SetUp()
        {
            _c = new TestContent(2, 2);
            _world = new WorldGrid(2, 2);
            ushort stone = _c.Index(_c.Stone);
            for (int i = 0; i < _world.Cells.Length; i++)
                _world.Cells[i] = new Cell(stone, Cell.Full, CellFlags.Revealed);
            _colony = new Colony(_c.DB, _c.Config.colony) { QueenCell = new int2(60, 60) };
            _b = new BuildingSystem(_c.DB, _c.Config.logistics, _world, _colony, new EventBus(), null, _c.Config.waterworks);
            _fluids = new FluidSystem(_world, _c.Config.fluids);
            _b.SetFluids(_fluids);
        }

        [TearDown]
        public void TearDown()
        {
            _fluids.Dispose();
            _b.Dispose();
            _world.Dispose();
            _c.Dispose();
        }

        void Open(int x, int y) => _world[x, y] = new Cell(_world[x, y].Material, 0, CellFlags.Revealed);

        void OpenRect(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Open(x, y);
        }

        int Place(BuildingDef def, int x, int y, int2 dir = default)
        {
            Assert.AreEqual(PlaceResult.Ok, _b.Place(_c.BuildingIndex(def), new int2(x, y), dir), $"place {def.Id} at {x},{y}");
            _b.CompleteAllBlueprints();
            return _b.At(new int2(x, y));
        }

        void Fill(int x0, int y0, int x1, int y1, FluidKind kind, byte level = 255)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                _fluids.Set(new int2(x, y), kind, level);
        }

        /// <summary>Runs buildings and fluids together like the simulation does.</summary>
        void Run(float seconds)
        {
            int ticks = (int)math.round(seconds / Dt);
            for (int i = 0; i < ticks; i++)
            {
                _b.Tick(Dt);
                if ((++_tick & 1) != 0) continue;
                _b.ApplyFluidBlocks(_fluids);
                _fluids.Step();
                _b.StepFluids(_fluids, StepSeconds);
            }
        }

        FluidCell At(int x, int y) => _world.Fluid[_world.Index(x, y)];

        int Sum(int x0, int y0, int x1, int y1, FluidKind kind)
        {
            int total = 0;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (At(x, y).Kind == kind) total += At(x, y).Level;
            return total;
        }

        [Test]
        public void Pump_MovesWaterFromBehindToTheFront_AndNothingFlowsThroughIt()
        {
            OpenRect(10, 10, 30, 10);
            int pump = Place(_c.Pump, 20, 10, new int2(1, 0));
            Assert.IsFalse(_world[20, 10].HoldsFluid, "a finished pump blocks fluid in its cell");
            Fill(12, 10, 19, 10, FluidKind.Water);
            long total = _fluids.Total(FluidKind.Water);

            Run(1f);
            Assert.AreEqual(MachineStatus.Working, _b.Get(pump).Status);
            Run(14f);

            Assert.AreEqual(total, _fluids.Total(FluidKind.Water), "pumping keeps the water");
            Assert.AreEqual(0, At(20, 10).Level, "never any water inside the pump");
            Assert.Less(Sum(10, 10, 19, 10, FluidKind.Water), 255, "the intake side is pumped (almost) dry");
            Assert.Greater(Sum(21, 10, 30, 10, FluidKind.Water), total - 255, "the water is on the outlet side");
            Assert.Less(_b.Get(pump).Craft, 0.2f, "nothing much left to pump");
        }

        [Test]
        public void Pump_AboveAFloodedShaft_DrawsTheWaterUpThroughItsHose()
        {
            OpenRect(20, 10, 20, 30);
            Fill(20, 10, 20, 14, FluidKind.Water);
            Place(_c.Pump, 20, 15, new int2(0, 1));
            Run(10f);
            Assert.Less(Sum(20, 10, 20, 14, FluidKind.Water), 128, "the bottom of the shaft is drained");
            Assert.Greater(Sum(20, 16, 20, 30, FluidKind.Water), 4 * 255, "the water stands on top of the pump");
        }

        [Test]
        public void Pump_LeavesLava_AndStopsWhenTheOutletHoldsAnotherFluid()
        {
            OpenRect(10, 10, 30, 10);
            int pump = Place(_c.Pump, 20, 10, new int2(1, 0));
            _fluids.Set(new int2(19, 10), FluidKind.Lava, 200);
            Run(1f);
            Assert.AreEqual(0, Sum(21, 10, 30, 10, FluidKind.Lava), "lava is too thick to pump");
            Assert.AreEqual(MachineStatus.Idle, _b.Get(pump).Status);
            Fill(10, 10, 19, 10, FluidKind.Lava, 0);

            _fluids.Set(new int2(19, 10), FluidKind.Water, 255);
            _fluids.Set(new int2(21, 10), FluidKind.Gas, 255);
            for (int x = 22; x <= 30; x++) _world[x, 10] = new Cell(_world[x, 10].Material, Cell.Full, CellFlags.Revealed); // gas can't escape
            Run(0.5f);
            Assert.AreEqual(MachineStatus.OutputBlocked, _b.Get(pump).Status, "the outlet is full of gas");
        }

        [Test]
        public void WaterWheel_TurnsWhileWaterPoursThrough_AndSpeedsUpMachinesNearby()
        {
            // A 2-wide shaft with the wheel in it, a basin below that drains sideways, a machine room next to the basin.
            OpenRect(20, 10, 21, 40);
            OpenRect(10, 10, 40, 11);
            int wheel = Place(_c.Wheel, 20, 15);
            int smelter = Place(_c.Smelter, 24, 10);
            Fill(20, 17, 21, 40, FluidKind.Water);
            Assert.IsFalse(_world[21, 16].HoldsFluid, "the wheel blocks fluid");

            Run(3f);
            var w = _b.Get(wheel);
            Assert.AreEqual(MachineStatus.Working, w.Status, "water pours through");
            Assert.Greater(w.Craft, 0.7f, "near full power");
            Assert.Greater(Sum(10, 10, 40, 11, FluidKind.Water), 0, "the water lands below the wheel");
            var m = _b.MachineOf(smelter);
            Assert.AreEqual(0.5f * w.Craft, m.WheelBoost, 0.1f, "the machine nearby works faster");

            m.Progress = 0f;
            m.TryInsert(_c.Item(_c.Ore));
            m.TryInsert(_c.Item(_c.Ore));
            Run(0.5f);
            Assert.Greater(m.Progress, 0.6f, "+50% speed (1 s recipe)");

            Run(30f);
            Assert.AreEqual(0f, _b.Get(wheel).Craft, "the water ran out: the wheel stops");
            Assert.AreEqual(0f, m.WheelBoost);
            Assert.AreEqual(0, Sum(20, 15, 21, 16, FluidKind.Water), "never any water inside the wheel");
        }

        [Test]
        public void Irrigation_GivesExtraOutput_AndDrinksTheWater()
        {
            OpenRect(18, 10, 23, 10);
            int farm = Place(_c.Farm, 20, 10);
            _fluids.Set(new int2(22, 10), FluidKind.Water, 255); // no fluid steps here: the puddle stays put
            var m = _b.MachineOf(farm);
            ushort seed = _c.Item(_c.Seed), leaf = _c.Item(_c.Leaf);
            for (int i = 0; i < 4; i++) Assert.IsTrue(m.TryInsert(seed));
            for (int i = 0; i < 90; i++) _b.Tick(Dt); // 4 crafts of 1 s

            Assert.IsTrue(m.Irrigated);
            Assert.AreEqual(4 * 2 + 2 * 2, _colony.Stats.Crafted[leaf], "every second craft yields an extra batch (+50%)");
            Assert.AreEqual(255 - 4 * _c.Config.waterworks.irrigationWater, At(22, 10).Level, "each craft drank some water");

            _fluids.Set(new int2(22, 10), FluidKind.Water, 0);
            for (int i = 0; i < 2; i++) Assert.IsTrue(m.TryInsert(seed));
            for (int i = 0; i < 50; i++) _b.Tick(Dt);
            Assert.IsFalse(m.Irrigated);
            Assert.AreEqual(12 + 2 * 2, _colony.Stats.Crafted[leaf], "dry: no extra");
        }

        [Test]
        public void Heat_MagmaUnderASmelterReplacesTheFuel_AndDoublesItsSpeed()
        {
            OpenRect(18, 10, 23, 11);
            int forge = Place(_c.Forge, 20, 10);
            var m = _b.MachineOf(forge);
            ushort ore = _c.Item(_c.Ore), coal = _c.Item(_c.Coal), bar = _c.Item(_c.Bar);
            _b.Tick(Dt * 12); // a heat check
            Assert.IsFalse(m.Heated);
            Assert.Greater(m.Room(coal), 0, "cold: it wants coal");
            m.TryInsert(ore);
            m.TryInsert(ore);
            for (int i = 0; i < 60; i++) _b.Tick(Dt);
            Assert.AreEqual(0, _colony.Stats.Crafted[bar], "no coal, no iron");

            // Magma rock under one corner of it.
            _world[21, 9] = new Cell(_c.Index(_c.Magma), Cell.Full, CellFlags.Revealed);
            _b.Tick(Dt * 12); // the heat check, then 0.6 s of work at double speed
            Assert.IsTrue(m.Heated);
            Assert.AreEqual(0, m.Room(coal), "heated machines take no fuel");
            Assert.AreEqual(1.2f, m.Progress, 1e-3f, "twice as fast");
            for (int i = 0; i < 17; i++) _b.Tick(Dt);
            Assert.AreEqual(1, _colony.Stats.Crafted[bar], "smelted without coal");

            // Lava beside it heats it too.
            _world[21, 9] = new Cell(_c.Index(_c.Stone), Cell.Full, CellFlags.Revealed);
            _b.Tick(Dt * 12);
            Assert.IsFalse(m.Heated);
            _fluids.Set(new int2(19, 10), FluidKind.Lava, 120);
            _b.Tick(Dt * 12);
            Assert.IsTrue(m.Heated, "lava next to it");
        }
    }
}
