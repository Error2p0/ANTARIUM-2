using System;
using System.IO;
using Antarium.Content;
using Antarium.Sim;
using NUnit.Framework;
using Unity.Mathematics;

namespace Antarium.Tests
{
    /// <summary>A colony with helpers to carve tunnels, build at once and control which ants exist.</summary>
    public abstract class TunnelFixture : SimulationFixture
    {
        protected void Open(int2 c, bool explored = true) =>
            Sim.SetCell(c, new Cell(Sim.World[c.x, c.y].Material, 0, CellFlags.Revealed | (explored ? CellFlags.Explored : 0)));

        /// <summary>A tunnel (two cells high) going right from the royal chamber's floor; returns its first cell past the chamber.</summary>
        protected int2 CarveTunnel(int length, int height = 2)
        {
            for (int x = Queen.x; x <= Queen.x + 6 + length; x++)
            for (int y = 0; y < height; y++)
            {
                Open(new int2(x, Queen.y + y));
                Reveal.Around(Sim.World, new int2(x, Queen.y + y), 2); // as digging would
            }
            Sim.Tick();
            return new int2(Queen.x + 6, Queen.y);
        }

        protected int BuildNow(BuildingDef def, int2 origin, ushort filter = 0)
        {
            Sim.EnqueueBuild(C.BuildingIndex(def), origin, default, filter);
            Sim.Tick();
            Sim.Buildings.CompleteAllBlueprints();
            Sim.Tick();
            int id = Sim.Buildings.At(origin);
            Assert.GreaterOrEqual(id, 0, def.Id + " built");
            return id;
        }

        protected void ClearAnts()
        {
            Sim.Ants.Ants.Clear();
            Sim.Tick();
        }

        protected int Spawn(int2 cell, AntCaste caste)
        {
            Sim.Ants.Spawn(cell, 3, caste);
            return Sim.Ants.Count - 1;
        }

        protected void Paint(PheromoneKind kind, int2 cell, float radius = 1.2f, bool set = true)
        {
            Sim.EnqueuePheromone(kind, new float2(cell) + 0.5f, radius, set);
            Sim.Tick();
        }

        protected bool IsOpen(int2 c) => Sim.World[c.x, c.y].IsOpen;
    }

    /// <summary>Painting, fading, emitters, trails, Avoid, priority scents, Defend and the Pheromone Burst.</summary>
    public class PheromoneTests : TunnelFixture
    {
        PheromoneSystem P => Sim.Pheromones;

        [Test]
        public void Scent_IsPaintedOnVisibleCells_AndFades()
        {
            C.Config.pheromones.decayPerSecond = 100f;
            Paint(PheromoneKind.Trail, Queen, 2f);
            Assert.AreEqual(PheromoneSystem.Full, P.Get(PheromoneKind.Trail, Queen));
            Assert.Greater(P.Cells[(int)PheromoneKind.Trail], 4);
            Assert.AreEqual(0, P.Get(PheromoneKind.Dig, Queen), "kinds are separate");

            Run(1.1f);
            Assert.AreEqual(PheromoneSystem.Full - 100, P.Get(PheromoneKind.Trail, Queen), "fades once a second");
            Run(2.1f);
            Assert.AreEqual(0, P.Cells[(int)PheromoneKind.Trail], "gone");

            // Hidden cells can't be painted; erasing clears.
            var hidden = new int2(Queen.x, 3);
            Assert.IsFalse(Sim.World[hidden.x, hidden.y].IsRevealed);
            Paint(PheromoneKind.Avoid, hidden, 0.6f);
            Assert.AreEqual(0, P.Get(PheromoneKind.Avoid, hidden));
            Paint(PheromoneKind.Gather, Queen, 1f);
            Paint(PheromoneKind.Gather, Queen, 3f, set: false);
            Assert.AreEqual(0, P.Cells[(int)PheromoneKind.Gather]);
        }

