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
    }
}
