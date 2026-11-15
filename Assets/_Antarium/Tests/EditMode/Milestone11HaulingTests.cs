using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>
    /// Milestone 11 follow-up: workers fetch dug-up ore before surface forage, piles in caves nobody has found are no jobs,
    /// leaves don't fill the stock, and the stars are the farthest background layer.
    /// </summary>
    public class Milestone11HaulingTests
    {
        TestContent _c;
        Simulation _sim;

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _c?.Dispose();
            _sim = null;
            _c = null;
        }

        void Make()
        {
            _c = new TestContent(4, 4, caves: false);
            _c.Config.colony.fieldRefreshTicks = 1;
            _c.Config.colony.forageHaulDelay = 20;
            _c.Config.colony.eggInterval = 1000f;
            _sim = new Simulation(_c.DB, _c.Config, 3);
        }

        /// <summary>A tunnel along the chamber floor to the right, explored like a dug one; returns its far end's floor.</summary>
        int2 DigTunnel(int length)
        {
            var q = _sim.Colony.QueenCell;
            for (int x = q.x; x <= q.x + length; x++)
            for (int y = q.y; y <= q.y + 1; y++)
                if (!_sim.World[x, y].IsOpen)
                    _sim.SetCell(new int2(x, y), new Cell(_sim.World[x, y].Material, 0, CellFlags.Revealed | CellFlags.Explored));
            return _sim.World.FloorBelow(new int2(q.x + length - 1, q.y + 1));
        }

        ushort ItemsAt(int2 c) => _sim.Fields.Get(FieldKind.Items, _sim.World, c);

        [Test]
        public void DugOre_IsFetchedBeforeSurfaceForage_UnlessTheColonyStarves()
        {
            Make();
            var ore = DigTunnel(22);
            int sx = _sim.Colony.QueenCell.x + 2;
            var seed = new int2(sx, _sim.World.SurfaceY[sx] + 1);
            _sim.Colony.Piles.Add(ore, _c.Item(_c.Ore), 1);
            _sim.Colony.Piles.Add(seed, _c.Item(_c.Seed), 1);
            _sim.Tick();
            int delay = _c.Config.colony.forageHaulDelay * FlowFields.StraightCost;
            Assert.AreEqual(0, ItemsAt(ore), "the ore underground is a first job");
            Assert.AreEqual(delay, ItemsAt(seed), "the forage on the surface only counts from forageHaulDelay cells on");

            // Starving: food on the surface comes first again.
            _sim.Colony.Inventory.Food = 0f;
            _sim.Tick();
            _sim.Tick();
            Assert.IsTrue(_sim.Colony.Starving);
            Assert.AreEqual(0, ItemsAt(seed), "food first while the colony starves");
        }

        [Test]
        public void PilesInUnfoundCaves_AreNoJobs_SoWorkersStillDig()
        {
            Make();
            var q = _sim.Colony.QueenCell;
            // A sealed pocket deep below, never explored, full of piles (like wild mushrooms in an unfound cave).
            var pocket = new int2(q.x + 20, q.y - 30);
            for (int x = pocket.x; x < pocket.x + 6; x++)
                _sim.SetCell(new int2(x, pocket.y), new Cell(_sim.World[x, pocket.y].Material, 0, CellFlags.None));
            for (int x = pocket.x; x < pocket.x + 6; x++) _sim.Colony.Piles.Add(new int2(x, pocket.y), _c.Item(_c.Ore), 20);
            // A mining order right under the chamber floor.
            _sim.EnqueueDesignate(new float2(q.x + 0.5f, q.y - 1.5f), 2f, true);
            for (int i = 0; i < (int)(20f / _sim.TickDuration); i++) _sim.Tick();
            Assert.Greater(_sim.Colony.Stats.CellsDug, 0, "workers dig instead of chasing piles they can't reach");
            Assert.AreEqual(FlowFields.Unreachable, ItemsAt(pocket));
        }

        [Test]
        public void Leaves_FillTheStockOnlyUpToTheirLimit_OreStillFits()
        {
            Make();
            var inv = _sim.Colony.Inventory;
            ushort leaf = _c.Item(_c.Leaf), ore = _c.Item(_c.Ore);
            int limit = _c.Config.colony.plantForageStockLimit;
            Assert.Greater(limit, 0);
            for (int i = 0; i < limit; i++)
            {
                Assert.IsTrue(inv.CanAccept(leaf));
                Assert.IsTrue(inv.Deliver(leaf, _sim.Colony.QueenCell));
            }
            Assert.IsFalse(inv.CanAccept(leaf), "no more leaves into the stock");
            Assert.IsTrue(inv.CanAccept(ore), "room left for ore");
        }

        [Test]
        public void Content_StarsAreTheFarthestLayer()
        {
#if UNITY_EDITOR
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/_Antarium/Content/GameConfig.asset");
            Assert.IsNotNull(config);
            var r = config.render;
            Assert.Less(r.starParallax, r.cloudParallax, "stars move less than the clouds");
            foreach (var layer in r.sceneryLayers)
                Assert.Less(r.starParallax, layer.parallax, "and less than every mountain, hill or treeline layer");
            Assert.Greater(config.colony.forageHaulDelay, 0);
            Assert.Greater(config.colony.plantForageStockLimit, 0);
#endif
        }
    }
}