        [Test]
        public void Emitter_KeepsItsScentFresh()
        {
            C.Config.pheromones.decayPerSecond = 100f;
            var start = CarveTunnel(30);
            var emitterCell = start + new int2(4, 0);
            BuildNow(C.Emitter, emitterCell, (ushort)PheromoneKind.Dig);
            Paint(PheromoneKind.Dig, emitterCell + new int2(3, 1), 1f);   // within the 5-cell radius
            Paint(PheromoneKind.Dig, emitterCell + new int2(20, 1), 1f);  // far away
            Run(5f);
            Assert.AreEqual(PheromoneSystem.Full, P.Get(PheromoneKind.Dig, emitterCell + new int2(3, 1)), "kept fresh");
            Assert.AreEqual(PheromoneSystem.Full, P.Get(PheromoneKind.Dig, emitterCell), "the emitter scents its own cell");
            Assert.AreEqual(0, P.Get(PheromoneKind.Dig, emitterCell + new int2(20, 1)), "out of reach: faded");

            // Clicking it with the emitter selected changes its scent.
            Sim.EnqueueSetFilter(emitterCell, (ushort)PheromoneKind.Trail);
            Sim.Tick();
            Assert.AreEqual((ushort)PheromoneKind.Trail, Sim.Buildings.Get(Sim.Buildings.At(emitterCell)).Filter);
        }

        [Test]
        public void Trail_ShortensPaths_AndTrafficKeepsItFresh()
        {
            var start = CarveTunnel(30);
            var far = start + new int2(30, 0);
            ushort before = Sim.Fields.Get(FieldKind.Dropoff, Sim.World, far);
            ushort home = Sim.Fields.Get(FieldKind.Home, Sim.World, far);
            Assert.AreNotEqual(FlowFields.Unreachable, before);
            for (int x = Queen.x; x <= far.x; x += 2) Paint(PheromoneKind.Trail, new int2(x, Queen.y), 1.6f);
            Run(0.3f);
            ushort after = Sim.Fields.Get(FieldKind.Dropoff, Sim.World, far);
            Assert.Less(after, before * 0.7f, "the trail makes the way home cheaper");
            Assert.AreEqual(home, Sim.Fields.Get(FieldKind.Home, Sim.World, far), "fields enemies follow ignore pheromones");

            // An ant carrying a load over a fading trail refreshes it.
            C.Config.pheromones.decayPerSecond = 20f;
            Run(2f);
            int i = Spawn(far, AntCaste.Worker);
            var a = Sim.Ants.Ants[i];
            a.Carry = C.Item(C.Ore);
            a.CarryCount = 1;
            Sim.Ants.Ants[i] = a;
            Inv.BeginTransit(a.Carry);
            int Freshest()
            {
                int best = 0;
                for (int x = start.x + 10; x < far.x; x++)
                for (int y = 0; y < 2; y++)
                    best = math.max(best, P.Get(PheromoneKind.Trail, new int2(x, Queen.y + y)));
                return best;
            }
            int before2 = Freshest();
            Run(3f);
            Assert.Greater(Freshest(), before2 - 3 * 20, "where the carrier walked, the trail faded less");
        }

        [Test]
        public void Avoid_KeepsJobsAndPathsOut()
        {
            var start = CarveTunnel(20);
            var ceiling = start + new int2(12, 2);
            Sim.EnqueueDesignate(new float2(ceiling) + 0.5f, 0.5f, true);
            Run(0.3f);
            Assert.AreNotEqual(FlowFields.Unreachable, Sim.Fields.Get(FieldKind.Dig, Sim.World, Queen));
            ushort through = Sim.Fields.Get(FieldKind.Dropoff, Sim.World, start + new int2(20, 0));

            Paint(PheromoneKind.Avoid, start + new int2(12, 1), 1.6f);
            Run(0.3f);
            Assert.AreEqual(FlowFields.Unreachable, Sim.Fields.Get(FieldKind.Dig, Sim.World, Queen), "no jobs inside Avoid");
            Assert.Greater(Sim.Fields.Get(FieldKind.Dropoff, Sim.World, start + new int2(20, 0)), through, "crossing it costs more");
        }

        [Test]
        public void DigScent_IsDugFirst()
        {
            var start = CarveTunnel(24);
            ClearAnts();
            Spawn(Queen, AntCaste.Worker);
            var near = start + new int2(1, 2);
            var far = start + new int2(20, 2);
            Sim.EnqueueDesignate(new float2(near) + 0.5f, 0.5f, true);
            Sim.EnqueueDesignate(new float2(far) + 0.5f, 0.5f, true);
            Paint(PheromoneKind.Dig, far, 1f);
            for (int t = 0; t < 60 * 20 && !IsOpen(near) && !IsOpen(far); t++) Sim.Tick();
            Assert.IsTrue(IsOpen(far), "the scented order was dug");
            Assert.IsFalse(IsOpen(near), "before the nearer plain one");
        }

