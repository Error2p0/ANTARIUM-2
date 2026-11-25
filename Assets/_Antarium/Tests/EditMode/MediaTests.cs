using Antarium.Content;
using Antarium.Gameplay;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;

namespace Antarium.Tests
{
    /// <summary>Milestone 9 store media: the instant showcase edits, the showcase colony and capture requests.</summary>
    public class MediaTests
    {
        [Test]
        public void CarveNow_DigsIntoTheFog()
        {
            using var content = new TestContent(4, 4, caves: false);
            using var sim = new Simulation(content.DB, content.Config, 7u);
            var below = sim.Colony.QueenCell + new int2(0, -30);
            Assert.IsFalse(sim.World[below.x, below.y].IsRevealed, "the test spot should start in the fog");
            sim.CarveNow(new float2(below.x + 0.5f, below.y + 0.5f), 1.5f);
            Assert.IsTrue(sim.World[below.x, below.y].IsEmpty);
            Assert.IsTrue(sim.World[below.x + 1, below.y].IsEmpty, "the whole brush is dug, not just the centre");

            sim.CarveLineNow(new float2(below.x + 0.5f, below.y + 0.5f), new float2(below.x + 10.5f, below.y + 0.5f), 1f);
            for (int x = 0; x <= 10; x++) Assert.IsTrue(sim.World[below.x + x, below.y].IsOpen, $"tunnel cell {x}");

            sim.FillRectNow(below, below + new int2(2, 0), content.DB.GetMaterialIndex(content.Stone));
            Assert.IsFalse(sim.World[below.x + 1, below.y].IsOpen);
            Assert.IsTrue(sim.World[below.x + 1, below.y].IsRevealed);
        }

        [Test]
        public void PlaceNow_BuildsFinishedWithoutPayingOrUnlocking()
        {
            using var content = new TestContent(4, 4, caves: false);
            using var sim = new Simulation(content.DB, content.Config, 7u);
            var q = sim.Colony.QueenCell;
            var min = q + new int2(10, -20);
            sim.CarveRectNow(min, min + new int2(6, 4));
            sim.FillRectNow(min + new int2(0, -1), min + new int2(6, -1), content.DB.GetMaterialIndex(content.Stone));
            int smelter = content.DB.GetBuildingIndex(content.Smelter);
            bool unlocked = sim.Colony.BuildingUnlocked[smelter];
            int stored = sim.Colony.Inventory.Stored;

            Assert.AreEqual(PlaceResult.Ok, sim.PlaceNow(smelter, min + new int2(1, 0), default));
            int id = sim.Buildings.At(min + new int2(2, 1));
            Assert.GreaterOrEqual(id, 0);
            Assert.AreEqual(BuildingState.Active, sim.Buildings.Get(id).State);
            Assert.AreEqual(stored, sim.Colony.Inventory.Stored, "nothing paid");
            Assert.AreEqual(unlocked, sim.Colony.BuildingUnlocked[smelter], "the lock is left as it was");
            Assert.AreEqual(PlaceResult.Occupied, sim.PlaceNow(smelter, min + new int2(2, 0), default));
        }

        [Test]
        public void ResearchAllNow_CompletesEveryNode()
        {
            using var content = new TestContent(4, 4, caves: false);
            using var sim = new Simulation(content.DB, content.Config, 7u);
            sim.ResearchAllNow();
            Assert.AreEqual(content.DB.Research.Length, sim.Research.DoneCount);
            Assert.IsTrue(sim.Colony.SoldiersUnlocked);
        }

