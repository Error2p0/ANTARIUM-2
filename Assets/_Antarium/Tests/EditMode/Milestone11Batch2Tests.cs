using Antarium.Content;
using Antarium.Gameplay;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>Milestone 11, batch 2: the Dig tool's strength, brush sizes 1..3, walls built from the inside out.</summary>
    public class Milestone11Batch2Tests : TunnelFixture
    {
        DigConfig Dig => C.Config.dig;

        // ---------------------------------------------------------------- digging strength

        [Test]
        public void Stamina_RunsDry_RestsUntilAQuarter_AndComesBackOnlyAfterTheDelay()
        {
            Dig.staminaPerHit = 0.1f;
            Dig.staminaRegen = 0.5f;
            Dig.staminaRegenDelay = 1f;
            Dig.staminaResume = 0.25f;
            var s = new DigStamina(Dig);
            Assert.AreEqual(1f, s.Value);
            Assert.IsTrue(s.CanDig);

            for (int i = 0; i < 10; i++) s.Spend(s.HitCost(2));
            Assert.AreEqual(0f, s.Value, 1e-5f);
            Assert.IsTrue(s.Exhausted);
            Assert.IsFalse(s.CanDig);

            s.Update(0.9f);
            Assert.AreEqual(0f, s.Value, 1e-5f, "nothing comes back right after digging");
            s.Update(0.2f);
            Assert.Greater(s.Value, 0f, "then it refills");
            Assert.IsTrue(s.Exhausted, "but the tool rests until a quarter is back");
            s.Update(0.5f);
            Assert.IsFalse(s.Exhausted);
            Assert.IsTrue(s.CanDig);
            s.Update(10f);
            Assert.AreEqual(1f, s.Value, "full again, never more");
        }

        [Test]
        public void Stamina_BiggerBrushesCostByArea()
        {
            var s = new DigStamina(Dig);
            float small = s.HitCost(1), medium = s.HitCost(2), large = s.HitCost(3);
            Assert.AreEqual(Dig.staminaPerHit, medium, 1e-6f, "size 2 costs the base");
            Assert.Less(small, medium);
            Assert.Greater(large, medium);
            float scale = Dig.brushSizeScales[2];
            Assert.AreEqual(Dig.staminaPerHit * scale * scale, large, 1e-6f);
        }

        // ---------------------------------------------------------------- brush sizes

        [Test]
        public void BrushSizes_TwoIsTheBaseRadius_AndEachToolKeepsItsOwn()
        {
            Assert.AreEqual(1.6f, BrushSizes.Radius(Dig, 1.6f, 2), 1e-5f);
            Assert.Less(BrushSizes.Radius(Dig, 1.6f, 1), 1.6f);
            Assert.Greater(BrushSizes.Radius(Dig, 1.6f, 3), 1.6f);
            Assert.AreEqual(BrushSizes.Radius(Dig, 1.6f, 1), BrushSizes.Radius(Dig, 1.6f, 0), "clamped to 1..3");
            Assert.AreEqual(BrushSizes.Radius(Dig, 1.6f, 3), BrushSizes.Radius(Dig, 1.6f, 9));

            var sizes = ToolController.NewBrushSizes();
            var tools = new ToolController(C.Config, C.DB, new System.Collections.Generic.List<Presentation.BuildGhost>(), sizes);
            Assert.AreEqual(2, tools.BrushSize);
            tools.SetBrushSize(3);
            Assert.AreEqual(3, tools.BrushSize);
            Assert.AreEqual(BrushSizes.Radius(Dig, Dig.brushRadius, 3), tools.BrushRadius(ToolMode.Dig), 1e-5f);
            tools.SetMode(ToolMode.Mine);
            Assert.AreEqual(2, tools.BrushSize, "the Mine tool has its own size");
            tools.SetBrushSize(1);
            tools.SetMode(ToolMode.Dig);
            Assert.AreEqual(3, tools.BrushSize);
            Assert.AreEqual(1, sizes[(int)ToolMode.Mine], "kept in the shared array (it outlives a colony)");
            tools.SetMode(ToolMode.Build);
            tools.SetBrushSize(1);
            Assert.AreEqual(2, sizes[(int)ToolMode.Build], "the Build tool has no brush");
        }

        [Test]
        public void MineOrders_FollowTheBrushSize()
        {
            int2 at = Queen + new int2(0, -6);
            Sim.EnqueueDesignate(new float2(at) + 0.5f, BrushSizes.Radius(Dig, Dig.orderBrushRadius, 1), true);
            Sim.Tick();
            int small = Sim.Colony.Designated.Count;
            Sim.EnqueueDesignate(new float2(at) + 0.5f, BrushSizes.Radius(Dig, Dig.orderBrushRadius, 3), true);
            Sim.Tick();
            Assert.GreaterOrEqual(small, 1);
            Assert.Greater(Sim.Colony.Designated.Count, small * 4, "size 3 covers far more than size 1");
        }

        // ---------------------------------------------------------------- wall order

        bool IsBuildSource(int2 c) => Sim.Fields.Get(FieldKind.Build, Sim.World, c) == 0;

        [Test]
        public void Walls_TheFarthestFromTheNestAreBuiltFirst()
        {
            var start = CarveTunnel(10);
            int wall = C.BuildingIndex(C.Wall);
            // A block three cells deep across the two-high tunnel.
            for (int x = 3; x <= 5; x++)
            for (int y = 0; y <= 1; y++)
                Sim.EnqueueBuild(wall, start + new int2(x, y), default);
            Run(0.5f);

            Assert.IsTrue(IsBuildSource(start + new int2(5, 0)) && IsBuildSource(start + new int2(5, 1)), "the far column is a site");
            Assert.IsFalse(IsBuildSource(start + new int2(3, 0)) || IsBuildSource(start + new int2(3, 1)), "the near column waits");
            Assert.IsFalse(IsBuildSource(start + new int2(4, 0)) || IsBuildSource(start + new int2(4, 1)), "the middle column waits");

            // The ants build all six: a near wall never shuts them off from the rest.
            Run(60f);
            for (int x = 3; x <= 5; x++)
            for (int y = 0; y <= 1; y++)
            {
                var c = start + new int2(x, y);
                Assert.AreEqual(BuildingSystem.None, Sim.Buildings.At(c), $"wall at {c} done");
                Assert.IsFalse(Sim.World[c.x, c.y].IsOpen, $"{c} is solid");
            }
        }

        [Test]
        public void Walls_SeparateGroupsAreOrderedOnTheirOwn()
        {
            var start = CarveTunnel(14);
            int wall = C.BuildingIndex(C.Wall);
            Sim.EnqueueBuild(wall, start + new int2(2, 0), default);
            Sim.EnqueueBuild(wall, start + new int2(3, 0), default);
            Sim.EnqueueBuild(wall, start + new int2(10, 1), default); // a lone wall farther on
            Run(0.5f);
            Assert.IsTrue(IsBuildSource(start + new int2(3, 0)));
            Assert.IsFalse(IsBuildSource(start + new int2(2, 0)));
            Assert.IsTrue(IsBuildSource(start + new int2(10, 1)), "a group of one is always a site");
        }
    }
}
