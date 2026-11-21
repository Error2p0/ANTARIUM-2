using System;
using System.Collections.Generic;
using System.IO;
using Antarium.Content;
using Antarium.Gameplay;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>
    /// The Steam Next Fest demo (GDD 2.16): the Ancient Seal at the bottom of the last demo biome, full game locks (research,
    /// evolution levels, contracts, the flight), reaching the seal and its glimpse, the timed end offer, saving, and the full game
    /// opening the seal of a colony carried over from the demo.
    /// </summary>
    public class DemoTests
    {
        static readonly DateTime SavedAt = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        const uint Seed = 4242;

        TestContent C;

        [SetUp]
        public void SetUp() => C = new TestContent(caves: true, demo: true);

        [TearDown]
        public void TearDown() => C.Dispose();

        Simulation NewSim(bool demo)
        {
            C.DB.DemoActive = demo;
            return new Simulation(C.DB, C.Config, Seed);
        }

        int Research(ResearchDef def) => C.DB.TryGetResearchIndex(def.Id, out int i) ? i : -1;
        int Contract(ContractDef def) => C.DB.TryGetContractIndex(def.Id, out int i) ? i : -1;

        [Test]
        public void Bake_MarksWhatNeedsTheFullGame()
        {
            var d = C.DB.Demo;
            Assert.IsNotNull(d);
            Assert.AreEqual(0, d.LastBiome, "the demo ends at the bottom of Topsoil");
            Assert.AreEqual(C.Topsoil.thickness, d.SealDepth(C.WorldGen));
            Assert.IsTrue(d.FullGameItem[C.Item(C.Relic)]);
            Assert.IsFalse(d.FullGameItem[C.Item(C.Ore)]);
            Assert.IsTrue(d.FullGameResearch[Research(C.AncientResearch)], "listed");
            Assert.IsFalse(d.FullGameResearch[Research(C.FastBelts)]);
            Assert.IsTrue(d.FullGameBuilding[C.BuildingIndex(C.Engine)], "only full game research unlocks it");
            Assert.IsTrue(d.FullGameBuilding[C.BuildingIndex(C.Throne)], "only a genetic trait unlocks it");
            Assert.IsFalse(d.FullGameBuilding[C.BuildingIndex(C.Belt)]);

            // Chitin is a full game item here: Brood's second level, both Lineage levels and so glow and fire ants.
            var brood = d.FullGameEvolution[C.EvolutionIndex(C.EvoBrood)];
            CollectionAssert.AreEqual(new[] { false, true }, brood);
            CollectionAssert.AreEqual(new[] { true, true }, d.FullGameEvolution[C.EvolutionIndex(C.EvoLineage)]);
            CollectionAssert.AreEqual(new[] { false }, d.FullGameEvolution[C.EvolutionIndex(C.EvoQuicken)]);
            Assert.IsTrue(d.FullGameCaste[(int)AntCaste.Glow]);
            Assert.IsTrue(d.FullGameCaste[(int)AntCaste.Fire]);
            Assert.IsFalse(d.FullGameCaste[(int)AntCaste.Miner]);

            Assert.IsTrue(d.FullGameContract[Contract(C.DeliverRelic)]);
            Assert.IsFalse(d.FullGameContract[Contract(C.DeliverOre)]);
            Assert.IsFalse(d.FullGameContract[Contract(C.DigDeeper)], "dig deeper is checked against the depth when it is drawn");
        }

        [Test]
        public void Bake_RefusesASealThatCanBeDug()
        {
            C.Seal.flags = MaterialFlags.None;
            Assert.Throws<ArgumentException>(() => C.Rebuild().Dispose());
        }

        [Test]
        public void World_TheSealSplitsTheWorldAndChangesNothingElse()
        {
            using var demo = NewSim(true);
            using var full = NewSim(false);
            Assert.IsTrue(demo.Demo.Sealed);
            Assert.IsFalse(full.Demo.Sealed);
            ushort seal = C.Index(C.Seal);
            var w = demo.World;
            int edge = C.WorldGen.bedrockThickness, sealCells = 0;
            for (int x = edge; x < w.Width - edge; x++)
                Assert.GreaterOrEqual(demo.Demo.SealTop[x], 0, $"column {x} has the seal");

            // Everything but the seal is the same world (no extra random numbers drawn).
            for (int y = 0; y < w.Height; y++)
            for (int x = 0; x < w.Width; x++)
            {
                var a = w[x, y];
                var b = full.World[x, y];
                if (a.Material == seal)
                {
                    sealCells++;
                    Assert.IsFalse(a.IsOpen);
                    Assert.AreEqual(FluidKind.None, w.Fluid[w.Index(x, y)].Kind, "no fluid in the seal");
                    continue;
                }
                Assert.AreEqual(b.Material, a.Material, $"material at {x},{y}");
                Assert.AreEqual(b.Density, a.Density, $"density at {x},{y}");
            }
            Assert.Greater(sealCells, (w.Width - 2 * edge) * C.Demo.sealThickness - 1);

            // Even with every other rock dug out, nothing above the seal reaches below it (8-way, as ants crawl).
            var seen = new bool[w.Width, w.Height];
            var queue = new Queue<int2>();
            for (int x = 0; x < w.Width; x++)
            {
                seen[x, w.Height - 1] = true;
                queue.Enqueue(new int2(x, w.Height - 1));
            }
            ushort bedrock = C.Index(C.Bedrock);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                Assert.Greater(c.y, demo.Demo.SealTop[c.x], $"{c} is at or below the seal");
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = c + new int2(dx, dy);
                    if (!w.InBounds(n.x, n.y) || seen[n.x, n.y]) continue;
                    var m = w[n.x, n.y].Material;
                    if (m == seal || m == bedrock) continue;
                    seen[n.x, n.y] = true;
                    queue.Enqueue(n);
                }
            }
        }

        [Test]
        public void Demo_LocksResearchEvolutionsAndTheFlight()
        {
            using var sim = NewSim(true);
            int ancient = Research(C.AncientResearch);
            Assert.AreEqual(ResearchResult.FullGame, sim.Research.Check(ancient));
            int brood = C.EvolutionIndex(C.EvoBrood);
            Assert.AreEqual(0, sim.Colony.Inventory.Add(C.Item(C.Honeydew), 4, sim.Colony.QueenCell), "all fit");
            Assert.AreEqual(0, sim.Colony.Inventory.Add(C.Item(C.Chitin), 2, sim.Colony.QueenCell));
            Assert.AreEqual(EvolutionResult.Ok, sim.Evolutions.Buy(brood));
            Assert.IsTrue(sim.Evolutions.NextIsFullGame(brood), "level 2 costs chitin");
            Assert.AreEqual(EvolutionResult.FullGame, sim.Evolutions.Buy(brood), "even with the chitin in the stock");
            Assert.AreEqual(1, sim.Evolutions.Levels[brood]);

            sim.Colony.Inventory.Knowledge = 100f;
            sim.GiveResources(10, 50f);
            Assert.AreEqual(ResearchResult.Ok, sim.Research.Check(Research(C.FastBelts)), "the rest is as before");

            sim.Colony.Princesses = 3;
            sim.Colony.Stats.MaxDepth = 400;
            Assert.IsFalse(sim.TryNuptialFlight(out _, out _), "no flight in the demo");

            using var full = NewSim(false);
            Assert.AreNotEqual(ResearchResult.FullGame, full.Research.Check(ancient));
            Assert.IsFalse(full.Evolutions.NextIsFullGame(brood));
        }

        [Test]
        public void Demo_ContractsNeverAskForTheFullGame()
        {
            using var sim = NewSim(true);
            int relic = Contract(C.DeliverRelic), deeper = Contract(C.DigDeeper);
            // Claim the intro and keep drawing: the relic contract never comes, dig deeper only while it fits above the seal.
            for (int round = 0; round < 40; round++)
            {
                foreach (var c in sim.Contracts.Active) Assert.AreNotEqual(relic, c.Def, "full game contract offered");
                for (int slot = 0; slot < sim.Contracts.Active.Count; slot++)
                {
                    var c = sim.Contracts.Active[slot];
                    if (c.Def == deeper) Assert.LessOrEqual(sim.Colony.Stats.MaxDepth + c.Target, sim.DemoDepthLimit);
                    var forced = c;
                    forced.Progress = forced.Target;
                    sim.Contracts.Active[slot] = forced;
                    sim.Contracts.Claim(slot);
                    break;
                }
            }
            // Near the seal: every slot is redrawn a few times; dig deeper never comes back.
            sim.Colony.Stats.MaxDepth = sim.DemoDepthLimit - 5;
            for (int round = 0; round < 20; round++)
            {
                for (int slot = sim.Contracts.Active.Count - 1; slot >= 0; slot--)
                {
                    var c = sim.Contracts.Active[slot];
                    c.Progress = c.Target;
                    sim.Contracts.Active[slot] = c;
                    sim.Contracts.Claim(slot);
                }
                foreach (var c in sim.Contracts.Active) Assert.AreNotEqual(deeper, c.Def, "10 deeper would pass the seal");
            }
        }

        [Test]
        public void Demo_TheSealCantBeDugOrOrderedPast()
        {
            using var sim = NewSim(true);
            int x = sim.World.Width / 2 + 9;
            int top = sim.Demo.SealTop[x];
            var seal = new int2(x, top);
            Reveal.Around(sim.World, seal, 3);
            sim.Enqueue(new DigCommand(new float2(x + 0.5f, top + 0.5f), 0.6f, 1000f));
            sim.Tick();
            Assert.IsTrue(sim.IsSeal(seal), "the seal stays");

            sim.EnqueueDesignate(new float2(x + 0.5f, top - 5.5f), 0.6f, true);
            sim.EnqueueDesignate(new float2(x + 0.5f, top + 4.5f), 0.6f, true);
            sim.Tick();
            Assert.IsFalse(sim.Colony.Designated.Contains(sim.World.Index(x, top - 5)), "no mining orders beyond the seal");
            Assert.IsTrue(sim.Colony.Designated.Contains(sim.World.Index(x, top + 4)) || sim.World[x, top + 4].IsOpen, "above it as before");
        }

        [Test]
        public void Demo_ReachingTheSealRevealsAGlimpseBeyondIt()
        {
            using var sim = NewSim(true);
            int x = sim.World.Width / 2 + 9;
            int top = sim.Demo.SealTop[x];
            int bottom = top;
            while (sim.IsSeal(new int2(x, bottom - 1))) bottom--;
            var beyond = new int2(x, bottom - 1 - C.Demo.glimpseDepth);
            Assert.IsFalse(sim.World[beyond.x, beyond.y].IsRevealed, "hidden at first");
            Assert.IsFalse(sim.Demo.SealReached);

            var above = new int2(x, top + 1);
            sim.SetCell(above, new Cell(C.Index(C.Dirt), Cell.Full));
            Reveal.Around(sim.World, above, 1);
            sim.Enqueue(new DigCommand(new float2(x + 0.5f, top + 1.5f), 0.4f, 1000f));
            sim.Tick();

            Assert.IsTrue(sim.World[above.x, above.y].IsOpen, "dug out");
            Assert.IsTrue(sim.Demo.SealReached);
            Assert.AreEqual(above, sim.Demo.Contact);
            var reached = sim.Events.Read<DemoSealReachedEvent>();
            Assert.AreEqual(1, reached.Length);
            Assert.AreEqual(beyond, reached[0].Glimpse);
            Assert.IsTrue(sim.World[beyond.x, beyond.y].IsRevealed, "the window beyond the seal is revealed");
            Assert.IsFalse(sim.DemoEndDue, "the end screen is the seal's, not the timed one");
        }

        [Test]
        public void Demo_TheTimedEndOfferComesOnce()
        {
            using var sim = NewSim(true);
            Assert.IsFalse(sim.DemoEndDue);
            Run(sim, C.Demo.endScreenMinutes * 60f + 0.5f);
            Assert.IsTrue(sim.DemoEndDue);
            sim.MarkDemoEndShown();
            Assert.IsFalse(sim.DemoEndDue);

            using var full = NewSim(false);
            Run(full, C.Demo.endScreenMinutes * 60f + 0.5f);
            Assert.IsFalse(full.DemoEndDue, "not in the full game");
        }

        static void Run(Simulation sim, float seconds)
        {
            int ticks = (int)math.ceil(seconds / sim.TickDuration);
            for (int i = 0; i < ticks; i++) sim.Tick();
        }

        [Test]
        public void Save_TheDemoKeepsItsSealAndTheFullGameOpensIt()
        {
            using var demo = NewSim(true);
            demo.Demo.SealReached = true;
            demo.Demo.Contact = new int2(3, 4);
            demo.MarkDemoEndShown();
            int x = demo.World.Width / 2 + 9;
            var hidden = new int2(x, demo.Demo.SealTop[x]);
            Reveal.Around(demo.World, hidden, 1);

            using var stream = new MemoryStream();
            SaveGame.Save(demo, stream, SavedAt);
            var bytes = stream.ToArray();

            // The demo loads it sealed.
            using (var again = SaveGame.Load(new MemoryStream(bytes), C.DB, C.Config, out _))
            {
                Assert.IsTrue(again.Demo.Sealed);
                Assert.IsTrue(again.Demo.SealReached);
                Assert.AreEqual(new int2(3, 4), again.Demo.Contact);
                Assert.IsTrue(again.Demo.EndShown);
                Assert.AreEqual(demo.Demo.SealTop[x], again.Demo.SealTop[x], "the seal is found again");
            }

            // The full game opens it: every seal cell gets back what the world had there.
            C.DB.DemoActive = false;
            using var fresh = new Simulation(C.DB, C.Config, Seed);
            using var opened = SaveGame.Load(new MemoryStream(bytes), C.DB, C.Config, out _);
            Assert.IsFalse(opened.Demo.Sealed);
            Assert.AreEqual(1, opened.Events.Read<DemoSealBrokenEvent>().Length);
            ushort seal = C.Index(C.Seal);
            for (int i = 0; i < opened.World.Cells.Length; i++)
            {
                Assert.AreNotEqual(seal, opened.World.Cells[i].Material, "no seal left");
                if (demo.World.Cells[i].Material != seal) continue;
                Assert.AreEqual(fresh.World.Cells[i].Material, opened.World.Cells[i].Material);
                Assert.AreEqual(fresh.World.Cells[i].Density, opened.World.Cells[i].Density);
            }
            Assert.IsTrue(opened.World[hidden.x, hidden.y].IsRevealed, "what was seen stays seen");
            Assert.AreNotEqual(ResearchResult.FullGame, opened.Research.Check(Research(C.AncientResearch)), "nothing is locked in the full game");
        }

        [Test]
        public void Platform_TheStorePageUrl()
        {
            Assert.AreEqual("https://store.steampowered.com/app/4453940/", LocalPlatform.StoreUrl(PlatformServices.SteamAppId));
        }

        [Test]
        public void Content_TheShippedDemoEndsAtTheStoneStrata()
        {
#if UNITY_EDITOR
            var db = UnityEditor.AssetDatabase.LoadAssetAtPath<ContentDatabase>("Assets/_Antarium/Content/ContentDatabase.asset");
            Assert.IsNotNull(db.demo, "the ContentDatabase has the DemoConfig");
            Assert.AreEqual("stone_strata", db.demo.lastBiome.Id);
            Assert.IsTrue((db.demo.sealMaterial.flags & MaterialFlags.Indestructible) != 0);
            Assert.IsTrue(db.materials.Contains(db.demo.sealMaterial));
            Assert.Greater(db.demo.fullGameFeatures.Count, 3);
            Assert.AreEqual(PlatformServices.SteamAppId, db.demo.fullGameAppId);
            using var content = ContentDB.Build(db);
            var d = content.Demo;
            bool Locked(string id) => content.TryGetResearchIndex(id, out int i) && d.FullGameResearch[i];
            foreach (var id in new[] { "nuptial_flight", "crystal_resonance", "fire_ants", "obsidian_plating", "ancient_masonry", "resonant_lattice", "ancient_engine" })
                Assert.IsTrue(Locked(id), id + " is full game");
            foreach (var id in new[] { "glow_ants", "soldier_caste", "fortification", "hydraulics", "water_mills", "reinforced_silk", "vein_instinct" })
                Assert.IsFalse(Locked(id), id + " is in the demo");
            Assert.AreEqual(690, d.SealDepth(content.WorldGen));
            Assert.IsTrue(content.TryGetBuildingIndex("princess_chamber", out int chamber) && d.FullGameBuilding[chamber]);
            Assert.IsTrue(d.FullGameCaste[(int)AntCaste.Fire]);
            Assert.IsFalse(d.FullGameCaste[(int)AntCaste.Glow]);
#endif
        }
    }
}