        [Test]
        public void CaptureRequest_RoundTripsAndFilters()
        {
            var request = new CaptureRequest { Shots = new[] { "stills", "raid" }, OutputDirectory = "D:/media", RebuildShowcase = true };
            var back = CaptureRequest.Decode(request.Encode());
            CollectionAssert.AreEqual(request.Shots, back.Shots);
            Assert.AreEqual("D:/media", back.OutputDirectory);
            Assert.IsTrue(back.RebuildShowcase);
            Assert.IsTrue(back.Wants("RAID"));
            Assert.IsFalse(back.Wants("flight"));
            Assert.IsTrue(CaptureRequest.Decode("all||0").Wants("flight"));
            Assert.IsTrue(CaptureRequest.Decode("||0").Wants("flight"), "no names = every shot");
            var store = CaptureRequest.Decode("store||0");
            Assert.IsTrue(store.Wants("surface", storeMedia: true));
            Assert.IsFalse(store.Wants("trees", storeMedia: false), "\"store\" leaves out the review stills");
            Assert.IsTrue(CaptureRequest.Decode("trees||0").Wants("trees", storeMedia: false));
            var shorts = CaptureRequest.Decode("shorts||0");
            Assert.IsTrue(shorts.Wants("shorts_deep"), "\"shorts\" runs the vertical short-form shots");
            Assert.IsFalse(shorts.Wants("surface", storeMedia: true), "and nothing else");
        }

