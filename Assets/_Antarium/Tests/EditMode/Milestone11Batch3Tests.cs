using System;
using System.Collections.Generic;
using System.IO;
using Antarium.Content;
using Antarium.Presentation;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Tests
{
    /// <summary>
    /// Milestone 11, batch 3: the shooting star achievement, the Guiding Light and Vein Instinct research (with its switch),
    /// rival ants robbing the nearest store, the giant worm event and buildings switched off.
    /// </summary>
    public class Milestone11Batch3Tests : TunnelFixture
    {
        int EnemyIndex(EnemyDef d) => C.DB.TryGetEnemyIndex(d.Id, out int i) ? i : -1;
        int EventIndex(RandomEventDef d) => Array.FindIndex(C.DB.Events, e => e.Def == d);
        int ResearchIndex(ResearchDef d) => Array.FindIndex(C.DB.Research, r => r.Def == d);

        Simulation Reload()
        {
            using var ms = new MemoryStream();
            SaveGame.Save(Sim, ms, DateTime.UtcNow);
            ms.Position = 0;
            return SaveGame.Load(ms, C.DB, C.Config, out _);
        }

        void Research(ResearchDef def)
        {
            Inv.Knowledge += 10f;
            Sim.EnqueueResearch(ResearchIndex(def));
            Sim.Tick();
            Assert.IsTrue(Sim.Research.Done[ResearchIndex(def)], def.Id + " researched");
        }

        // ---------------------------------------------------------------- shooting stars

        [Test]
        public void ShootingStar_CountsTowardTheAchievement_AndIsSaved()
        {
            var def = ScriptableObject.CreateInstance<AchievementDef>();
            try
            {
                def.kind = AchievementKind.ShootingStars;
                def.amount = 1;
                def.hidden = true;
                var data = new AchievementData { Def = def };
                Assert.IsFalse(AchievementProgress.IsReached(Sim, data));

                Sim.EnqueueShootingStar(new float2(Queen) + new float2(0f, 30f));
                Sim.Tick();
                Assert.AreEqual(1, Sim.Colony.Stats.StarsCaught);
                Assert.AreEqual(1, Sim.Events.Read<ShootingStarCaughtEvent>().Length);
                Assert.IsTrue(AchievementProgress.IsReached(Sim, data));

                using var loaded = Reload();
                Assert.AreEqual(1, loaded.Colony.Stats.StarsCaught);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(def);
            }
        }

        // ---------------------------------------------------------------- research features

        [Test]
        public void GuidingLight_IsSwitchedOnByResearch_AndKeptOnLoad()
        {
            Assert.IsFalse(Sim.Colony.Has(ResearchFeature.CursorLight));
            Research(C.LightResearch);
            Assert.IsTrue(Sim.Colony.Has(ResearchFeature.CursorLight));
            Assert.IsFalse(Sim.Colony.Has(ResearchFeature.VeinInstinct));
            using var loaded = Reload();
            Assert.IsTrue(loaded.Colony.Has(ResearchFeature.CursorLight));
        }

        [Test]
        public void VeinInstinct_IsResearched_AndOnlyOrdersOreWhileSwitchedOn()
        {
            ushort stone = C.Index(C.Stone); // drops ore in the test content
            int2 floor = Queen + new int2(0, -1);
            int2 ore = Queen + new int2(1, -2);
            Sim.SetCell(ore, new Cell(stone, Cell.Full, CellFlags.Revealed));
            int oreIndex = Sim.World.Index(ore.x, ore.y);

            Assert.IsFalse(Sim.VeinInstinctKnown);
            Research(C.VeinResearch);
            Assert.IsTrue(Sim.VeinInstinctKnown);
            Assert.IsTrue(Sim.Colony.AutoMarkOres, "on by default");

            // Switched off: digging next to the ore orders nothing.
            Sim.EnqueueAutoMark(false);
            Sim.Enqueue(new DigCommand(new float2(floor) + 0.5f, 0.6f, 1000f));
            Sim.Tick();
            Assert.IsTrue(Sim.World[floor.x, floor.y].IsOpen);
            Assert.IsFalse(Sim.VeinInstinctActive);
            Assert.IsFalse(Sim.Colony.Designated.Contains(oreIndex), "switched off: nothing ordered");

            // On again: the next dug cell next to it orders it.
            Sim.EnqueueAutoMark(true);
            Sim.Enqueue(new DigCommand(new float2(floor) + new float2(0.5f, -0.5f), 0.6f, 1000f));
            Sim.Tick();
            Assert.IsTrue(Sim.Colony.Designated.Contains(oreIndex), "switched on: the ore is ordered");

            Sim.EnqueueAutoMark(false);
            Sim.Tick();
            using var loaded = Reload();
            Assert.IsTrue(loaded.VeinInstinctKnown);
            Assert.IsFalse(loaded.Colony.AutoMarkOres, "the switch is saved");
        }

        [Test]
        public void VeinInstinct_TheGeneticTraitKnowsItWithoutResearch()
        {
            Sim.Legacy.Memory = 1;
            Sim.EnqueueGenetic(Array.FindIndex(C.DB.Genetics, g => g.Def == C.GeneVein));
            Sim.Tick();
            Assert.IsTrue(Sim.VeinInstinctKnown);
            Assert.IsFalse(Sim.Colony.Has(ResearchFeature.VeinInstinct), "not researched, known through the bloodline");
        }

        // ---------------------------------------------------------------- rival ants

        StockContainer StorageAt(int2 c) => Inv.ContainerAt(Sim.World.Index(c.x, c.y));

        [Test]
        public void RivalAnts_RobTheNearestStore_NotTheRoyalChamber()
        {
            C.SetCaste(AntCaste.Worker, d => d.attack = 0f);
            ClearAnts();
            var start = CarveTunnel(24);
            // A storage cell far out along the tunnel, with bars; ore in the royal chamber.
            int2 store = start + new int2(20, 0);
            Sim.EnqueueStorageZone(new float2(store) + 0.5f, 0.4f, true);
            Sim.Tick();
            var container = StorageAt(store);
            Assert.IsNotNull(container, "storage cell painted");
            ushort bar = C.Item(C.Bar), ore = C.Item(C.Ore);
            for (int i = 0; i < 4; i++) Assert.IsTrue(Inv.DeliverInto(container, bar));
            Inv.Add(ore, 10, Queen);
            int royalOre = Inv.Royal.Counts[ore];
            Assert.Greater(royalOre, 0);

            // A thief arrives at the tunnel's far end: the storage cell is much nearer than the royal chamber.
            Sim.Enemies.Spawn(EnemyIndex(C.Thief), start + new int2(24, 0), EnemyState.Raid);
            for (int t = 0; t < 40 * 20 && Sim.Enemies.Count > 0; t++)
            {
                Sim.Tick();
                if (Sim.Enemies.Count > 0 && Sim.Enemies.Enemies[0].State == EnemyState.Leave) break;
            }
            Assert.AreEqual(1, Sim.Enemies.Count);
            var thief = Sim.Enemies.Enemies[0];
            Assert.AreEqual(EnemyState.Leave, thief.State, "it filled up and left");
            Assert.AreEqual(bar, thief.Loot, "it took what the nearest store holds");
            Assert.AreEqual(2, (int)thief.LootCount);
            Assert.AreEqual(2, container.Counts[bar], "two bars gone from the storage cell");
            Assert.AreEqual(royalOre, Inv.Royal.Counts[ore], "the royal chamber was not touched");
        }

        [Test]
        public void RivalAnts_GoOnToTheNextStore_WhenTheNearestRunsDry()
        {
            C.SetCaste(AntCaste.Worker, d => d.attack = 0f);
            ClearAnts();
            var start = CarveTunnel(24);
            int2 store = start + new int2(20, 0);
            Sim.EnqueueStorageZone(new float2(store) + 0.5f, 0.4f, true);
            Sim.Tick();
            ushort ore = C.Item(C.Ore);
            Assert.IsTrue(Inv.DeliverInto(StorageAt(store), ore), "one ore out there");
            Inv.Add(ore, 10, Queen);
            int stock = Inv.Items[ore];

            Sim.Enemies.Spawn(EnemyIndex(C.Thief), start + new int2(24, 0), EnemyState.Raid);
            for (int t = 0; t < 60 * 20 && Sim.Enemies.Count > 0 && Sim.Enemies.Enemies[0].State != EnemyState.Leave; t++) Sim.Tick();
            Assert.AreEqual(EnemyState.Leave, Sim.Enemies.Enemies[0].State);
            Assert.AreEqual(2, (int)Sim.Enemies.Enemies[0].LootCount, "one from the storage cell, one from the royal chamber");
            Assert.AreEqual(0, StorageAt(store).Counts[ore]);
            Assert.AreEqual(stock - 2, Inv.Items[ore]);
        }

        // ---------------------------------------------------------------- the giant worm

        int TriggerWorm()
        {
            Assert.IsTrue(Sim.RandomEvents.Trigger(EventIndex(C.WormEvent)), "the worm found soft ground near the ants");
            Sim.Tick();
            int w = Sim.Enemies.WormIndex();
            Assert.GreaterOrEqual(w, 0);
            return w;
        }

        [Test]
        public void Worm_BurrowsATunnel_InTheSoftGround_AndItsBodyFollows()
        {
            int w = TriggerWorm();
            Assert.AreEqual(EnemyState.Tunnel, Sim.Enemies.Enemies[w].State);
            Assert.AreEqual(C.Config.threats.wormSegments + 1, Sim.Enemies.WormCells.Count);
            Assert.IsTrue(Sim.RandomEvents.Active.Exists(e => e.Kind == RandomEventKind.GiantWorm));
            Assert.IsFalse(Sim.RandomEvents.TriggerKind(RandomEventKind.GiantWorm), "only one worm at a time");

            Run(15f);
            w = Sim.Enemies.WormIndex();
            Assert.GreaterOrEqual(w, 0, "still burrowing");
            var worm = Sim.Enemies.Enemies[w];
            var cells = new HashSet<int2> { worm.Cell };
            var body = Sim.Enemies.WormCells;
            for (int k = 0; k < C.Config.threats.wormSegments; k++) cells.Add(body[k]);
            Assert.AreEqual(C.Config.threats.wormSegments + 1, cells.Count, "the body has stretched out: every segment in its own cell");
            foreach (var c in cells)
            {
                var cell = Sim.World[c.x, c.y];
                Assert.IsTrue(cell.IsOpen, $"{c} is dug out");
                Assert.AreNotEqual(0, (int)(cell.Flags & CellFlags.Explored), $"{c} joins the known tunnels");
                Assert.GreaterOrEqual(Sim.World.Depth(c.x, c.y), C.Config.threats.wormMinDepth, "never up to the surface");
            }
            for (int k = 1; k < C.Config.threats.wormSegments; k++)
            {
                int2 d = math.abs(body[k] - body[k - 1]);
                Assert.AreEqual(1, d.x + d.y, "segments follow each other cell by cell");
            }
            int2 e = Sim.RandomEvents.Active.Find(a => a.Kind == RandomEventKind.GiantWorm).Cell;
            Assert.AreEqual(worm.Cell, e, "the card's Go button follows the head");
        }

        [Test]
        public void Worm_KilledWinsTheEvent_WithItsReward()
        {
            TriggerWorm();
            Run(3f);
            int w = Sim.Enemies.WormIndex();
            var worm = Sim.Enemies.Enemies[w];
            float health = worm.Health;

            // The Dig tool hurts it anywhere along its body.
            int2 tail = Sim.Enemies.WormCells[C.Config.threats.wormSegments - 1];
            Sim.Enqueue(new DigCommand(new float2(tail) + 0.5f, 0.6f, 2f));
            Sim.Tick();
            w = Sim.Enemies.WormIndex();
            Assert.Less(Sim.Enemies.Enemies[w].Health, health, "a Dig tool hit on its tail hurts it");

            float food = Inv.Food = 10f;
            float knowledge = Inv.Knowledge;
            int won = Sim.Colony.Stats.EventsWon;
            worm = Sim.Enemies.Enemies[w];
            worm.Health = 0f;
            Sim.Enemies.Enemies[w] = worm;
            Sim.Tick();
            Assert.AreEqual(-1, Sim.Enemies.WormIndex(), "dead");
            Assert.AreEqual(0, Sim.Enemies.WormCells.Count);
            Assert.IsFalse(Sim.RandomEvents.Active.Exists(e => e.Kind == RandomEventKind.GiantWorm));
            Assert.AreEqual(won + 1, Sim.Colony.Stats.EventsWon);
            Assert.AreEqual(food + 10f, Inv.Food, 1e-3f, "a feast");
            Assert.GreaterOrEqual(Inv.Knowledge, knowledge + 6f - 1e-3f);
        }

        [Test]
        public void Worm_BurrowsAwayWhenTimeRunsOut_AndSurvivesALoad()
        {
            TriggerWorm();
            Run(2f);
            using (var loaded = Reload())
            {
                int lw = loaded.Enemies.WormIndex();
                Assert.GreaterOrEqual(lw, 0, "the worm is saved");
                Assert.AreEqual(Sim.Enemies.WormCells.Count, loaded.Enemies.WormCells.Count, "with its body");
                Assert.IsTrue(loaded.RandomEvents.Active.Exists(e => e.Kind == RandomEventKind.GiantWorm), "and its event");
                loaded.Tick();
                Assert.GreaterOrEqual(loaded.Enemies.WormIndex(), 0, "still linked to its event after a tick");
            }

            int won = Sim.Colony.Stats.EventsWon;
            Run(C.WormEvent.duration);
            Assert.AreEqual(-1, Sim.Enemies.WormIndex(), "gone");
            Assert.AreEqual(0, Sim.Enemies.WormCells.Count);
            Assert.IsFalse(Sim.RandomEvents.Active.Exists(e => e.Kind == RandomEventKind.GiantWorm));
            Assert.AreEqual(won, Sim.Colony.Stats.EventsWon, "missed");
            var lost = Sim.Events.Read<WormEvent>();
            Assert.IsTrue(Array.Exists(lost.ToArray(), e => e.Phase == WormPhase.Left));
        }

        // ---------------------------------------------------------------- on / off

        [Test]
        public void SwitchedOffMachine_TakesNothing_AndWaits_ThenGoesOn()
        {
            var start = CarveTunnel(10);
            int id = BuildNow(C.Smelter, start + new int2(3, 0));
            var m = Sim.Buildings.MachineOf(id);
            ushort ore = C.Item(C.Ore);
            Assert.Greater(m.Room(ore), 0);

            Sim.EnqueueSwitch(start + new int2(3, 0), false);
            Sim.Tick();
            Assert.IsTrue(Sim.Buildings.Get(id).Off);
            Assert.AreEqual(0, m.Room(ore), "takes nothing in");
            Assert.IsFalse(m.TryInsert(ore));
            m.Off = false; // fill it by hand, then switch it off again
            for (int i = 0; i < 2; i++) Assert.IsTrue(m.TryInsert(ore));
            m.Off = true;
            Run(3f);
            Assert.AreEqual(MachineStatus.Off, Sim.Buildings.Get(id).Status);
            Assert.AreEqual(0, m.Crafted, "nothing is made while it is off");

            using (var loaded = Reload())
            {
                int lid = loaded.Buildings.At(start + new int2(3, 0));
                Assert.IsTrue(loaded.Buildings.Get(lid).Off, "saved switched off");
                Assert.AreEqual(0, loaded.Buildings.MachineOf(lid).Room(ore));
            }

            Sim.EnqueueSwitch(start + new int2(3, 0), true);
            Run(3f);
            Assert.IsFalse(Sim.Buildings.Get(id).Off);
            Assert.GreaterOrEqual(m.Crafted, 1, "on again, it smelts what it held");
            Assert.AreEqual(2, Sim.Events.Read<BuildingSwitchedEvent>().Length, "off, then on");
        }

        [Test]
        public void OnlyWorkingBuildingsCanBeSwitched()
        {
            Assert.IsTrue(BuildingSystem.IsSwitchable(BuildingKind.Processor));
            Assert.IsTrue(BuildingSystem.IsSwitchable(BuildingKind.Unloader));
            Assert.IsTrue(BuildingSystem.IsSwitchable(BuildingKind.Emitter));
            Assert.IsTrue(BuildingSystem.IsSwitchable(BuildingKind.Pump));
            Assert.IsTrue(BuildingSystem.IsSwitchable(BuildingKind.Resonator));
            Assert.IsFalse(BuildingSystem.IsSwitchable(BuildingKind.Belt));
            Assert.IsFalse(BuildingSystem.IsSwitchable(BuildingKind.Gate));
            Assert.IsFalse(BuildingSystem.IsSwitchable(BuildingKind.Chest));

            var start = CarveTunnel(6);
            int gate = BuildNow(C.Gate, start + new int2(2, 0));
            Sim.EnqueueSwitch(start + new int2(2, 0), false);
            Sim.Tick();
            Assert.IsFalse(Sim.Buildings.Get(gate).Off, "a gate has no switch");
        }

        [Test]
        public void SwitchedOffEmitter_KeepsNoScent()
        {
            var start = CarveTunnel(10);
            int2 at = start + new int2(4, 0);
            BuildNow(C.Emitter, at, (ushort)PheromoneKind.Trail);
            Sim.EnqueueSwitch(at, false);
            Sim.Tick();
            Paint(PheromoneKind.Trail, at + new int2(2, 0));
            C.Config.pheromones.decayPerSecond = 255f;
            Run(2f);
            Assert.AreEqual(0, Sim.Pheromones.Get(PheromoneKind.Trail, at + new int2(2, 0)), "switched off, the scent fades");
        }

        // ---------------------------------------------------------------- art

        [Test]
        public void ColonyAtlas_SpritesDoNotOverlap()
        {
            var sprites = new ColonySprites();
            int width = sprites.Texture.width, height = sprites.Texture.height;
            var rects = new List<RectInt>();
            for (int i = 0; i < sprites.Rects.Length; i++)
            {
                var px = ArtOverrides.PixelRect(sprites.Rects[i], width, height);
                if (px.width > 0 && px.height > 0) rects.Add(px);
            }
            float wormBody = sprites.Rects[ColonySprites.WormBodyRect].z;
            UnityEngine.Object.DestroyImmediate(sprites.Texture);
            sprites.Dispose();
            Assert.Greater(wormBody, 0f, "the worm's body has a sprite");
            for (int a = 0; a < rects.Count; a++)
            for (int b = a + 1; b < rects.Count; b++)
                Assert.IsFalse(rects[a].Overlaps(rects[b]) && !rects[a].Equals(rects[b]), $"sprite {rects[a]} overlaps {rects[b]}");
        }
    }
}
