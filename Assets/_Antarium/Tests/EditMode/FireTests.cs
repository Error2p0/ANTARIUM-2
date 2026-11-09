using System;
using System.IO;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>Fire (10.3) on a hand-made 64×64 grid: a tunnel at y = 20 through stone, with coal laid into its floor.</summary>
    public class FireTests
    {
        const float Step = 0.1f;

        TestContent _c;
        WorldGrid _world;
        FluidSystem _fluids;
        FireSystem _fire;
        ushort _coal, _stone;

        [SetUp]
        public void SetUp()
        {
            _c = new TestContent(2, 2);
            _world = new WorldGrid(2, 2);
            _stone = _c.Index(_c.Stone);
            _coal = _c.Index(_c.CoalRock);
            for (int i = 0; i < _world.Cells.Length; i++)
                _world.Cells[i] = new Cell(_stone, Cell.Full, CellFlags.Revealed);
            for (int x = 10; x <= 40; x++) _world[x, 20] = new Cell(_stone, 0, CellFlags.Revealed);
            _fluids = new FluidSystem(_world, _c.Config.fluids);
            _fluids.SetMaterials(_c.DB.MaterialSim);
            _fire = new FireSystem(_world, _c.Config.fire, _c.DB.MaterialSim, _c.Index(_c.Ash));
        }

        [TearDown]
        public void TearDown()
        {
            _fluids.Dispose();
            _world.Dispose();
            _c.Dispose();
        }

        void Coal(int x, int y) => _world[x, y] = new Cell(_coal, Cell.Full, CellFlags.Revealed);
        FluidCell At(int x, int y) => _world.Fluid[_world.Index(x, y)];
        bool Burning(int x, int y) => At(x, y).Kind == FluidKind.Fire;

        void Run(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                _fluids.Step();
                _fire.Step(_fluids, Step);
            }
        }

        [Test]
        public void Fire_SpreadsAlongCellsThatTouchAir_NeverThroughBuriedOnes()
        {
            for (int x = 15; x <= 25; x++)
            {
                Coal(x, 19); // the tunnel floor
                Coal(x, 17); // buried: two cells under the floor
            }
            _c.Config.fire.spreadPerSecond = 1f;
            Assert.IsFalse(_fire.CanBurn(new int2(20, 17)), "no air: buried coal can't burn");
            Assert.IsTrue(_fire.Ignite(new int2(15, 19), Step));
            Assert.AreEqual(1, _fire.Started.Count);

            int burnt = 0;
            for (int i = 0; i < 400; i++)
            {
                _fluids.Step();
                _fire.Step(_fluids, Step);
                burnt += _fire.BurntOut.Count;
            }
            Assert.AreEqual(11, burnt, "the whole floor seam burnt");
            for (int x = 15; x <= 25; x++)
            {
                Assert.IsFalse(Burning(x, 17), "the buried seam never caught");
                Assert.AreEqual(_c.Index(_c.Ash), _world[x, 19].Material, "burnt into ash");
            }
            Assert.AreEqual(0, _fire.Count);
        }

        [Test]
        public void Fire_GivesOffSmoke_AndBurnsOutAfterItsTime()
        {
            Coal(20, 19);
            _c.Config.fire.spreadPerSecond = 0f;
            _fire.Ignite(new int2(20, 19), Step);
            int levels = _fire.BurnLevels(Step);
            Assert.AreEqual((int)math.ceil(_c.Config.fire.burnSeconds / Step), levels);
            Run(levels - 1);
            Assert.AreEqual(1, _fire.Count, "still burning");
            Assert.Greater(_fluids.Total(FluidKind.Gas), 0, "smoke rose into the tunnel");
            _fluids.Step();
            _fire.Step(_fluids, Step);
            Assert.AreEqual(1, _fire.BurntOut.Count, "burnt out");
            Assert.AreEqual(0, At(20, 19).Level);
        }

        [Test]
        public void Water_PutsAFireOut()
        {
            Coal(20, 19);
            _fire.Ignite(new int2(20, 19), Step);
            _fluids.Set(new int2(20, 20), FluidKind.Water, 200);
            _fire.Step(_fluids, Step);
            Assert.AreEqual(1, _fire.Doused.Count);
            Assert.AreEqual(0, _fire.Count);
            Assert.AreEqual(FluidKind.None, At(20, 19).Kind);
            Assert.AreEqual(200 - _c.Config.fire.extinguishWater, At(20, 20).Level, "some water hissed away");
            Assert.IsFalse(_fire.CanBurn(new int2(20, 19)), "under water nothing burns");
        }

        [Test]
        public void MovingLava_LightsFlammableCellsNextToIt()
        {
            Coal(20, 19);
            _fluids.IgniteChance = 1f;
            _fluids.Set(new int2(20, 20), FluidKind.Lava, 200);
            bool lit = false;
            for (int i = 0; i < 8 && !lit; i++)
            {
                _fluids.Step();
                for (int k = 0; k < _fluids.Ignitions.Length; k++) lit |= _fire.Ignite(_fluids.Ignitions[k], Step);
            }
            Assert.IsTrue(lit);
            Assert.IsTrue(Burning(20, 19));
        }
    }

    /// <summary>Fire inside a whole simulation: ash, burned ants, digging a fire out, saving.</summary>
    public class FireSimulationTests : SimulationFixture
    {
        /// <summary>An open cell in the royal chamber (not the queen's) with solid floor under it, and that floor cell.</summary>
        (int2 cell, int2 floor) FloorSpot()
        {
            for (int dy = 0; dy <= 2; dy++)
            for (int dx = 2; dx <= 6; dx++)
            {
                var c = Queen + new int2(dx, dy);
                var f = c + new int2(0, -1);
                if (Sim.World[c.x, c.y].IsOpen && !Sim.World[f.x, f.y].IsOpen) return (c, f);
            }
            Assert.Fail("no floor next to the queen");
            return default;
        }

        void SetCoal(int2 c) => Sim.SetCell(c, new Cell(C.Index(C.CoalRock), Cell.Full, CellFlags.Revealed));

        [Test]
        public void BurntOutCells_TurnIntoAsh_ThatFallsWhenUndermined()
        {
            C.Config.fire.burnSeconds = 1f;
            C.Config.fire.spreadPerSecond = 0f;
            // A coal cell in the ceiling of a pocket away from the royal chamber (nothing falls into the chamber): once it is
            // ash it has nothing to hold it up.
            var pocket = Queen + new int2(16, 0);
            Sim.SetCell(pocket, new Cell(C.Index(C.Stone), 0, CellFlags.Revealed | CellFlags.Explored));
            var c = pocket + new int2(0, 1);
            SetCoal(c);
            Assert.IsTrue(Sim.Ignite(c));
            Run(3f);
            Assert.AreEqual(0, Sim.Fire.Count);
            var below = c + new int2(0, -1);
            Assert.IsTrue(Sim.World[c.x, c.y].IsOpen, "the ash fell out of the ceiling");
            Assert.AreEqual(C.Index(C.Ash), Sim.World[below.x, below.y].Material, "and lies below, ordered for mining");
            Assert.IsTrue((Sim.World[below.x, below.y].Flags & CellFlags.DigDesignated) != 0);
        }

        [Test]
        public void AntsNextToAFire_AreBurned()
        {
            var (_, floor) = FloorSpot();
            Sim.Tick();
            int ant = -1;
            for (int i = 0; i < Sim.Ants.Count; i++)
            {
                var a = Sim.Ants.Ants[i];
                var under = a.Cell + new int2(0, -1);
                if (Sim.World[under.x, under.y].IsOpen) continue;
                ant = i;
                floor = under;
                break;
            }
            Assert.GreaterOrEqual(ant, 0, "an ant standing on a floor");
            float before = Sim.Ants.Ants[ant].Health;
            SetCoal(floor);
            Assert.IsTrue(Sim.Ignite(floor));
            Sim.Tick();
            Assert.Less(Sim.Ants.Ants[ant].Health, before, "scorched");
        }

        [Test]
        public void DiggingABurningCell_PutsTheFireOut()
        {
            var (_, floor) = FloorSpot();
            SetCoal(floor);
            Sim.Ignite(floor);
            Assert.AreEqual(1, Sim.Fire.Count);
            Sim.Enqueue(new DigCommand(new float2(floor) + 0.5f, 0.4f, 1000f));
            Sim.Tick();
            Assert.IsTrue(Sim.World[floor.x, floor.y].IsOpen);
            Assert.AreEqual(0, Sim.Fire.Count);
            Assert.AreEqual(FluidKind.None, Sim.World.Fluid[Sim.World.Index(floor.x, floor.y)].Kind);
        }

        [Test]
        public void Save_KeepsFires_AndPumpsBlockFluidAgainAfterLoading()
        {
            C.Config.fire.spreadPerSecond = 0f;
            var (cell, floor) = FloorSpot();
            SetCoal(floor);
            Sim.Ignite(floor);
            Assert.AreEqual(PlaceResult.Ok, Sim.Buildings.Place(C.BuildingIndex(C.Pump), cell, new int2(1, 0)));
            Sim.Buildings.CompleteAllBlueprints();
            Assert.IsFalse(Sim.World[cell.x, cell.y].HoldsFluid);

            using var ms = new MemoryStream();
            SaveGame.Save(Sim, ms, DateTime.UtcNow);
            ms.Position = 0;
            using var loaded = SaveGame.Load(ms, C.DB, C.Config, out _);
            Assert.AreEqual(1, loaded.Fire.Count, "the fire burns on");
            Assert.AreEqual(FluidKind.Fire, loaded.World.Fluid[loaded.World.Index(floor.x, floor.y)].Kind);
            Assert.IsFalse(loaded.World[cell.x, cell.y].HoldsFluid, "the pump blocks fluid again");
            Assert.AreEqual(BuildingKind.Pump, loaded.Buildings.Get(loaded.Buildings.At(cell)).Kind);
        }
    }
}
