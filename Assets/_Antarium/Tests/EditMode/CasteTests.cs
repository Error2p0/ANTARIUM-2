using System;
using System.IO;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>The caste mix, each specialist caste's job, nurseries, possession, and saving / offline progress of all of it.</summary>
    public class CasteTests : TunnelFixture
    {
        [Test]
        public void Shares_StayWithinLimits_AndPickTheCasteFurthestBelow()
        {
            var colony = Sim.Colony;
            Assert.IsFalse(colony.CasteUnlocked[(int)AntCaste.Miner], "specialists start locked");
            Unlock(C.SoldierResearch, C.CasteResearch);
            Assert.AreEqual(0.8f, colony.SpecialistShare, 1e-4f, "defaults: soldiers 20%, six specialists 10% each");

            Sim.EnqueueCasteShare(AntCaste.Miner, 0.5f);
            Sim.Tick();
            Assert.AreEqual(0.1f, colony.CasteShare[(int)AntCaste.Miner], 1e-4f, "all specialists together stay at 80% at most");
            Sim.EnqueueCasteShare(AntCaste.Soldier, 0f);
            Sim.EnqueueCasteShare(AntCaste.Miner, 0.3f);
            Sim.Tick();
            Assert.AreEqual(0.3f, colony.CasteShare[(int)AntCaste.Miner], 1e-4f);
            Sim.EnqueueCasteShare(AntCaste.Worker, 0.5f);
            Sim.Tick();
            Assert.AreEqual(0f, colony.CasteShare[(int)AntCaste.Worker], "workers just fill the rest");

            Assert.AreEqual(AntCaste.Miner, colony.NextCaste(Alive(workers: 10)), "the biggest deficit");
            var alive = Alive(workers: 10);
            alive[(int)AntCaste.Miner] = 5;
            Assert.AreNotEqual(AntCaste.Miner, colony.NextCaste(alive), "enough miners already");
        }

        [Test]
        public void Miner_OnlyDigs_AndDigsFast()
        {
            var start = CarveTunnel(12);
            ClearAnts();
            int m = Spawn(start, AntCaste.Miner);
            Sim.Colony.Piles.Add(start + new int2(2, 0), C.Item(C.Ore), 2);
            var target = start + new int2(4, 2);
            Sim.EnqueueDesignate(new float2(target) + 0.5f, 0.5f, true);
            int ticks = 0;
            for (; ticks < 20 * 20 && !IsOpen(target); ticks++)
            {
                Sim.Tick();
                var s = Sim.Ants.Ants[m].State;
                Assert.AreNotEqual(AntState.SeekItem, s, "miners don't haul");
                Assert.AreNotEqual(AntState.Carrying, s);
            }
            Assert.IsTrue(IsOpen(target), "dug out");
            // Worker dig power 0.5 per second against dirt (hardness 1): a miner (x3) is done well before a worker could be.
            Assert.Less(ticks * Sim.TickDuration, 1f / (C.Config.worker.digPower) + 1f);
        }

        [Test]
        public void Hauler_CarriesSeveralAtOnce()
        {
            var start = CarveTunnel(10);
            ClearAnts();
            int h = Spawn(Queen, AntCaste.Hauler);
            ushort ore = C.Item(C.Ore);
            Sim.Colony.Piles.Add(start + new int2(6, 0), ore, 5);
            int most = 0;
            for (int t = 0; t < 30 * 20 && Inv.Items[ore] < 5; t++)
            {
                Sim.Tick();
                most = math.max(most, Sim.Ants.Ants[h].CarryCount);
            }
            Assert.AreEqual(3, most, "a hauler takes three at once");
            Assert.AreEqual(5, Inv.Items[ore], "and brings everything in");
            Assert.AreEqual(0, Inv.InTransit[ore]);
        }

        [Test]
        public void Builder_OnlyBuilds()
        {
            var start = CarveTunnel(10);
            ClearAnts();
            int b = Spawn(Queen, AntCaste.Builder);
            Sim.EnqueueDesignate(new float2(start + new int2(1, 2)) + 0.5f, 0.5f, true);
            Sim.EnqueueBuild(C.BuildingIndex(C.Post), start + new int2(5, 0), default);
            for (int t = 0; t < 15 * 20; t++)
            {
                Sim.Tick();
                Assert.AreNotEqual(AntState.Digging, Sim.Ants.Ants[b].State, "builders don't dig");
            }
            Assert.IsTrue(Sim.Buildings.Get(Sim.Buildings.At(start + new int2(5, 0))).IsActive, "built");
        }

        [Test]
        public void Nurse_CarriesEggsToANursery_WhereTheyHatchFaster()
        {
            Unlock(C.CasteResearch);
            Assert.IsTrue(FindMachineSpot(C.Nursery, out var origin));
            int nursery = BuildNow(C.Nursery, origin);
            ClearAnts();
            Spawn(Queen, AntCaste.Nurse);
            C.Config.colony.hatchTime = 10f;
            var eggCell = Sim.World.FloorBelow(Sim.World.OpenAbove(Queen + new int2(2, 0))); // the chamber floor curves
            Sim.Colony.Eggs.Add(new Egg { Cell = eggCell, Nursery = -1, Caste = AntCaste.Miner });
            Assert.AreEqual(1, Sim.Colony.LooseEggs);

            bool moved = false;
            float seconds = 0f;
            for (; seconds < 9.5f && Sim.Colony.Stats.HatchedCaste[(int)AntCaste.Miner] == 0; seconds += Sim.TickDuration)
            {
                Sim.Tick();
                foreach (var e in Sim.Colony.Eggs)
                    if (e.Nursery == nursery) moved = true;
            }
            Assert.IsTrue(moved, "the nurse put the egg in the nursery");
            Assert.AreEqual(0, Sim.Colony.LooseEggs, "freeing the queen's egg slots");
            Assert.AreEqual(1, Sim.Colony.Stats.HatchedCaste[(int)AntCaste.Miner], "hatched before the normal 10 seconds");
            Assert.AreEqual(1, Sim.AliveByCaste[(int)AntCaste.Miner]);
        }

        [Test]
        public void Scout_SensesHiddenRock()
        {
            var start = CarveTunnel(20);
            ClearAnts();
            Spawn(start + new int2(10, 0), AntCaste.Scout);
            int hidden = 0;
            foreach (var cell in Sim.World.Cells)
                if (!cell.IsRevealed) hidden++;
            Run(6f);
            int after = 0;
            foreach (var cell in Sim.World.Cells)
                if (!cell.IsRevealed) after++;
            Assert.Less(after, hidden, "the scout revealed rock around the tunnel");
            Assert.AreEqual(hidden - after, Sim.Colony.Stats.CellsScouted);
        }

        [Test]
        public void Engineer_TendsAMachine_ThatWorksFaster()
        {
            Unlock(C.FastBelts, C.LabResearch);
            Assert.IsTrue(FindMachineSpot(C.Lab, out var origin));
            int lab = BuildNow(C.Lab, origin);
            var m = Sim.Buildings.MachineOf(lab);
            ClearAnts();
            Spawn(Queen, AntCaste.Engineer);
            Run(4f);
            Assert.AreEqual(1, m.Tenders, "an engineer tends it");
            int crafted = m.Crafted;
            for (int t = 0; t < 4 * 20; t++)
            {
                m.Inputs[0] = m.Data.InputCaps[0]; // never short of ore
                Sim.Tick();
            }
            Assert.GreaterOrEqual(m.Crafted - crafted, 7, "1-second studies at double speed: about 8 in 4 seconds");
        }

        [Test]
        public void Possession_MovesDigsCarriesAndLetsGo()
        {
            var start = CarveTunnel(12);
            ClearAnts();
            Spawn(start, AntCaste.Worker);
            Sim.EnqueuePossess(new float2(start) + 0.5f);
            Sim.Tick();
            int i = Sim.PossessedAnt;
            Assert.AreEqual(0, i);
            Assert.IsTrue(Sim.Ants.Ants[i].Possessed);

            // Stands still without input (no AI), walks where pushed.
            Sim.EnqueueDesignate(new float2(start + new int2(1, 2)) + 0.5f, 0.5f, true);
            Run(1f);
            Assert.AreEqual(start, Sim.Ants.Ants[i].Cell, "no AI while possessed");
            Sim.PossessInput = new int2(1, 0);
            Run(1.5f);
            Assert.Greater(Sim.Ants.Ants[i].Cell.x, start.x + 2);
            Sim.PossessInput = int2.zero;
            Run(0.5f);

            // Bites the rock within reach.
            var ant = Sim.Ants.Ants[i];
            var rock = ant.Cell + new int2(0, 2);
            for (int t = 0; t < 40 && !IsOpen(rock); t++)
            {
                Sim.EnqueuePossessAct(new float2(rock) + 0.5f, 1f);
                Sim.Tick();
            }
            Assert.IsTrue(IsOpen(rock), "dug");

            // Picks up a pile next to it and delivers it in the royal chamber.
            ushort ore = C.Item(C.Ore);
            Sim.Colony.Piles.Add(Sim.Ants.Ants[i].Cell, ore, 1);
            Sim.EnqueuePossessInteract();
            Sim.Tick();
            Assert.AreEqual(ore, Sim.Ants.Ants[i].Carry);
            Sim.PossessInput = new int2(-1, 0);
            for (int t = 0; t < 10 * 20 && !Sim.Colony.IsRoyalCell(Sim.Ants.Ants[i].Cell); t++) Sim.Tick();
            Sim.PossessInput = int2.zero;
            int before = Inv.Items[ore];
            Sim.EnqueuePossessInteract();
            Sim.Tick();
            Assert.AreEqual(before + 1, Inv.Items[ore], "delivered");

            Sim.EnqueueRelease();
            Sim.Tick();
            Assert.AreEqual(-1, Sim.PossessedAnt);
        }

        [Test]
        public void Possession_EndsWhenTheAntDies()
        {
            Sim.EnqueuePossess(Sim.Ants.Ants[0].Pos);
            Sim.Tick();
            int i = Sim.PossessedAnt;
            Assert.GreaterOrEqual(i, 0);
            var a = Sim.Ants.Ants[i];
            a.State = AntState.Dead;
            a.Cause = DeathCause.Crushed;
            Sim.Ants.Ants[i] = a;
            Sim.Events.Flush();
            Sim.Tick();
            Assert.AreEqual(-1, Sim.PossessedAnt);
            bool died = false;
            foreach (var e in Sim.Events.Read<PossessionEvent>()) died |= e.Phase == PossessionPhase.Died;
            Assert.IsTrue(died);
        }

        [Test]
        public void Save_KeepsCastesNurseryEggsAndLoads()
        {
            Unlock(C.SoldierResearch, C.CasteResearch);
            Sim.EnqueueCasteShare(AntCaste.Soldier, 0f);
            Sim.EnqueueCasteShare(AntCaste.Hauler, 0.25f);
            Assert.IsTrue(FindMachineSpot(C.Nursery, out var origin));
            int nursery = BuildNow(C.Nursery, origin);
            var b = Sim.Buildings.Get(nursery);
            Sim.Colony.Eggs.Add(new Egg { Cell = Simulation.NurserySlotCell(b, 1), Nursery = nursery, Slot = 1, Caste = AntCaste.Scout, Age = 3f });
            int h = Spawn(Queen, AntCaste.Hauler);
            var a = Sim.Ants.Ants[h];
            a.Carry = C.Item(C.Ore);
            a.CarryCount = 3;
            a.State = AntState.Carrying;
            Sim.Ants.Ants[h] = a;
            for (int n = 0; n < 3; n++) Inv.BeginTransit(a.Carry);
            int nurse = Spawn(Queen, AntCaste.Nurse);
            var na = Sim.Ants.Ants[nurse];
            na.CarryingEgg = true;
            na.EggCaste = AntCaste.Miner;
            na.EggAge = 5f;
            na.State = AntState.CarryEgg;
            Sim.Ants.Ants[nurse] = na;

            using var stream = new MemoryStream();
            SaveGame.Save(Sim, stream, new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc));
            stream.Position = 0;
            using var loaded = SaveGame.Load(stream, C.DB, C.Config, out _);
            Assert.AreEqual(0.25f, loaded.Colony.CasteShare[(int)AntCaste.Hauler], 1e-4f);
            Assert.AreEqual(0f, loaded.Colony.CasteShare[(int)AntCaste.Soldier], 1e-4f);
            Assert.IsTrue(loaded.Colony.CasteUnlocked[(int)AntCaste.Engineer]);
            var egg = loaded.Colony.Eggs.Find(e => e.Caste == AntCaste.Scout);
            Assert.IsTrue(egg.InNursery, "the egg is still in its nursery");
            Assert.AreEqual(BuildingKind.Nursery, loaded.Buildings.Get(egg.Nursery).Kind);
            Assert.AreEqual(1, egg.Slot);
            int haulers = 0, carryingEggs = 0;
            foreach (var la in loaded.Ants.Ants)
            {
                if (la.Caste == AntCaste.Hauler && la.CarryCount == 3) haulers++;
                if (la.CarryingEgg && la.EggCaste == AntCaste.Miner) carryingEggs++;
            }
            Assert.AreEqual(1, haulers, "the hauler still carries three");
            Assert.AreEqual(1, carryingEggs, "the nurse still carries the egg");
            Assert.AreEqual(3, loaded.Colony.Inventory.InTransit[C.Item(C.Ore)], "all three count as in transit");
        }

        [Test]
        public void Offline_HatchesCastesByShare()
        {
            Unlock(C.CasteResearch);
            foreach (var caste in new[] { AntCaste.Hauler, AntCaste.Nurse, AntCaste.Builder, AntCaste.Scout, AntCaste.Engineer })
                Sim.EnqueueCasteShare(caste, 0f);
            Sim.EnqueueCasteShare(AntCaste.Miner, 0.5f);
            Sim.Tick();
            Inv.Food = 100f;
            C.Config.colony.eggInterval = 6f;
            var report = OfflineProgress.Apply(Sim, 600.0);
            Assert.Greater(report.Hatched, 0);
            Assert.Greater(Sim.AliveByCaste[(int)AntCaste.Miner], 0, "miners hatched while away");
            Assert.AreEqual(Sim.AliveByCaste[(int)AntCaste.Miner], Sim.Colony.Stats.HatchedCaste[(int)AntCaste.Miner]);
        }
    }
}
