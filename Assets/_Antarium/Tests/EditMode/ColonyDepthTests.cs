using System;
using System.IO;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>10.4: heat and fire ants, glow ants, queen evolutions and the Quickening, job priorities, experience, traits, lanes, saving.</summary>
    public class ColonyDepthTests : TunnelFixture
    {
        static readonly DateTime SavedAt = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        ushort Honeydew => C.Item(C.Honeydew);
        ushort Chitin => C.Item(C.Chitin);

        void Solid(int2 c, TerrainMaterialDef material) => Sim.SetCell(c, new Cell(C.Index(material), 255, CellFlags.Revealed));

        [Test]
        public void Heat_HurtsAntsNextToHotRock_ButNotFireAnts()
        {
            C.Config.combat.regenPerSecond = 0f;
            var start = CarveTunnel(10);
            ClearAnts();
            var spot = start + new int2(3, 1);
            Solid(spot + new int2(0, 1), C.Magma); // hot rock in the ceiling
            int worker = Spawn(spot, AntCaste.Worker);
            int fire = Spawn(spot, AntCaste.Fire);
            Sim.Tick();
            float dt = Sim.TickDuration;
            Assert.AreEqual(40f - C.Config.combat.heatDamage * dt, Sim.Ants.Ants[worker].Health, 1e-3f, "heat next to magma rock");
            Assert.AreEqual(60f, Sim.Ants.Ants[fire].Health, 1e-3f, "fire ants are heat-proof");

            // Kept there, the worker dies of heat.
            var a = Sim.Ants.Ants[worker];
            a.Health = 0.01f;
            a.Cell = a.From = spot + new int2(0, 0);
            Sim.Ants.Ants[worker] = a;
            Sim.Tick();
            bool heat = false;
            foreach (var e in Sim.Events.Read<AntDiedEvent>()) heat |= e.Cause == DeathCause.Heat;
            Assert.IsTrue(heat, "died of heat");
        }

        [Test]
        public void Heat_OfAHotBiomeHurtsEverywhereInIt()
        {
            C.Deep.heat = 2f;
            using var sim = new Simulation(C.DB, C.Config, 5);
            int surface = sim.World.SurfaceY[sim.World.Width / 2];
            int deepRow = surface - C.Topsoil.thickness - 4;
            Assert.AreEqual(0f, sim.Ants.HeatByRow[surface - 4], "topsoil is not hot");
            Assert.AreEqual(2f, sim.Ants.HeatByRow[deepRow], "the deep biome is");

            var cell = new int2(sim.World.Width / 2 + 10, deepRow);
            sim.SetCell(cell, new Cell(C.Index(C.Stone), 0, CellFlags.Revealed | CellFlags.Explored));
            C.Config.combat.regenPerSecond = 0f;
            sim.Ants.Ants.Clear();
            sim.Ants.Spawn(cell, 1, AntCaste.Worker);
            sim.Ants.Spawn(cell, 1, AntCaste.Fire);
            sim.Tick();
            Assert.AreEqual(40f - 2f * sim.TickDuration, sim.Ants.Ants[0].Health, 1e-3f);
            Assert.AreEqual(60f, sim.Ants.Ants[1].Health, 1e-3f);
        }

        [Test]
        public void FireAnt_TakesAQuarterOfLavaDamage_AndNoFireDamage()
        {
            C.Config.combat.regenPerSecond = 0f;
            var start = CarveTunnel(10);
            ClearAnts();
            var spot = start + new int2(3, 0);
            int worker = Spawn(spot, AntCaste.Worker);
            int fire = Spawn(spot, AntCaste.Fire);
            Sim.Fluids.Set(spot, FluidKind.Lava, 200);
            Sim.Tick();
            float lava = C.Config.combat.lavaDamage * Sim.TickDuration;
            Assert.AreEqual(40f - lava, Sim.Ants.Ants[worker].Health, 1e-3f);
            Assert.AreEqual(60f - lava * 0.25f, Sim.Ants.Ants[fire].Health, 1e-3f);

            // A fire next to them: the worker is scorched, the fire ant is not.
            ClearAnts();
            Sim.Fluids.Set(spot, FluidKind.None, 0);
            var coal = spot + new int2(0, -1);
            Solid(coal, C.CoalRock);
            Sim.Fluids.Set(coal, FluidKind.Fire, 200);
            worker = Spawn(spot, AntCaste.Worker);
            fire = Spawn(spot, AntCaste.Fire);
            Sim.Tick();
            Assert.Less(Sim.Ants.Ants[worker].Health, 40f);
            Assert.AreEqual(60f, Sim.Ants.Ants[fire].Health, 1e-3f);
        }

        [Test]
        public void Evolution_PaysFromTheStock_AppliesItsLevels_AndUnlocksCastes()
        {
            int brood = C.EvolutionIndex(C.EvoBrood), lineage = C.EvolutionIndex(C.EvoLineage);
            var evo = Sim.Evolutions;
            Assert.IsFalse(Sim.Colony.CasteUnlocked[(int)AntCaste.Glow], "glow ants start locked");
            Assert.IsFalse(Sim.Colony.CasteUnlocked[(int)AntCaste.Fire]);

            Sim.EnqueueEvolution(lineage);
            Sim.EnqueueEvolution(brood);
            Sim.Tick();
            Assert.AreEqual(0, evo.Levels[lineage], "needs Bigger Broods first");
            Assert.AreEqual(0, evo.Levels[brood], "no honeydew yet");
            var failed = Sim.Events.Read<EvolutionFailedEvent>();
            Assert.AreEqual(EvolutionResult.NeedsPrerequisites, failed[0].Result);
            Assert.AreEqual(EvolutionResult.CantAfford, failed[1].Result);

            Inv.SetReserve(Honeydew, 99); // the queen would eat it
            Inv.Add(Honeydew, 4, Queen);
            Inv.Add(Chitin, 3, Queen);
            Sim.EnqueueEvolution(brood);
            Sim.Tick();
            Assert.AreEqual(1, evo.Levels[brood]);
            Assert.AreEqual(2, Inv.Items[Honeydew], "paid from the stock");
            Assert.AreEqual(1.15f, Sim.Colony.Mods.Mult(StatKind.LayingSpeed), 1e-4f);

            Sim.EnqueueEvolution(lineage);
            Sim.Tick();
            Assert.IsTrue(Sim.Colony.CasteUnlocked[(int)AntCaste.Glow], "level 1: glow ants");
            Assert.IsFalse(Sim.Colony.CasteUnlocked[(int)AntCaste.Fire]);
            Assert.AreEqual(C.GlowCaste.defaultShare, Sim.Colony.CasteShare[(int)AntCaste.Glow], 1e-4f);
            Sim.EnqueueEvolution(lineage);
            Sim.Tick();
            Assert.IsTrue(Sim.Colony.CasteUnlocked[(int)AntCaste.Fire], "level 2: fire ants");
            Sim.EnqueueEvolution(lineage);
            Sim.Tick();
            Assert.AreEqual(2, evo.Levels[lineage], "two levels only");
            Assert.AreEqual(1, Inv.Items[Chitin]);

            Sim.EnqueueEvolution(brood);
            Sim.Tick();
            Assert.AreEqual(2, evo.Levels[brood]);
            Assert.AreEqual(1.3f, Sim.Colony.Mods.Mult(StatKind.LayingSpeed), 1e-4f, "stats add up per level");
            Assert.AreEqual(4, evo.TotalLevels);
        }

        [Test]
        public void Quickening_IsEvolved_AndHatchesEveryEgg()
        {
            int ability = AbilityIndex(C.Quickening);
            Assert.IsFalse(Sim.Colony.AbilityUnlocked[ability], "the queen has to evolve it");
            Inv.SetReserve(Honeydew, 99);
            Inv.Add(Honeydew, 1, Queen);
            Sim.EnqueueEvolution(C.EvolutionIndex(C.EvoQuicken));
            Sim.Tick();
            Assert.IsTrue(Sim.Colony.AbilityUnlocked[ability]);

            Sim.EnqueueAbility(ability, Queen);
            Sim.Tick();
            Assert.AreEqual(AbilityResult.NoEffect, Sim.Events.Read<AbilityFailedEvent>()[0].Result, "nothing to hatch");

            for (int i = 0; i < 3; i++) Sim.Colony.LayEggNow(Queen);
            int ants = Sim.Ants.Count;
            Sim.EnqueueAbility(ability, Queen);
            Sim.Tick();
            Assert.AreEqual(0, Sim.Colony.Eggs.Count, "all hatched");
            Assert.AreEqual(ants + 3, Sim.Ants.Count);
            Assert.Greater(Sim.Abilities.Cooldown[ability], 0f, "recharging");
        }

        AntState FirstJob(int digPriority, int haulPriority)
        {
            var start = new int2(Queen.x + 6, Queen.y);
            ClearAnts();
            Sim.EnqueuePriority(JobKind.Dig, digPriority);
            Sim.EnqueuePriority(JobKind.Haul, haulPriority);
            int w = Spawn(start, AntCaste.Worker);
            Sim.Tick();
            return Sim.Ants.Ants[w].State;
        }

        [Test]
        public void Priorities_SteerWhichJobAnIdleWorkerTakes()
        {
            var start = CarveTunnel(12);
            Sim.Colony.Piles.Add(start + new int2(5, 0), C.Item(C.Ore), 1);
            Sim.EnqueueDesignate(new float2(start + new int2(5, 2)) + 0.5f, 0.5f, true);
            Sim.Tick();
            Assert.AreEqual(AntState.SeekItem, FirstJob(3, 3), "hauling weighs a little less at the same distance");
            Assert.AreEqual(AntState.SeekDig, FirstJob(5, 1), "digging first");
            Assert.AreEqual(5, Sim.Colony.JobPriority[(int)JobKind.Dig]);
            Sim.EnqueuePriority(JobKind.Dig, 9);
            Sim.Tick();
            Assert.AreEqual(5, Sim.Colony.JobPriority[(int)JobKind.Dig], "1..5 only");
        }

        int DigTicks(int ant, int2 target)
        {
            Sim.EnqueueDesignate(new float2(target) + 0.5f, 0.5f, true);
            int digging = 0;
            for (int t = 0; t < 60 * 20 && !IsOpen(target); t++)
            {
                Sim.Tick();
                if (Sim.Ants.Ants[ant].State == AntState.Digging) digging++;
            }
            Assert.IsTrue(IsOpen(target), "dug out");
            return digging;
        }

        [Test]
        public void Experience_TheFirstJobBecomesTheSkill_AndLevelsMakeItFaster()
        {
            var growth = GrowthTick.From(C.Config.growth);
            Assert.AreEqual(1, growth.Level(0f));
            Assert.AreEqual(2, growth.Level(C.Config.growth.levelXp[0]));
            Assert.AreEqual(5, growth.Level(1e6f), "top level");

            var start = CarveTunnel(12);
            ClearAnts();
            int w = Spawn(start, AntCaste.Worker);
            Assert.AreEqual(Jobs.NoSkill, Sim.Ants.Ants[w].Skill);
            int slow = DigTicks(w, start + new int2(2, 2));
            var a = Sim.Ants.Ants[w];
            Assert.IsTrue(a.HasSkill(JobKind.Dig), "its first job");
            Assert.Greater(a.Xp, 0f);

            // Hauling doesn't count toward a digger's experience.
            float xp = a.Xp;
            Sim.Colony.Piles.Add(start + new int2(3, 0), C.Item(C.Ore), 1);
            Run(15f);
            Assert.AreEqual(xp, Sim.Ants.Ants[w].Xp, 1e-4f);

            a = Sim.Ants.Ants[w];
            a.Xp = C.Config.growth.levelXp[3] - 0.01f; // a whisker below level 5
            Sim.Ants.Ants[w] = a;
            int fast = DigTicks(w, start + new int2(5, 2));
            Assert.AreEqual(5, Sim.AntLevel(Sim.Ants.Ants[w]));
            bool levelled = false;
            foreach (var e in Sim.Events.Read<AntLevelUpEvent>()) levelled |= e.Level == 5 && e.Skill == JobKind.Dig;
            Assert.IsTrue(levelled, "level-up published");
            Assert.Less(fast, slow, "a level 4-5 digger digs faster");
        }

        [Test]
        public void Traits_AreDrawnAtHatch_AndStrongAntsCarryMore()
        {
            var seen = new bool[5];
            for (int i = 0; i < 60; i++)
            {
                int index = Sim.Ants.Spawn(Queen, (uint)(100 + i), AntCaste.Worker, traitChance: 1f);
                seen[(int)Sim.Ants.Ants[index].Trait] = true;
            }
            Assert.IsFalse(seen[(int)AntTrait.None], "chance 1: every ant has one");
            Assert.IsTrue(seen[1] && seen[2] && seen[3] && seen[4], "all four kinds");

            var start = CarveTunnel(10);
            ClearAnts();
            int w = Spawn(start, AntCaste.Worker);
            var a = Sim.Ants.Ants[w];
            a.Trait = AntTrait.Strong;
            Sim.Ants.Ants[w] = a;
            Assert.AreEqual(2, Sim.Ants.CarryCapacity(a));
            ushort ore = C.Item(C.Ore);
            Sim.Colony.Piles.Add(start + new int2(5, 0), ore, 3);
            int most = 0;
            for (int t = 0; t < 30 * 20 && Inv.Items[ore] < 3; t++)
            {
                Sim.Tick();
                most = math.max(most, Sim.Ants.Ants[w].CarryCount);
            }
            Assert.AreEqual(2, most, "a strong worker carries two");
            Assert.AreEqual(3, Inv.Items[ore]);
        }

        [Test]
        public void Lanes_SpreadAntsOnOneRoute()
        {
            int lo = 0, hi = 0;
            for (int i = 0; i < 50; i++)
            {
                int index = Sim.Ants.Spawn(Queen, (uint)i);
                lo = math.min(lo, Sim.Ants.Ants[index].Lane);
                hi = math.max(hi, Sim.Ants.Ants[index].Lane);
            }
            Assert.Less(lo, -60);
            Assert.Greater(hi, 60);

            // With jitter on, two ants with opposite lanes reach a far pile at different times.
            C.Config.growth.speedJitter = 0.3f;
            var start = CarveTunnel(14);
            ClearAnts();
            int fast = Spawn(start, AntCaste.Worker), slow = Spawn(start, AntCaste.Worker);
            var a = Sim.Ants.Ants[fast];
            a.Lane = 127;
            Sim.Ants.Ants[fast] = a;
            a = Sim.Ants.Ants[slow];
            a.Lane = -127;
            Sim.Ants.Ants[slow] = a;
            Sim.EnqueueDesignate(new float2(start + new int2(12, 2)) + 0.5f, 0.5f, true);
            Run(1.5f);
            Assert.Greater(Sim.Ants.Ants[fast].Pos.x, Sim.Ants.Ants[slow].Pos.x + 0.5f, "the fast lane pulls ahead");
        }

        [Test]
        public void Save_RoundTripsExperienceTraitsEvolutionsAndPriorities()
        {
            Inv.SetReserve(Honeydew, 99);
            Inv.Add(Honeydew, 2, Queen);
            Sim.EnqueueEvolution(C.EvolutionIndex(C.EvoBrood));
            Sim.EnqueuePriority(JobKind.Supply, 5);
            Sim.EnqueuePriority(JobKind.Nurse, 1);
            Sim.Tick();
            var a = Sim.Ants.Ants[0];
            a.Xp = 77f;
            a.Skill = (byte)JobKind.Build;
            a.Trait = AntTrait.Lucky;
            a.Lane = -42;
            Sim.Ants.Ants[0] = a;

            using var stream = new MemoryStream();
            SaveGame.Save(Sim, stream, SavedAt);
            stream.Position = 0;
            using var loaded = SaveGame.Load(stream, C.DB, C.Config, out _);
            var b = loaded.Ants.Ants[0];
            Assert.AreEqual(77f, b.Xp, 1e-4f);
            Assert.IsTrue(b.HasSkill(JobKind.Build));
            Assert.AreEqual(AntTrait.Lucky, b.Trait);
            Assert.AreEqual(-42, b.Lane);
            Assert.AreEqual(1, loaded.Evolutions.Levels[C.EvolutionIndex(C.EvoBrood)]);
            Assert.AreEqual(1.15f, loaded.Colony.Mods.Mult(StatKind.LayingSpeed), 1e-4f, "the evolution's stat is applied again");
            Assert.AreEqual(5, loaded.Colony.JobPriority[(int)JobKind.Supply]);
            Assert.AreEqual(1, loaded.Colony.JobPriority[(int)JobKind.Nurse]);
            Assert.AreEqual(3, loaded.Colony.JobPriority[(int)JobKind.Dig]);
        }
    }
}
