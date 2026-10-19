using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Antarium.Gameplay;
using Antarium.Presentation;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>Transport network and machines on a hand-made grid: one long tunnel at y = 20, stock around x = 50.</summary>
    public class TransportTests
    {
        const float Dt = 0.05f;
        const int Row = 20;

        TestContent _c;
        WorldGrid _world;
        Colony _colony;
        EventBus _events;
        BuildingSystem _b;
        ushort _ore, _bar;

        [SetUp]
        public void SetUp()
        {
            _c = new TestContent(2, 2);
            _world = new WorldGrid(2, 2);
            ushort stone = _c.Index(_c.Stone);
            for (int i = 0; i < _world.Cells.Length; i++)
                _world.Cells[i] = new Cell(stone, Cell.Full, CellFlags.Revealed);
            for (int x = 2; x < 62; x++) Open(x, Row);

            _colony = new Colony(_c.DB, _c.Config.colony) { QueenCell = new int2(50, Row) };
            _events = new EventBus();
            _b = new BuildingSystem(_c.DB, _c.Config.logistics, _world, _colony, _events);
            _ore = _c.Item(_c.Ore);
            _bar = _c.Item(_c.Bar);
        }

        [TearDown]
        public void TearDown()
        {
            _b.Dispose();
            _world.Dispose();
            _c.Dispose();
        }

        void Open(int x, int y) => _world[x, y] = new Cell(_world[x, y].Material, 0, CellFlags.Revealed);

        int Index(BuildingDef def) => _c.BuildingIndex(def);

        void Place(BuildingDef def, int x, int y, int2 dir = default, ushort filter = 0)
        {
            Assert.AreEqual(PlaceResult.Ok, _b.Place(Index(def), new int2(x, y), dir, filter), $"place {def.Id} at {x},{y}");
            _b.CompleteAllBlueprints();
        }

        void BeltRow(int fromX, int toX, int y)
        {
            for (int x = fromX; x <= toX; x++) Place(_c.Belt, x, y, new int2(1, 0));
        }

        void Run(float seconds, System.Action perTick = null)
        {
            int ticks = (int)math.round(seconds / Dt);
            for (int i = 0; i < ticks; i++)
            {
                perTick?.Invoke();
                _b.Tick(Dt);
            }
        }

        int Delivered(int2 cell, ushort item)
        {
            int n = 0;
            foreach (var e in _events.Read<ItemDeliveredEvent>())
                if (e.Cell.Equals(cell) && e.Item == item) n++;
            return n;
        }

        /// <summary>Lift going down from just under (x, Row) to y = 11, ending in a storage cell at y = 10.</summary>
        void LiftDownToStorage(int x)
        {
            for (int y = 10; y < Row; y++) Open(x, y);
            for (int y = 11; y < Row; y++) Place(_c.Lift, x, y, new int2(0, -1));
            _colony.StorageCells.Add(_world.Index(x, 10));
            _colony.Inventory.AddStorageCell(new int2(x, 10), _world.Index(x, 10));
            _b.MarkNetworkDirty();
        }

        [Test]
        public void Belt_CarriesItemsIntoTheStock()
        {
            BeltRow(30, 45, Row);
            BeltRow(5, 10, Row); // dead end: leads nowhere
            _b.Tick(Dt);

            Assert.IsFalse(_b.TryCatch(new int2(5, Row), _ore), "a line that leads nowhere takes nothing");
            Assert.IsTrue(_b.TryCatch(new int2(30, Row), _ore));
            Assert.AreEqual(1, _colony.Inventory.InTransit[_ore], "items on belts are reserved like carried items");

            Run(6f);
            Assert.AreEqual(1, _colony.Inventory.Items[_ore]);
            Assert.AreEqual(0, _colony.Inventory.InTransit[_ore]);
        }

        [Test]
        public void Belt_BacksUpWhenTheStockIsFullAndResumes()
        {
            _c.Config.colony.baseItemCapacity = 0;
            _colony.Inventory.RefreshCapacity();
            BeltRow(40, 45, Row);
            _b.Tick(Dt);

            int accepted = 0;
            Run(12f, () => { if (_b.TryCatch(new int2(40, Row), _ore)) accepted++; });
            Assert.AreEqual(12, accepted, "six cells hold two items each");
            Assert.AreEqual(0, _colony.Inventory.Items[_ore]);
            Assert.IsTrue(_b.Get(_b.At(new int2(45, Row))).Jammed, "the end of the line is jammed");

            _c.Config.colony.baseItemCapacity = 100;
            _colony.Inventory.RefreshCapacity();
            Run(8f);
            Assert.AreEqual(12, _colony.Inventory.Items[_ore]);
            Assert.IsFalse(_b.Get(_b.At(new int2(45, Row))).Jammed);
        }

        [Test]
        public void Splitter_SharesItemsBetweenItsOutputs()
        {
            _colony.Inventory.Add(_bar, 10, _colony.QueenCell);
            BeltRow(25, 29, Row);
            Place(_c.Splitter, 30, Row);
            BeltRow(31, 45, Row);
            LiftDownToStorage(30);
            _b.Tick(Dt);

            int fed = 0;
            Run(20f, () => { if (fed < 10 && _b.TryCatch(new int2(25, Row), _ore)) fed++; });
            int right = Delivered(new int2(45, Row), _ore);
            int down = Delivered(new int2(30, 11), _ore);
            Assert.AreEqual(10, right + down);
            Assert.GreaterOrEqual(right, 4);
            Assert.GreaterOrEqual(down, 4);
        }

        [Test]
        public void Filter_SendsTheFilteredItemOneWayAndTheRestOn()
        {
            _colony.Inventory.Add(_bar, 10, _colony.QueenCell);
            BeltRow(25, 29, Row);
            Place(_c.Filter, 30, Row, new int2(0, -1), _bar);
            BeltRow(31, 45, Row);
            LiftDownToStorage(30);
            _b.Tick(Dt);

            int fed = 0;
            Run(20f, () =>
            {
                if (fed < 8 && _b.TryCatch(new int2(25, Row), fed % 2 == 0 ? _ore : _bar)) fed++;
            });
            Assert.AreEqual(4, Delivered(new int2(30, 11), _bar), "bars go down");
            Assert.AreEqual(4, Delivered(new int2(45, Row), _ore), "everything else continues");
            Assert.AreEqual(0, Delivered(new int2(30, 11), _ore));
            Assert.AreEqual(4, _colony.Inventory.ContainerAt(_world.Index(30, 10)).Counts[_bar], "kept in the storage cell at the end of the lift");
        }

        [Test]
        public void Machine_CraftsFromItsBufferAndLeavesAPile()
        {
            Open(35, Row + 1);
            Open(36, Row + 1);
            Place(_c.Smelter, 35, Row);
            _b.Tick(Dt);

            Assert.IsFalse(_b.TryFeed(new int2(35, Row), _bar), "not an input");
            for (int i = 0; i < 4; i++) Assert.IsTrue(_b.TryFeed(new int2(35, Row), _ore));
            Assert.IsFalse(_b.TryFeed(new int2(35, Row), _ore), "buffer holds two crafts' worth");

            Run(2.5f);
            Assert.AreEqual(2, _colony.Piles.CountAt(new int2(36, Row), _bar), "two crafts, product left on the machine floor");
            Assert.AreEqual(MachineStatus.Idle, _b.Get(_b.At(new int2(35, Row))).Status);
        }

        [Test]
        public void Machine_PausesWhenItsOutputPileIsFull()
        {
            _c.Smelter.outputLimit = 1;
            Open(35, Row + 1);
            Open(36, Row + 1);
            Place(_c.Smelter, 35, Row);
            for (int i = 0; i < 4; i++) _b.TryFeed(new int2(35, Row), _ore);

            Run(3f);
            Assert.AreEqual(1, _colony.Piles.CountAt(new int2(36, Row), _bar));
            Assert.AreEqual(MachineStatus.OutputBlocked, _b.Get(_b.At(new int2(35, Row))).Status);
        }

        [Test]
        public void Belts_FeedMachinesAndCarryProductsAway()
        {
            Open(35, Row + 1);
            Open(36, Row + 1);
            BeltRow(30, 34, Row);
            Place(_c.Smelter, 35, Row);
            BeltRow(37, 45, Row); // output belt into the stock
            _b.Tick(Dt);

            var feed = _b.Get(_b.At(new int2(30, Row)));
            Assert.AreEqual(BuildingSystem.Bit(_ore), feed.Mask, "the feed line only carries what the machine takes");
            Assert.IsFalse(_b.TryCatch(new int2(30, Row), _bar));

            int fed = 0;
            Run(12f, () => { if (fed < 4 && _b.TryCatch(new int2(30, Row), _ore)) fed++; });
            Assert.AreEqual(2, _colony.Inventory.Items[_bar], "4 ore -> 2 bars, delivered by the output belt");
            Assert.AreEqual(0, _colony.Piles.Count);
            Assert.AreEqual(0, _colony.Inventory.InTransit[_bar]);
        }

        [Test]
        public void Placement_ChecksSpaceFloorAndCost()
        {
            int smelter = Index(_c.Smelter);
            Assert.AreEqual(PlaceResult.NotOpen, _b.CanPlace(smelter, new int2(38, Row + 1), default), "inside rock");

            for (int x = 10; x <= 11; x++) { Open(x, Row + 1); Open(x, Row - 1); }
            Assert.AreEqual(PlaceResult.NeedsFloor, _b.CanPlace(smelter, new int2(10, Row), default));

            var hidden = _world[20, Row];
            hidden.Flags &= ~CellFlags.Revealed;
            _world[20, Row] = hidden;
            Assert.AreEqual(PlaceResult.Hidden, _b.CanPlace(Index(_c.Belt), new int2(20, Row), new int2(1, 0)));

            Place(_c.Belt, 30, Row, new int2(1, 0));
            Assert.AreEqual(PlaceResult.Unchanged, _b.CanPlace(Index(_c.Belt), new int2(30, Row), new int2(1, 0)));
            Assert.AreEqual(PlaceResult.Occupied, _b.CanPlace(Index(_c.Filter), new int2(30, Row), new int2(1, 0)), "a different building");
            Assert.AreEqual(PlaceResult.Rotated, _b.Place(Index(_c.Belt), new int2(30, Row), new int2(-1, 0)));
            Assert.AreEqual(new int2(-1, 0), _b.Get(_b.At(new int2(30, Row))).Dir);

            // Splitters cost 2 bars in the test content.
            int splitter = Index(_c.Splitter);
            Assert.AreEqual(PlaceResult.CantAfford, _b.Place(splitter, new int2(33, Row), default));
            _colony.Inventory.Add(_bar, 3, _colony.QueenCell);
            Assert.AreEqual(PlaceResult.Ok, _b.Place(splitter, new int2(33, Row), default));
            Assert.AreEqual(1, _colony.Inventory.Items[_bar]);
            Assert.IsTrue(_b.Remove(new int2(33, Row)));
            Assert.AreEqual(3, _colony.Inventory.Items[_bar], "blueprint refunded");
            Assert.AreEqual(BuildingSystem.None, _b.At(new int2(33, Row)));
        }

        [Test]
        public void Removing_ABeltDropsItsItemsAsPiles()
        {
            BeltRow(30, 45, Row);
            _b.Tick(Dt);
            Assert.IsTrue(_b.TryCatch(new int2(30, Row), _ore));
            Assert.IsTrue(_b.Remove(new int2(30, Row)));
            Assert.AreEqual(1, _colony.Piles.CountAt(new int2(30, Row), _ore));
            Assert.AreEqual(0, _colony.Inventory.InTransit[_ore]);
        }
    }

    /// <summary>Workers building, hauling to machines and belts catching drops, in a generated world.</summary>
    public class LogisticsSimulationTests
    {
        TestContent _content;
        Simulation _sim;

        [SetUp]
        public void SetUp()
        {
            _content = new TestContent(4, 4, caves: false);
            var c = _content.Config.colony;
            c.startingWorkers = 6;
            c.startingFood = 50f;
            c.forageInterval = 1000f;
            c.workerUpkeep = 0f;
            c.queenUpkeep = 0f;
            c.eggInterval = 1000f;
            c.fieldRefreshTicks = 1;
            _content.Config.logistics.consumerBias = 0.1f;
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

        bool FindMachineSpot(out int2 origin)
        {
            int2 q = _sim.Colony.QueenCell;
            int smelter = _content.BuildingIndex(_content.Smelter);
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -6; dx <= 6; dx++)
            {
                origin = q + new int2(dx, dy);
                if (_sim.Buildings.CanPlace(smelter, origin, default) == PlaceResult.Ok) return true;
            }
            origin = default;
            return false;
        }

        [Test]
        public void Workers_BuildAMachineAndHaulOreThroughIt()
        {
            Assert.IsTrue(FindMachineSpot(out int2 origin), "room for a 2x2 machine in the nest");
            _sim.EnqueueBuild(_content.BuildingIndex(_content.Smelter), origin, default);
            _sim.Tick();
            int id = _sim.Buildings.At(origin);
            Assert.AreEqual(BuildingState.Blueprint, _sim.Buildings.Get(id).State);

            Run(10f);
            Assert.AreEqual(BuildingState.Active, _sim.Buildings.Get(id).State, "workers built it");

            ushort ore = _content.Item(_content.Ore);
            ushort bar = _content.Item(_content.Bar);
            int x = _sim.Colony.QueenCell.x + 12;
            _sim.Colony.Piles.Add(new int2(x, _sim.World.SurfaceY[x] + 1), ore, 4);

            Run(60f);
            Assert.AreEqual(2, _sim.Buildings.MachineOf(id).Crafted, "all four ore went into the machine (two crafts)");
            Assert.AreEqual(2, _sim.Colony.Inventory.Items[bar], "the bars were hauled to the stock");
            Assert.AreEqual(0, _sim.Colony.Inventory.Items[ore]);
        }

        [Test]
        public void BuildTool_DragLaysABeltAlongTheDragDirection()
        {
            var tool = new BuildTool(_content.DB);
            tool.Select(_content.BuildingIndex(_content.Belt));
            var ghosts = new List<BuildGhost>();
            int2 q = _sim.Colony.QueenCell;
            float2 Cell(int dx, int dy) => new float2(q.x + dx + 0.5f, q.y + dy + 0.5f);
            for (int dx = -3; dx <= 0; dx++) // make sure the row is dug out (the test nest is narrow)
            {
                var c = _sim.World[q.x + dx, q.y];
                _sim.World[q.x + dx, q.y] = new Cell(c.Material, 0, c.Flags | CellFlags.Revealed);
            }

            // Press at the queen, drag left three cells (wobbling up a row), release.
            tool.Update(_sim, true, true, false, Cell(0, 0), ghosts);
            tool.Update(_sim, true, false, false, Cell(-2, 1), ghosts);
            tool.Update(_sim, true, false, false, Cell(-3, 0), ghosts);
            tool.Update(_sim, false, false, false, Cell(-3, 0), ghosts);
            _sim.Tick();

            for (int dx = 0; dx >= -3; dx--)
            {
                int id = _sim.Buildings.At(new int2(q.x + dx, q.y));
                Assert.GreaterOrEqual(id, 0, $"belt at dx {dx}");
                Assert.AreEqual(new int2(-1, 0), _sim.Buildings.Get(id).Dir, "flows the way the drag went, including the first cell");
            }
            Assert.AreEqual(BuildingSystem.None, _sim.Buildings.At(new int2(q.x - 2, q.y + 1)), "locked to the starting row");
            Assert.AreEqual(1, ghosts.Count);

            // Right-drag removes.
            tool.Update(_sim, false, false, true, Cell(-3, 0), ghosts);
            _sim.Tick();
            Assert.AreEqual(BuildingSystem.None, _sim.Buildings.At(new int2(q.x - 3, q.y)));
        }

        [Test]
        public void Workers_FeedAMachineFromTheStock()
        {
            Assert.IsTrue(FindMachineSpot(out int2 origin));
            _sim.EnqueueBuild(_content.BuildingIndex(_content.Smelter), origin, default);
            _sim.Tick();
            _sim.Buildings.CompleteAllBlueprints();
            ushort ore = _content.Item(_content.Ore);
            ushort bar = _content.Item(_content.Bar);
            Assert.AreEqual(0, _sim.Colony.Inventory.Add(ore, 4, _sim.Colony.QueenCell));

            Run(40f);
            Assert.AreEqual(0, _sim.Colony.Inventory.Items[ore], "workers fetched the stored ore for the smelter");
            Assert.AreEqual(2, _sim.Colony.Inventory.Items[bar], "and hauled the bars back");
        }

        [Test]
        public void Carriers_PutDownWhatNothingWillTake()
        {
            ushort ore = _content.Item(_content.Ore);
            _content.Config.colony.baseItemCapacity = 0;
            _sim.Colony.Inventory.RefreshCapacity();
            var ant = _sim.Ants.Ants[0];
            ant.Carry = ore;
            ant.State = AntState.Carrying;
            _sim.Ants.Ants[0] = ant;
            _sim.Colony.Inventory.BeginTransit(ore);

            Run(0.5f);
            Assert.IsFalse(_sim.Ants.Ants[0].IsCarrying, "stock full and no machine: the worker drops it");
            Assert.AreEqual(1, _sim.Colony.Piles.CountItem(ore));
            Assert.AreEqual(0, _sim.Colony.Inventory.InTransit[ore]);
        }

        [Test]
        public void Drops_LandOnABeltBelow()
        {
            var world = _sim.World;
            ushort stone = _content.Index(_content.Stone);
            ushort ore = _content.Item(_content.Ore);
            int2 q = _sim.Colony.QueenCell;
            // A column of the nest beside the queen with open space between a block and the floor.
            int2 floor = default, block = default;
            bool found = false;
            for (int dx = -4; dx <= 4 && !found; dx++)
            {
                if (dx == 0) continue;
                block = new int2(q.x + dx, q.y + 2);
                if (!world[block.x, block.y].IsOpen) continue;
                floor = world.FloorBelow(block);
                found = floor.y < block.y;
            }
            Assert.IsTrue(found, "test setup: open column in the nest");

            _sim.EnqueueBuild(_content.BuildingIndex(_content.Belt), floor, new int2(1, 0));
            _sim.Tick();
            _sim.Buildings.CompleteAllBlueprints();
            _sim.Tick();
            world[block.x, block.y] = new Cell(stone, Cell.Full, CellFlags.Revealed);

            _sim.Enqueue(new DigCommand(new float2(block) + 0.5f, 0.5f, 100f));
            _sim.Tick();
            Assert.AreEqual(0, _sim.Colony.Piles.CountAt(floor, ore), "caught by the belt, not left as a pile");
            Run(2f);
            Assert.AreEqual(1, _sim.Colony.Inventory.Items[ore], "the belt in the royal chamber delivered it");
        }
    }
}
