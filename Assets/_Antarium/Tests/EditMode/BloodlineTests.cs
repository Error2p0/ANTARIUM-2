using System;
using System.IO;
using Antarium.Content;
using Antarium.Gameplay;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>Milestone 10.6: respec, the Queen's Throne, trait-only buildings, biome variants, save slots and the format 6 header.</summary>
    public class BloodlineTests : SimulationFixture
    {
        static readonly DateTime SavedAt = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

        int Gene(GeneticDef def) => C.GeneticIndex(def);
        int Throne => C.BuildingIndex(C.Throne);

        void Buy(GeneticDef def)
        {
            Sim.EnqueueGenetic(Gene(def));
            Sim.Tick();
        }

        void Respec()
        {
            Sim.EnqueueRespec();
            Sim.Tick();
        }

        Simulation Found(Legacy legacy, params WorldModifierDef[] modifiers) =>
            new(C.DB, C.Config, 11, legacy, Array.ConvertAll(modifiers, C.ModifierIndex));

        static Simulation RoundTrip(Simulation sim, ContentDB content, out SaveHeader header)
        {
            using var stream = new MemoryStream();
            SaveGame.Save(sim, stream, SavedAt);
            stream.Position = 0;
            return SaveGame.Load(stream, content, sim.Config, out header);
        }

        // ---------------------------------------------------------------- respec

        [Test]
        public void Respec_RefundsEverythingOncePerColonyThenTakesAFee()
        {
            var legacy = Sim.Legacy;
            legacy.Memory = 10;
            Buy(C.GeneBrood);
            Buy(C.GeneBrood);
            Buy(C.GeneJaws);
            Assert.AreEqual(5, legacy.Memory);
            Assert.AreEqual(5, legacy.Spent, "levels cost 1 and 2, Jaws 2");
            Assert.AreEqual(1.5f, Sim.Colony.Mods.Mult(StatKind.WorkerDig), 1e-4f);
            Assert.AreEqual((5, 0), Sim.RespecPrice(), "the first respec of a colony is free");

            Respec();
            Assert.AreEqual(10, legacy.Memory, "everything spent came back");
            CollectionAssert.AreEqual(new int[legacy.Levels.Length], legacy.Levels);
            Assert.AreEqual(1f, Sim.Colony.Mods.Mult(StatKind.WorkerDig), 1e-4f, "colony bonuses stop at once");
            Assert.AreEqual(1, Sim.Respecs);
            var e = Sim.Events.Read<RespecEvent>();
            Assert.AreEqual(1, e.Length);
            Assert.AreEqual(5, e[0].Refund);
            Assert.AreEqual(0, e[0].Fee);

            Buy(C.GeneBrood);
            Buy(C.GeneBrood);
            Buy(C.GeneJaws);
            Assert.AreEqual((3, 2), Sim.RespecPrice(), "then a quarter of the refund, rounded up");
            Respec();
            Assert.AreEqual(8, legacy.Memory);
            Assert.AreEqual(2, Sim.Respecs);

            Respec();
            Assert.AreEqual(2, Sim.Respecs, "nothing to unlearn: nothing happens");
            Assert.AreEqual(8, legacy.Memory);

            using var next = Found(legacy.Clone());
            Assert.AreEqual(0, next.Respecs, "every colony gets its free respec");
        }

        [Test]
        public void Respec_KeepsInheritedResearchInThisColonyOnly()
        {
            int belts = ResearchIndex(C.FastBelts);
            Sim.Legacy.Memory = 1;
            Buy(C.GeneInherit);
            Assert.IsTrue(Sim.Research.Done[belts]);
            Respec();
            Assert.IsTrue(Sim.Research.Done[belts], "research already done stays");
            Assert.AreEqual(1, Sim.Legacy.Memory);
            using var next = Found(Sim.Legacy.Clone());
            Assert.IsFalse(next.Research.Done[belts], "the next colony does not inherit it any more");
        }

        [Test]
        public void Respec_LocksWhatTheTraitsUnlocked()
        {
            Sim.Legacy.Memory = 3;
            Buy(C.GeneThrone);
            Buy(C.GeneStrange);
            Assert.IsTrue(Sim.Colony.BuildingUnlocked[Throne]);
            Assert.IsTrue(Sim.Legacy.ModifierUnlocked(C.ModifierIndex(C.ModHard)));
            Respec();
            Assert.IsFalse(Sim.Colony.BuildingUnlocked[Throne], "the throne is a secret again");
            Assert.IsFalse(Sim.Legacy.ModifierUnlocked(C.ModifierIndex(C.ModHard)));
            Assert.AreEqual(3, Sim.Legacy.Memory);
        }

        // ---------------------------------------------------------------- the Queen's Throne

        [Test]
        public void TraitBuilding_StartsLockedAndIsUnlockedByTheTraitInEveryColony()
        {
            Assert.IsTrue(C.DB.BuildingStartsLocked[Throne], "a building named by a trait starts locked");
            Assert.IsFalse(Sim.Colony.BuildingUnlocked[Throne]);
            Assert.AreEqual(PlaceResult.Locked, Sim.Buildings.CanPlace(Throne, Queen, default));

            Sim.Legacy.Memory = 2;
            Buy(C.GeneThrone);
            Assert.IsTrue(Sim.Colony.BuildingUnlocked[Throne], "unlocked at once");
            using (var next = Found(Sim.Legacy.Clone()))
                Assert.IsTrue(next.Colony.BuildingUnlocked[Throne], "and in every colony after");
            using (var loaded = RoundTrip(Sim, C.DB, out _))
                Assert.IsTrue(loaded.Colony.BuildingUnlocked[Throne], "and after loading");
        }

        /// <summary>A throne origin whose footprint has the queen's cell in its bottom row and can be built.</summary>
        int2 ThroneSpot()
        {
            string tried = "";
            for (int dx = -3; dx <= 0; dx++)
            {
                var origin = Queen + new int2(dx, 0);
                var result = Sim.Buildings.CanPlace(Throne, origin, default);
                if (result == PlaceResult.Ok) return origin;
                tried += $" {dx}:{result}";
            }
            Assert.Fail("no room for the throne around the queen:" + tried);
            return default;
        }

        [Test]
        public void Throne_IsBuiltAroundTheQueenAndHoldsCourtWhileItStands()
        {
            // The game's royal chamber (the test nest's floor is a single cell).
            Sim.Dispose();
            C.WorldGen.nestRadius = new UnityEngine.Vector2(7f, 3.5f);
            Sim = new Simulation(C.DB, C.Config, 11);
            Sim.Legacy.Memory = 2;
            Buy(C.GeneThrone);
            Inv.Add(C.Item(C.Bar), 3, Queen);

            bool refused = false;
            for (int dy = 0; dy <= 1 && !refused; dy++)
            for (int dx = -8; dx <= 8 && !refused; dx++)
            {
                var o = Queen + new int2(dx, dy);
                bool covers = Queen.y == o.y && Queen.x >= o.x && Queen.x < o.x + 4;
                if (!covers && Sim.Buildings.CanPlace(Throne, o, default) == PlaceResult.NeedsQueen) refused = true;
            }
            Assert.IsTrue(refused, "away from the queen it is refused");

            var origin = ThroneSpot();
            int layingBefore = (int)math.round(Sim.Colony.Mods.Mult(StatKind.LayingSpeed) * 100f);
            int eggsBefore = Sim.Colony.MaxEggs;
            Assert.AreEqual(1f, Sim.ThroneWorkFactor(Queen), "no throne, no aura");
            BuildNow(origin);
            Assert.IsTrue(Sim.Buildings.ThroneStanding);
            Assert.AreEqual(layingBefore + 50, (int)math.round(Sim.Colony.Mods.Mult(StatKind.LayingSpeed) * 100f), "the queen lays 50% faster");
            Assert.AreEqual(eggsBefore + 2, Sim.Colony.MaxEggs);
            Assert.AreEqual(2f, Sim.ThroneWorkFactor(Queen), 1e-4f, "ants near the throne work twice as fast");
            Assert.AreEqual(1f, Sim.ThroneWorkFactor(Queen + new int2(12, 0)), "far from it they don't");

            using (var loaded = RoundTrip(Sim, C.DB, out _))
            {
                loaded.Tick();
                Assert.AreEqual(eggsBefore + 2, loaded.Colony.MaxEggs, "a loaded throne holds court again");
            }

            Sim.EnqueueRemove(origin);
            Sim.Tick();
            Sim.Tick();
            Assert.IsFalse(Sim.Buildings.ThroneStanding);
            Assert.AreEqual(eggsBefore, Sim.Colony.MaxEggs, "its bonuses go with it");
            Assert.AreEqual(layingBefore, (int)math.round(Sim.Colony.Mods.Mult(StatKind.LayingSpeed) * 100f));
        }

        [Test]
        public void Throne_NeedsNoFloorWhereTheQueensFloorWasDugAway()
        {
            Sim.Dispose();
            C.WorldGen.nestRadius = new UnityEngine.Vector2(7f, 3.5f);
            Sim = new Simulation(C.DB, C.Config, 11);
            Sim.Legacy.Memory = 2;
            Buy(C.GeneThrone);
            Inv.Add(C.Item(C.Bar), 3, Queen);
            // Dig out the floor under the queen's row (walls can't go into the royal chamber to put it back).
            for (int dx = -4; dx <= 4; dx++)
            {
                var c = Queen + new int2(dx, -1);
                Sim.SetCell(c, new Cell(Sim.World[c.x, c.y].Material, 0, CellFlags.Revealed | CellFlags.Explored));
            }
            Assert.IsTrue(Sim.World[Queen.x, Queen.y - 1].IsOpen, "the floor is gone");
            var origin = ThroneSpot();
            BuildNow(origin);
            Assert.IsTrue(Sim.Buildings.ThroneStanding, "the throne stands over the hole");
        }

        void BuildNow(int2 origin)
        {
            Sim.EnqueueBuild(Throne, origin, default);
            Sim.Tick();
            Assert.GreaterOrEqual(Sim.Buildings.At(origin), 0, "placed");
            Sim.Buildings.CompleteAllBlueprints();
            Sim.Tick();
        }

        // ---------------------------------------------------------------- biome variants

        [Test]
        public void BiomeVariant_SwapsTheBaseRockNameAndCreatures()
        {
            Assert.IsTrue(C.DB.TryGetEnemyIndex(C.CaveBeetle.Id, out int beetle), "creatures named only by a variant are registered");
            int deep = C.DB.WorldGen.biomes.IndexOf(C.Deep);
            using var plain = Found(new Legacy(C.DB));
            using var variant = Found(new Legacy(C.DB), C.ModVariant);

            Assert.Greater(Count(plain, C.Stone), 0);
            Assert.AreEqual(0, Count(plain, C.VariantRock));
            Assert.AreEqual(0, Count(variant, C.Stone), "the deep layer is made of the variant's rock");
            Assert.AreEqual(Count(plain, C.Stone), Count(variant, C.VariantRock), "with the same shape");
            Assert.AreEqual("Red Deep", variant.BiomeName(deep));
            Assert.AreEqual(C.Deep.DisplayName, plain.BiomeName(deep));
            CollectionAssert.AreEqual(new[] { beetle }, variant.Tweaks.Creatures(deep, C.DB.Biomes[deep].Creatures));
            Assert.AreSame(C.DB.Biomes[deep].Creatures, plain.Tweaks.Creatures(deep, C.DB.Biomes[deep].Creatures));

            using var loaded = RoundTrip(variant, C.DB, out _);
            Assert.AreEqual("Red Deep", loaded.BiomeName(deep), "the variant comes back with the world modifiers");
        }

        int Count(Simulation sim, TerrainMaterialDef def)
        {
            ushort m = C.Index(def);
            int n = 0;
            foreach (var cell in sim.World.Cells)
                if (cell.Material == m) n++;
            return n;
        }

        // ---------------------------------------------------------------- saves

        [Test]
        public void Save_HeaderHoldsTheGenerationAndLegacyTheRespecs()
        {
            Sim.Legacy.Flights = 2;
            Sim.Legacy.Memory = 1;
            Buy(C.GeneBrood);
            Respec();
            using var loaded = RoundTrip(Sim, C.DB, out var header);
            Assert.AreEqual(6, SaveGame.Version);
            Assert.AreEqual(3, header.Generation, "generation = flights + 1");
            Assert.AreEqual(1, loaded.Respecs, "the free respec stays used");

            using var stream = new MemoryStream();
            SaveGame.Save(Sim, stream, SavedAt);
            stream.Position = 0;
            Assert.AreEqual(3, SaveFormat.ReadHeader(stream).Generation, "readable without loading the save");
        }

        [Test]
        public void SaveSlots_KeepTheirOwnFilesBackupsAndHeaders()
        {
            string dir = Path.Combine(Path.GetTempPath(), "antarium_slots_" + Guid.NewGuid().ToString("N"));
            try
            {
                var stores = new SaveStore[SaveStore.SlotCount];
                for (int i = 0; i < stores.Length; i++) stores[i] = new SaveStore(dir, i);
                Assert.AreEqual(0, SaveStore.LatestSlot(stores), "no saves: slot 1");
                Assert.IsNull(stores[1].TryReadHeader());
                StringAssert.EndsWith("colony.sav", stores[0].MainPath, "slot 1 keeps the old file name");
                StringAssert.EndsWith("colony3.sav", stores[2].MainPath);

                stores[0].Save(SaveGame.Capture(Sim, SavedAt));
                stores[2].Save(SaveGame.Capture(Sim, SavedAt.AddMinutes(5)));
                Assert.AreEqual(2, SaveStore.LatestSlot(stores), "Continue picks the slot saved last");
                Assert.IsFalse(stores[1].HasSave);
                Assert.AreEqual(1, stores[2].TryReadHeader().Generation);

                stores[0].Save(SaveGame.Capture(Sim, SavedAt.AddMinutes(10)));
                Assert.IsTrue(File.Exists(stores[0].BackupPath), "the previous save is kept");
                Assert.IsTrue(File.Exists(stores[0].OldPath), "and an older copy");
                File.WriteAllBytes(stores[0].MainPath, new byte[] { 1, 2, 3 });
                Assert.AreEqual(SavedAt, stores[0].TryReadHeader().SavedUtc, "a damaged main file is skipped for the backup's header");
                using (var sim = stores[0].Load(C.DB, C.Config, out var header))
                    Assert.AreEqual(SavedAt, header.SavedUtc, "the backup loads");

                stores[0].Delete();
                Assert.IsFalse(stores[0].HasSave);
                Assert.IsTrue(stores[2].HasSave, "other slots are untouched");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
