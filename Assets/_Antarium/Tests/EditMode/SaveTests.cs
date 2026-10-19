using System;
using System.Collections.Generic;
using System.IO;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    public class SaveTests : SimulationFixture
    {
        static readonly DateTime SavedAt = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Builds up a colony that touches every saved system.</summary>
        void PlayABit()
        {
            ushort ore = C.Item(C.Ore), seed = C.Item(C.Seed);
            Unlock(C.FastBelts, C.SeismicResearch);
            Inv.Add(ore, 10, Queen);
            Sim.EnqueueStorageZone(new float2(Queen) + new float2(-2.5f, 0.5f), 1.2f, true);
            int x = Queen.x + 12;
            Sim.Colony.Piles.Add(new int2(x, Sim.World.SurfaceY[x] + 1), seed, 2);

            Assert.IsTrue(FindMachineSpot(C.Smelter, out var origin));
            Sim.EnqueueBuild(C.BuildingIndex(C.Smelter), origin, default);
            for (int bx = Queen.x + 20; bx < Queen.x + 24; bx++)
                Sim.EnqueueBuild(C.BuildingIndex(C.Belt), new int2(bx, Sim.World.SurfaceY[bx] + 1), new int2(1, 0));
            Sim.Tick();
            Assert.AreEqual(5, Sim.Buildings.BlueprintCount, "smelter and four belts placed");
            Sim.Buildings.CompleteAllBlueprints();
            Sim.Tick();
            Assert.AreEqual(5, Sim.Buildings.ActiveCount);
            Sim.Buildings.TryFeed(origin, ore);
            int bx0 = Queen.x + 20;
            Sim.Buildings.TryCatch(new int2(bx0, Sim.World.SurfaceY[bx0] + 1), ore);
            Sim.EnqueueBuild(C.BuildingIndex(C.Smelter), origin + new int2(0, 0), default); // refused: occupied

            Sim.Enqueue(new DigCommand(new float2(Queen.x + 0.5f, Queen.y - 2f), 2.5f, 1000f));
            Sim.EnqueueAbility(AbilityIndex(C.Frenzy), Queen);
            Sim.EnqueueAbility(AbilityIndex(C.Decree), Queen);
            Assert.IsTrue(Sim.RandomEvents.Trigger(EventIndex(C.BeetleEvent)));
            Run(3f);
        }

        Simulation RoundTrip(Simulation sim, ContentDB content, out SaveHeader header)
        {
            using var stream = new MemoryStream();
            SaveGame.Save(sim, stream, SavedAt);
            stream.Position = 0;
            return SaveGame.Load(stream, content, C.Config, out header);
        }

        [Test]
        public void Save_RoundTripsTheColony()
        {
            PlayABit();
            using var loaded = RoundTrip(Sim, C.DB, out var header);

            Assert.AreEqual(SaveGame.Version, header.Version);
            Assert.AreEqual(SavedAt, header.SavedUtc);
            Assert.AreEqual(11u, header.Seed);
            Assert.AreEqual(Sim.Ants.Count, header.Ants);
            Assert.AreEqual(Sim.PlayTime, loaded.PlayTime, 1e-9);

            // World: every cell.
            for (int i = 0; i < Sim.World.Cells.Length; i++)
            {
                var a = Sim.World.Cells[i];
                var b = loaded.World.Cells[i];
                if (a.Material != b.Material || a.Density != b.Density || a.Flags != b.Flags)
                    Assert.Fail($"cell {GridIndex.ToCell(i, Sim.World.ChunksX)} differs");
            }
            CollectionAssert.AreEqual(Sim.World.SurfaceY, loaded.World.SurfaceY);

            // Colony and stock.
            var inv = loaded.Colony.Inventory;
            Assert.AreEqual(Queen, loaded.Colony.QueenCell);
            Assert.AreEqual(Inv.Food, inv.Food, 1e-4f);
            Assert.AreEqual(Inv.Knowledge, inv.Knowledge, 1e-4f);
            CollectionAssert.AreEqual(Inv.Items, inv.Items);
            CollectionAssert.AreEqual(Inv.InTransit, inv.InTransit);
            Assert.AreEqual(Inv.Capacity, inv.Capacity);
            Assert.AreEqual(Inv.Containers.Count, inv.Containers.Count);
            Assert.Greater(inv.StorageCells, 0);
            Assert.AreEqual(Sim.Colony.StorageCells.Count, loaded.Colony.StorageCells.Count);
            Assert.AreEqual(Sim.Colony.Eggs.Count, loaded.Colony.Eggs.Count);
            Assert.AreEqual(Sim.Colony.Piles.Count, loaded.Colony.Piles.Count);
            Assert.AreEqual(Sim.Colony.Stats.CellsDug, loaded.Colony.Stats.CellsDug);
            Assert.AreEqual(Sim.Colony.Stats.MaxDepth, loaded.Colony.Stats.MaxDepth);

            // Ants.
            Assert.AreEqual(Sim.Ants.Count, loaded.Ants.Count);
            for (int i = 0; i < Sim.Ants.Count; i++)
            {
                var a = Sim.Ants.Ants[i];
                var b = loaded.Ants.Ants[i];
                Assert.AreEqual(a.Cell, b.Cell);
                Assert.AreEqual(a.State, b.State);
                Assert.AreEqual(a.Carry, b.Carry);
                Assert.AreEqual(a.Rng.state, b.Rng.state);
            }

            // Buildings, belts and machines.
            int compared = 0;
            for (int id = 0; id < Sim.Buildings.All.Length; id++)
            {
                var a = Sim.Buildings.All[id];
                if (!a.Alive) continue;
                int other = loaded.Buildings.At(a.Origin);
                Assert.GreaterOrEqual(other, 0, $"building at {a.Origin}");
                var b = loaded.Buildings.Get(other);
                Assert.AreEqual(a.Def, b.Def);
                Assert.AreEqual(a.State, b.State);
                Assert.AreEqual(a.Dir, b.Dir);
                Assert.AreEqual(a.Front.Item, b.Front.Item);
                var ma = Sim.Buildings.MachineOf(id);
                if (ma != null) CollectionAssert.AreEqual(ma.Inputs, loaded.Buildings.MachineOf(other).Inputs);
                compared++;
            }
            Assert.AreEqual(5, compared, "smelter + four belts");

            // Progression.
            CollectionAssert.AreEqual(Sim.Research.Done, loaded.Research.Done);
            Assert.AreEqual(2f, loaded.Colony.Mods.Mult(StatKind.BeltSpeed), 1e-4f, "research effects re-applied");
            Assert.IsTrue(loaded.Colony.AbilityUnlocked[AbilityIndex(C.Seismic)]);
            CollectionAssert.AreEqual(Sim.Abilities.Cooldown, loaded.Abilities.Cooldown);
            Assert.AreEqual(Sim.Contracts.Active.Count, loaded.Contracts.Active.Count);
            for (int i = 0; i < Sim.Contracts.Active.Count; i++)
            {
                Assert.AreEqual(Sim.Contracts.Active[i].Def, loaded.Contracts.Active[i].Def);
                Assert.AreEqual(Sim.Contracts.Active[i].Target, loaded.Contracts.Active[i].Target);
                Assert.AreEqual(Sim.Contracts.Active[i].Progress, loaded.Contracts.Active[i].Progress);
            }
            Assert.AreEqual(1, loaded.RandomEvents.Active.Count);
            Assert.AreEqual(1, loaded.RandomEvents.Critters.Count);

            // The loaded colony keeps running.
            for (int i = 0; i < 100; i++) loaded.Tick();
            foreach (int t in loaded.Colony.Inventory.InTransit) Assert.GreaterOrEqual(t, 0);
        }

        [Test]
        public void Load_MapsContentByIdNotIndex()
        {
            ushort ore = C.Item(C.Ore), bar = C.Item(C.Bar);
            Inv.Add(ore, 7, Queen);
            Inv.Add(bar, 3, Queen);
            using var reordered = C.BuildReordered();
            Assert.AreNotEqual(ore, reordered.GetItemIndex(C.Ore), "test setup: indices differ");

            var loaded = RoundTrip(Sim, reordered, out _);
            try
            {
                Assert.AreEqual(7, loaded.Colony.Inventory.Items[reordered.GetItemIndex(C.Ore)]);
                Assert.AreEqual(3, loaded.Colony.Inventory.Items[reordered.GetItemIndex(C.Bar)]);
                for (int i = 0; i < Sim.World.Cells.Length; i += 97)
                {
                    string before = C.DB.Materials[Sim.World.Cells[i].Material].Id;
                    string after = reordered.Materials[loaded.World.Cells[i].Material].Id;
                    Assert.AreEqual(before, after);
                }
            }
            finally
            {
                loaded.Dispose();
            }
        }

        [Test]
        public void Load_RejectsDamagedFilesAndNewerVersions()
        {
            Assert.Throws<InvalidDataException>(() => SaveGame.Load(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6 }), C.DB, C.Config, out _));

            using var stream = new MemoryStream();
            SaveFormat.Write(stream, new SaveHeader { Version = SaveGame.Version + 1, SavedUtc = SavedAt }, new List<KeyValuePair<string, byte[]>>());
            stream.Position = 0;
            Assert.Throws<InvalidDataException>(() => SaveGame.Load(stream, C.DB, C.Config, out _));
        }

        [Test]
        public void Header_CanBeReadWithoutLoading()
        {
            using var stream = new MemoryStream();
            SaveGame.Save(Sim, stream, SavedAt);
            stream.Position = 0;
            var header = SaveFormat.ReadHeader(stream);
            Assert.AreEqual(SavedAt, header.SavedUtc);
            Assert.AreEqual(Sim.Ants.Count, header.Ants);
        }
    }
}
