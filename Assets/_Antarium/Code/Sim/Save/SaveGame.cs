using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Antarium.Content;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// A captured game, ready to be compressed and written (on any thread). Capturing copies the state on the
    /// main thread; the expensive part (splitting the world into planes, compressing) happens in <see cref="WriteTo"/>.
    /// </summary>
    public sealed class SaveSnapshot
    {
        public SaveHeader Header;
        internal readonly List<KeyValuePair<string, byte[]>> Sections = new();
        internal int ChunksX, ChunksY;
        internal int[] SurfaceY;
        internal Cell[] Cells;
        internal FluidCell[] Fluid;
        internal byte[] Pheromones;

        public void WriteTo(Stream stream)
        {
            var all = new List<KeyValuePair<string, byte[]>>(Sections)
            {
                new("WRLD", SaveGame.WriteWorld(this)),
                new("FLUD", SaveGame.WriteFluid(this)),
                new("PHER", SaveGame.WritePheromones(this)),
            };
            SaveFormat.Write(stream, Header, all);
        }
    }

    /// <summary>
    /// Saves and loads a whole colony: world grid, colony, ants, buildings, research, contracts, abilities and
    /// events. Content is stored by id (never by index), so saves survive content being added or reordered;
    /// content that no longer exists is dropped. Derived state (light, flow fields, the belt network) is rebuilt.
    /// </summary>
    public static class SaveGame
    {
        /// <summary>
        /// Format version written into new saves. Bump it (and migrate in <see cref="Migrate"/>) on breaking changes.
        /// Version 2 (Milestone 5): sections carry their own versions (IDS / COLN / ANTS / BLDG 2) and FLUD + THRT exist.
        /// Version 3 (Milestone 6): COLN 3 (caste shares, nursery eggs), ANTS 3 (carried counts and eggs) and PHER exist.
        /// </summary>
        public const int Version = 3;

        // ---------------------------------------------------------------- save

        /// <summary>Copies the simulation state (main thread). Write the snapshot with <see cref="SaveSnapshot.WriteTo"/>.</summary>
        public static SaveSnapshot Capture(Simulation sim, DateTime nowUtc)
        {
            var world = sim.World;
            var snap = new SaveSnapshot
            {
                Header = new SaveHeader
                {
                    Version = Version,
                    SavedUtc = nowUtc,
                    Seed = sim.Seed,
                    PlayTime = sim.PlayTime,
                    Ants = sim.Ants.Count,
                    MaxDepth = sim.Colony.Stats.MaxDepth,
                },
                ChunksX = world.ChunksX,
                ChunksY = world.ChunksY,
                SurfaceY = (int[])world.SurfaceY.Clone(),
                Cells = world.Cells.ToArray(),
                Fluid = world.Fluid.ToArray(),
                Pheromones = sim.Pheromones.Strength.ToArray(),
            };
            snap.Sections.Add(new("IDS", Section(w => WriteIds(w, sim.Content), 2)));
            snap.Sections.Add(new("SIM", Section(w => WriteSim(w, sim))));
            snap.Sections.Add(new("COLN", Section(w => WriteColony(w, sim), 3)));
            snap.Sections.Add(new("ANTS", Section(w => WriteAnts(w, sim), 3)));
            snap.Sections.Add(new("BLDG", Section(w => WriteBuildings(w, sim), 2)));
            snap.Sections.Add(new("PROG", Section(w => WriteProgress(w, sim))));
            snap.Sections.Add(new("THRT", Section(w => WriteThreats(w, sim))));
            return snap;
        }

        /// <summary>Captures and writes in one go (tests, quitting).</summary>
        public static void Save(Simulation sim, Stream stream, DateTime nowUtc) => Capture(sim, nowUtc).WriteTo(stream);

        static byte[] Section(Action<BinaryWriter> write, byte version = 1)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(version);
                write(w);
            }
            return ms.ToArray();
        }

        static void WriteIds(BinaryWriter w, ContentDB c)
        {
            w.WriteIds(Array.ConvertAll(c.Materials, m => m.Id));
            w.WriteIds(Array.ConvertAll(c.Items, m => m.Id));
            w.WriteIds(Array.ConvertAll(c.Buildings, m => m.Def.Id));
            w.WriteIds(Array.ConvertAll(c.Recipes, m => m.Def.Id));
            w.WriteIds(Array.ConvertAll(c.Research, m => m.Def.Id));
            w.WriteIds(Array.ConvertAll(c.Abilities, m => m.Id));
            w.WriteIds(Array.ConvertAll(c.Contracts, m => m.Def.Id));
            w.WriteIds(Array.ConvertAll(c.Events, m => m.Def.Id));
            w.WriteIds(Array.ConvertAll(c.Enemies, m => m.Def.Id)); // v2
        }

        /// <summary>Fluid as two planes (level, kind).</summary>
        internal static byte[] WriteFluid(SaveSnapshot s)
        {
            int n = s.Fluid.Length;
            var level = new byte[n];
            var kind = new byte[n];
            for (int i = 0; i < n; i++)
            {
                level[i] = s.Fluid[i].Level;
                kind[i] = (byte)s.Fluid[i].Kind;
            }
            return Section(w =>
            {
                w.Write(n);
                w.Write(level);
                w.Write(kind);
            });
        }

        /// <summary>Pheromone strengths, one plane per kind (mostly zeros: they compress to almost nothing).</summary>
        internal static byte[] WritePheromones(SaveSnapshot s) => Section(w =>
        {
            w.Write(Antarium.Content.Pheromones.Count);
            w.Write(s.Pheromones.Length);
            w.Write(s.Pheromones);
        });

        /// <summary>World cells as separate planes (material, density, flags): they compress far better than interleaved.</summary>
        internal static byte[] WriteWorld(SaveSnapshot s)
        {
            int n = s.Cells.Length;
            var materials = new byte[n * 2];
            var density = new byte[n];
            var flags = new byte[n];
            for (int i = 0; i < n; i++)
            {
                var c = s.Cells[i];
                materials[i * 2] = (byte)c.Material;
                materials[i * 2 + 1] = (byte)(c.Material >> 8);
                density[i] = c.Density;
                flags[i] = (byte)c.Flags;
            }
            return Section(w =>
            {
                w.Write(s.ChunksX);
                w.Write(s.ChunksY);
                foreach (int y in s.SurfaceY) w.Write(y);
                w.Write(materials);
                w.Write(density);
                w.Write(flags);
            });
        }

        static void WriteSim(BinaryWriter w, Simulation sim)
        {
            w.Write(sim.Rng.state);
            w.Write(sim.PlayTime);
        }

        static void WriteCounts(BinaryWriter w, int[] counts)
        {
            int n = 0;
            foreach (int c in counts) if (c != 0) n++;
            w.Write(n);
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] == 0) continue;
                w.Write((ushort)i);
                w.Write(counts[i]);
            }
        }

        static void WriteColony(BinaryWriter w, Simulation sim)
        {
            var colony = sim.Colony;
            var inv = colony.Inventory;
            w.Write(colony.QueenCell);
            w.Write(inv.Food);
            w.Write(inv.Knowledge);
            w.Write(colony.EggTimer);
            w.Write(colony.ForageTimer);
            w.Write(colony.Starving);

            w.Write(colony.Eggs.Count);
            foreach (var egg in colony.Eggs)
            {
                w.Write(egg.Cell);
                w.Write(egg.Age);
            }
            w.Write(colony.RoyalDisplayCells.Count);
            foreach (var c in colony.RoyalDisplayCells) w.Write(c);

            w.Write(colony.Piles.Count);
            for (int i = 0; i < colony.Piles.Count; i++)
            {
                var p = colony.Piles[i];
                w.Write(p.Cell);
                w.Write(p.Item);
                w.Write(p.Count);
            }

            // Containers: the royal chamber first, then each storage cell.
            w.Write(inv.Containers.Count);
            foreach (var c in inv.Containers)
            {
                w.Write(c.Cell);
                w.Write(c.WorldIndex);
                WriteCounts(w, c.Counts);
            }

            var stats = colony.Stats;
            WriteLongs(w, stats.Delivered);
            WriteLongs(w, stats.Crafted);
            w.Write(stats.Built.Length);
            foreach (int b in stats.Built) w.Write(b);
            w.Write(stats.CellsDug);
            w.Write(stats.MaxDepth);
            w.Write(stats.AntsHatched);
            w.Write(stats.ContractsCompleted);
            w.Write(stats.EventsWon);
            w.Write(stats.KnowledgeEarned);

            var meter = colony.Meter;
            w.Write(ProductionMeter.Minutes);
            w.Write(meter.Current);
            w.Write(meter.Filled);
            w.Write(meter.CurrentSeconds);
            for (int b = 0; b < ProductionMeter.Minutes; b++)
            {
                w.Write(meter.Food[b]);
                w.Write(meter.Knowledge[b]);
                var items = meter.Items[b];
                w.Write(items.Length);
                foreach (float v in items) w.Write(v);
            }

            // v2: castes of the eggs (same order), soldier share, threat statistics.
            w.Write(colony.Eggs.Count);
            foreach (var egg in colony.Eggs) w.Write((byte)egg.Caste);
            w.Write(colony.CasteShare[(int)AntCaste.Soldier]);
            w.Write(stats.EnemiesKilled);
            w.Write(stats.AntsLost);
            w.Write(stats.RaidsRepelled);

            // v3: every caste's share and hatch count, scouting, eggs in nurseries (by the nursery's origin cell).
            w.Write(Castes.Count);
            for (int c = 0; c < Castes.Count; c++)
            {
                w.Write(colony.CasteShare[c]);
                w.Write(stats.HatchedCaste[c]);
            }
            w.Write(stats.CellsScouted);
            w.Write(colony.Eggs.Count);
            foreach (var egg in colony.Eggs)
            {
                bool inNursery = egg.InNursery && egg.Nursery < sim.Buildings.All.Length && sim.Buildings.All[egg.Nursery].Alive;
                w.Write(inNursery);
                if (!inNursery) continue;
                w.Write(sim.Buildings.All[egg.Nursery].Origin);
                w.Write(egg.Slot);
            }
        }

        static void WriteLongs(BinaryWriter w, long[] values)
        {
            w.Write(values.Length);
            foreach (long v in values) w.Write(v);
        }

        static void WriteAnts(BinaryWriter w, Simulation sim)
        {
            var ants = sim.Ants;
            w.Write(ants.SpawnCounter);
            w.Write(ants.Count);
            foreach (var a in ants.Ants)
            {
                w.Write(a.Cell);
                w.Write(a.From);
                w.Write(a.Progress);
                w.Write(a.StepLength);
                w.Write(a.Heading);
                w.Write((byte)a.State);
                w.Write(a.Carry);
                w.Write((byte)a.Dest);
                w.Write(a.Want);
                w.Write(a.Target);
                w.Write(a.Timer);
                w.Write(a.WalkPhase);
                w.Write(a.Rng.state);
                w.Write(a.PrevPos);
                w.Write(a.Pos);
                w.Write((byte)a.Caste); // v2
                w.Write(a.Health);
                w.Write(a.SinceHurt);
                w.Write(a.CarryCount); // v3
                w.Write(a.CarryingEgg);
                w.Write((byte)a.EggCaste);
                w.Write(a.EggAge);
            }
        }

        static void WriteSlot(BinaryWriter w, in ItemSlot s)
        {
            w.Write(s.Item);
            w.Write(s.T);
        }

        static void WriteBuildings(BinaryWriter w, Simulation sim)
        {
            var buildings = sim.Buildings;
            int alive = 0;
            foreach (var b in buildings.All) if (b.Alive) alive++;
            w.Write(alive);
            for (int id = 0; id < buildings.All.Length; id++)
            {
                var b = buildings.All[id];
                if (!b.Alive) continue;
                w.Write(b.Def);
                w.Write(b.Origin);
                w.Write(b.Dir);
                w.Write((byte)b.State);
                w.Write(b.WorkLeft);
                w.Write(b.Filter);
                WriteSlot(w, b.Front);
                WriteSlot(w, b.Back);
                w.Write(b.RoundRobin);
                w.Write(b.Stuck);
                w.Write(b.Rate);
                w.Write(b.Health); // v2
                w.Write(b.Rearm);

                var m = buildings.MachineOf(id);
                w.Write(m != null);
                if (m == null) continue;
                w.Write(m.Inputs.Length);
                for (int i = 0; i < m.Inputs.Length; i++)
                {
                    w.Write(m.Data.InputItems[i]);
                    w.Write(m.Inputs[i]);
                }
                w.Write(m.Pending.Count);
                foreach (ushort item in m.Pending) w.Write(item);
                w.Write(m.Recipe);
                w.Write(m.Progress);
                w.Write(m.NextRecipe);
                w.Write(m.Crafted);
            }
        }

        static void WriteProgress(BinaryWriter w, Simulation sim)
        {
            var research = sim.Research;
            w.Write(research.Done.Length);
            foreach (bool d in research.Done) w.Write(d);

            var abilities = sim.Abilities;
            w.Write(abilities.Cooldown.Length);
            for (int i = 0; i < abilities.Cooldown.Length; i++)
            {
                w.Write(abilities.Cooldown[i]);
                w.Write(abilities.Remaining[i]);
            }

            var contracts = sim.Contracts;
            w.Write(contracts.Rng.state);
            w.Write(contracts.Completed.Length);
            foreach (int c in contracts.Completed) w.Write(c);
            w.Write(contracts.Active.Count);
            foreach (var c in contracts.Active)
            {
                w.Write(c.Def);
                w.Write(c.Target);
                w.Write(c.Baseline);
                w.Write(c.RewardScale);
            }

            var events = sim.RandomEvents;
            w.Write(events.Rng.state);
            w.Write(events.Timer);
            w.Write(events.NextId);
            w.Write(events.Active.Count);
            foreach (var e in events.Active)
            {
                w.Write(e.Id);
                w.Write(e.Def);
                w.Write(e.Cell);
                w.Write(e.Min);
                w.Write(e.Max);
                w.Write(e.TimeLeft);
                w.Write(e.Count);
            }
            w.Write(events.Critters.Count);
            foreach (var c in events.Critters)
            {
                w.Write(c.EventId);
                w.Write(c.Cell);
                w.Write(c.From);
                w.Write(c.Progress);
                w.Write(c.StepLength);
                w.Write(c.Heading);
                w.Write(c.WalkPhase);
                w.Write(c.Pos);
            }
        }

        static void WriteThreats(BinaryWriter w, Simulation sim)
        {
            var t = sim.Enemies;
            w.Write(t.Rng.state);
            w.Write(t.RaidTimer);
            w.Write(t.RaidWarned);
            w.Write(t.RaidSpawn);
            w.Write(t.RaidCount);
            w.Write(t.RaidActive);
            w.Write(t.RaidSize);
            w.Write(t.RaidFoodLost);
            w.Write(t.Enemies.Count);
            foreach (var e in t.Enemies)
            {
                w.Write(e.Def);
                w.Write((byte)e.State);
                w.Write(e.Cell);
                w.Write(e.From);
                w.Write(e.Progress);
                w.Write(e.StepLength);
                w.Write(e.Heading);
                w.Write(e.Health);
                w.Write(e.AttackTimer);
                w.Write(e.Held);
                w.Write(e.Home);
                w.Write(e.Eaten);
                w.Write(e.Raid);
            }
        }

        // ---------------------------------------------------------------- load

        /// <summary>Saved index → current index for every kind of content (missing content maps to -1 / NoItem).</summary>
        sealed class Remap
        {
            public ushort[] Material, Item;
            public int[] Building, Recipe, Research, Ability, Contract, Event, Enemy;

            public ushort ItemOf(ushort saved) => saved < Item.Length ? Item[saved] : ContentDB.NoItem;
            public static int Of(int[] table, int saved) => saved >= 0 && saved < table.Length ? table[saved] : -1;
        }

        /// <summary>Reads a save into a new simulation. Throws <see cref="InvalidDataException"/> for damaged files.</summary>
        public static Simulation Load(Stream stream, ContentDB content, GameConfig config, out SaveHeader header)
        {
            var sections = SaveFormat.Read(stream, out header);
            if (header.Version > Version)
                throw new InvalidDataException($"This save was made by a newer version of the game (format {header.Version}, this build reads up to {Version}).");
            Migrate(header.Version, sections);

            if (!sections.TryGetValue("IDS", out var ids) || !sections.TryGetValue("WRLD", out var worldData))
                throw new InvalidDataException("Corrupt save: the world is missing.");
            var map = ReadRemap(ids, content);
            var world = ReadWorld(worldData, map, content);
            Simulation sim = null;
            try
            {
                sim = new Simulation(content, config, header.Seed, world);
                var nurseryEggs = new List<(int egg, int2 origin, int slot)>();
                Read(sections, "FLUD", (r, _) => ReadFluid(r, sim.World));
                Read(sections, "PHER", (r, _) => ReadPheromones(r, sim));
                Read(sections, "SIM", (r, _) => ReadSim(r, sim));
                Read(sections, "PROG", (r, _) => ReadProgress(r, sim, map)); // research first: it sets capacities and unlocks
                Read(sections, "COLN", (r, v) => ReadColony(r, sim, map, v, nurseryEggs));
                Read(sections, "ANTS", (r, v) => ReadAnts(r, sim, map, v));
                Read(sections, "BLDG", (r, v) => ReadBuildings(r, sim, map, v));
                Read(sections, "THRT", (r, _) => ReadThreats(r, sim, map));
                PlaceNurseryEggs(sim, nurseryEggs);
                sim.FinishLoad();
                return sim;
            }
            catch
            {
                if (sim != null) sim.Dispose();
                else world.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Upgrades the sections of an older save in place. 1 → 2 → 3 need nothing: every changed section carries its
        /// own version byte and the readers fill in defaults for older data (full health, workers, no fluid, no
        /// pheromones, the soldier share as the only caste share, one item carried).
        /// </summary>
        static void Migrate(int fromVersion, Dictionary<string, byte[]> sections)
        {
        }

        static void Read(Dictionary<string, byte[]> sections, string tag, Action<BinaryReader, byte> read)
        {
            if (!sections.TryGetValue(tag, out var data)) return; // missing section: keep defaults
            using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            byte version = r.ReadByte();
            try
            {
                read(r, version);
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException($"Corrupt save: section '{tag}' is truncated.", e);
            }
        }

        static Remap ReadRemap(byte[] data, ContentDB content)
        {
            using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            byte version = r.ReadByte();
            var map = new Remap();
            var materials = r.ReadIds();
            ushort fallback = content.GetMaterialIndex(content.WorldGen.biomes[0].baseMaterial);
            map.Material = Array.ConvertAll(materials, id => content.TryGetMaterialIndex(id, out var i) ? i : fallback);
            map.Item = Array.ConvertAll(r.ReadIds(), id => content.TryGetItemIndex(id, out var i) ? i : ContentDB.NoItem);
            map.Building = Array.ConvertAll(r.ReadIds(), id => content.TryGetBuildingIndex(id, out var i) ? i : -1);
            map.Recipe = Array.ConvertAll(r.ReadIds(), id => content.TryGetRecipeIndex(id, out var i) ? i : -1);
            map.Research = Array.ConvertAll(r.ReadIds(), id => content.TryGetResearchIndex(id, out var i) ? i : -1);
            map.Ability = Array.ConvertAll(r.ReadIds(), id => content.TryGetAbilityIndex(id, out var i) ? i : -1);
            map.Contract = Array.ConvertAll(r.ReadIds(), id => content.TryGetContractIndex(id, out var i) ? i : -1);
            map.Event = Array.ConvertAll(r.ReadIds(), id => content.TryGetEventIndex(id, out var i) ? i : -1);
            map.Enemy = version >= 2 ? Array.ConvertAll(r.ReadIds(), id => content.TryGetEnemyIndex(id, out var i) ? i : -1) : Array.Empty<int>();
            return map;
        }

        static void ReadFluid(BinaryReader r, WorldGrid world)
        {
            int n = r.ReadInt32();
            if (n != world.Fluid.Length) throw new InvalidDataException("Corrupt save: the fluid plane does not match the world.");
            var level = r.ReadBytes(n);
            var kind = r.ReadBytes(n);
            if (kind.Length != n) throw new InvalidDataException("Corrupt save: the fluid plane is truncated.");
            var fluid = new NativeArray<FluidCell>(n, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++)
            {
                var k = (FluidKind)kind[i];
                fluid[i] = Enum.IsDefined(typeof(FluidKind), k) && world.Cells[i].IsOpen ? new FluidCell(k, level[i]) : FluidCell.Empty;
            }
            world.Fluid.CopyFrom(fluid);
            fluid.Dispose();
        }

        static void ReadPheromones(BinaryReader r, Simulation sim)
        {
            int kinds = r.ReadInt32();
            int n = r.ReadInt32();
            var data = r.ReadBytes(n);
            if (data.Length != n) throw new InvalidDataException("Corrupt save: the pheromone planes are truncated.");
            int cells = sim.Pheromones.CellCount;
            if (n != kinds * cells) throw new InvalidDataException("Corrupt save: the pheromone planes do not match the world.");
            var strengths = new byte[Antarium.Content.Pheromones.Count * cells];
            Array.Copy(data, strengths, Math.Min(data.Length, strengths.Length)); // kinds added later start empty
            sim.Pheromones.Restore(strengths);
        }

        /// <summary>Eggs saved in a nursery go back into the nursery now standing at that origin (else they lie loose).</summary>
        static void PlaceNurseryEggs(Simulation sim, List<(int egg, int2 origin, int slot)> eggs)
        {
            var colony = sim.Colony;
            foreach (var (index, origin, slot) in eggs)
            {
                if (index >= colony.Eggs.Count) continue;
                int id = sim.Buildings.At(origin);
                if (id < 0 || sim.Buildings.Get(id).Kind != BuildingKind.Nursery) continue;
                var egg = colony.Eggs[index];
                egg.Nursery = id;
                egg.Slot = slot;
                egg.Cell = Simulation.NurserySlotCell(sim.Buildings.Get(id), slot);
                colony.Eggs[index] = egg;
            }
        }

        static WorldGrid ReadWorld(byte[] data, Remap map, ContentDB content)
        {
            using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            r.ReadByte();
            int chunksX = r.ReadInt32(), chunksY = r.ReadInt32();
            if (chunksX <= 0 || chunksY <= 0 || chunksX > 1024 || chunksY > 1024) throw new InvalidDataException("Corrupt save: bad world size.");
            var world = new WorldGrid(chunksX, chunksY);
            try
            {
                for (int x = 0; x < world.Width; x++) world.SurfaceY[x] = r.ReadInt32();
                int n = world.Cells.Length;
                var materials = r.ReadBytes(n * 2);
                var density = r.ReadBytes(n);
                var flags = r.ReadBytes(n);
                if (flags.Length != n) throw new InvalidDataException("Corrupt save: the world is truncated.");
                var cells = new NativeArray<Cell>(n, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < n; i++)
                {
                    int saved = materials[i * 2] | (materials[i * 2 + 1] << 8);
                    ushort material = saved < map.Material.Length ? map.Material[saved] : ContentDB.AirIndex;
                    cells[i] = new Cell(material, density[i], (CellFlags)flags[i]);
                }
                world.Cells.CopyFrom(cells);
                cells.Dispose();
                world.MarkAll(ChunkFlags.TerrainUpload | ChunkFlags.LightUpload);
                return world;
            }
            catch
            {
                world.Dispose();
                throw;
            }
        }

        static void ReadSim(BinaryReader r, Simulation sim)
        {
            uint state = r.ReadUInt32();
            if (state != 0) sim.Rng = new Unity.Mathematics.Random { state = state };
            sim.PlayTime = r.ReadDouble();
        }

        static void ReadCounts(BinaryReader r, Remap map, Action<ushort, int> add)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                ushort item = map.ItemOf(r.ReadUInt16());
                int count = r.ReadInt32();
                if (item != ContentDB.NoItem && count > 0) add(item, count);
            }
        }

        static void ReadColony(BinaryReader r, Simulation sim, Remap map, byte version, List<(int egg, int2 origin, int slot)> nurseryEggs)
        {
            var colony = sim.Colony;
            var inv = colony.Inventory;
            var world = sim.World;
            colony.QueenCell = r.ReadInt2();
            inv.Food = r.ReadSingle();
            inv.Knowledge = r.ReadSingle();
            colony.EggTimer = r.ReadSingle();
            colony.ForageTimer = r.ReadSingle();
            colony.Starving = r.ReadBoolean();

            colony.Eggs.Clear();
            int eggs = r.ReadInt32();
            for (int i = 0; i < eggs; i++) colony.Eggs.Add(new Egg { Cell = r.ReadInt2(), Age = r.ReadSingle(), Nursery = -1 });
            colony.RoyalDisplayCells.Clear();
            int display = r.ReadInt32();
            for (int i = 0; i < display; i++) colony.RoyalDisplayCells.Add(r.ReadInt2());

            int piles = r.ReadInt32();
            for (int i = 0; i < piles; i++)
            {
                var cell = r.ReadInt2();
                ushort item = map.ItemOf(r.ReadUInt16());
                int count = r.ReadInt32();
                if (item != ContentDB.NoItem && world.InBounds(cell.x, cell.y)) colony.Piles.Add(cell, item, count);
            }

            inv.ResetForLoad();
            int containers = r.ReadInt32();
            for (int i = 0; i < containers; i++)
            {
                var cell = r.ReadInt2();
                int worldIndex = r.ReadInt32();
                StockContainer target = null;
                if (worldIndex < 0) target = inv.Royal;
                else if (worldIndex < world.Cells.Length && (world.Cells[worldIndex].Flags & CellFlags.Storage) != 0)
                {
                    inv.AddStorageCell(cell, worldIndex);
                    target = inv.ContainerAt(worldIndex);
                }
                ReadCounts(r, map, (item, count) =>
                {
                    if (sim.Content.Items[item].IsFood) return;
                    if (target != null) inv.RestoreCount(target, item, count);
                    else colony.Piles.Add(world.FloorBelow(cell), item, count); // storage cell vanished: drop
                });
            }

            var stats = colony.Stats;
            ReadLongs(r, (i, v) => { ushort item = map.ItemOf((ushort)i); if (item != ContentDB.NoItem) stats.Delivered[item] = v; });
            ReadLongs(r, (i, v) => { ushort item = map.ItemOf((ushort)i); if (item != ContentDB.NoItem) stats.Crafted[item] = v; });
            int built = r.ReadInt32();
            for (int i = 0; i < built; i++)
            {
                int v = r.ReadInt32();
                int b = Remap.Of(map.Building, i);
                if (b >= 0) stats.Built[b] = v;
            }
            stats.CellsDug = r.ReadInt64();
            stats.MaxDepth = r.ReadInt32();
            stats.AntsHatched = r.ReadInt64();
            stats.ContractsCompleted = r.ReadInt32();
            stats.EventsWon = r.ReadInt32();
            stats.KnowledgeEarned = r.ReadDouble();

            var meter = colony.Meter;
            int minutes = r.ReadInt32();
            meter.Current = math.clamp(r.ReadInt32(), 0, ProductionMeter.Minutes - 1);
            meter.Filled = math.clamp(r.ReadInt32(), 0, ProductionMeter.Minutes - 1);
            meter.CurrentSeconds = r.ReadSingle();
            for (int b = 0; b < minutes; b++)
            {
                float food = r.ReadSingle(), knowledge = r.ReadSingle();
                int items = r.ReadInt32();
                bool keep = b < ProductionMeter.Minutes;
                if (keep)
                {
                    meter.Food[b] = food;
                    meter.Knowledge[b] = knowledge;
                }
                for (int i = 0; i < items; i++)
                {
                    float v = r.ReadSingle();
                    ushort item = map.ItemOf((ushort)i);
                    if (keep && item != ContentDB.NoItem) meter.Items[b][item] = v;
                }
            }
            if (version < 2) return;

            int castes = r.ReadInt32();
            for (int i = 0; i < castes; i++)
            {
                var caste = (AntCaste)r.ReadByte();
                if (i >= colony.Eggs.Count || !Enum.IsDefined(typeof(AntCaste), caste)) continue;
                var egg = colony.Eggs[i];
                egg.Caste = caste;
                colony.Eggs[i] = egg;
            }
            float share = r.ReadSingle();
            if (version < 3) colony.SetCasteShare(AntCaste.Soldier, share);
            stats.EnemiesKilled = r.ReadInt64();
            stats.AntsLost = r.ReadInt64();
            stats.RaidsRepelled = r.ReadInt32();
            if (version < 3) return;

            int casteCount = r.ReadInt32();
            for (int c = 0; c < casteCount; c++)
            {
                float s = r.ReadSingle();
                long hatched = r.ReadInt64();
                if (c >= Castes.Count) continue;
                stats.HatchedCaste[c] = hatched;
                if (c != (int)AntCaste.Worker) colony.SetCasteShare((AntCaste)c, s);
            }
            stats.CellsScouted = r.ReadInt64();
            int eggCount = r.ReadInt32();
            for (int i = 0; i < eggCount; i++)
            {
                if (!r.ReadBoolean()) continue;
                var origin = r.ReadInt2();
                int slot = r.ReadInt32();
                nurseryEggs.Add((i, origin, slot));
            }
        }

        static void ReadLongs(BinaryReader r, Action<int, long> set)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) set(i, r.ReadInt64());
        }

        static void ReadAnts(BinaryReader r, Simulation sim, Remap map, byte version)
        {
            var ants = sim.Ants;
            ants.HealthMultiplier = sim.Colony.Mods.Mult(StatKind.AntHealth); // research is loaded first
            ants.SpawnCounter = r.ReadUInt32();
            int count = r.ReadInt32();
            ants.Ants.Clear();
            for (int i = 0; i < count; i++)
            {
                var a = new Ant
                {
                    Cell = r.ReadInt2(),
                    From = r.ReadInt2(),
                    Progress = r.ReadSingle(),
                    StepLength = r.ReadSingle(),
                    Heading = r.ReadInt2(),
                    State = (AntState)r.ReadByte(),
                    Carry = r.ReadUInt16(),
                    Dest = (CarryDest)r.ReadByte(),
                    Want = r.ReadUInt16(),
                    Target = r.ReadInt2(),
                    Timer = r.ReadSingle(),
                    WalkPhase = r.ReadSingle(),
                };
                uint rng = r.ReadUInt32();
                a.Rng = new Unity.Mathematics.Random { state = rng != 0 ? rng : 1u };
                a.PrevPos = r.ReadFloat2();
                a.Pos = r.ReadFloat2();
                a.Killer = -1;
                if (version >= 2)
                {
                    a.Caste = (AntCaste)r.ReadByte();
                    if (!Enum.IsDefined(typeof(AntCaste), a.Caste)) a.Caste = AntCaste.Worker;
                    a.Health = r.ReadSingle();
                    a.SinceHurt = r.ReadSingle();
                }
                if (version >= 3)
                {
                    a.CarryCount = r.ReadByte();
                    a.CarryingEgg = r.ReadBoolean();
                    a.EggCaste = (AntCaste)r.ReadByte();
                    a.EggAge = r.ReadSingle();
                    if (!Enum.IsDefined(typeof(AntCaste), a.EggCaste)) a.CarryingEgg = false;
                }
                float max = ants.MaxHealth(a.Caste);
                a.Health = version >= 2 ? math.clamp(a.Health, 1f, max) : max;
                if (version < 2) a.SinceHurt = 1000f;
                if (a.State == AntState.Dead || !Enum.IsDefined(typeof(AntState), a.State)) a.State = AntState.Idle;
                if (a.State == AntState.Tending || a.State == AntState.Rallying) a.State = AntState.Idle; // re-found after loading

                bool wasCarrying = a.Carry != ContentDB.NoItem;
                a.Carry = wasCarrying ? map.ItemOf(a.Carry) : ContentDB.NoItem;
                a.CarryCount = a.Carry == ContentDB.NoItem ? (byte)0 : (byte)math.max(1, (int)a.CarryCount);
                a.Want = map.ItemOf(a.Want);
                if (a.Want == ContentDB.NoItem) a.Want = 0;
                if (!sim.World.InBounds(a.Cell.x, a.Cell.y) || !sim.World.InBounds(a.From.x, a.From.y)) continue;
                if ((wasCarrying && a.Carry == ContentDB.NoItem) || !Enum.IsDefined(typeof(AntState), a.State)) a.State = AntState.Idle;
                if (a.StepLength <= 0f) a.StepLength = 1f;
                ants.Ants.Add(a);
            }
        }

        static ItemSlot ReadSlot(BinaryReader r, Remap map, in Building b)
        {
            ushort saved = r.ReadUInt16();
            float t = r.ReadSingle();
            if (saved == ContentDB.NoItem) return ItemSlot.Empty;
            ushort item = map.ItemOf(saved);
            if (item == ContentDB.NoItem) return ItemSlot.Empty;
            var pos = BuildingSystem.SlotPos(b, t);
            return new ItemSlot { Item = item, T = t, Prev = pos, Pos = pos };
        }

        static void ReadBuildings(BinaryReader r, Simulation sim, Remap map, byte version)
        {
            var buildings = sim.Buildings;
            var content = sim.Content;
            int count = r.ReadInt32();
            for (int n = 0; n < count; n++)
            {
                int def = Remap.Of(map.Building, r.ReadInt32());
                var origin = r.ReadInt2();
                var dir = r.ReadInt2();
                var state = (BuildingState)r.ReadByte();
                float workLeft = r.ReadSingle();
                ushort rawFilter = r.ReadUInt16();
                // Filters store an item (remapped by id); emitters store a pheromone kind.
                bool emitter = def >= 0 && content.Buildings[def].Def.kind == BuildingKind.Emitter;
                ushort filter = emitter ? (rawFilter < Antarium.Content.Pheromones.Count ? rawFilter : (ushort)0) : map.ItemOf(rawFilter);
                if (filter == ContentDB.NoItem) filter = 0;
                int id = def >= 0 ? buildings.Restore(def, origin, dir, filter, state, workLeft) : BuildingSystem.None;
                var probe = id >= 0 ? buildings.Get(id) : new Building { Origin = origin, Dir = dir };
                var front = ReadSlot(r, map, probe);
                var back = ReadSlot(r, map, probe);
                byte roundRobin = r.ReadByte();
                float stuck = r.ReadSingle();
                float rate = r.ReadSingle();
                float health = version >= 2 ? r.ReadSingle() : -1f;
                float rearm = version >= 2 ? r.ReadSingle() : 0f;
                if (id >= 0)
                {
                    ref var b = ref buildings.Get(id);
                    if (b.IsTransport && b.IsActive)
                    {
                        if (front.IsEmpty) (front, back) = (back, ItemSlot.Empty);
                        b.Front = front;
                        b.Back = back;
                    }
                    b.RoundRobin = roundRobin;
                    b.Stuck = stuck;
                    b.Rate = rate;
                    if (b.Kind == BuildingKind.Gate && b.IsActive && health > 0f) b.Health = math.min(health, buildings.GateMaxHealth(b.Def));
                    if (b.Kind == BuildingKind.Trap) b.Rearm = math.max(0f, rearm);
                }

                if (!r.ReadBoolean()) continue;
                var m = id >= 0 ? buildings.MachineOf(id) : null;
                int inputs = r.ReadInt32();
                for (int i = 0; i < inputs; i++)
                {
                    ushort item = map.ItemOf(r.ReadUInt16());
                    int value = r.ReadInt32();
                    int at = m != null && item != ContentDB.NoItem ? m.InputIndex(item) : -1;
                    if (at >= 0) m.Inputs[at] = math.clamp(value, 0, m.Data.InputCaps[at]);
                }
                int pending = r.ReadInt32();
                for (int i = 0; i < pending; i++)
                {
                    ushort item = map.ItemOf(r.ReadUInt16());
                    if (m != null && item != ContentDB.NoItem) m.Pending.Add(item);
                }
                int recipe = Remap.Of(map.Recipe, r.ReadInt32());
                float progress = r.ReadSingle();
                int next = r.ReadInt32();
                int crafted = r.ReadInt32();
                if (m == null) continue;
                m.Recipe = recipe >= 0 && Array.IndexOf(m.Data.Recipes, recipe) >= 0 ? recipe : -1;
                m.Progress = m.Recipe >= 0 ? math.clamp(progress, 0f, content.Recipes[m.Recipe].Duration) : 0f;
                m.NextRecipe = m.Data.Recipes.Length > 0 ? math.clamp(next, 0, m.Data.Recipes.Length - 1) : 0;
                m.Crafted = crafted;
            }
        }

        static void ReadThreats(BinaryReader r, Simulation sim, Remap map)
        {
            var t = sim.Enemies;
            uint state = r.ReadUInt32();
            if (state != 0) t.Rng = new Unity.Mathematics.Random { state = state };
            t.RaidTimer = r.ReadSingle();
            t.RaidWarned = r.ReadBoolean();
            t.RaidSpawn = r.ReadInt2();
            t.RaidCount = r.ReadInt32();
            t.RaidActive = r.ReadInt32();
            t.RaidSize = r.ReadInt32();
            t.RaidFoodLost = r.ReadSingle();
            t.Enemies.Clear();
            int count = r.ReadInt32();
            var world = sim.World;
            for (int i = 0; i < count; i++)
            {
                var e = new Enemy
                {
                    Def = Remap.Of(map.Enemy, r.ReadInt32()),
                    State = (EnemyState)r.ReadByte(),
                    Cell = r.ReadInt2(),
                    From = r.ReadInt2(),
                    Progress = r.ReadSingle(),
                    StepLength = r.ReadSingle(),
                    Heading = r.ReadInt2(),
                    Health = r.ReadSingle(),
                    AttackTimer = r.ReadSingle(),
                    Held = r.ReadSingle(),
                    Home = r.ReadInt2(),
                    Eaten = r.ReadSingle(),
                    Raid = r.ReadInt32(),
                    SinceHurt = 1000f,
                };
                if (e.Def < 0 || !world.InBounds(e.Cell.x, e.Cell.y) || !world.InBounds(e.From.x, e.From.y) || e.Health <= 0f) continue;
                if (!Enum.IsDefined(typeof(EnemyState), e.State)) e.State = EnemyState.Wander;
                if (e.StepLength <= 0f) e.StepLength = 1f;
                e.Health = math.min(e.Health, t.MaxHealth(e.Def));
                e.Pos = e.PrevPos = math.lerp(new float2(e.From), new float2(e.Cell), e.Progress) + 0.5f;
                t.Enemies.Add(e);
            }
            t.Changed = true;
        }

        static void ReadProgress(BinaryReader r, Simulation sim, Remap map)
        {
            int research = r.ReadInt32();
            for (int i = 0; i < research; i++)
            {
                bool done = r.ReadBoolean();
                int index = Remap.Of(map.Research, i);
                if (done && index >= 0) sim.Research.Apply(index);
            }

            var abilities = sim.Abilities;
            int abilityCount = r.ReadInt32();
            for (int i = 0; i < abilityCount; i++)
            {
                float cooldown = r.ReadSingle(), remaining = r.ReadSingle();
                int index = Remap.Of(map.Ability, i);
                if (index < 0) continue;
                abilities.Cooldown[index] = math.max(0f, cooldown);
                abilities.Remaining[index] = math.max(0f, remaining);
            }

            var contracts = sim.Contracts;
            uint contractRng = r.ReadUInt32();
            if (contractRng != 0) contracts.Rng = new Unity.Mathematics.Random { state = contractRng };
            int completed = r.ReadInt32();
            for (int i = 0; i < completed; i++)
            {
                int times = r.ReadInt32();
                int index = Remap.Of(map.Contract, i);
                if (index >= 0) contracts.Completed[index] = times;
            }
            contracts.Active.Clear();
            int active = r.ReadInt32();
            for (int i = 0; i < active; i++)
            {
                var c = new ActiveContract
                {
                    Def = Remap.Of(map.Contract, r.ReadInt32()),
                    Target = r.ReadInt64(),
                    Baseline = r.ReadInt64(),
                    RewardScale = r.ReadSingle(),
                };
                if (c.Def >= 0) contracts.Active.Add(c);
            }

            var events = sim.RandomEvents;
            uint eventRng = r.ReadUInt32();
            if (eventRng != 0) events.Rng = new Unity.Mathematics.Random { state = eventRng };
            events.Timer = r.ReadSingle();
            events.NextId = r.ReadInt32();
            events.Active.Clear();
            int activeEvents = r.ReadInt32();
            for (int i = 0; i < activeEvents; i++)
            {
                var e = new ActiveEvent
                {
                    Id = r.ReadInt32(),
                    Def = Remap.Of(map.Event, r.ReadInt32()),
                    Cell = r.ReadInt2(),
                    Min = r.ReadInt2(),
                    Max = r.ReadInt2(),
                    TimeLeft = r.ReadSingle(),
                    Count = r.ReadInt32(),
                };
                if (e.Def < 0) continue;
                e.Kind = sim.Content.Events[e.Def].Def.kind;
                events.Active.Add(e);
            }
            events.Critters.Clear();
            int critters = r.ReadInt32();
            for (int i = 0; i < critters; i++)
            {
                var c = new Critter
                {
                    EventId = r.ReadInt32(),
                    Cell = r.ReadInt2(),
                    From = r.ReadInt2(),
                    Progress = r.ReadSingle(),
                    StepLength = r.ReadSingle(),
                    Heading = r.ReadInt2(),
                    WalkPhase = r.ReadSingle(),
                };
                c.Pos = c.PrevPos = r.ReadFloat2();
                if (c.StepLength <= 0f) c.StepLength = 1f;
                if (events.TryGetActive(c.EventId, out _)) events.Critters.Add(c);
            }
        }
    }
}
