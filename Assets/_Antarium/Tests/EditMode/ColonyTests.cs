using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Tests
{
    public class NavigationTests
    {
        TestContent _content;
        WorldGrid _world;

        [SetUp]
        public void SetUp()
        {
            _content = new TestContent(2, 2);
            _world = new WorldGrid(2, 2);
            ushort stone = _content.Index(_content.Stone);
            for (int i = 0; i < _world.Cells.Length; i++)
                _world.Cells[i] = new Cell(stone, Cell.Full, CellFlags.Revealed);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            _content.Dispose();
        }

        void Open(int x, int y) => _world[x, y] = new Cell(_world[x, y].Material, 0, CellFlags.Revealed);

        [Test]
        public void Walkable_EverywhereUndergroundButOnlyNearGroundInTheSky()
        {
            for (int y = 10; y < 20; y++)
            for (int x = 10; x < 20; x++)
                Open(x, y);
            ushort air = _content.Index(_content.Air);
            for (int y = 40; y < 64; y++)
            for (int x = 0; x < 64; x++)
                _world[x, y] = new Cell(air, 0, CellFlags.Revealed);

            var nav = new NavGrid(_world);
            Assert.IsTrue(nav.IsWalkable(new int2(15, 10)), "floor");
            Assert.IsTrue(nav.IsWalkable(new int2(10, 15)), "wall");
            Assert.IsTrue(nav.IsWalkable(new int2(15, 15)), "middle of a chamber: the back wall");
            Assert.IsFalse(nav.IsWalkable(new int2(5, 5)), "solid");
            Assert.IsTrue(nav.IsWalkable(new int2(20, 40)), "sky touching the ground");
            Assert.IsFalse(nav.IsWalkable(new int2(20, 45)), "open sky");
        }

        [Test]
        public void Diagonal_WrapsCornersButNotThroughAirOrCracks()
        {
            // L-shaped tunnel: floor at y=10 going right, then a shaft going down at x=20.
            for (int x = 10; x <= 20; x++) Open(x, 10);
            for (int y = 5; y <= 10; y++) Open(20, y);
            var nav = new NavGrid(_world);
            Assert.IsTrue(nav.CanMove(new int2(19, 10), new int2(1, -1)), "around the inside corner via one solid cell");

            // Two open cells touching only diagonally: a crack.
            Open(30, 30);
            Open(31, 31);
            Assert.IsFalse(nav.CanMove(new int2(30, 30), new int2(1, 1)), "squeezing through a crack");

            // Inside a chamber the back wall allows diagonal steps.
            for (int y = 40; y < 46; y++)
            for (int x = 40; x < 46; x++)
                Open(x, y);
            Assert.IsTrue(nav.CanMove(new int2(42, 42), new int2(1, 1)), "diagonal across the back wall");
        }

        [Test]
        public void FlowField_DistancesFollowTheTunnel()
        {
            for (int x = 5; x <= 40; x++) Open(x, 20);
            using var fields = new FlowFields(_world);
            var sources = fields.Sources(FieldKind.Home);
            sources.Add(new int2(5, 20));
            fields.Rebuild(_world);

            Assert.AreEqual(0, fields.Get(FieldKind.Home, _world, new int2(5, 20)));
            Assert.AreEqual(35 * FlowFields.StraightCost, fields.Get(FieldKind.Home, _world, new int2(40, 20)));
            Assert.AreEqual(FlowFields.Unreachable, fields.Get(FieldKind.Home, _world, new int2(50, 50)));

            // Diagonal steps cost more than straight ones (octile distance).
            for (int y = 21; y <= 30; y++)
            for (int x = 5; x <= 15; x++)
                Open(x, y);
            fields.MarkDirty(FieldKind.Home);
            fields.Rebuild(_world);
            Assert.AreEqual(10 * FlowFields.DiagonalCost, fields.Get(FieldKind.Home, _world, new int2(15, 30)), "straight diagonal line across a chamber");
            Assert.IsFalse(fields.IsDirty(FieldKind.Home));
            Assert.AreEqual(FlowFields.Unreachable, fields.Get(FieldKind.Dig, _world, new int2(20, 20)), "field without sources is unreachable");
        }
    }

    public class InventoryTests
    {
        [Test]
        public void Capacity_CountsItemsInTransit()
        {
            using var content = new TestContent();
            var config = new ColonyConfig { baseItemCapacity = 2, startingFood = 0f, baseFoodCapacity = 3f };
            var inv = new Inventory(content.DB, config);
            ushort ore = content.DB.GetItemIndex(content.Ore);
            ushort seed = content.DB.GetItemIndex(content.Seed);

            Assert.IsTrue(inv.CanAccept(ore));
            inv.BeginTransit(ore);
            inv.BeginTransit(ore);
            Assert.IsFalse(inv.CanAccept(ore), "two in transit fill a capacity of 2");
            Assert.IsTrue(inv.Deliver(ore));
            Assert.AreEqual(1, inv.Items[ore]);

            Assert.IsTrue(inv.CanAccept(seed));
            inv.BeginTransit(seed);
            Assert.IsFalse(inv.CanAccept(seed), "food capacity 3, one seed (2) in transit");
            Assert.IsTrue(inv.Deliver(seed));
            Assert.AreEqual(2f, inv.Food, 1e-4f);

            inv.SetStorageCells(10);
            Assert.AreEqual(2 + 10 * config.storageItemsPerCell, inv.ItemCapacity);
        }

        [Test]
        public void Piles_TakeOnlyAcceptableItems()
        {
            using var content = new TestContent();
            var inv = new Inventory(content.DB, new ColonyConfig { baseItemCapacity = 0 });
            ushort ore = content.DB.GetItemIndex(content.Ore);
            var piles = new ItemPiles(content.DB.Items.Length);
            piles.Add(new int2(3, 3), ore, 2);
            Assert.IsFalse(piles.TryTake(new int2(3, 3), inv, out _), "no capacity");

            inv.SetStorageCells(1);
            Assert.IsTrue(piles.TryTake(new int2(3, 3), inv, out ushort taken));
            Assert.AreEqual(ore, taken);
            Assert.AreEqual(1, piles.CountAt(new int2(3, 3), ore));
        }

        [Test]
        public void NumberFormat_UsesSuffixesAndTruncates()
        {
            Assert.AreEqual("0", NumberFormat.Format(0));
            Assert.AreEqual("999", NumberFormat.Format(999.9));
            Assert.AreEqual("1.00K", NumberFormat.Format(1000));
            Assert.AreEqual("1.23K", NumberFormat.Format(1239));
            Assert.AreEqual("12.3K", NumberFormat.Format(12399));
            Assert.AreEqual("999K", NumberFormat.Format(999999));
            Assert.AreEqual("1.00M", NumberFormat.Format(1e6));
            Assert.AreEqual("-4.50B", NumberFormat.Format(-4.5e9));
            Assert.AreEqual("1.23e4", NumberFormat.Format(12345, scientific: true));
        }
    }

    public class ColonySimulationTests
    {
        TestContent _content;
        Simulation _sim;

        [SetUp]
        public void SetUp()
        {
            _content = new TestContent(4, 4, caves: false);
            var c = _content.Config.colony;
            c.startingWorkers = 4;
            c.startingFood = 50f;
            c.forageInterval = 1000f;
            c.workerUpkeep = 0f;
            c.queenUpkeep = 0f;
            c.eggInterval = 1000f;
            c.fieldRefreshTicks = 1;
            _sim = new Simulation(_content.DB, _content.Config, 11);
        }

        [TearDown]
        public void TearDown()
        {
            _sim.Dispose();
            _content.Dispose();
        }

        void Run(float seconds)
        {
            int ticks = (int)(seconds / _sim.TickDuration);
            for (int i = 0; i < ticks; i++) _sim.Tick();
        }

        int2 SurfaceCell(int x) => new(x, _sim.World.SurfaceY[x] + 1);

        [Test]
        public void Queen_SitsOnTheNestFloor()
        {
            var nav = new NavGrid(_sim.World);
            Assert.IsTrue(nav.IsWalkable(_sim.Colony.QueenCell));
            Assert.IsTrue(nav.IsSolid(_sim.Colony.QueenCell + new int2(0, -1)));
            Assert.AreEqual(4, _sim.Ants.Count);
        }

        [Test]
        public void Workers_HaulSurfaceItemsToTheQueen()
        {
            ushort ore = _content.DB.GetItemIndex(_content.Ore);
            int x = _sim.Colony.QueenCell.x + 12;
            _sim.Colony.Piles.Add(SurfaceCell(x), ore, 3);

            Run(40f);

            Assert.AreEqual(3, _sim.Colony.Inventory.Items[ore], "all ore delivered");
            Assert.AreEqual(0, _sim.Colony.Piles.Count);
            Assert.AreEqual(0, _sim.Colony.Inventory.InTransit[ore]);
        }

        [Test]
        public void Workers_MineOrderedCellsAndHaulTheDrops()
        {
            var world = _sim.World;
            ushort stone = _content.Index(_content.Stone);
            ushort ore = _content.DB.GetItemIndex(_content.Ore);
            int2 q = _sim.Colony.QueenCell;

            // The first wall cell right of the queen (one row up, where the oval chamber is wider):
            // replace it with revealed stone and order it mined.
            var target = new int2(q.x, q.y + 1);
            while (world[target.x, target.y].IsOpen) target.x++;
            world[target.x, target.y] = new Cell(stone, Cell.Full, CellFlags.Revealed);
            _sim.EnqueueDesignate(new float2(target) + 0.5f, 0.5f, true);
            _sim.Tick();
            Assert.AreEqual(1, _sim.Colony.Designated.Count);

            Run(40f);

            Assert.IsTrue(world[target.x, target.y].IsEmpty, "ordered cell was dug out");
            Assert.AreEqual(0, _sim.Colony.Designated.Count);
            Assert.AreEqual(1, _sim.Colony.Inventory.Items[ore], "stone dropped ore that was delivered");
        }

        [Test]
        public void Queen_LaysEggsThatHatchIntoWorkers()
        {
            var c = _content.Config.colony;
            c.eggInterval = 0.5f;
            c.hatchTime = 2f;
            c.eggFoodCost = 5f;
            c.maxEggs = 2;
            float food = _sim.Colony.Inventory.Food;

            Run(1.2f);
            Assert.AreEqual(2, _sim.Colony.Eggs.Count, "capped at maxEggs");
            Assert.AreEqual(food - 10f, _sim.Colony.Inventory.Food, 1e-3f);

            Run(2.5f);
            Assert.GreaterOrEqual(_sim.Ants.Count, 6);
        }

        [Test]
        public void Starvation_SlowsColonyAndStopsEggs()
        {
            var c = _content.Config.colony;
            c.queenUpkeep = 60000f;
            c.eggInterval = 0.1f;
            Run(0.5f);
            Assert.IsTrue(_sim.Colony.Starving);
            Assert.AreEqual(0f, _sim.Colony.Inventory.Food);
            Assert.AreEqual(0, _sim.Colony.Eggs.Count);
            Assert.Less(_sim.Colony.SpeedFactor, 1f);
        }

        [Test]
        public void Drops_FallToTheFloor()
        {
            var world = _sim.World;
            ushort stone = _content.Index(_content.Stone);
            ushort ore = _content.DB.GetItemIndex(_content.Ore);
            int2 q = _sim.Colony.QueenCell;
            // A stone block placed in the open column above the queen; the floor is 3 cells below it.
            var block = new int2(q.x, q.y + 3);
            world[block.x, block.y] = new Cell(stone, Cell.Full, CellFlags.Revealed);
            for (int y = q.y; y < block.y; y++)
                Assert.IsTrue(world[block.x, y].IsOpen, "test setup: open below the block");

            _sim.Enqueue(new DigCommand(new float2(block) + 0.5f, 0.5f, 100f));
            _sim.Tick();

            var inv = _sim.Colony.Inventory;
            Assert.AreEqual(1, _sim.Colony.Piles.CountAt(new int2(block.x, q.y), ore) + inv.InTransit[ore] + inv.Items[ore],
                "ore landed on the chamber floor (or was already picked up)");
        }
    }
}
