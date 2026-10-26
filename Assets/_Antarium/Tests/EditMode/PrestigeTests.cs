using System;
using System.Collections.Generic;
using System.IO;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    public class PrestigeTests : SimulationFixture
    {
        int Gene(GeneticDef def) => C.GeneticIndex(def);
        int Mod(WorldModifierDef def) => C.ModifierIndex(def);

        void Buy(GeneticDef def)
        {
            Sim.EnqueueGenetic(Gene(def));
            Sim.Tick();
        }

        /// <summary>A colony founded from <paramref name="legacy"/> on the fixture's seed, with the fixture's config.</summary>
        Simulation Found(Legacy legacy, params WorldModifierDef[] modifiers) =>
            new(C.DB, C.Config, 11, legacy, Array.ConvertAll(modifiers, Mod));

        [Test]
        public void Content_BakesTraitsAndModifiers()
        {
            Assert.AreEqual(8, C.DB.Genetics.Length);
            Assert.AreEqual(3, C.DB.Modifiers.Length);
            Assert.AreEqual(C.ModRich, C.DB.Modifiers[0], "modifiers are in flight-screen order");
            Assert.IsTrue(C.DB.ModifierStartsLocked[Mod(C.ModHard)], "a modifier named by a trait starts locked");
            Assert.IsFalse(C.DB.ModifierStartsLocked[Mod(C.ModRich)]);
            var jaws = C.DB.Genetics[Gene(C.GeneJaws)];
            CollectionAssert.AreEqual(new[] { Gene(C.GeneBrood) }, jaws.Prerequisites);
            Assert.AreEqual(1, C.DB.Genetics[Gene(C.GeneBrood)].Cost(0));
            Assert.AreEqual(3, C.DB.Genetics[Gene(C.GeneBrood)].Cost(2), "each level costs more");
            int raise = C.DB.TryGetRecipeIndex(C.Raise.Id, out int r) ? r : -1;
            Assert.AreEqual(1, C.DB.Recipes[raise].Princesses, "a recipe may output only princesses");
        }

        [Test]
        public void Genetics_CostMemoryNeedPrerequisitesAndApplyAtOnce()
        {
            var legacy = Sim.Legacy;
            legacy.Memory = 4;
            Buy(C.GeneJaws);
            Assert.AreEqual(0, legacy.Levels[Gene(C.GeneJaws)], "Jaws needs Brood first");
            Assert.AreEqual(GeneticResult.NeedsPrerequisites, Sim.Events.Read<GeneticFailedEvent>()[0].Result);

            Buy(C.GeneBrood);
            Buy(C.GeneBrood);
            Assert.AreEqual(2, legacy.Levels[Gene(C.GeneBrood)]);
            Assert.AreEqual(1, legacy.Memory, "levels cost 1, then 2");

            Buy(C.GeneJaws);
            Assert.AreEqual(GeneticResult.CantAfford, Sim.Events.Read<GeneticFailedEvent>()[1].Result);
            legacy.Memory += 5;
            Assert.AreEqual(1f, Sim.Colony.Mods.Mult(StatKind.WorkerDig), 1e-4f);
            Buy(C.GeneJaws);
            Assert.AreEqual(1.5f, Sim.Colony.Mods.Mult(StatKind.WorkerDig), 1e-4f, "colony bonuses work right away");
            Buy(C.GeneJaws);
            Assert.AreEqual(GeneticResult.MaxLevel, Sim.Events.Read<GeneticFailedEvent>()[2].Result);
            Assert.AreEqual(4, legacy.Memory);
            Assert.AreEqual(3, Sim.Events.Read<GeneticBoughtEvent>().Length);
        }

        [Test]
        public void InheritedResearch_IsDoneNowAndInEveryNewColony()
        {
            int belts = ResearchIndex(C.FastBelts);
            Sim.Legacy.Memory = 1;
            Buy(C.GeneInherit);
            Assert.IsTrue(Sim.Research.Done[belts], "bought mid-colony: researched at once");
            Assert.AreEqual(2f, Sim.Colony.Mods.Mult(StatKind.BeltSpeed), 1e-4f);

            using var next = Found(Sim.Legacy.Clone());
            Assert.IsTrue(next.Research.Done[belts]);
            Assert.AreEqual(2f, next.Colony.Mods.Mult(StatKind.BeltSpeed), 1e-4f, "the research effect applies once");
        }

        [Test]
        public void NewColony_GetsTheStartingBonuses()
        {
            var legacy = new Legacy(C.DB) { Memory = 10 };
            Assert.AreEqual(GeneticResult.Ok, legacy.Buy(Gene(C.GeneBrood)));
            Assert.AreEqual(GeneticResult.Ok, legacy.Buy(Gene(C.GeneBrood)));
            Assert.AreEqual(GeneticResult.Ok, legacy.Buy(Gene(C.GeneStart)));

            using var colony = Found(legacy);
            var inv = colony.Colony.Inventory;
            Assert.AreEqual(C.Config.colony.startingWorkers + 4, colony.Ants.Count, "two levels of +2 workers");
            Assert.AreEqual(math.min(inv.FoodCapacity, C.Config.colony.startingFood + 20f), inv.Food, 1e-3f);
            Assert.AreEqual(7f, inv.Knowledge, 1e-3f);
            Assert.AreEqual(0d, colony.Colony.Stats.KnowledgeEarned, "starting knowledge does not count as earned");
            Assert.AreEqual(2, inv.Items[C.Item(C.Bar)]);

            int2 below = colony.Colony.QueenCell + new int2(0, -9);
            Assert.IsFalse(Sim.World[below.x, below.y].IsRevealed, "a plain colony does not see that far");
            Assert.IsTrue(colony.World[below.x, below.y].IsRevealed, "Ancestral Map reveals around the nest");
        }

        [Test]
        public void PrincessChamber_RaisesPrincessesUpToTheLimit()
        {
            ushort ore = C.Item(C.Ore);
            Sim.Colony.PrincessCap = 2;
            Assert.IsTrue(FindMachineSpot(C.Chamber, out var origin));
            Sim.EnqueueBuild(C.BuildingIndex(C.Chamber), origin, default);
            Sim.Tick();
            Sim.Buildings.CompleteAllBlueprints();
            Sim.Tick();
            int id = Sim.Buildings.At(origin);
            for (int i = 0; i < 4; i++)
            {
                Sim.Buildings.TryFeed(origin, ore);
                Run(1.5f);
            }
            Assert.AreEqual(2, Sim.Colony.Princesses, "it stops at the limit");
            Assert.AreEqual(2, Sim.Colony.Stats.PrincessesRaised);
            Assert.AreEqual(2, Sim.Events.Read<PrincessRaisedEvent>().Length);
            Assert.AreEqual(MachineStatus.OutputBlocked, Sim.Buildings.Get(id).Status, "resting while princesses are at the limit");
            Assert.AreEqual(0, Sim.Buildings.PrincessesInProgress());

            Sim.Colony.PrincessCap = 3;
            Run(1.5f);
            Assert.AreEqual(3, Sim.Colony.Princesses, "a higher limit lets it go on");
        }

        [Test]
        public void Flight_NeedsPrincessesAndEarnsMemoryFromTheColony()
        {
            var stats = Sim.Colony.Stats;
            stats.MaxDepth = 400;
            stats.KnowledgeEarned = 400;
            Assert.IsFalse(Sim.EstimateFlight().CanFly, "no princesses");
            Assert.IsFalse(Sim.TryNuptialFlight(out _, out _));

            Sim.Colony.Princesses = 3;
            var e = Sim.EstimateFlight();
            Assert.AreEqual(80f, e.Score, 1e-3f, "depth 400 x 0.1 + knowledge 400 x 0.1");
            Assert.AreEqual(math.sqrt(20f), e.Base, 1e-3f);
            Assert.AreEqual(1.5f, e.PrincessFactor, 1e-4f, "+25% for each princess after the first");
            Assert.AreEqual(6, e.Memory);

            Sim.Colony.Princesses = 9;
            Assert.AreEqual(2f, Sim.EstimateFlight().PrincessFactor, 1e-4f, "only the limit (5) counts");

            Sim.Legacy.Memory = 1;
            Buy(C.GeneGain);
            Sim.Colony.Princesses = 3;
            Assert.AreEqual(13, Sim.EstimateFlight().Memory, "Genetic Memory gain doubles it");

            Assert.IsTrue(Sim.TryNuptialFlight(out var next, out int earned));
            Assert.AreEqual(13, earned);
            Assert.AreEqual(13, next.Memory);
            Assert.AreEqual(13, next.MemoryEarned);
            Assert.AreEqual(1, next.Flights);
            Assert.AreEqual(400, next.BestDepth);
            Assert.AreEqual(1, next.Levels[Gene(C.GeneGain)], "traits are kept");
            Assert.AreEqual(0, Sim.Legacy.Flights, "the flown colony keeps its own legacy");

            using var founded = Found(next);
            Assert.AreEqual(0, founded.Colony.Princesses);
            Assert.AreEqual(2f, founded.Colony.Mods.Mult(StatKind.GeneticGain), 1e-4f);
        }

        [Test]
        public void Legacy_LocksModifiersAndCountsSlotsAndPrincesses()
        {
            var cfg = C.Config.prestige;
            var legacy = new Legacy(C.DB) { Memory = 5 };
            Assert.IsTrue(legacy.ModifierUnlocked(Mod(C.ModRich)));
            Assert.IsFalse(legacy.ModifierUnlocked(Mod(C.ModHard)));
            Assert.AreEqual(cfg.modifierSlots, legacy.ModifierSlots(cfg));
            Assert.AreEqual(cfg.maxPrincesses, legacy.MaxPrincesses(cfg));

            legacy.Buy(Gene(C.GeneStrange));
            legacy.Buy(Gene(C.GeneRoyal));
            Assert.IsTrue(legacy.ModifierUnlocked(Mod(C.ModHard)));
            Assert.AreEqual(cfg.modifierSlots + 1, legacy.ModifierSlots(cfg));
            Assert.AreEqual(cfg.maxPrincesses + 1, legacy.MaxPrincesses(cfg));
            using var colony = Found(legacy);
            Assert.AreEqual(cfg.maxPrincesses + 1, colony.Colony.PrincessCap);
        }

        [Test]
        public void WorldModifiers_ShapeTheWorldAndTheColony()
        {
            using var content = new TestContent(4, 4, caves: false, ores: true);
            content.WorldGen.biomeWarpAmplitude = 0f; // straight biome borders, so depths can be checked
            ushort oreRock = content.Index(content.OreRock);
            int CountOre(Simulation s)
            {
                int n = 0;
                foreach (var cell in s.World.Cells)
                    if (cell.Material == oreRock) n++;
                return n;
            }
            int rich = content.ModifierIndex(content.ModRich), shallow = content.ModifierIndex(content.ModShallow);
            using var plain = new Simulation(content.DB, content.Config, 5, new Legacy(content.DB));
            using var veins = new Simulation(content.DB, content.Config, 5, new Legacy(content.DB), new[] { rich });
            using var thin = new Simulation(content.DB, content.Config, 5, new Legacy(content.DB), new[] { shallow, shallow });

            Assert.Greater(CountOre(plain), 0);
            Assert.Greater(CountOre(veins), CountOre(plain) * 2, "Rich Veins: far more ore");
            Assert.AreEqual(1, veins.WorldModifiers.Count);
            Assert.AreEqual(2f, veins.Colony.Mods.Mult(StatKind.RaidSize), 1e-4f);
            Assert.AreEqual(1, thin.WorldModifiers.Count, "a modifier counts once");
            Assert.AreEqual(0.5f, thin.Tweaks.BiomeScale, 1e-4f);
            Assert.AreEqual(0, plain.BiomeAtDepth(20));
            Assert.AreEqual(1, thin.BiomeAtDepth(20), "the topsoil is half as thick");
            int surface = thin.World.SurfaceY[10];
            Assert.AreNotEqual(content.Index(content.Dirt), thin.World[10, surface - 22].Material, "the deep biome starts sooner");
            Assert.AreEqual(content.Index(content.Dirt), plain.World[10, surface - 22].Material);
        }

        [Test]
        public void RaidSize_FollowsTheWorldModifiers()
        {
            using var rich = Found(new Legacy(C.DB), C.ModRich);
            int plain = Sim.Enemies.NextRaidSize();
            Assert.AreEqual(math.min(plain * 2, C.Config.threats.raidMaxSize * 2), rich.Enemies.NextRaidSize(), 1);
        }

        [Test]
        public void VeinInstinct_OrdersOreNextToDugCells()
        {
            ushort stone = C.Index(C.Stone); // drops ore in the test content
            int2 floor = Queen + new int2(0, -1);
            int2 ore = Queen + new int2(1, -2);
            Sim.SetCell(ore, new Cell(stone, Cell.Full, CellFlags.Revealed));
            Sim.Enqueue(new DigCommand(new float2(floor) + 0.5f, 0.6f, 1000f));
            Sim.Tick();
            Assert.IsTrue(Sim.World[floor.x, floor.y].IsOpen);
            Assert.IsFalse(Sim.Colony.Designated.Contains(Sim.World.Index(ore.x, ore.y)), "without the trait nothing is ordered");

            Sim.Legacy.Memory = 1;
            Buy(C.GeneVein);
            int2 floor2 = floor + new int2(-1, 0);
            Sim.Enqueue(new DigCommand(new float2(floor2) + 0.5f, 0.6f, 1000f));
            Sim.Enqueue(new DigCommand(new float2(floor) + new float2(0.5f, -0.5f), 0.6f, 1000f));
            Sim.Tick();
            Assert.IsTrue(Sim.Colony.Designated.Contains(Sim.World.Index(ore.x, ore.y)), "the ore next to the dug cell is ordered");
            Assert.AreNotEqual(0, (int)(Sim.World[ore.x, ore.y].Flags & CellFlags.DigDesignated));
        }

        [Test]
        public void Save_RoundTripsTheLegacyModifiersAndPrincesses()
        {
            var legacy = new Legacy(C.DB) { Memory = 9, MemoryEarned = 20, Flights = 2, BestDepth = 300, BestFlight = 12 };
            legacy.Levels[Gene(C.GeneBrood)] = 2;
            legacy.Levels[Gene(C.GeneRoyal)] = 1;
            legacy.Levels[Gene(C.GeneGain)] = 1;
            using var sim = Found(legacy, C.ModRich, C.ModShallow);
            sim.Colony.Princesses = 4;
            sim.Colony.Stats.PrincessesRaised = 6;

            using var stream = new MemoryStream();
            SaveGame.Save(sim, stream, DateTime.UtcNow);
            stream.Position = 0;
            using var loaded = SaveGame.Load(stream, C.DB, C.Config, out _);

            var l = loaded.Legacy;
            Assert.AreEqual(9, l.Memory);
            Assert.AreEqual(20, l.MemoryEarned);
            Assert.AreEqual(2, l.Flights);
            Assert.AreEqual(300, l.BestDepth);
            Assert.AreEqual(12, l.BestFlight);
            CollectionAssert.AreEqual(legacy.Levels, l.Levels);
            CollectionAssert.AreEquivalent(sim.WorldModifiers, loaded.WorldModifiers);
            Assert.AreEqual(0.5f, loaded.Tweaks.BiomeScale, 1e-4f);
            Assert.AreEqual(4, loaded.Colony.Princesses);
            Assert.AreEqual(6, loaded.Colony.Stats.PrincessesRaised);
            Assert.AreEqual(sim.Colony.PrincessCap, loaded.Colony.PrincessCap);
            foreach (StatKind stat in Enum.GetValues(typeof(StatKind)))
                Assert.AreEqual(sim.Colony.Mods.Mult(stat), loaded.Colony.Mods.Mult(stat), 1e-4f, stat.ToString());
            Assert.AreEqual(sim.Ants.Count, loaded.Ants.Count, "starting bonuses are not applied again on load");
        }

        [Test]
        public void Load_WithoutALegacySection_StartsWithAnEmptyLegacy()
        {
            Sim.Colony.Princesses = 2;
            using var saved = new MemoryStream();
            SaveGame.Save(Sim, saved, DateTime.UtcNow);
            saved.Position = 0;
            var sections = SaveFormat.Read(saved, out var header);
            Assert.IsTrue(sections.Remove("LGCY"));
            using var stripped = new MemoryStream();
            SaveFormat.Write(stripped, header, new List<KeyValuePair<string, byte[]>>(sections));
            stripped.Position = 0;

            using var loaded = SaveGame.Load(stripped, C.DB, C.Config, out _);
            Assert.AreEqual(0, loaded.Legacy.Memory);
            Assert.AreEqual(0, loaded.Legacy.Flights);
            Assert.AreEqual(0, loaded.WorldModifiers.Count);
            Assert.AreEqual(2, loaded.Colony.Princesses, "princesses live in the colony section");
            Assert.AreEqual(C.Config.prestige.maxPrincesses, loaded.Colony.PrincessCap);
        }
    }
}
