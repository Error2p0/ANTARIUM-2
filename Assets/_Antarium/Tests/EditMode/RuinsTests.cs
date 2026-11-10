using System;
using System.IO;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>
    /// Milestone 10.5 on a hand-made 64×64 grid of stone (surface at row 63, so rows below 34 are the Deep biome): the Crystal
    /// Resonator and its cluster, studying relics into lore, the ancient research tier and the Ancient Engine's stages.
    /// </summary>
    public class RuinsTests
    {
        const float Dt = 0.05f;

        TestContent _c;
        WorldGrid _world;
        Colony _colony;
        BuildingSystem _b;
        EventBus _events;

        [SetUp]
        public void SetUp()
        {
            _c = new TestContent(2, 2);
            _world = new WorldGrid(2, 2);
            ushort stone = _c.Index(_c.Stone);
            for (int i = 0; i < _world.Cells.Length; i++)
                _world.Cells[i] = new Cell(stone, Cell.Full, CellFlags.Revealed);
            for (int x = 0; x < _world.Width; x++) _world.SurfaceY[x] = 63;
            _colony = new Colony(_c.DB, _c.Config.colony) { QueenCell = new int2(60, 60) };
            _events = new EventBus();
            _b = new BuildingSystem(_c.DB, _c.Config.logistics, _world, _colony, _events, null, _c.Config.waterworks);
            for (int i = 0; i < _colony.BuildingUnlocked.Length; i++) _colony.BuildingUnlocked[i] = true;
        }

        [TearDown]
        public void TearDown()
        {
            _b.Dispose();
            _world.Dispose();
            _c.Dispose();
        }

        void Set(int x, int y, TerrainMaterialDef mat) => _world[x, y] = new Cell(_c.Index(mat), Cell.Full, CellFlags.Revealed);

        void OpenRect(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                _world[x, y] = new Cell(_world[x, y].Material, 0, CellFlags.Revealed);
        }

        void Run(float seconds)
        {
            int ticks = (int)math.round(seconds / Dt);
            for (int i = 0; i < ticks; i++) _b.Tick(Dt);
        }

        int Place(BuildingDef def, int x, int y)
        {
            Assert.AreEqual(PlaceResult.Ok, _b.Place(_c.BuildingIndex(def), new int2(x, y), default), $"place {def.Id} at {x},{y}");
            _b.CompleteAllBlueprints();
            return _b.At(new int2(x, y));
        }

        [Test]
        public void Resonator_NeedsConductiveRock_AndGrowsWithItsCrystalCluster()
        {
            OpenRect(12, 10, 24, 13);
            int resonator = _c.BuildingIndex(_c.Resonator);
            Assert.AreEqual(PlaceResult.NeedsCrystal, _b.CanPlace(resonator, new int2(16, 10), default), "plain stone all round");

            // Two crystals touch it on the left; two more are reached through a vein of conductive ore.
            Set(11, 10, _c.Crystal);
            Set(11, 11, _c.Crystal);
            Set(11, 12, _c.OreRock);
            Set(10, 12, _c.OreRock);
            Set(9, 12, _c.OreRock);
            int id = Place(_c.Resonator, 12, 10);
            Run(1f);
            var r = _b.ResonanceOf(id);
            Assert.AreEqual(2, r.Crystals);
            Assert.AreEqual(5, r.Cells, "the ore vein belongs to the cluster");
            Assert.AreEqual(0.5f, _b.Get(id).Craft, 1e-4f, "2 of 4 resonance: half power");

            Set(8, 12, _c.Crystal);
            Set(8, 11, _c.Crystal);
            Set(7, 11, _c.Crystal);
            Run(1f);
            Assert.AreEqual(5, _b.ResonanceOf(id).Crystals);
            Assert.AreEqual(1f, _b.Get(id).Craft, 1e-4f, "capped at full power");
            Assert.AreEqual(MachineStatus.Working, _b.Get(id).Status);

            // Knowledge: 1 a second at full power.
            float before = _colony.Inventory.Knowledge;
            Run(10f);
            Assert.AreEqual(10f, _colony.Inventory.Knowledge - before, 1.01f);

            // Mining the vein cuts the far crystals off.
            OpenRect(10, 12, 10, 12);
            Run(1f);
            Assert.AreEqual(2, _b.ResonanceOf(id).Crystals);
            Assert.AreEqual(0.5f, _b.Get(id).Craft, 1e-4f);
        }

        [Test]
        public void Resonator_SpeedsUpMachinesNearIt()
        {
            OpenRect(12, 10, 30, 13);
            Set(11, 10, _c.Crystal);
            Set(11, 11, _c.Crystal);
            Set(11, 12, _c.Crystal);
            Set(11, 13, _c.Crystal);
            int resonator = Place(_c.Resonator, 12, 10);
            int near = Place(_c.Smelter, 16, 10);
            int far = Place(_c.Lab, 27, 10);
            Run(1f);
            Assert.AreEqual(1f, _b.Get(resonator).Craft, 1e-4f);
            Assert.AreEqual(0.5f, _b.MachineOf(near).ResonanceBoost, 1e-4f, "within 6 cells: +50%");
            Assert.AreEqual(0f, _b.MachineOf(far).ResonanceBoost, "too far away");
            Assert.AreEqual(1, _b.MachinesNear(resonator));
        }

        [Test]
        public void StudyingRelics_FindsLore_ThatTheAncientResearchNeeds()
        {
            OpenRect(12, 10, 20, 13);
            int scriptorium = Place(_c.Scriptorium, 12, 10);
            var lore = new LoreSystem(_c.DB, _colony, _events);
            var research = new ResearchSystem(_c.DB, _colony, _events, lore);
            int ancient = _c.DB.TryGetResearchIndex(_c.AncientResearch.Id, out int a) ? a : -1;
            _colony.Inventory.Knowledge = 10f;
            Assert.AreEqual(ResearchResult.NeedsLore, research.Check(ancient));
            Assert.AreEqual(2, research.LoreRequired(ancient));

            ushort relic = _c.Item(_c.Relic);
            Assert.IsTrue(_b.TryFeed(new int2(12, 10), relic));
            Run(1f);
            lore.Update();
            Assert.AreEqual(1, _colony.Stats.Studied[relic], "the relic was studied");
            Assert.AreEqual(1, lore.FoundCount, "the first fragment needs one relic");
            Assert.IsTrue(lore.Found[_c.DB.TryGetLoreIndex(_c.LoreMarks.Id, out int marks) ? marks : -1]);
            Assert.AreEqual(1, lore.Remaining(_c.DB.TryGetLoreIndex(_c.LoreStory.Id, out int story) ? story : -1));
            Assert.AreEqual(ResearchResult.NeedsLore, research.Check(ancient));

            Assert.IsTrue(_b.TryFeed(new int2(12, 10), relic));
            Run(1f);
            lore.Update();
            Assert.AreEqual(2, lore.FoundCount);
            Assert.AreEqual(ResearchResult.Ok, research.Check(ancient), "two fragments: the ancient tier opens");
            Assert.AreEqual(2, _events.Read<LoreFoundEvent>().Length);
            Assert.Greater(_colony.Inventory.Knowledge, 19f, "relics still give knowledge");
            Assert.GreaterOrEqual(scriptorium, 0);

            // Tablets find their own fragment.
            Assert.IsTrue(_b.TryFeed(new int2(12, 10), _c.Item(_c.Tablet)));
            Run(1f);
            lore.Update();
            Assert.AreEqual(3, lore.FoundCount);
        }

        [Test]
        public void Engine_OnlyInItsBiome_OnlyOnce_RestoredStepByStep_AndPermanent()
        {
            int engine = _c.BuildingIndex(_c.Engine);
            var inv = _colony.Inventory;
            ushort bar = _c.Item(_c.Bar), relic = _c.Item(_c.Relic);
            OpenRect(20, 45, 25, 46); // topsoil (depth 17-18)
            OpenRect(20, 10, 25, 11); // the deep biome
            inv.Add(bar, 1, _colony.QueenCell);
            Assert.AreEqual(PlaceResult.WrongBiome, _b.CanPlace(engine, new int2(20, 45), default));
            int id = Place(_c.Engine, 20, 10);
            inv.Add(bar, 1, _colony.QueenCell);
            Assert.AreEqual(PlaceResult.Unique, _b.CanPlace(engine, new int2(23, 10), default), "one per colony");
            Assert.AreEqual(0, _b.Get(id).Stage);
            Assert.IsFalse(_b.EngineRunning);

            // Step 1: paid (one bar), cancelled by removing it (refunded), then built.
            inv.Take(bar, 1);
            Assert.IsFalse(_b.BeginStage(new int2(21, 11)), "no bar");
            Assert.AreEqual(StageResult.CantAfford, _events.Read<StageFailedEvent>()[0].Result);
            inv.Add(bar, 1, _colony.QueenCell);
            Assert.IsTrue(_b.BeginStage(new int2(21, 11)));
            Assert.AreEqual(0, inv.Items[bar]);
            Assert.AreEqual(BuildingState.Blueprint, _b.Get(id).State);
            Assert.IsTrue(_b.Get(id).Restoring);
            Assert.IsTrue(_b.Remove(new int2(20, 10)), "removing cancels the step");
            Assert.AreEqual(1, inv.Items[bar], "refunded");
            Assert.AreEqual(BuildingState.Active, _b.Get(id).State);
            Assert.AreEqual(0, _b.Get(id).Stage);
            Assert.IsTrue(_b.BeginStage(new int2(20, 10)));
            Assert.AreEqual(WorkResult.Working, _b.Work(new int2(20, 10), 0.2f));
            Assert.AreEqual(WorkResult.Completed, _b.Work(new int2(20, 10), 1f));
            Assert.AreEqual(1, _b.Get(id).Stage);
            Assert.IsFalse(_b.EngineRunning);

            // Step 2: a relic and 5 food; the last step wakes it.
            inv.Add(relic, 1, _colony.QueenCell);
            inv.Food = 4f;
            Assert.IsFalse(_b.BeginStage(new int2(20, 10)), "4 food is not enough");
            inv.Food = 10f;
            Assert.IsTrue(_b.BeginStage(new int2(20, 10)));
            Assert.AreEqual(5f, inv.Food, 1e-4f);
            _b.CompleteAllBlueprints();
            Assert.AreEqual(2, _b.Get(id).Stage);
            Assert.IsTrue(_b.EngineRunning);
            Assert.IsFalse(_b.BeginStage(new int2(20, 10)), "nothing left to restore");
            Assert.AreEqual(-1, _b.NextStage(new int2(20, 10)));
            var stages = _events.Read<EngineStageEvent>();
            Assert.AreEqual(EngineStagePhase.Awakened, stages[stages.Length - 1].Phase);

            // Running: 2 knowledge a second; it can't be taken down.
            float before = inv.Knowledge;
            Run(5f);
            Assert.AreEqual(10f, inv.Knowledge - before, 1.01f);
            Assert.IsFalse(_b.Remove(new int2(20, 10)));
            Assert.AreEqual(id, _b.At(new int2(20, 10)));
        }
    }

    /// <summary>Milestone 10.5 in a running colony: rival ants, the mole cricket, tablets in the ruins, the engine's flight bonus, saves.</summary>
    public class RuinsColonyTests : SimulationFixture
    {
        static readonly DateTime SavedAt = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        int EnemyIndex(EnemyDef d) => C.DB.TryGetEnemyIndex(d.Id, out int i) ? i : -1;

        void Open(int2 c) => Sim.SetCell(c, new Cell(Sim.World[c.x, c.y].Material, 0, CellFlags.Revealed | CellFlags.Explored));

        void SetSolid(int2 c, TerrainMaterialDef mat) => Sim.SetCell(c, new Cell(C.Index(mat), Cell.Full, CellFlags.Revealed));

        /// <summary>An open, revealed room in the Deep biome (rows below the topsoil), 2 cells high on a stone floor.</summary>
        int2 DeepRoom()
        {
            int x = Queen.x + 10;
            int y = Sim.World.SurfaceY[x] - 50;
            for (int dx = 0; dx < 6; dx++)
            {
                SetSolid(new int2(x + dx, y - 1), C.Stone);
                Open(new int2(x + dx, y));
                Open(new int2(x + dx, y + 1));
            }
            return new int2(x, y);
        }

        [Test]
        public void RivalRaid_StealsTheMostStockedItem_AndCarriesItOff()
        {
            C.SetCaste(AntCaste.Worker, d => d.attack = 0f);
            C.Config.threats.raidBaseSize = 2;
            C.Config.threats.raidPerAnt = 0f;
            C.Config.threats.raidDistance = new UnityEngine.Vector2Int(20, 30);
            ushort ore = C.Item(C.Ore), bar = C.Item(C.Bar);
            Inv.Add(ore, 10, Queen);
            Inv.Add(bar, 3, Queen);
            Inv.Food = 50f;
            Sim.EnqueueTriggerRaid(rival: true);
            Sim.Tick();
            Assert.IsTrue(Sim.Enemies.RaidIsRival);
            Assert.AreEqual(2, Sim.Enemies.CountRaiders());
            foreach (var e in Sim.Enemies.Enemies) Assert.AreEqual(EnemyIndex(C.Thief), e.Def);

            for (int t = 0; t < 150 * 20 && Sim.Enemies.Count > 0; t++) Sim.Tick();
            Assert.AreEqual(0, Sim.Enemies.Count, "they left again");
            Assert.AreEqual(6, Inv.Items[ore], "two thieves, two ore each (the most stocked item)");
            Assert.AreEqual(3, Inv.Items[bar]);
            Assert.AreEqual(50f, Inv.Food, 1e-3f, "rival ants don't eat");
            Assert.AreEqual(4, Sim.Events.Read<ItemStolenEvent>().Length);
            var raids = Sim.Events.Read<RaidEvent>();
            var last = raids[raids.Length - 1];
            Assert.AreEqual(RaidPhase.Ended, last.Phase, "items were lost: not repelled");
            Assert.IsTrue(last.Rival);
            Assert.AreEqual(4, last.ItemsLost);
            Assert.IsFalse(Sim.Enemies.RaidIsRival);
        }

        [Test]
        public void KilledThief_DropsItsLoot()
        {
            int thief = EnemyIndex(C.Thief);
            int2 cell = Queen + new int2(2, 0);
            Sim.Enemies.Spawn(thief, cell, EnemyState.Leave);
            var e = Sim.Enemies.Enemies[0];
            e.Loot = C.Item(C.Bar);
            e.LootCount = 2;
            e.Health = 0f;
            Sim.Enemies.Enemies[0] = e;
            int before = Sim.Colony.Piles.CountAt(Sim.World.FloorBelow(cell), C.Item(C.Bar));
            Sim.Tick();
            Assert.AreEqual(0, Sim.Enemies.Count);
            Assert.AreEqual(before + 2, Sim.Colony.Piles.CountAt(Sim.World.FloorBelow(cell), C.Item(C.Bar)), "the loot lies where it died");
        }

        [Test]
        public void MoleCricket_TunnelsTowardTheNest_AroundRock_AndBreaksIn()
        {
            C.Config.threats.moleGiveUpSeconds = 60f;
            int mole = EnemyIndex(C.Mole);
            int2 start = Queen + new int2(22, 0);
            // A stone wall (too hard for it) right in its way.
            for (int dy = -3; dy <= 3; dy++) SetSolid(Queen + new int2(14, dy), C.Stone);
            Sim.SetCell(start, new Cell(C.Index(C.Dirt), 0, CellFlags.None));
            Sim.Enemies.Spawn(mole, start, EnemyState.Burrow);
            Sim.Tick();
            Assert.IsTrue(Sim.Enemies.AnyBurrowing);

            bool brokeIn = false;
            for (int t = 0; t < 60 * 20 && !brokeIn; t++)
            {
                Sim.Tick();
                foreach (var ev in Sim.Events.Read<DiggerEvent>()) brokeIn |= ev.Phase == DiggerPhase.BrokeIn;
            }
            Assert.IsTrue(brokeIn, "it reached the colony's tunnels");
            Assert.AreEqual(1, Sim.Enemies.Count);
            Assert.AreNotEqual(EnemyState.Burrow, Sim.Enemies.Enemies[0].State, "now it hunts");
            for (int dy = -3; dy <= 3; dy++)
                Assert.IsFalse(Sim.World[Queen.x + 14, Queen.y + dy].IsOpen, "it went around the stone");
            // Its tunnel shows through the fog and joined the explored tunnels.
            var c = Sim.Enemies.Enemies[0].Cell;
            Assert.IsTrue(Sim.World[start.x, start.y].IsRevealed);
            Assert.IsTrue((Sim.World[start.x, start.y].Flags & CellFlags.Explored) != 0);
            Assert.IsTrue(Sim.World[c.x, c.y].IsOpen);
        }

        [Test]
        public void MoleCricket_WalledInByRock_GivesUp()
        {
            C.Config.threats.moleGiveUpSeconds = 2f;
            var room = DeepRoom();
            int2 cell = room + new int2(0, -20); // solid stone all round
            Sim.SetCell(cell, new Cell(C.Index(C.Stone), 0, CellFlags.Revealed));
            Sim.Enemies.Spawn(EnemyIndex(C.Mole), cell, EnemyState.Burrow);
            Run(4f);
            Assert.AreEqual(0, Sim.Enemies.Count);
            Assert.AreEqual(DiggerPhase.GaveUp, Sim.Events.Read<DiggerEvent>()[0].Phase);
        }

        [Test]
        public void TriggerDigger_StartsInSoftGround_AwayFromTheNest()
        {
            C.Config.threats.moleDistance = new UnityEngine.Vector2Int(20, 26);
            Assert.IsTrue(Sim.Enemies.TriggerDigger());
            Assert.AreEqual(1, Sim.Enemies.Count);
            var e = Sim.Enemies.Enemies[0];
            Assert.AreEqual(EnemyState.Burrow, e.State);
            float d = math.distance(new float2(e.Cell), new float2(Queen));
            Assert.GreaterOrEqual(d, 12f);
            Assert.IsTrue(Sim.World[e.Cell.x, e.Cell.y].IsOpen, "its first cell is dug out");
            Assert.AreEqual(DiggerPhase.Appeared, Sim.Events.Read<DiggerEvent>()[0].Phase);
        }

        [Test]
        public void RunningEngine_DoublesTheFlightsGeneticMemory()
        {
            var room = DeepRoom();
            Sim.Colony.BuildingUnlocked[C.BuildingIndex(C.Engine)] = true;
            Inv.Add(C.Item(C.Bar), 2, Queen);
            Inv.Add(C.Item(C.Relic), 1, Queen);
            Inv.Food = 50f;
            Sim.Colony.Princesses = 1;
            Sim.Colony.Stats.MaxDepth = 400;
            Sim.Colony.Stats.KnowledgeEarned = 2000;
            var before = Sim.EstimateFlight();
            Assert.AreEqual(1f, before.EngineFactor);

            Sim.EnqueueBuild(C.BuildingIndex(C.Engine), room, default);
            Sim.Tick();
            Sim.Buildings.CompleteAllBlueprints();
            for (int step = 0; step < 2; step++)
            {
                Sim.EnqueueStage(room);
                Sim.Tick();
                Sim.Buildings.CompleteAllBlueprints();
            }
            Assert.IsTrue(Sim.Buildings.EngineRunning);
            var after = Sim.EstimateFlight();
            Assert.AreEqual(2f, after.EngineFactor, 1e-4f);
            Assert.GreaterOrEqual(after.Memory, before.Memory * 2 - 1);
            Assert.Greater(before.Memory, 0);
        }

        [Test]
        public void Tablets_LieInRuinRoomFloors()
        {
            using var content = new TestContent(4, 4, caves: false);
            content.Deep.rooms = true;
            content.Deep.roomSize = new UnityEngine.Vector2Int(10, 8);
            content.Deep.roomChance = 0.8f;
            content.WorldGen.tabletCount = 3;
            content.WorldGen.tabletSpacing = 12;
            using var world = WorldGenerator.Create(content.DB, 5);
            ushort tablet = content.Index(content.TabletRock);
            int found = 0;
            for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                var cell = world[x, y];
                if (cell.Material != tablet) continue;
                found++;
                Assert.IsFalse(cell.IsOpen);
                Assert.IsTrue(world[x, y + 1].IsOpen, "an open room above it");
                Assert.GreaterOrEqual(world.Depth(x, y), content.Topsoil.thickness, "in the ruins");
            }
            Assert.AreEqual(3, found);
        }

        [Test]
        public void Save_RoundTripsTheEngineLoreThievesAndDiggers()
        {
            var room = DeepRoom();
            Sim.Colony.BuildingUnlocked[C.BuildingIndex(C.Engine)] = true;
            Inv.Add(C.Item(C.Bar), 2, Queen);
            Sim.EnqueueBuild(C.BuildingIndex(C.Engine), room, default);
            Sim.Tick();
            Sim.Buildings.CompleteAllBlueprints();
            Sim.EnqueueStage(room);
            Sim.Tick();
            int engine = Sim.Buildings.At(room);
            Sim.Buildings.Work(room, 0.1f);
            float workLeft = Sim.Buildings.Get(engine).WorkLeft;

            ushort relic = C.Item(C.Relic);
            Sim.Colony.Stats.Studied[relic] = 1;
            Sim.Tick();
            Assert.AreEqual(1, Sim.Lore.FoundCount);

            Sim.Enemies.Spawn(EnemyIndex(C.Thief), Queen + new int2(2, 0), EnemyState.Leave);
            var thief = Sim.Enemies.Enemies[0];
            thief.Loot = C.Item(C.Bar);
            thief.LootCount = 2;
            Sim.Enemies.Enemies[0] = thief;
            int2 burrow = room + new int2(0, -10);
            Sim.SetCell(burrow, new Cell(C.Index(C.Stone), 0, CellFlags.Revealed));
            Sim.Enemies.Spawn(EnemyIndex(C.Mole), burrow, EnemyState.Burrow);

            using var stream = new MemoryStream();
            SaveGame.Save(Sim, stream, SavedAt);
            stream.Position = 0;
            using var loaded = SaveGame.Load(stream, C.DB, C.Config, out _);

            int e = loaded.Buildings.Engine;
            Assert.GreaterOrEqual(e, 0);
            var b = loaded.Buildings.Get(e);
            Assert.AreEqual(0, b.Stage);
            Assert.IsTrue(b.Restoring);
            Assert.AreEqual(BuildingState.Blueprint, b.State);
            Assert.AreEqual(workLeft, b.WorkLeft, 1e-4f);
            Assert.AreEqual(C.Engine.stages[0].buildWork, b.WorkTotal, 1e-4f);
            Assert.AreEqual(1, loaded.Colony.Stats.Studied[relic]);
            Assert.AreEqual(1, loaded.Lore.FoundCount);
            Assert.AreEqual(2, loaded.Enemies.Count);
            Assert.AreEqual(C.Item(C.Bar), loaded.Enemies.Enemies[0].Loot);
            Assert.AreEqual(2, loaded.Enemies.Enemies[0].LootCount);
            Assert.AreEqual(EnemyState.Burrow, loaded.Enemies.Enemies[1].State);

            // The step finishes after loading.
            loaded.Buildings.CompleteAllBlueprints();
            Assert.AreEqual(1, loaded.Buildings.Get(e).Stage);
        }
    }
}
