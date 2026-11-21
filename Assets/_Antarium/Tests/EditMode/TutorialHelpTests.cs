using System.Collections.Generic;
using System.IO;
using System.Linq;
using Antarium.Content;
using Antarium.Gameplay;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Antarium.Tests
{
    /// <summary>The first-colony tutorial (goals, steps, skipping, ending), the help topics (placeholders, reveals) and their settings.</summary>
    public class TutorialHelpTests : TunnelFixture
    {
        readonly List<Object> _made = new();

        [TearDown]
        public void DestroyMade()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        TutorialStepDef Step(string id, int order, TutorialGoal goal, int amount = 1, BuildingDef building = null)
        {
            var s = ScriptableObject.CreateInstance<TutorialStepDef>();
            s.EditorSetIdentity(id, id);
            s.order = order;
            s.goal = goal;
            s.amount = amount;
            s.building = building;
            _made.Add(s);
            return s;
        }

        HelpTopicDef Topic(HelpReveal reveal, BiomeDef biome = null, ResearchDef research = null)
        {
            var t = ScriptableObject.CreateInstance<HelpTopicDef>();
            t.EditorSetIdentity("topic", "Topic");
            t.reveal = reveal;
            t.revealBiome = biome;
            t.revealResearch = research;
            _made.Add(t);
            return t;
        }

        /// <summary>Checks the goal of a one-step tutorial against <paramref name="probe"/> and the colony.</summary>
        static Tutorial Single(TutorialStepDef step) => new(new[] { step });

        // ---------------------------------------------------------------- steps and flow

        [Test]
        public void Steps_AreSortedByOrder_AndFoundById()
        {
            var t = new Tutorial(new[] { Step("c", 30, TutorialGoal.None), Step("a", 10, TutorialGoal.None), Step("b", 20, TutorialGoal.None) });
            Assert.AreEqual(new[] { "a", "b", "c" }, t.Steps.Select(s => s.Id).ToArray());
            Assert.AreEqual(2, t.IndexOf("c"));
            Assert.AreEqual(-1, t.IndexOf("missing"));
            Assert.AreEqual(-1, t.IndexOf(""));
            Assert.IsFalse(t.Active, "not running until started");
        }

        [Test]
        public void MessageStep_WaitsForContinue_GoalStep_FinishesByItself_ThenTheNextStarts()
        {
            var probe = new TutorialProbe();
            var t = new Tutorial(new[] { Step("hello", 1, TutorialGoal.None), Step("dig", 2, TutorialGoal.DigYourself, 3), Step("bye", 3, TutorialGoal.None) });
            var started = new List<int>();
            var completed = new List<int>();
            bool? finished = null;
            t.StepStarted += started.Add;
            t.StepCompleted += completed.Add;
            t.Finished += done => finished = done;

            t.Start(0);
            for (int i = 0; i < 20; i++) t.Update(Sim, probe, 0.1f);
            Assert.AreEqual(0, t.Index, "a message step waits");
            Assert.AreEqual(0, t.Target);
            t.Skip(); // Skip on a message works like Continue
            Assert.AreEqual(1, t.Index);
            t.Evaluate(Sim, probe); // the first check of a step (next frame in play) takes its starting point

            probe.PlayerDug = 2;
            t.Evaluate(Sim, probe);
            Assert.AreEqual(2, t.Progress);
            Assert.AreEqual(3, t.Target);
            Assert.AreEqual("Cells dug: 2 / 3", t.ProgressText());
            Assert.IsFalse(t.StepDone);
            probe.PlayerDug = 5;
            t.Evaluate(Sim, probe);
            Assert.IsTrue(t.StepDone);
            Assert.AreEqual(3, t.Progress, "progress stops at the target");
            Assert.AreEqual("Done!", t.ProgressText());
            Assert.AreEqual(new[] { 1 }, completed.ToArray());

            t.Update(Sim, probe, Tutorial.DoneSeconds * 0.5f);
            Assert.AreEqual(1, t.Index, "the done step stays a moment");
            t.Update(Sim, probe, Tutorial.DoneSeconds);
            Assert.AreEqual(2, t.Index);
            t.Continue();
            Assert.IsFalse(t.Active);
            Assert.AreEqual(true, finished, "running out of steps finishes it");
            Assert.AreEqual(new[] { 0, 1, 2 }, started.ToArray());
        }

        [Test]
        public void Ending_StopsTheTutorial_AndSaysSo()
        {
            var t = new Tutorial(new[] { Step("a", 1, TutorialGoal.None), Step("b", 2, TutorialGoal.None) });
            bool? finished = null;
            t.Finished += done => finished = done;
            t.Start(1);
            Assert.AreEqual(1, t.Index, "starts (resumes) at any step");
            t.End();
            Assert.IsFalse(t.Active);
            Assert.AreEqual(false, finished, "ended by the player");
            t.Continue();
            t.Skip();
            Assert.IsFalse(t.Active, "nothing happens once it ended");
        }

        [Test]
        public void ProbeGoals_CountOnlyWhatHappensDuringTheStep()
        {
            var probe = new TutorialProbe { CameraTravel = 100f, Zooms = 4, PlayerDug = 50, QueenOpened = 2, HelpOpened = 1 };

            var look = Single(Step("look", 1, TutorialGoal.LookAround, 8));
            look.Start(0);
            look.Evaluate(Sim, probe);
            Assert.AreEqual(0, look.Progress, "earlier panning and zooming don't count");
            probe.CameraTravel += 9f;
            look.Evaluate(Sim, probe);
            Assert.AreEqual(1, look.Progress);
            StringAssert.Contains("Pan the view: done", look.ProgressText());
            StringAssert.Contains("Zoom: not yet", look.ProgressText());
            probe.Zooms++;
            look.Evaluate(Sim, probe);
            Assert.IsTrue(look.StepDone);

            var queen = Single(Step("queen", 1, TutorialGoal.OpenQueen));
            queen.Start(0);
            queen.Evaluate(Sim, probe);
            Assert.IsFalse(queen.StepDone, "the queen panel opened before doesn't count");
            probe.QueenOpened++;
            queen.Evaluate(Sim, probe);
            Assert.IsTrue(queen.StepDone);

            var help = Single(Step("help", 1, TutorialGoal.OpenHelp));
            help.Start(0);
            help.Evaluate(Sim, probe);
            Assert.IsFalse(help.StepDone);
            probe.HelpOpened++;
            help.Evaluate(Sim, probe);
            Assert.IsTrue(help.StepDone);
        }

        [Test]
        public void MiningOrders_CountTheMostOrderedAtOnce()
        {
            var probe = new TutorialProbe();
            var t = Single(Step("orders", 1, TutorialGoal.MiningOrders, 5));
            t.Start(0);
            int2 rock = Queen + new int2(12, 0);
            Sim.EnqueueDesignate(new float2(rock) + 0.5f, 1.6f, true);
            Sim.Tick();
            int ordered = Sim.Colony.Designated.Count;
            Assert.GreaterOrEqual(ordered, 5, "a size-2 brush orders several cells");
            t.Evaluate(Sim, probe);
            Assert.IsTrue(t.StepDone);

            // Orders dug away by workers still count: the peak is kept.
            var again = Single(Step("orders2", 1, TutorialGoal.MiningOrders, ordered + 1));
            again.Start(0);
            again.Evaluate(Sim, probe);
            Assert.AreEqual(ordered, again.Progress);
            Sim.EnqueueDesignate(new float2(rock) + 0.5f, 1.6f, false);
            Sim.Tick();
            again.Evaluate(Sim, probe);
            Assert.AreEqual(ordered, again.Progress, "erased (or dug) orders don't lower the progress");
        }

        [Test]
        public void StorageItemsContractsResearchAndBuildings_FollowTheColony()
        {
            var probe = new TutorialProbe();
            var start = CarveTunnel(10);

            var storage = Single(Step("storage", 1, TutorialGoal.StorageZone, 3));
            storage.Start(0);
            storage.Evaluate(Sim, probe);
            Assert.AreEqual(0, storage.Progress);
            Sim.EnqueueStorageZone(new float2(start + new int2(5, 0)) + 0.5f, 1.6f, true);
            Sim.Tick();
            storage.Evaluate(Sim, probe);
            Assert.IsTrue(storage.StepDone, $"{Sim.Colony.StorageCells.Count} storage cells");

            Sim.Colony.Stats.Delivered[0] += 7; // delivered before the step: doesn't count
            var items = Single(Step("items", 1, TutorialGoal.ItemsStored, 5));
            items.Start(0);
            items.Evaluate(Sim, probe);
            Assert.AreEqual(0, items.Progress);
            Sim.Colony.Stats.Delivered[0] += 3;
            Sim.Colony.Stats.Delivered[1] += 2;
            items.Evaluate(Sim, probe);
            Assert.IsTrue(items.StepDone);

            var contracts = Single(Step("contracts", 1, TutorialGoal.ClaimContract));
            contracts.Start(0);
            contracts.Evaluate(Sim, probe);
            Assert.IsFalse(contracts.StepDone);
            Sim.Colony.Stats.ContractsCompleted = 1;
            contracts.Evaluate(Sim, probe);
            Assert.IsTrue(contracts.StepDone);

            var research = Single(Step("research", 1, TutorialGoal.Research));
            research.Start(0);
            int before = Sim.Research.DoneCount;
            research.Evaluate(Sim, probe);
            Assert.AreEqual(before > 0, research.StepDone);
            Sim.Research.Done[ResearchIndex(C.SeismicResearch)] = true;
            research.Evaluate(Sim, probe);
            Assert.IsTrue(research.StepDone);

            var build = Single(Step("build", 1, TutorialGoal.PlaceBuilding, 1, C.Belt));
            build.Start(0);
            build.Evaluate(Sim, probe);
            Assert.IsFalse(build.StepDone);
            StringAssert.Contains(C.Belt.DisplayName + " placed", build.ProgressText());
            Sim.GiveResources(20, 0f);
            Sim.EnqueueBuild(C.BuildingIndex(C.Belt), start + new int2(2, 0), new int2(1, 0));
            Sim.Tick();
            build.Evaluate(Sim, probe);
            Assert.IsTrue(build.StepDone, "a blueprint counts as placed");
        }

        [Test]
        public void Rebase_MeasuresAgainFromTheNewColony()
        {
            var probe = new TutorialProbe();
            var t = Single(Step("items", 1, TutorialGoal.ItemsStored, 5));
            Sim.Colony.Stats.Delivered[0] = 100;
            t.Start(0);
            t.Evaluate(Sim, probe);
            Sim.Colony.Stats.Delivered[0] = 2; // a new colony with fewer deliveries
            t.Rebase();
            t.Evaluate(Sim, probe);
            Assert.AreEqual(0, t.Progress);
            Sim.Colony.Stats.Delivered[0] = 6;
            t.Evaluate(Sim, probe);
            Assert.AreEqual(4, t.Progress);
        }

        // ---------------------------------------------------------------- help

        [Test]
        public void HelpText_FillsKeys_AndLeavesTheRest()
        {
            Keys.Use(null);
            Assert.AreEqual($"Press <color={HelpText.KeyColor}>2</color> to mine.", HelpText.Expand("Press {Mine} to mine."));
            Assert.AreEqual($"<color={HelpText.KeyColor}>W A S D</color>", HelpText.Expand("{Pan}"));
            Assert.AreEqual($"<color={HelpText.KeyColor}>X</color>", HelpText.Expand("{Ability2}"));
            Assert.AreEqual("Costs 12 Copper Ore.", HelpText.Expand("Costs {Cost}.", n => n == "Cost" ? "12 Copper Ore" : null));
            Assert.AreEqual("{Nope} and { open", HelpText.Expand("{Nope} and { open"));
            Assert.AreEqual("", HelpText.Expand(null));
            Assert.AreEqual(new[] { "Mine", "Cost" }, HelpText.Placeholders("a {Mine} b {Cost} c {").ToArray());
            Assert.IsTrue(HelpText.IsKey("Help"));
            Assert.IsFalse(HelpText.IsKey("Cost"));
        }

        [Test]
        public void HelpTopics_RevealWithDepthResearchAndFlights()
        {
            Assert.IsTrue(HelpLibrary.Revealed(Topic(HelpReveal.Always), null), "Always needs no colony");

            var deep = Topic(HelpReveal.Biome, C.Deep);
            Assert.IsFalse(HelpLibrary.Revealed(deep, null));
            Sim.Colony.Stats.MaxDepth = 2;
            Assert.IsFalse(HelpLibrary.Revealed(deep, Sim));
            StringAssert.Contains(C.Deep.DisplayName, HelpLibrary.LockedHint(deep));
            Sim.Colony.Stats.MaxDepth = C.Topsoil.thickness + 5;
            Assert.IsTrue(HelpLibrary.Revealed(deep, Sim), "digging into the layer reveals it");
            Assert.IsTrue(HelpLibrary.Revealed(Topic(HelpReveal.Biome, C.Topsoil), Sim));

            var research = Topic(HelpReveal.Research, research: C.SeismicResearch);
            Assert.IsFalse(HelpLibrary.Revealed(research, Sim));
            StringAssert.Contains(C.SeismicResearch.DisplayName, HelpLibrary.LockedHint(research));
            Sim.Research.Done[ResearchIndex(C.SeismicResearch)] = true;
            Assert.IsTrue(HelpLibrary.Revealed(research, Sim));

            var flight = Topic(HelpReveal.Flight, research: C.LabResearch);
            Assert.IsFalse(HelpLibrary.Revealed(flight, Sim));
            Sim.Legacy.Flights = 1;
            Assert.IsTrue(HelpLibrary.Revealed(flight, Sim), "a bloodline that has flown knows the flight");
        }

        [Test]
        public void HelpLibrary_SortsByOrder()
        {
            var a = Topic(HelpReveal.Always);
            a.order = 20;
            var b = Topic(HelpReveal.Always);
            b.order = 10;
            var lib = new HelpLibrary(new[] { a, null, b });
            Assert.AreEqual(new[] { b, a }, lib.Topics);
            Assert.AreEqual(1, lib.IndexOf(a));
        }

        // ---------------------------------------------------------------- settings

        [Test]
        public void Settings_TutorialForNewPlayers_NotForSettingsFromBefore()
        {
            var fresh = new GameSettings();
            fresh.Sanitize();
            Assert.IsFalse(fresh.tutorialDone, "a new player gets the tutorial");

            string dir = Path.Combine(Path.GetTempPath(), "antarium_tutorial_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var store = new SettingsStore(dir);
                File.WriteAllText(store.Path, "{\"version\":1,\"masterVolume\":0.5}");
                var old = store.Load();
                Assert.IsTrue(old.tutorialDone, "settings from before the tutorial: someone who has played");
                Assert.AreEqual("", old.tutorialStep);
                Assert.AreEqual(GameSettings.CurrentVersion, old.version);

                var s = new GameSettings { tutorialStep = "tutorial_storage" };
                store.Save(s);
                var back = store.Load();
                Assert.AreEqual("tutorial_storage", back.tutorialStep, "the step in progress is kept");
                Assert.IsFalse(back.tutorialDone);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // ---------------------------------------------------------------- the shipped content

        [Test]
        public void ShippedHelpAndTutorial_AreComplete()
        {
            var db = AssetDatabase.LoadAssetAtPath<ContentDatabase>("Assets/_Antarium/Content/ContentDatabase.asset");
            Assert.IsNotNull(db);
            Assert.GreaterOrEqual(db.helpTopics.Count, 30);
            Assert.AreEqual(db.helpTopics.Count, db.helpTopics.Select(t => t.Id).Distinct().Count(), "help ids are unique");
            var biomes = db.worldGen.biomes;
            foreach (var t in db.helpTopics)
            {
                Assert.IsNotNull(t);
                Assert.IsFalse(string.IsNullOrWhiteSpace(t.body), t.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(t.category), t.Id);
                foreach (string p in HelpText.Placeholders(t.body)) Assert.IsTrue(HelpText.IsKey(p), $"{t.Id}: unknown placeholder {{{p}}}");
                if (t.reveal == HelpReveal.Biome) Assert.IsTrue(t.revealBiome != null && biomes.Contains(t.revealBiome), $"{t.Id}: reveal biome");
                if (t.reveal is HelpReveal.Research or HelpReveal.Flight)
                    Assert.IsTrue(t.revealResearch != null && db.research.Contains(t.revealResearch), $"{t.Id}: reveal research");
            }
            Assert.IsTrue(db.helpTopics.Any(t => t.reveal == HelpReveal.Always), "something to read from the start");

            var steps = new Tutorial(db.tutorialSteps).Steps;
            Assert.GreaterOrEqual(steps.Count, 8);
            Assert.AreEqual(steps.Count, steps.Select(s => s.Id).Distinct().Count(), "step ids are unique");
            Assert.AreEqual(TutorialGoal.None, steps[0].goal, "it opens with a message");
            Assert.AreEqual(TutorialGoal.None, steps[steps.Count - 1].goal, "and closes with one");
            Assert.IsTrue(steps.Any(s => s.goal == TutorialGoal.OpenHelp), "it shows where the help is");
            foreach (var s in steps)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.body), s.Id);
                foreach (string p in HelpText.Placeholders(s.body))
                    Assert.IsTrue(HelpText.IsKey(p) || p == "Cost" && s.building != null, $"{s.Id}: unknown placeholder {{{p}}}");
                if (s.goal == TutorialGoal.PlaceBuilding) Assert.IsTrue(s.building != null && db.buildings.Contains(s.building), s.Id);
                if (s.helpTopic != null) Assert.IsTrue(db.helpTopics.Contains(s.helpTopic), $"{s.Id}: help topic in the database");
            }
        }
    }
}
