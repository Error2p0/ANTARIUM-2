using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>Shared setup: a generated world without caves, no upkeep, no foraging, flow fields every tick.</summary>
    public abstract class SimulationFixture
    {
        protected TestContent C;
        protected Simulation Sim;

        [SetUp]
        public void SetUp()
        {
            C = new TestContent(4, 4, caves: false);
            var c = C.Config.colony;
            c.startingWorkers = 6;
            c.startingFood = 50f;
            c.forageInterval = 1000f;
            c.workerUpkeep = 0f;
            c.queenUpkeep = 0f;
            c.eggInterval = 1000f;
            c.fieldRefreshTicks = 1;
            Sim = new Simulation(C.DB, C.Config, 11);
        }

        [TearDown]
        public void TearDown()
        {
            Sim.Dispose();
            C.Dispose();
        }

        protected void Run(float seconds)
        {
            int ticks = (int)math.round(seconds / Sim.TickDuration);
            for (int i = 0; i < ticks; i++) Sim.Tick();
        }

        protected int2 Queen => Sim.Colony.QueenCell;
        protected Inventory Inv => Sim.Colony.Inventory;

        protected int ResearchIndex(ResearchDef d) => C.DB.TryGetResearchIndex(d.Id, out int i) ? i : -1;
        protected int AbilityIndex(AbilityDef d) => C.DB.TryGetAbilityIndex(d.Id, out int i) ? i : -1;
        protected int ContractIndex(ContractDef d) => C.DB.TryGetContractIndex(d.Id, out int i) ? i : -1;
        protected int EventIndex(RandomEventDef d) => C.DB.TryGetEventIndex(d.Id, out int i) ? i : -1;

        /// <summary>Researches the given nodes (gives the knowledge and a bar first).</summary>
        protected void Unlock(params ResearchDef[] nodes)
        {
            Inv.Knowledge += 100f;
            Inv.Add(C.Item(C.Bar), 1, Queen);
            foreach (var n in nodes) Sim.EnqueueResearch(ResearchIndex(n));
            Sim.Tick();
            foreach (var n in nodes) Assert.IsTrue(Sim.Research.Done[ResearchIndex(n)], n.Id);
        }

        protected bool FindMachineSpot(BuildingDef def, out int2 origin)
        {
            int index = C.BuildingIndex(def);
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -6; dx <= 6; dx++)
            {
                origin = Queen + new int2(dx, dy);
                if (Sim.Buildings.CanPlace(index, origin, default) == PlaceResult.Ok) return true;
            }
            origin = default;
            return false;
        }

        protected int CountMaterial(TerrainMaterialDef def, bool revealedOnly = false)
        {
            ushort m = C.Index(def);
            int n = 0;
            foreach (var cell in Sim.World.Cells)
                if (cell.Material == m && !cell.IsOpen && (!revealedOnly || cell.IsRevealed)) n++;
            return n;
        }

        /// <summary>Digs a tunnel from <paramref name="from"/> to <paramref name="to"/> with the player's dig command, one step per tick.</summary>
        protected void DigLine(float2 from, float2 to)
        {
            int steps = (int)math.ceil(math.distance(from, to) / 0.5f);
            for (int i = 0; i <= steps; i++)
            {
                Sim.Enqueue(new DigCommand(math.lerp(from, to, i / (float)math.max(1, steps)), 1.2f, 1000f));
                Sim.Tick();
            }
        }
    }

    public class ResearchTests : SimulationFixture
    {
        [Test]
        public void Research_NeedsPrerequisitesAndPaysItsCost()
        {
            int belts = ResearchIndex(C.FastBelts), lab = ResearchIndex(C.LabResearch);
            int labBuilding = C.BuildingIndex(C.Lab);
            ushort bar = C.Item(C.Bar);
            Assert.IsFalse(Sim.Colony.BuildingUnlocked[labBuilding], "locked behind research");
            Assert.AreEqual(ResearchResult.CantAfford, Sim.Research.Check(belts));
            Assert.AreEqual(ResearchResult.NeedsPrerequisites, Sim.Research.Check(lab));

            Inv.Knowledge = 12f;
            Inv.Add(bar, 1, Queen);
            Sim.EnqueueResearch(lab);
            Sim.Tick();
            Assert.IsFalse(Sim.Research.Done[lab]);
            bool refused = false;
            foreach (var e in Sim.Events.Read<ResearchFailedEvent>())
                refused |= e.Research == lab && e.Result == ResearchResult.NeedsPrerequisites;
            Assert.IsTrue(refused, "refusal is reported");

            Sim.EnqueueResearch(belts);
            Sim.EnqueueResearch(lab);
            Sim.Tick();
            Assert.IsTrue(Sim.Research.Done[belts] && Sim.Research.Done[lab]);
            Assert.AreEqual(2f, Inv.Knowledge, 1e-4f, "5 + 5 knowledge paid");
            Assert.AreEqual(0, Inv.Items[bar], "item cost paid");
            Assert.AreEqual(2f, Sim.Colony.Mods.Mult(StatKind.BeltSpeed), 1e-4f, "+100% belt speed");
            Assert.IsTrue(Sim.Colony.BuildingUnlocked[labBuilding]);
            Assert.AreEqual(ResearchResult.AlreadyDone, Sim.Research.Check(belts));
        }

        [Test]
        public void LockedBuildings_CannotBePlaced()
        {
            Assert.IsTrue(FindMachineSpot(C.Smelter, out var origin), "room for a 2x2 machine");
            Assert.AreEqual(PlaceResult.Locked, Sim.Buildings.CanPlace(C.BuildingIndex(C.Lab), origin, default));
            Unlock(C.FastBelts, C.LabResearch);
            Assert.AreEqual(PlaceResult.Ok, Sim.Buildings.CanPlace(C.BuildingIndex(C.Lab), origin, default));
        }

        [Test]
        public void Lab_StudiesItemsIntoKnowledge()
        {
            Unlock(C.FastBelts, C.LabResearch);
            Inv.Knowledge = 0f;
            Assert.IsTrue(FindMachineSpot(C.Lab, out var origin));
            Sim.EnqueueBuild(C.BuildingIndex(C.Lab), origin, default);
            Sim.Tick();
            Sim.Buildings.CompleteAllBlueprints();
            Sim.Tick();

            ushort ore = C.Item(C.Ore);
            Assert.IsTrue(Sim.Buildings.TryFeed(origin, ore));
            Assert.IsTrue(Sim.Buildings.TryFeed(origin, ore));
            Run(2.5f);
            Assert.AreEqual(6f, Inv.Knowledge, 1e-3f, "two studies of 3 knowledge each");
            Assert.AreEqual(6.0, Sim.Colony.Stats.KnowledgeEarned, 1e-3);
        }
    }

    public class ContractTests : SimulationFixture
    {
        int SlotOf(ContractDef def)
        {
            int index = ContractIndex(def);
            for (int i = 0; i < Sim.Contracts.Active.Count; i++)
                if (Sim.Contracts.Active[i].Def == index) return i;
            return -1;
        }

        [Test]
        public void Contracts_IntroFirstThenRepeatablesThatGrow()
        {
            var contracts = Sim.Contracts;
            Assert.AreEqual(2, contracts.Active.Count, "both contracts are on the board");
            Assert.AreEqual(ContractIndex(C.DigIntro), contracts.Active[0].Def, "the intro contract comes first");

            // Dig a few cells below the nest floor.
            Sim.Enqueue(new DigCommand(new float2(Queen.x + 0.5f, Queen.y - 2f), 2.5f, 1000f));
            Sim.Tick();
            Assert.GreaterOrEqual(Sim.Colony.Stats.CellsDug, 5);
            int intro = SlotOf(C.DigIntro);
            Assert.IsTrue(contracts.Active[intro].IsDone);

            float knowledge = Inv.Knowledge;
            Sim.EnqueueClaim(intro);
            Sim.Tick();
            Assert.AreEqual(knowledge + 2f, Inv.Knowledge, 1e-4f, "reward paid");
            Assert.AreEqual(-1, SlotOf(C.DigIntro), "intro contracts are offered once");
            Assert.AreEqual(1, contracts.Completed[ContractIndex(C.DigIntro)]);

            ushort ore = C.Item(C.Ore), bar = C.Item(C.Bar);
            for (int i = 0; i < 3; i++)
            {
                Inv.BeginTransit(ore);
                Assert.IsTrue(Inv.Deliver(ore, Queen));
            }
            Sim.Tick();
            int deliver = SlotOf(C.DeliverOre);
            Assert.AreEqual(3, contracts.Active[deliver].Progress);
            Assert.IsTrue(contracts.Active[deliver].IsDone);
            Sim.EnqueueClaim(deliver);
            Sim.Tick();
            Assert.AreEqual(1, Inv.Items[bar], "item reward");
            Assert.AreEqual(2, Sim.Colony.Stats.ContractsCompleted);

            deliver = SlotOf(C.DeliverOre);
            Assert.GreaterOrEqual(deliver, 0, "the repeatable contract comes back");
            Assert.AreEqual(6, contracts.Active[deliver].Target, "and grows (+100%)");
            Assert.AreEqual(0, contracts.Active[deliver].Progress, "progress counts from the new start");
        }

        [Test]
        public void Contracts_CannotBeClaimedEarly()
        {
            float knowledge = Inv.Knowledge;
            Sim.EnqueueClaim(0);
            Sim.Tick();
            Assert.AreEqual(knowledge, Inv.Knowledge);
            Assert.AreEqual(ContractIndex(C.DigIntro), Sim.Contracts.Active[0].Def);
        }
    }

    public class AbilityTests : SimulationFixture
    {
        [Test]
        public void Frenzy_SpeedsWorkersUntilItRunsOut()
        {
            int frenzy = AbilityIndex(C.Frenzy);
            Sim.EnqueueAbility(frenzy, Queen);
            Sim.Tick();
            Assert.AreEqual(1.75f, Sim.Abilities.WorkFactor, 1e-4f);
            Assert.Greater(Sim.Abilities.Cooldown[frenzy], 0f);

            Sim.Events.Flush();
            Sim.EnqueueAbility(frenzy, Queen);
            Sim.Tick();
            bool cooling = false;
            foreach (var e in Sim.Events.Read<AbilityFailedEvent>()) cooling |= e.Result == AbilityResult.Cooling;
            Assert.IsTrue(cooling, "can't be used while recharging");

            Run(5.1f);
            Assert.AreEqual(1f, Sim.Abilities.WorkFactor, 1e-4f, "effect over");
        }

        [Test]
        public void SeismicSense_IsLockedThenRevealsOreThroughTheFog()
        {
            int seismic = AbilityIndex(C.Seismic);
            Assert.AreEqual(AbilityResult.Locked, Sim.Abilities.Check(seismic));
            Unlock(C.SeismicResearch);

            var target = Queen + new int2(0, -40); // deep in the stone (stone drops ore)
            int hidden = 0;
            ushort stone = C.Index(C.Stone);
            for (int y = -10; y <= 10; y++)
            for (int x = -10; x <= 10; x++)
            {
                if (x * x + y * y > 100) continue;
                var cell = Sim.World[target.x + x, target.y + y];
                if (cell.Material == stone && !cell.IsRevealed) hidden++;
            }
            Assert.Greater(hidden, 100, "test setup: hidden stone around the target");

            Sim.Events.Flush();
            Sim.EnqueueAbility(seismic, target);
            Sim.Tick();
            int revealed = 0;
            foreach (var e in Sim.Events.Read<AbilityUsedEvent>()) revealed = e.Amount;
            Assert.AreEqual(hidden, revealed);
            Assert.IsTrue(Sim.World[target.x, target.y].IsRevealed);
        }

        [Test]
        public void RoyalDecree_LaysABurstOfEggs()
        {
            Assert.AreEqual(0, Sim.Colony.Eggs.Count);
            Sim.EnqueueAbility(AbilityIndex(C.Decree), Queen);
            Sim.Tick();
            Assert.AreEqual(3, Sim.Colony.Eggs.Count);
        }
    }

    public class RandomEventTests : SimulationFixture
    {
        bool Published(RandomEventPhase phase)
        {
            foreach (var e in Sim.Events.Read<RandomEventEvent>())
                if (e.Phase == phase) return true;
            return false;
        }

        [Test]
        public void Meteorite_LeavesADiggableLumpNearTheNest()
        {
            Assert.AreEqual(0, CountMaterial(C.Meteor));
            Assert.IsTrue(Sim.RandomEvents.Trigger(EventIndex(C.MeteorEvent)));
            int lump = CountMaterial(C.Meteor);
            Assert.Greater(lump, 4);
            Assert.AreEqual(lump, CountMaterial(C.Meteor, revealedOnly: true), "the lump is visible");
            Assert.IsTrue(Published(RandomEventPhase.Started));
        }

        [Test]
        public void RichVein_TurnsGroundBesideTheTunnelsIntoOre()
        {
            int before = CountMaterial(C.Crystal, revealedOnly: true);
            Assert.IsTrue(Sim.RandomEvents.Trigger(EventIndex(C.VeinEvent)));
            Assert.Greater(CountMaterial(C.Crystal, revealedOnly: true), before + 5);
        }

        [Test]
        public void GoldenBeetle_CanBeCaughtForItsReward()
        {
            Assert.IsTrue(Sim.RandomEvents.Trigger(EventIndex(C.BeetleEvent)));
            Assert.AreEqual(1, Sim.RandomEvents.Critters.Count);
            Run(1f); // it wanders
            float knowledge = Inv.Knowledge;
            Sim.EnqueueCatch(Sim.RandomEvents.Critters[0].Pos + new float2(0.4f, 0f));
            Sim.Tick();
            Assert.AreEqual(0, Sim.RandomEvents.Critters.Count);
            Assert.AreEqual(0, Sim.RandomEvents.Active.Count);
            Assert.AreEqual(knowledge + 5f, Inv.Knowledge, 1e-4f);
            Assert.AreEqual(2, Inv.Items[C.Item(C.Bar)]);
            Assert.AreEqual(1, Sim.Colony.Stats.EventsWon);
            Assert.IsTrue(Published(RandomEventPhase.Won));
        }

        [Test]
        public void GoldenBeetle_GetsAwayWhenIgnored()
        {
            Assert.IsTrue(Sim.RandomEvents.Trigger(EventIndex(C.BeetleEvent)));
            Run(31f);
            Assert.AreEqual(0, Sim.RandomEvents.Critters.Count);
            Assert.IsTrue(Published(RandomEventPhase.Lost));
        }

        [Test]
        public void TrappedAnts_JoinWhenDugOut()
        {
            int ants = Sim.Ants.Count;
            Assert.IsTrue(Sim.RandomEvents.Trigger(EventIndex(C.TrappedEvent)));
            Assert.AreEqual(ants + 3, Sim.Ants.Count);
            var e = Sim.RandomEvents.Active[0];
            Run(1f);
            Assert.AreEqual(1, Sim.RandomEvents.Active.Count, "still trapped");

            DigLine(new float2(Queen) + 0.5f, new float2(e.Cell) + 0.5f);
            Run(1f);
            Assert.AreEqual(0, Sim.RandomEvents.Active.Count);
            Assert.IsTrue(Published(RandomEventPhase.Won));
            Assert.AreEqual(ants + 3, Sim.Ants.Count, "the rescued ants stay");
        }

        [Test]
        public void TrappedAnts_AreLostWhenTimeRunsOut()
        {
            int ants = Sim.Ants.Count;
            Assert.IsTrue(Sim.RandomEvents.Trigger(EventIndex(C.TrappedEvent)));
            Run(61f);
            Assert.AreEqual(0, Sim.RandomEvents.Active.Count);
            Assert.IsTrue(Published(RandomEventPhase.Lost));
            Assert.AreEqual(ants, Sim.Ants.Count);
        }
    }

    public class OfflineTests : SimulationFixture
    {
        [Test]
        public void Offline_ReplaysRecentRatesIntoTheStock()
        {
            ushort ore = C.Item(C.Ore);
            var meter = Sim.Colony.Meter;
            meter.AddItem(ore, 30f);
            meter.AddFood(20f);
            meter.AddKnowledge(4f);
            meter.Update(60f);
            Assert.AreEqual(30f, meter.ItemPerMinute(ore), 1e-3f);

            C.Config.save.offlineEfficiency = 0.5f;
            var report = OfflineProgress.Apply(Sim, 600.0);
            Assert.AreEqual(600.0, report.CreditedSeconds, 1e-6);
            Assert.AreEqual(Inv.Capacity, Inv.Items[ore], "150 ore made, only what fits is kept");
            Assert.AreEqual(Inv.Capacity, report.Items[ore]);
            Assert.AreEqual(150 - Inv.Capacity, report.ItemsLost);
            Assert.AreEqual(20f, Inv.Knowledge, 1e-3f, "4/min * 50% * 10 min");
            Assert.AreEqual(Inv.FoodCapacity, Inv.Food, 1e-3f, "+100 food, capped");
            Assert.AreEqual(Inv.Capacity, (int)Sim.Colony.Stats.Delivered[ore], "counts as delivered (contracts)");
        }

        [Test]
        public void Offline_IsCappedAndTheQueenKeepsLaying()
        {
            var c = C.Config.colony;
            c.eggInterval = 6f;
            c.eggFoodCost = 6f;
            c.workerUpkeep = 0.25f;
            c.queenUpkeep = 1.5f;
            C.Config.save.offlineMaxHours = 8f;
            Sim.Colony.Meter.AddFood(60f);
            Sim.Colony.Meter.Update(60f);

            int ants = Sim.Ants.Count;
            var report = OfflineProgress.Apply(Sim, 10 * 3600.0);
            Assert.AreEqual(8 * 3600.0, report.CreditedSeconds, 1e-6);
            Assert.IsTrue(report.WasCapped);
            Assert.Greater(report.Hatched, 20);
            Assert.AreEqual(ants + report.Hatched, Sim.Ants.Count);
            // 30 food/min feeds the queen plus (30 - 1.5) / 0.25 = 114 workers at most.
            Assert.LessOrEqual(Sim.Ants.Count, 120, "upkeep limits growth");
        }

        [Test]
        public void Offline_ShortAbsenceWithoutHistoryChangesNothing()
        {
            float food = Inv.Food;
            var report = OfflineProgress.Apply(Sim, 120.0);
            Assert.AreEqual(food, Inv.Food, 1e-4f);
            Assert.AreEqual(0, report.Hatched);
        }
    }
}