        [Test]
        public void GatherScent_IsHauledFirst()
        {
            var start = CarveTunnel(24);
            ClearAnts();
            Spawn(Queen, AntCaste.Worker);
            ushort ore = C.Item(C.Ore);
            var near = start + new int2(1, 0);
            var far = start + new int2(18, 0);
            Sim.Colony.Piles.Add(near, ore, 1);
            Sim.Colony.Piles.Add(far, ore, 1);
            Paint(PheromoneKind.Gather, far, 1f);
            for (int t = 0; t < 40 * 20 && Sim.Colony.Piles.CountAt(near, ore) + Sim.Colony.Piles.CountAt(far, ore) == 2; t++) Sim.Tick();
            Assert.AreEqual(0, Sim.Colony.Piles.CountAt(far, ore), "the scented pile was picked up");
            Assert.AreEqual(1, Sim.Colony.Piles.CountAt(near, ore), "the nearer plain one waits");
        }

        [Test]
        public void DefendScent_SoldiersPatrolThere()
        {
            Unlock(C.SoldierResearch);
            var start = CarveTunnel(24);
            ClearAnts();
            int s = Spawn(Queen, AntCaste.Soldier);
            var post = start + new int2(20, 0);
            Paint(PheromoneKind.Defend, post, 1.2f);
            Run(15f);
            Assert.LessOrEqual(math.distance(Sim.Ants.Ants[s].Pos, new float2(post) + 0.5f), 5f, "the soldier waits at the Defend scent");
        }

        [Test]
        public void PheromoneBurst_CallsAntsToTheSpot()
        {
            var start = CarveTunnel(24);
            var spot = start + new int2(4, 0);
            Sim.EnqueueAbility(AbilityIndex(C.Burst), spot);
            Sim.Tick();
            int answered = 0;
            foreach (var e in Sim.Events.Read<AbilityUsedEvent>()) answered = e.Amount;
            Assert.Greater(answered, 0);
            Assert.Greater(Sim.RallyTimeLeft, 0f);
            int rallying = 0;
            foreach (var a in Sim.Ants.Ants)
                if (a.State == AntState.Rallying) rallying++;
            Assert.AreEqual(answered, rallying);

            Run(6f);
            int near = 0;
            foreach (var a in Sim.Ants.Ants)
                if (math.distance(a.Pos, new float2(spot) + 0.5f) <= C.Config.pheromones.rallyArriveCells + 2) near++;
            Assert.GreaterOrEqual(near, answered / 2 + 1, "most of them came");

            // Far from every ant it does nothing (and costs no cooldown).
            Sim.Events.Flush();
            Sim.Abilities.Cooldown[AbilityIndex(C.Burst)] = 0f;
            Sim.EnqueueAbility(AbilityIndex(C.Burst), start + new int2(24, 0));
            Sim.Tick();
            bool noEffect = false;
            foreach (var e in Sim.Events.Read<AbilityFailedEvent>()) noEffect |= e.Result == AbilityResult.NoEffect;
            Assert.IsTrue(noEffect);
            Assert.AreEqual(0f, Sim.Abilities.Cooldown[AbilityIndex(C.Burst)]);
        }

        [Test]
        public void Save_KeepsPheromonesAndEmitters()
        {
            var start = CarveTunnel(12);
            BuildNow(C.Emitter, start + new int2(3, 0), (ushort)PheromoneKind.Avoid);
            Paint(PheromoneKind.Trail, start, 2f);
            Paint(PheromoneKind.Defend, start + new int2(8, 0), 1f);
            Run(1.1f);

            using var stream = new MemoryStream();
            SaveGame.Save(Sim, stream, new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc));
            stream.Position = 0;
            using var loaded = SaveGame.Load(stream, C.DB, C.Config, out var header);
            Assert.AreEqual(SaveGame.Version, header.Version);
            for (int k = 0; k < Pheromones.Count; k++)
                Assert.AreEqual(Sim.Pheromones.Cells[k], loaded.Pheromones.Cells[k], ((PheromoneKind)k).ToString());
            Assert.AreEqual(Sim.Pheromones.Get(PheromoneKind.Trail, start), loaded.Pheromones.Get(PheromoneKind.Trail, start));
            var emitter = loaded.Buildings.Get(loaded.Buildings.At(start + new int2(3, 0)));
            Assert.AreEqual(BuildingKind.Emitter, emitter.Kind);
            Assert.AreEqual((ushort)PheromoneKind.Avoid, emitter.Filter);
        }
    }
}
