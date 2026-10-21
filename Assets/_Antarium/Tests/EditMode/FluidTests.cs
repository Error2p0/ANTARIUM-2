using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>The fluid automaton on its own: a 64x64 world of solid stone with a box carved into it.</summary>
    public class FluidTests
    {
        TestContent _content;
        WorldGrid _world;
        FluidSystem _fluids;
        FluidConfig _config;

        [SetUp]
        public void SetUp()
        {
            _content = new TestContent(2, 2);
            _world = new WorldGrid(2, 2);
            ushort stone = _content.Index(_content.Stone);
            for (int i = 0; i < _world.Cells.Length; i++)
                _world.Cells[i] = new Cell(stone, Cell.Full, CellFlags.Revealed);
            for (int y = 10; y <= 19; y++)
            for (int x = 10; x <= 29; x++)
                _world[x, y] = new Cell(stone, 0, CellFlags.Revealed); // not Explored: nothing evaporates
            _config = new FluidConfig();
            _fluids = new FluidSystem(_world, _config);
        }

        [TearDown]
        public void TearDown()
        {
            _fluids.Dispose();
            _world.Dispose();
            _content.Dispose();
        }

        FluidCell At(int x, int y) => _world.Fluid[_world.Index(x, y)];

        int Run(int steps)
        {
            int reactions = 0;
            for (int i = 0; i < steps; i++) reactions += _fluids.Step().Reactions;
            return reactions;
        }

        [Test]
        public void Water_FallsAndLevelsOutAcrossTheFloor()
        {
            _fluids.Set(new int2(12, 19), FluidKind.Water, 255);
            _fluids.Set(new int2(13, 19), FluidKind.Water, 255);
            Run(600);

            Assert.AreEqual(510, _fluids.Total(FluidKind.Water), "water is conserved");
            int min = 255, max = 0;
            for (int x = 10; x <= 29; x++)
            {
                min = math.min(min, At(x, 10).Level);
                max = math.max(max, At(x, 10).Level);
                Assert.AreEqual(0, At(x, 11).Level, "everything ran down to the floor row");
            }
            // A slope of at most one level (of 255) per cell is a settled state: well under a pixel here.
            Assert.LessOrEqual(max - min, 20, $"the floor row is level ({min}..{max})");
            Assert.Greater(min, 10);

            for (int i = 0; i < 4000 && _fluids.ActiveChunks > 0; i++) _fluids.Step();
            Assert.AreEqual(0, _fluids.ActiveChunks, "settled chunks go to sleep");
        }

        [Test]
        public void Gas_RisesAndSpreadsUnderTheCeiling()
        {
            _fluids.Set(new int2(15, 10), FluidKind.Gas, 255);
            Run(300);
            Assert.AreEqual(255, _fluids.Total(FluidKind.Gas), "unexplored gas does not fade");
            int ceiling = 0;
            for (int x = 10; x <= 29; x++) ceiling += At(x, 19).Level;
            Assert.AreEqual(255, ceiling, "all gas sits in the top row");
        }

        [Test]
        public void Lava_IsThickAndTurnsToStoneAtWater()
        {
            // Lava creeps: after 20 steps it is still mostly where it started, while water has run across the floor.
            _fluids.Set(new int2(10, 10), FluidKind.Lava, 255);
            _fluids.Set(new int2(29, 10), FluidKind.Water, 255);
            Run(20);
            Assert.Greater(At(10, 10).Level, 100, "lava still mostly where it started");
            Assert.Greater(At(20, 10).Level, 0, "water got halfway across");

            int reactions = Run(400);
            Assert.Greater(reactions, 0, "lava and water met");
            Assert.Less(_fluids.Total(FluidKind.Water), 255, "water lost as steam");
        }

        [Test]
        public void DeepWater_BlocksPaths_ShallowDoesNot()
        {
            var nav = new NavGrid(_world);
            _world.Fluid[_world.Index(15, 10)] = new FluidCell(FluidKind.Water, 60);
            _world.Fluid[_world.Index(16, 10)] = new FluidCell(FluidKind.Water, 200);
            _world.Fluid[_world.Index(17, 10)] = new FluidCell(FluidKind.Lava, 10);
            Assert.IsTrue(nav.IsWalkable(new int2(15, 10)), "shallow water");
            Assert.IsFalse(nav.IsWalkable(new int2(16, 10)), "deep water");
            Assert.IsTrue(nav.IsFlooded(new int2(16, 10)));
            Assert.IsFalse(nav.IsWalkable(new int2(17, 10)), "any lava");
        }

        [Test]
        public void Displace_PushesFluidUpWhenTheCellTurnsSolid()
        {
            _world.Fluid[_world.Index(15, 10)] = new FluidCell(FluidKind.Water, 200);
            _world[15, 10] = new Cell(_content.Index(_content.Stone), Cell.Full, CellFlags.Revealed);
            _fluids.Displace(new int2(15, 10));
            Assert.AreEqual(0, At(15, 10).Level);
            Assert.AreEqual(200, At(15, 11).Level);
        }

        [Test]
        public void WorldGen_DropsPocketsToTheFloorAndCarvesRuinRooms()
        {
            using var content = new TestContent(4, 4);
            content.Deep.fluidPockets.Add(new BiomeDef.FluidPocket { kind = FluidKind.Water, scale = 0.05f, threshold = -0.5f });
            using (var world = WorldGenerator.Create(content.DB, 5))
            {
                long water = 0;
                for (int y = 1; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    var f = world.Fluid[world.Index(x, y)];
                    if (f.Kind != FluidKind.Water) continue;
                    water += f.Level;
                    Assert.IsTrue(world[x, y].IsOpen, "fluid only in open cells");
                    var below = world.Fluid[world.Index(x, y - 1)];
                    Assert.IsTrue(!world[x, y - 1].IsOpen || (below.Kind == FluidKind.Water && below.Level == FluidCell.Full),
                        $"water at {x},{y} rests on the floor or on full water");
                }
                Assert.Greater(water, 0, "caves in the deep biome hold water");
            }

            content.Deep.fluidPockets.Clear();
            content.Deep.rooms = true;
            content.Deep.roomChance = 1f;
            using (var world = WorldGenerator.Create(content.DB, 5))
            {
                int open = 0;
                for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    if (world.Depth(x, y) > 60 && world[x, y].IsOpen) open++;
                Assert.Greater(open, 500, "rooms carved in the deep biome");
            }
        }
    }

    /// <summary>Fluids, cave-ins and walls inside a running colony.</summary>
    public class HazardTests : SimulationFixture
    {
        ushort StoneOrDirt(int2 c) => Sim.World[c.x, c.y].Material;

        void Open(int2 c, bool explored = true) =>
            Sim.SetCell(c, new Cell(StoneOrDirt(c), 0, CellFlags.Revealed | (explored ? CellFlags.Explored : 0)));

        void Solid(int2 c, TerrainMaterialDef m) => Sim.SetCell(c, new Cell(C.Index(m), Cell.Full, CellFlags.Revealed));

        /// <summary>A tunnel going right from the royal chamber's floor, outside the royal radius.</summary>
        int2 CarveTunnel(int length)
        {
            for (int x = Queen.x; x <= Queen.x + 6 + length; x++)
            {
                Open(new int2(x, Queen.y));
                Open(new int2(x, Queen.y + 1));
            }
            return new int2(Queen.x + 6, Queen.y);
        }

        [Test]
        public void Ant_SwimsOutOfAFloodedPit()
        {
            var start = CarveTunnel(4);
            int2 pit = start + new int2(2, -1);
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                Open(pit + new int2(x, -y));
                Sim.Fluids.Set(pit + new int2(x, -y), FluidKind.Water, 255);
            }
            Sim.Ants.Spawn(pit + new int2(1, -2), 3);
            int index = Sim.Ants.Count - 1;
            Run(0.5f);
            Assert.AreEqual(AntState.Swimming, Sim.Ants.Ants[index].State);
            Run(8f);

            Assert.AreEqual(0, Sim.Colony.Stats.AntsLost, "nobody drowned");
            var ant = Sim.Ants.Ants[index];
            Assert.IsFalse(new NavGrid(Sim.World).IsFlooded(ant.Cell), "out of the water");
            Assert.Less(ant.Health, Sim.Ants.MaxHealth(AntCaste.Worker), "it took some damage");
        }

        [Test]
        public void Ant_DrownsInASealedPocket()
        {
            int2 p = Queen + new int2(0, -12);
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            {
                Open(p + new int2(x, y), explored: false);
                Sim.Fluids.Set(p + new int2(x, y), FluidKind.Water, 255);
            }
            int before = Sim.Ants.Count;
            Sim.Ants.Spawn(p, 3);
            Run(12f);
            Assert.AreEqual(before, Sim.Ants.Count, "the trapped ant is gone");
            Assert.AreEqual(1, Sim.Colony.Stats.AntsLost);
            bool drowned = false;
            foreach (var e in Sim.Events.Read<AntDiedEvent>()) drowned |= e.Cause == DeathCause.Drowned;
            Assert.IsTrue(drowned);
        }

        [Test]
        public void LavaMeetingWater_BecomesObsidian()
        {
            var start = CarveTunnel(6);
            Sim.Fluids.Set(start + new int2(1, 0), FluidKind.Lava, 255);
            Sim.Fluids.Set(start + new int2(4, 0), FluidKind.Water, 255);
            Run(20f);
            Assert.Greater(CountMaterial(C.Obsidian), 0, "a cell turned to obsidian");
            Assert.Greater(Sim.Events.Read<FluidReactionEvent>().Length, 0);
        }

        [Test]
        public void Sand_CollapsesIntoTheTunnelBelowAndIsOrderedForMining()
        {
            var start = CarveTunnel(6);
            int2 hole = start + new int2(3, 2); // solid ceiling cell above the tunnel
            Solid(hole, C.Dirt);
            Solid(hole + new int2(0, 1), C.Sand);
            Solid(hole + new int2(0, 2), C.Sand);
            Sim.Enqueue(new DigCommand(new float2(hole) + 0.5f, 0.6f, 1000f));
            Run(1f);

            Assert.IsTrue(Sim.World[hole.x, hole.y + 2].IsOpen, "the sand fell out of the ceiling");
            int fallen = 0;
            for (int y = start.y; y <= hole.y + 1; y++)
            for (int x = hole.x - 2; x <= hole.x + 2; x++)
            {
                var cell = Sim.World[x, y];
                if (cell.IsOpen || cell.Material != C.Index(C.Sand)) continue;
                fallen++;
                Assert.IsTrue((cell.Flags & CellFlags.DigDesignated) != 0, "fallen sand is ordered for mining");
                Assert.IsTrue(Sim.Colony.Designated.Contains(Sim.World.Index(x, y)));
            }
            Assert.AreEqual(2, fallen, "both sand cells came down (the second may slide off the first)");
            Assert.Greater(Sim.Events.Read<CaveInEvent>().Length, 0);
        }

        [Test]
        public void Sand_BuriesAnAntBelowIt()
        {
            // A sealed two-cell shaft under a sand cell: the ant cannot get out of the way.
            int2 hole = Queen + new int2(0, -12);
            Solid(hole + new int2(0, 1), C.Sand);
            Solid(hole, C.Dirt);
            Open(hole + new int2(0, -1), explored: false);
            Sim.Ants.Spawn(hole + new int2(0, -1), 3);
            int victim = Sim.Ants.Count - 1;
            Sim.Enqueue(new DigCommand(new float2(hole) + 0.5f, 0.6f, 1000f));
            Run(1f);

            var ant = Sim.Ants.Ants[victim];
            Assert.AreEqual(Sim.Ants.MaxHealth(AntCaste.Worker) - C.Config.combat.caveInDamage, ant.Health, 1e-3f, "buried once");
            Assert.IsTrue(Sim.World[ant.Cell.x, ant.Cell.y].IsOpen, "and pushed up out of the sand");
        }

        [Test]
        public void Sand_IsHeldUpByBuildings()
        {
            var start = CarveTunnel(6);
            int2 belt = start + new int2(3, 1);
            Sim.EnqueueBuild(C.BuildingIndex(C.Belt), belt, new int2(1, 0));
            Sim.Tick();
            Sim.Buildings.CompleteAllBlueprints();
            int2 hole = start + new int2(3, 2);
            Solid(hole, C.Dirt);
            Solid(hole + new int2(0, 1), C.Sand);
            Sim.Enqueue(new DigCommand(new float2(hole) + 0.5f, 0.6f, 1000f));
            Run(1.5f);
            Assert.IsFalse(Sim.World[hole.x, hole.y].IsOpen, "the sand rests on the belt cell");
            Assert.AreEqual(C.Index(C.Sand), Sim.World[hole.x, hole.y].Material);
            Assert.GreaterOrEqual(Sim.Buildings.At(belt), 0, "the belt is untouched");
        }

        [Test]
        public void Wall_TurnsItsCellBackIntoSoil()
        {
            var start = CarveTunnel(4);
            int2 c = start + new int2(2, 0);
            Sim.EnqueueBuild(C.BuildingIndex(C.Wall), c, default);
            Sim.Tick();
            Assert.GreaterOrEqual(Sim.Buildings.At(c), 0, "placed as a blueprint");
            Sim.Buildings.CompleteAllBlueprints();
            Sim.Tick();

            var cell = Sim.World[c.x, c.y];
            Assert.IsFalse(cell.IsOpen, "solid again");
            Assert.AreEqual(C.Index(C.Packed), cell.Material);
            Assert.AreEqual(BuildingSystem.None, Sim.Buildings.At(c), "the wall building is used up");
            Assert.AreEqual(1, Sim.Events.Read<WallBuiltEvent>().Length);
            Assert.AreEqual(PlaceResult.Protected, Sim.Buildings.CanPlace(C.BuildingIndex(C.Wall), Queen + new int2(1, 0), default),
                "no walls in the royal chamber");
        }
    }
}
