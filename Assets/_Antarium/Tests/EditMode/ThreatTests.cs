using System;
using System.IO;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>Enemies, raids, soldiers and defense buildings in a running colony.</summary>
    public class ThreatTests : SimulationFixture
    {
        int EnemyIndex(EnemyDef d) => C.DB.TryGetEnemyIndex(d.Id, out int i) ? i : -1;

        void Open(int2 c, bool explored = true) =>
            Sim.SetCell(c, new Cell(Sim.World[c.x, c.y].Material, 0, CellFlags.Revealed | (explored ? CellFlags.Explored : 0)));

        /// <summary>A tunnel (two cells high unless <paramref name="height"/> says otherwise) going right from the royal chamber's floor.</summary>
        int2 CarveTunnel(int length, int height = 2)
        {
            for (int x = Queen.x; x <= Queen.x + 6 + length; x++)
            for (int y = 0; y < height; y++)
                Open(new int2(x, Queen.y + y));
            return new int2(Queen.x + 6, Queen.y);
        }

        void Build(BuildingDef def, int2 cell)
        {
            Sim.EnqueueBuild(C.BuildingIndex(def), cell, default);
            Sim.Tick();
            Sim.Buildings.CompleteAllBlueprints();
            Sim.Tick();
            Assert.GreaterOrEqual(Sim.Buildings.At(cell), 0, def.Id + " built");
        }

        [Test]
        public void Soldiers_AreLaidByShareOnceUnlocked()
        {
            var colony = Sim.Colony;
            Assert.AreEqual(AntCaste.Worker, colony.NextCaste(10, 0), "locked: always workers");
            Unlock(C.SoldierResearch);
            Assert.IsTrue(colony.SoldiersUnlocked);
            Assert.AreEqual(C.Config.combat.defaultSoldierShare, colony.SoldierShare, 1e-4f);
            Assert.AreEqual(AntCaste.Soldier, colony.NextCaste(10, 0));
            Assert.AreEqual(AntCaste.Worker, colony.NextCaste(13, 3), "3 of 14 are soldiers already");

            Sim.EnqueueSoldierShare(0.37f);
            Sim.Tick();
            Assert.AreEqual(0.4f, colony.SoldierShare, 1e-4f, "snapped to the 10% step");
            Sim.EnqueueSoldierShare(0f);
            Sim.Tick();
            Assert.AreEqual(AntCaste.Worker, colony.NextCaste(10, 0));

            // A soldier egg hatches into a soldier with soldier hit points.
            Sim.EnqueueSoldierShare(0.5f);
            C.Config.colony.eggInterval = 0.5f;
            C.Config.colony.hatchTime = 1f;
            Inv.Food = 80f;
            Run(3f);
            Assert.Greater(Sim.Soldiers, 0);
            foreach (var a in Sim.Ants.Ants)
                if (a.IsSoldier) Assert.AreEqual(Sim.Ants.MaxHealth(AntCaste.Soldier), a.Health, 1e-3f);
        }

        [Test]
        public void Creatures_AppearWhenTheColonyBreaksIntoTheirCave()
        {
            using var content = new TestContent(4, 4, caves: false, creatures: true);
            content.Config.threats.minSpawnDistance = 3;
            using var sim = new Simulation(content.DB, content.Config, 11);
            var queen = sim.Colony.QueenCell;
            // A sealed, unexplored cave deep below the nest.
            int2 cave = queen + new int2(-10, -45);
            for (int y = 0; y < 5; y++)
            for (int x = 0; x < 20; x++)
            {
                var c = cave + new int2(x, y);
                sim.SetCell(c, new Cell(sim.World[c.x, c.y].Material, 0, CellFlags.None));
            }
            Assert.AreEqual(0, sim.Enemies.Count);

            // Dig straight down into it.
            for (int y = queen.y - 1; y >= cave.y + 4; y--)
            {
                sim.Enqueue(new DigCommand(new float2(queen.x + 0.5f, y + 0.5f), 0.7f, 1000f));
                sim.Tick();
            }
            sim.Tick();
            Assert.Greater(sim.Enemies.Count, 0, "creatures live in the deep cave");
            Assert.LessOrEqual(sim.Enemies.Count, 3, "capped per cave");
            foreach (var e in sim.Enemies.Enemies) Assert.AreEqual(EnemyState.Dormant, e.State);
            Assert.AreEqual(1, sim.Events.Read<CreaturesFoundEvent>().Length);
        }

        [Test]
        public void Soldiers_HuntDownACreature_ThatDropsLoot()
        {
            Unlock(C.SoldierResearch);
            for (int i = 0; i < 3; i++) Sim.Ants.Spawn(Queen + new int2(i - 1, 0), 7, AntCaste.Soldier);
            var start = CarveTunnel(10);
            Sim.Enemies.Spawn(EnemyIndex(C.Spider), start + new int2(8, 0), EnemyState.Hunt);
            Run(15f);

            Assert.AreEqual(0, Sim.Enemies.Count, "the spider is dead");
            Assert.AreEqual(1, Sim.Colony.Stats.EnemiesKilled);
            Assert.AreEqual(1, Sim.Events.Read<EnemyKilledEvent>().Length);
            ushort chitin = C.Item(C.Chitin);
            int found = Sim.Colony.Piles.CountItem(chitin) + Inv.Items[chitin] + Inv.InTransit[chitin];
            Assert.AreEqual(2, found, "it dropped two chitin");
            Assert.GreaterOrEqual(Inv.Knowledge, 3f, "and some knowledge");
        }

        [Test]
        public void Creature_KillsWorkers_WhenNobodyFights()
        {
            C.Config.combat.workerAttack = 0f;
            C.Spider.damage = 25f; // two bites kill a worker
            int2 next = Queen + new int2(0, 1);
            Sim.Enemies.Spawn(EnemyIndex(C.Spider), next, EnemyState.Hunt);
            Run(20f);
            Assert.Greater(Sim.Colony.Stats.AntsLost, 0, "the spider killed someone");
            bool killed = false;
            foreach (var e in Sim.Events.Read<AntDiedEvent>())
                killed |= e.Cause == DeathCause.Killed && e.Killer == EnemyIndex(C.Spider);
            Assert.IsTrue(killed);
        }

        [Test]
        public void Raid_ArrivesFromTheSurface_EatsFood_AndLeaves()
        {
            C.Config.combat.workerAttack = 0f;
            C.Config.threats.raidBaseSize = 2;
            C.Config.threats.raidPerAnt = 0f;
            C.Config.threats.raidDistance = new UnityEngine.Vector2Int(20, 30);
            Inv.Food = 50f;
            Sim.EnqueueTriggerRaid();
            Sim.Tick();
            Assert.AreEqual(2, Sim.Enemies.CountRaiders(), "two raiders arrived");
            foreach (var e in Sim.Enemies.Enemies)
                Assert.Less(Sim.World.Depth(e.Cell.x, e.Cell.y), 1, "on the surface");

            bool inChamber = false;
            for (int t = 0; t < 120 * 20 && Sim.Enemies.Count > 0; t++)
            {
                Sim.Tick();
                inChamber |= Sim.Colony.QueenDisturbed;
            }
            Assert.IsTrue(inChamber, "raiders reached the royal chamber and disturbed the queen");
            Assert.AreEqual(0, Sim.Enemies.Count, "they left again");
            Assert.Less(Inv.Food, 45f, "they ate");
            var raids = Sim.Events.Read<RaidEvent>();
            Assert.AreEqual(RaidPhase.Ended, raids[raids.Length - 1].Phase);
            Assert.Greater(raids[raids.Length - 1].FoodLost, 4f);
            Assert.AreEqual(2, Sim.Events.Read<EnemyEscapedEvent>().Length);
        }

        [Test]
        public void Gates_HoldRaidersUntilBroken()
        {
            C.Config.combat.workerAttack = 0f;
            C.Config.threats.raidBaseSize = 2;
            C.Config.threats.raidPerAnt = 0f;
            C.Config.threats.raidDistance = new UnityEngine.Vector2Int(20, 30);
            // Close the nest shaft (3 cells wide) a few cells above the chamber.
            int y = Queen.y + 5;
            var nav = new NavGrid(Sim.World);
            int gates = 0;
            for (int x = Queen.x - 2; x <= Queen.x + 2; x++)
            {
                if (!nav.IsWalkable(new int2(x, y))) continue;
                Build(C.Gate, new int2(x, y));
                gates++;
            }
            Assert.AreEqual(3, gates, "the shaft is three cells wide");

            Sim.EnqueueTriggerRaid();
            bool broken = false, passedBefore = false;
            for (int t = 0; t < 90 * 20 && !broken; t++)
            {
                Sim.Tick();
                broken = Sim.Events.Read<GateBrokenEvent>().Length > 0;
                foreach (var e in Sim.Enemies.Enemies)
                    if (!broken && e.Cell.y < y) passedBefore = true;
            }
            Assert.IsTrue(broken, "the raiders broke a gate");
            Assert.IsFalse(passedBefore, "nobody got past the gates while they stood");
            int id = -1;
            for (int x = Queen.x - 1; x <= Queen.x + 1 && id < 0; x++)
            {
                int b = Sim.Buildings.At(new int2(x, y));
                if (b >= 0 && Sim.Buildings.Get(b).State == BuildingState.Blueprint) id = b;
            }
            Assert.GreaterOrEqual(id, 0, "the broken gate waits for repair as a blueprint");
        }

        [Test]
        public void Trap_HurtsAndHoldsTheFirstEnemy()
        {
            C.Config.combat.workerAttack = 0f;
            var start = CarveTunnel(8, height: 1); // one cell high: the only way is over the trap
            int2 trap = start + new int2(1, 0);
            Build(C.Trap, trap);
            Sim.Enemies.Spawn(EnemyIndex(C.Raider), start + new int2(6, 0), EnemyState.Raid);
            bool held = false;
            for (int t = 0; t < 10 * 20 && !held; t++)
            {
                Sim.Tick();
                held = Sim.Enemies.Count > 0 && Sim.Enemies.Enemies[0].Held > 0f;
            }
            Assert.IsTrue(held, "the raider walked onto the trap and is held");
            Assert.AreEqual(1, Sim.Events.Read<TrapTriggeredEvent>().Length);
            Assert.AreEqual(20f - 15f, Sim.Enemies.Enemies[0].Health, 1e-3f);
            Assert.Greater(Sim.Buildings.Get(Sim.Buildings.At(trap)).Rearm, 0f, "the trap re-arms");
        }

        [Test]
        public void Save_KeepsFluidsEnemiesSoldiersAndDefense()
        {
            Unlock(C.SoldierResearch);
            Sim.EnqueueSoldierShare(0.3f);
            var start = CarveTunnel(10);
            Build(C.Gate, start + new int2(2, 0));
            int gate = Sim.Buildings.At(start + new int2(2, 0));
            Sim.Buildings.DamageGate(gate, 10f);
            Sim.Ants.Spawn(Queen, 5, AntCaste.Soldier);
            Sim.Fluids.Set(start + new int2(6, 0), FluidKind.Water, 200);
            Sim.Enemies.Spawn(EnemyIndex(C.Spider), start + new int2(9, 1), EnemyState.Dormant);
            Sim.Tick();

            using var stream = new MemoryStream();
            SaveGame.Save(Sim, stream, new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc));
            stream.Position = 0;
            using var loaded = SaveGame.Load(stream, C.DB, C.Config, out _);

            for (int i = 0; i < Sim.World.Fluid.Length; i++)
            {
                Assert.AreEqual(Sim.World.Fluid[i].Level, loaded.World.Fluid[i].Level);
                Assert.AreEqual(Sim.World.Fluid[i].Kind, loaded.World.Fluid[i].Kind);
            }
            Assert.AreEqual(Sim.Enemies.Count, loaded.Enemies.Count);
            Assert.AreEqual(Sim.Enemies.Enemies[0].Cell, loaded.Enemies.Enemies[0].Cell);
            Assert.AreEqual(Sim.Enemies.Enemies[0].Def, loaded.Enemies.Enemies[0].Def);
            Assert.AreEqual(Sim.Enemies.SecondsToRaid, loaded.Enemies.SecondsToRaid, 1e-3f);
            Assert.AreEqual(Sim.Soldiers, loaded.Soldiers);
            Assert.IsTrue(loaded.Colony.SoldiersUnlocked);
            Assert.AreEqual(0.3f, loaded.Colony.SoldierShare, 1e-4f);
            for (int i = 0; i < Sim.Ants.Count; i++)
            {
                Assert.AreEqual(Sim.Ants.Ants[i].Caste, loaded.Ants.Ants[i].Caste);
                Assert.AreEqual(Sim.Ants.Ants[i].Health, loaded.Ants.Ants[i].Health, 1e-3f);
            }
            int loadedGate = loaded.Buildings.At(start + new int2(2, 0));
            Assert.AreEqual(Sim.Buildings.Get(gate).Health, loaded.Buildings.Get(loadedGate).Health, 1e-3f);
        }
    }
}
