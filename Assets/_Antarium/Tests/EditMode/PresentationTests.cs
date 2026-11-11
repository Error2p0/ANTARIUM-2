using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;

namespace Antarium.Tests
{
    /// <summary>Milestone 10.7: the ore overlay's survey, tooltips and key hints, and the new sound cues.</summary>
    public class PresentationTests
    {
        TestContent C;

        [SetUp]
        public void SetUp() => C = new TestContent(1, 1, caves: false);

        [TearDown]
        public void TearDown() => C.Dispose();

        static void Set(WorldGrid world, int x, int y, ushort material, byte density, CellFlags flags) =>
            world.Cells[world.Index(x, y)] = new Cell(material, density, flags);

        [Test]
        public void OreSurvey_CountsKnownSolidFindsPerItem()
        {
            using var world = new WorldGrid(1, 1);
            ushort dirt = C.Index(C.Dirt), ore = C.Index(C.OreRock), coal = C.Index(C.CoalRock);
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
                Set(world, x, y, dirt, Cell.Full, CellFlags.Revealed);
            Set(world, 1, 1, ore, Cell.Full, CellFlags.Revealed);
            Set(world, 2, 1, ore, Cell.Full, CellFlags.Revealed);
            Set(world, 3, 1, ore, 200, CellFlags.Revealed);                 // partly dug, still solid
            Set(world, 4, 1, ore, Cell.Full, CellFlags.Scanned);            // in the fog, found by Seismic Sense
            Set(world, 5, 1, ore, Cell.Full, CellFlags.None);               // in the fog, unknown
            Set(world, 6, 1, ore, 0, CellFlags.Revealed);                   // dug out: the material stays for the wall
            Set(world, 1, 2, coal, Cell.Full, CellFlags.Revealed);
            Set(world, 2, 2, coal, Cell.Full, CellFlags.Revealed | CellFlags.Scanned);

            using var survey = new OreSurvey(C.DB);
            CollectionAssert.Contains(survey.Finds, C.Item(C.Ore));
            CollectionAssert.Contains(survey.Finds, C.Item(C.Coal));
            CollectionAssert.DoesNotContain(survey.Finds, C.Item(C.Seed), "food is not a find");
            Assert.AreEqual(ContentDB.NoItem, OreSurvey.FindItem(C.DB, dirt));
            Assert.AreEqual(C.Item(C.Ore), OreSurvey.FindItem(C.DB, ore));

            survey.Count(world);
            Assert.AreEqual(4, survey.Known(C.Item(C.Ore)), "revealed or scanned solid cells only");
            Assert.AreEqual(2, survey.Known(C.Item(C.Coal)));
            Assert.AreEqual(0, survey.Known(C.Item(C.Seed)));

            // Digging one out and counting again.
            Set(world, 1, 1, ore, 0, CellFlags.Revealed);
            survey.Count(world);
            Assert.AreEqual(3, survey.Known(C.Item(C.Ore)), "counts start over every time");
        }
    }
}
