using System.IO;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>
    /// Milestone 10.1 on a hand-made grid (one tunnel at y = 20, queen at x = 50): chests, storage filters, unloaders,
    /// reserves, recipe choice, belt upgrades and routers next to routers.
    /// </summary>
    public class StorageLogisticsTests
    {
        const float Dt = 0.05f;
        const int Row = 20;

        TestContent _c;
        WorldGrid _world;
        Colony _colony;
        EventBus _events;
        BuildingSystem _b;
        ushort _ore, _bar, _chitin;

        Inventory Inv => _colony.Inventory;

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
            _chitin = _c.Item(_c.Chitin);
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

        void Run(float seconds)
        {
            int ticks = (int)math.round(seconds / Dt);
            for (int i = 0; i < ticks; i++) _b.Tick(Dt);
        }

        StockContainer ContainerAt(int x, int y) => Inv.ContainerAt(_world.Index(x, y));

        void AddStorage(int x, int y)
        {
            var cell = _world[x, y];
            cell.Flags |= CellFlags.Storage;
            _world[x, y] = cell;
            _colony.StorageCells.Add(_world.Index(x, y));
            Inv.AddStorageCell(new int2(x, y), _world.Index(x, y));
            _b.MarkNetworkDirty();
        }

        /// <summary>Opens a 2×2 room with its bottom-left at (x, Row) for a machine.</summary>
        void OpenRoom(int x)
        {
            for (int dx = 0; dx < 2; dx++) Open(x + dx, Row + 1);
        }

        [Test]
        public void Chest_HoldsWhatBeltsBring_AndItsFilterSendsTheRestElsewhere()
        {
            BeltRow(30, 39, Row);
            Place(_c.Chest, 40, Row);
            var chest = ContainerAt(40, Row);
            Assert.IsNotNull(chest, "a finished chest is a container");
            Assert.IsTrue(chest.IsChest);
            Assert.AreEqual(4, chest.Capacity);
            Assert.IsTrue(_colony.IsStockCell(new int2(40, Row), _world.Index(40, Row)), "items delivered on a chest go into the stock");
            _b.Tick(Dt);

            Assert.IsTrue(_b.TryCatch(new int2(30, Row), _ore));
            Run(4f);
            Assert.AreEqual(1, chest.Counts[_ore], "the line ends at the chest and fills it");

            Inv.SetAllow(chest, BuildingSystem.Bit(_bar));
            Assert.IsTrue(_b.TryCatch(new int2(30, Row), _ore));
            Run(4f);
            Assert.AreEqual(1, chest.Counts[_ore], "a chest that only takes bars gets no more ore");
            Assert.AreEqual(1, Inv.Royal.Counts[_ore], "the ore went to the royal chamber instead");
            Assert.IsTrue(_b.TryCatch(new int2(30, Row), _bar));
            Run(4f);
            Assert.AreEqual(1, chest.Counts[_bar]);

            // Removing the chest moves its items to other storage.
            Assert.IsTrue(_b.Remove(new int2(40, Row)));
            Assert.IsNull(ContainerAt(40, Row));
            Assert.AreEqual(2, Inv.Royal.Counts[_ore]);
            Assert.AreEqual(1, Inv.Royal.Counts[_bar]);
        }

        [Test]
        public void Chest_CantGoOnAStorageZone()
        {
            AddStorage(35, Row);
            Assert.AreEqual(PlaceResult.Protected, _b.CanPlace(Index(_c.Chest), new int2(35, Row), default));
        }

        [Test]
        public void StorageFilter_KeepsItemsOutAndStopsHaulingWhenNothingTakesThem()
        {
            AddStorage(20, Row);
            AddStorage(21, Row);
            var a = ContainerAt(20, Row);
            Inv.SetAllow(a, BuildingSystem.Bit(_bar));
            Assert.IsTrue(Inv.Store(_ore, new int2(20, Row)));
            Assert.AreEqual(0, a.Counts[_ore], "the filtered cell refuses ore even though it is nearest");
            Assert.AreEqual(1, ContainerAt(21, Row).Counts[_ore]);
            Assert.IsTrue(Inv.Store(_bar, new int2(20, Row)));
            Assert.AreEqual(1, a.Counts[_bar]);

            // Nothing takes chitin: it is not worth hauling and the stock refuses it.
            Inv.SetAllow(ContainerAt(21, Row), BuildingSystem.Bit(_ore));
            Inv.SetAllow(Inv.Royal, 0);
            Assert.IsFalse(Inv.CanAccept(_chitin));
            Assert.IsFalse(Inv.StockAccepts(_chitin));
            Assert.IsFalse(Inv.Store(_chitin, new int2(20, Row)));
            Assert.IsTrue(Inv.CanAccept(_ore));
        }

        [Test]
        public void Unloader_FeedsAMachineFromAChest_AndLeavesTheReserve()
        {
            Place(_c.Chest, 20, Row);
            var chest = ContainerAt(20, Row);
            for (int i = 0; i < 4; i++) Assert.IsTrue(Inv.DeliverInto(chest, _ore) || Inv.Store(_ore, new int2(20, Row)));
            Inv.SetReserve(_ore, 0);
            OpenRoom(22);
            Place(_c.Smelter, 22, Row);
            Place(_c.Unloader, 21, Row, new int2(1, 0), ContentDB.NoItem);
            Assert.AreEqual(4, chest.Counts[_ore]);

            Inv.SetReserve(_ore, 2);
            Run(3f);
            var m = _b.MachineOf(_b.At(new int2(22, Row)));
            Assert.AreEqual(2, chest.Counts[_ore], "two ore are kept for building");
            Assert.GreaterOrEqual(m.Crafted, 1, "the smelter got two ore and smelted them");
            Assert.AreEqual(0, Inv.InTransit[_ore], "unloaded items handed to a machine are no longer in transit");
        }

        [Test]
        public void Unloader_PutsOnlyItsItemOntoABelt()
        {
            Place(_c.Chest, 20, Row);
            var chest = ContainerAt(20, Row);
            Inv.DeliverInto(chest, _ore);
            Inv.DeliverInto(chest, _bar);
            Place(_c.Unloader, 21, Row, new int2(1, 0), _bar);
            BeltRow(22, 45, Row); // to the royal chamber
            Run(8f);
            Assert.AreEqual(1, chest.Counts[_ore], "ore is not its item");
            Assert.AreEqual(0, chest.Counts[_bar]);
            Assert.AreEqual(1, Inv.Royal.Counts[_bar], "the bar rode the belt into the royal chamber");

            Assert.IsTrue(_b.SetFilter(new int2(21, Row), ContentDB.NoItem));
            Run(8f);
            Assert.AreEqual(0, chest.Counts[_ore], "set to any item it takes the ore too");
        }

        [Test]
        public void RecipeChoice_MachineOnlyTakesWhatTheChosenRecipeNeeds()
        {
            OpenRoom(30);
            Place(_c.Mixer, 30, Row);
            var m = _b.MachineOf(_b.At(new int2(30, Row)));
            Assert.Greater(m.Room(_ore), 0);
            Assert.Greater(m.Room(_chitin), 0);

            Assert.IsTrue(_b.SetRecipe(new int2(31, Row), 1)); // Press: chitin -> bar
            Assert.AreEqual(0, m.Room(_ore), "smelting is off: no room for ore");
            Assert.IsFalse(_b.TryFeed(new int2(30, Row), _ore));
            Assert.IsTrue(_b.TryFeed(new int2(30, Row), _chitin));
            Run(1.5f);
            Assert.AreEqual(1, m.Crafted);

            Assert.IsTrue(_b.SetRecipe(new int2(30, Row), -1));
            Assert.Greater(m.Room(_ore), 0, "back to any recipe");
        }

        [Test]
        public void IronBelt_UpgradesASilkBeltInPlace_KeepingItsItems()
        {
            BeltRow(30, 45, Row);
            _b.Tick(Dt);
            Assert.IsTrue(_b.TryCatch(new int2(33, Row), _ore));
            Inv.Add(_bar, 2, _colony.QueenCell);
            int before = Inv.Items[_bar];

            Assert.AreEqual(PlaceResult.Ok, _b.Place(Index(_c.IronBelt), new int2(33, Row), new int2(1, 0)));
            var b = _b.Get(_b.At(new int2(33, Row)));
            Assert.AreEqual(Index(_c.IronBelt), b.Def);
            Assert.AreEqual(BuildingState.Blueprint, b.State, "workers build the upgrade");
            Assert.IsFalse(b.Front.IsEmpty, "the item riding it stays");
            Assert.AreEqual(before - 1, Inv.Items[_bar], "the iron belt was paid");
            Assert.AreEqual(PlaceResult.Unchanged, _b.Place(Index(_c.IronBelt), new int2(33, Row), new int2(1, 0)));

            _b.CompleteAllBlueprints();
            Run(6f);
            Assert.AreEqual(1, Inv.Items[_ore], "the item continued into the stock");
            Assert.AreEqual(PlaceResult.Occupied, _b.CanPlace(Index(_c.Chest), new int2(34, Row), default), "other kinds don't replace belts");
        }

        [Test]
        public void Splitters_NextToEachOther_NeverHandItemsBack()
        {
            // belt -> A -> B -> belts into a smelter (ore only); A also feeds a lift down to storage (anything).
            Inv.Add(_bar, 4, _colony.QueenCell); // splitters cost bars
            BeltRow(25, 29, Row);
            Place(_c.Splitter, 30, Row);
            Place(_c.Splitter, 31, Row);
            BeltRow(32, 35, Row);
            OpenRoom(36);
            Place(_c.Smelter, 36, Row);
            for (int y = 10; y < Row; y++) Open(30, y);
            for (int y = 11; y < Row; y++) Place(_c.Lift, 30, y, new int2(0, -1));
            AddStorage(30, 10);
            _b.Tick(Dt);

            int a = _b.At(new int2(30, Row)), bId = _b.At(new int2(31, Row));
            Assert.AreEqual(0ul, _b.AcceptMask(bId, a) & BuildingSystem.Bit(_bar), "B can't deliver bars without handing them back to A");
            Assert.AreNotEqual(0ul, _b.AcceptMask(bId, a) & BuildingSystem.Bit(_ore), "B delivers ore to the smelter");
            Assert.AreNotEqual(0ul, _b.Get(a).Mask & BuildingSystem.Bit(_bar));

            int caught = 0;
            for (int i = 0; i < 40 && caught < 6; i++)
            {
                if (_b.TryCatch(new int2(25, Row), _bar)) caught++;
                Run(0.5f);
            }
            Run(10f);
            Assert.AreEqual(6, caught);
            Assert.AreEqual(6, ContainerAt(30, 10).Counts[_bar], "every bar went down the lift; none got stuck in B");
            Assert.IsTrue(_b.Get(bId).Front.IsEmpty);
        }
    }

    /// <summary>Milestone 10.1 state in a full simulation: saved and loaded, and the storage filter command.</summary>
    public class StorageLogisticsSaveTests : SimulationFixture
    {
        [Test]
        public void Save_KeepsChestsFiltersReservesAndRecipeChoice()
        {
            ushort ore = C.Item(C.Ore), bar = C.Item(C.Bar);
            var storage = new float2(Queen) + new float2(-2.5f, 0.5f);
            Sim.EnqueueStorageZone(storage, 1.2f, true);
            Sim.Tick();
            Assert.Greater(Sim.Colony.StorageCells.Count, 0, "a storage zone was painted");
            int2 zoneCell = default;
            foreach (int index in Sim.Colony.StorageCells) { zoneCell = GridIndex.ToCell(index, Sim.World.ChunksX); break; }
            Sim.EnqueueStorageFilter(zoneCell, BuildingSystem.Bit(bar));
            Sim.EnqueueReserve(ore, 7);

            Assert.IsTrue(FindMachineSpot(C.Mixer, out var mixer));
            Sim.EnqueueBuild(C.BuildingIndex(C.Mixer), mixer, default);
            Sim.Tick();
            // A free floor spot in the nest for a chest with an unloader to its right.
            var chestCell = new int2(-1, -1);
            for (int y = Queen.y - 2; y <= Queen.y + 2 && chestCell.x < 0; y++)
            for (int x = Queen.x - 6; x <= Queen.x + 6 && chestCell.x < 0; x++)
            {
                var c = new int2(x, y);
                if (Sim.Buildings.CanPlace(C.BuildingIndex(C.Chest), c, default) == PlaceResult.Ok
                    && Sim.Buildings.CanPlace(C.BuildingIndex(C.Unloader), c + new int2(1, 0), new int2(1, 0)) == PlaceResult.Ok)
                    chestCell = c;
            }
            Assert.GreaterOrEqual(chestCell.x, 0, "room for a chest and an unloader");
            Sim.EnqueueBuild(C.BuildingIndex(C.Chest), chestCell, default);
            Sim.EnqueueBuild(C.BuildingIndex(C.Unloader), chestCell + new int2(1, 0), new int2(1, 0), ContentDB.NoItem);
            Sim.Tick();
            Sim.Buildings.CompleteAllBlueprints();
            Sim.EnqueueRecipe(mixer, 1);
            Sim.Tick();
            var inv = Sim.Colony.Inventory;
            var chest = inv.ContainerAt(Sim.World.Index(chestCell.x, chestCell.y));
            Assert.IsNotNull(chest, "the chest was built");
            inv.SetAllow(chest, BuildingSystem.Bit(ore));
            Assert.IsTrue(inv.DeliverInto(chest, ore));
            foreach (var cell in Sim.ZoneCells(zoneCell))
                Assert.IsFalse(inv.ContainerAt(Sim.World.Index(cell.x, cell.y)).Accepts(ore), "the whole zone got the filter");
            int before = Sim.ZoneCells(zoneCell).Count;
            var next = zoneCell + new int2(-1, 0);
            Sim.CarveNow(new float2(next) + 0.5f, 0.6f);
            Sim.EnqueueStorageZone(new float2(next) + 0.5f, 0.5f, true); // grow the zone by one cell
            Sim.Tick();
            var grown = Sim.ZoneCells(zoneCell);
            Assert.Greater(grown.Count, before, "the zone grew");
            foreach (var cell in grown)
                Assert.IsFalse(inv.ContainerAt(Sim.World.Index(cell.x, cell.y)).Accepts(ore), "new cells join the zone's filter");

            using var stream = new MemoryStream();
            SaveGame.Save(Sim, stream, new System.DateTime(2026, 11, 9, 0, 0, 0, System.DateTimeKind.Utc));
            stream.Position = 0;
            using var loaded = SaveGame.Load(stream, C.DB, C.Config, out _);
            var linv = loaded.Colony.Inventory;
            var lchest = linv.ContainerAt(loaded.World.Index(chestCell.x, chestCell.y));
            Assert.IsNotNull(lchest);
            Assert.IsTrue(lchest.IsChest);
            Assert.AreEqual(1, lchest.Counts[ore]);
            Assert.IsFalse(lchest.Accepts(bar));
            Assert.IsFalse(linv.ContainerAt(loaded.World.Index(zoneCell.x, zoneCell.y)).Accepts(ore));
            Assert.AreEqual(7, linv.Reserve[ore]);
            Assert.AreEqual(1, loaded.Buildings.MachineOf(loaded.Buildings.At(mixer)).Choice);
            var unloader = loaded.Buildings.Get(loaded.Buildings.At(chestCell + new int2(1, 0)));
            Assert.AreEqual(ContentDB.NoItem, unloader.Filter, "the unloader still takes any item");
            Assert.AreEqual(inv.Stored, linv.Stored, "no item lost or doubled");
        }
    }
}