        [Test]
        public void CaptureSound_WritesAPlayable16BitWav()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "antarium_capture_sound_test.wav");
            try
            {
                CaptureSound.WriteWav(path, new[] { 0f, 1f, -1f, 0.5f, 2f, -2f }, 2, 22050);
                var bytes = System.IO.File.ReadAllBytes(path);
                Assert.AreEqual(44 + 6 * 2, bytes.Length);
                Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.AreEqual(36 + 12, System.BitConverter.ToInt32(bytes, 4));
                Assert.AreEqual(2, System.BitConverter.ToInt16(bytes, 22), "channels");
                Assert.AreEqual(22050, System.BitConverter.ToInt32(bytes, 24), "sample rate");
                Assert.AreEqual(16, System.BitConverter.ToInt16(bytes, 34), "bits");
                Assert.AreEqual(32767, System.BitConverter.ToInt16(bytes, 46));
                Assert.AreEqual(-32767, System.BitConverter.ToInt16(bytes, 48));
                Assert.AreEqual(32767, System.BitConverter.ToInt16(bytes, 52), "clipped, not wrapped");
                Assert.AreEqual(-32768, System.BitConverter.ToInt16(bytes, 54));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public void IntroScreen_PlaysLiftsAndSkips()
        {
            var intro = new IntroScreen();
            int finished = 0;
            intro.Finished += () => finished++;
            Assert.IsFalse(intro.Playing);
            intro.Play();
            Assert.IsTrue(intro.Playing);
            Assert.IsFalse(intro.Root.ClassListContains("hidden"));
            // A long hitch only counts as a short step: the logos are not skipped by a slow first frame.
            intro.Update(3f, false);
            Assert.IsTrue(intro.Playing);
            for (int i = 0; i < 20 * 10 && intro.Playing; i++) intro.Update(0.05f, false);
            Assert.IsFalse(intro.Playing, "ends on its own in under ten seconds");
            Assert.AreEqual(1, finished);
            Assert.IsTrue(intro.Root.ClassListContains("hidden"));

            // A press goes straight to a short lift, a second press ends it.
            intro.Play();
            intro.Update(0.05f, true);
            Assert.IsTrue(intro.Playing, "the lift still fades");
            intro.Update(0.05f, true);
            Assert.IsFalse(intro.Playing);
            Assert.AreEqual(2, finished);

            intro.Play();
            intro.Stop();
            Assert.IsFalse(intro.Playing);
            Assert.AreEqual(2, finished, "Stop is silent");
        }

        [Test]
        public void ShowcaseColony_BuildsWithEverythingInPlace()
        {
            var db = AssetDatabase.LoadAssetAtPath<ContentDatabase>("Assets/_Antarium/Content/ContentDatabase.asset");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/_Antarium/Content/GameConfig.asset");
            using var content = ContentDB.Build(db);
            using var sim = ShowcaseColony.Build(content, config, settleSeconds: 5f);
            CollectionAssert.IsEmpty(ShowcaseColony.Warnings, string.Join("; ", ShowcaseColony.Warnings));
            var layout = ShowcaseColony.Last;
            Assert.AreEqual(ShowcaseColony.OutpostDepths.Length, layout.Outposts.Length);
            Assert.Greater(sim.Ants.Count, 250);
            Assert.AreEqual(content.Research.Length, sim.Research.DoneCount);
            Assert.IsTrue(content.TryGetBuildingIndex("smelter", out int smelter));
            int id = sim.Buildings.At(layout.Smelter);
            Assert.GreaterOrEqual(id, 0, "smelter at its layout spot");
            Assert.AreEqual(smelter, sim.Buildings.Get(id).Def);
            Assert.GreaterOrEqual(sim.Buildings.At(layout.Lift), 0, "lift at its layout spot");
            foreach (var o in layout.Outposts) Assert.IsTrue(sim.World[o.x, o.y].IsOpen, $"outpost {o} is dug out");
            for (int y = layout.Outposts[^1].y + 2; y < layout.Queen.y - 8; y += 50)
                Assert.IsTrue(sim.World[layout.ShaftX, y].IsOpen, $"shaft open at {y}");
        }

        [Test]
        public void ShowcaseColony_InDemoMode_StopsAboveTheSealWithNothingFromTheFullGame()
        {
            var db = AssetDatabase.LoadAssetAtPath<ContentDatabase>("Assets/_Antarium/Content/ContentDatabase.asset");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/_Antarium/Content/GameConfig.asset");
            using var content = ContentDB.Build(db);
            content.DemoActive = true;
            using var sim = ShowcaseColony.Build(content, config, settleSeconds: 5f);
            var demo = content.ActiveDemo;
            Assert.IsNotNull(demo);
            Assert.IsTrue(sim.Demo.Sealed, "the demo showcase's world has the seal");
            Assert.IsFalse(sim.Demo.SealReached, "the build leaves the seal for the shot to reach");
            var layout = ShowcaseColony.Last;
            CollectionAssert.IsEmpty(layout.Outposts, "no outposts beyond the seal");

            // The deepest tunnel comes down to just above the seal: rock left above it in every column it covers.
            var s = layout.SealShaft;
            Assert.AreEqual(layout.ShaftX, s.x);
            Assert.IsTrue(sim.World[s.x, s.y].IsOpen, "the landing is dug out");
            for (int y = s.y; y < s.y + 25; y += 4) Assert.IsTrue(sim.World[s.x, y].IsOpen, $"tunnel open at {y}");
            for (int dx = -1; dx <= 1; dx++)
            {
                int top = sim.Demo.SealTop[s.x + dx];
                Assert.Greater(top, 0);
                Assert.IsFalse(sim.World[s.x + dx, top + 1].IsOpen, $"rock left above the seal in column {s.x + dx}");
            }

            // Nothing the demo doesn't have: research, buildings, items, princesses.
            for (int r = 0; r < content.Research.Length; r++)
                Assert.AreEqual(!demo.FullGameResearch[r], sim.Research.Done[r], $"research {content.Research[r].Def.Id}");
            foreach (var b in sim.Buildings.All)
                if (b.Alive) Assert.IsFalse(demo.FullGameBuilding[b.Def], $"full game building {content.Buildings[b.Def].Def.Id} placed");
            for (int i = 0; i < content.Items.Length; i++)
                if (demo.FullGameItem[i]) Assert.AreEqual(0, sim.Colony.Inventory.Items[i], $"full game item {content.Items[i].Id} in stock");
            Assert.AreEqual(0, sim.Colony.Princesses);
            CollectionAssert.IsEmpty(ShowcaseColony.Warnings, string.Join("; ", ShowcaseColony.Warnings));
        }
    }
}
