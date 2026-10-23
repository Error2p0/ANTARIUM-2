using System.Collections.Generic;
using Antarium.Content;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Specialist castes and pheromones: nurses carrying eggs to nurseries, engineers tending machines, scouts sensing
    /// the rock at the edge of the known tunnels, the Pheromone Burst rally, emitters and trails kept by traffic.
    /// </summary>
    public sealed partial class Simulation
    {
        readonly List<int> _nurseries = new();
        readonly List<int> _emitters = new();
        NativeList<int2> _frontier = new(256, Allocator.Persistent);
        bool _broodChanged = true;
        bool _forceFields;
        bool _frontierDirty = true;
        float _frontierTimer;
        float _senseTimer;
        int _machineOpenings = -1;
        int _critterCount;
        int _eggCount = -1;
        int2 _rallyCell;
        float _rallyTimer;
        float _rallySpeed = 1f;
        bool _rallyDirty;

        int RallyArriveCells => Config.pheromones.rallyArriveCells;

        /// <summary>Seconds left of the running Pheromone Burst (0 = none) and where it calls the ants.</summary>
        public float RallyTimeLeft => _rallyTimer;
        public int2 RallyCell => _rallyCell;

        // ---------------------------------------------------------------- brood

        /// <summary>Eggs nurses could carry right now: loose eggs, limited by free nursery room (eggs already being carried use it).</summary>
        int NurseSlots()
        {
            if (_nurseries.Count == 0) return 0;
            int room = 0;
            foreach (int id in _nurseries) room += NurseryRoom(id);
            int carried = 0;
            foreach (var a in Ants.Ants)
                if (a.CarryingEgg) carried++;
            return math.max(0, math.min(Colony.LooseEggs, room - carried));
        }

        int NurseryRoom(int id)
        {
            var b = Buildings.Get(id);
            if (!b.IsActive || b.Kind != BuildingKind.Nursery) return 0;
            int used = 0;
            foreach (var e in Colony.Eggs)
                if (e.Nursery == id) used++;
            return math.max(0, Content.Buildings[b.Def].Def.eggCapacity - used);
        }

        float NurseryBonus(int id)
        {
            if (id < 0 || id >= Buildings.All.Length) return 0f;
            var b = Buildings.All[id];
            return b.IsActive && b.Kind == BuildingKind.Nursery ? Content.Buildings[b.Def].Def.hatchBonus : 0f;
        }

        /// <summary>Eggs carried by nurses age too; one that hatches on the way becomes an ant right there.</summary>
        void AgeCarriedEggs(float dt)
        {
            var ants = Ants.Ants;
            int count = ants.Length;
            for (int i = 0; i < count; i++)
            {
                var a = ants[i];
                if (!a.CarryingEgg) continue;
                a.EggAge += dt;
                if (a.EggAge >= Colony.HatchTime)
                {
                    a.CarryingEgg = false;
                    if (a.State == AntState.CarryEgg) a.State = AntState.Idle;
                    ants[i] = a;
                    Hatch(a.Cell, a.EggCaste);
                    _broodChanged = true;
                    continue;
                }
                ants[i] = a;
            }
        }

        /// <summary>Eggs in a nursery that no longer exists lie loose where they are.</summary>
        void ValidateNurseryEggs()
        {
            Buildings.Collect(BuildingKind.Nursery, _nurseries);
            for (int i = 0; i < Colony.Eggs.Count; i++)
            {
                var e = Colony.Eggs[i];
                if (!e.InNursery || _nurseries.Contains(e.Nursery)) continue;
                e.Nursery = -1;
                e.Cell = World.FloorBelow(e.Cell);
                Colony.Eggs[i] = e;
            }
            _broodChanged = true;
        }

        /// <summary>Floor cell and pixel offset of nursery slot <paramref name="slot"/> (two eggs share a cell).</summary>
        public static int2 NurserySlotCell(in Building b, int slot) => b.Origin + new int2(slot % math.max(1, b.Size.x), 0);

        public static int NurserySlotOffset(in Building b, int slot) => (slot / math.max(1, b.Size.x)) % 2 == 0 ? -2 : 2;

        bool HandleCasteIntent(in AntIntent intent, ref Ant ant)
        {
            switch (intent.Kind)
            {
                case IntentKind.PickEgg:
                {
                    int found = -1;
                    for (int i = 0; i < Colony.Eggs.Count; i++)
                    {
                        var e = Colony.Eggs[i];
                        if (!e.InNursery && e.Cell.Equals(intent.Cell)) { found = i; break; }
                    }
                    if (found < 0 || ant.CarryingEgg || NurseSlots() <= 0)
                    {
                        ant.State = AntState.Idle;
                        return true;
                    }
                    var egg = Colony.Eggs[found];
                    Colony.Eggs.RemoveAt(found);
                    ant.CarryingEgg = true;
                    ant.EggCaste = egg.Caste;
                    ant.EggAge = egg.Age;
                    ant.State = AntState.CarryEgg;
                    _broodChanged = true;
                    return true;
                }
                case IntentKind.PlaceEgg:
                {
                    int id = Buildings.At(intent.Cell);
                    if (!ant.CarryingEgg || id < 0 || NurseryRoom(id) <= 0)
                    {
                        // Not here (full, or the field is stale): look again once the fields are rebuilt.
                        ant.State = AntState.Wander;
                        ant.Timer = 0.5f;
                        return true;
                    }
                    var b = Buildings.Get(id);
                    int slot = 0;
                    while (SlotTaken(id, slot)) slot++;
                    Colony.Eggs.Add(new Egg { Cell = NurserySlotCell(b, slot), Age = ant.EggAge, Caste = ant.EggCaste, Nursery = id, Slot = slot });
                    ant.CarryingEgg = false;
                    ant.State = AntState.Idle;
                    _broodChanged = true;
                    return true;
                }
                case IntentKind.DropEgg:
                    PutEggDown(ref ant, intent.Cell);
                    ant.State = AntState.Idle;
                    return true;
                case IntentKind.CatchCritter:
                    RandomEvents.Catch(new float2(intent.Cell) + 0.5f);
                    if (RandomEvents.CritterAt(new float2(intent.Cell) + 0.5f) < 0) ant.State = AntState.Idle;
                    return true;
            }
            return false;
        }

        bool SlotTaken(int nursery, int slot)
        {
            foreach (var e in Colony.Eggs)
                if (e.Nursery == nursery && e.Slot == slot) return true;
            return false;
        }

        /// <summary>A carried egg goes back on the floor as a loose egg.</summary>
        void PutEggDown(ref Ant ant, int2 c)
        {
            if (!ant.CarryingEgg) return;
            Colony.Eggs.Add(new Egg { Cell = World.FloorBelow(c), Age = ant.EggAge, Caste = ant.EggCaste, Nursery = -1 });
            ant.CarryingEgg = false;
            _broodChanged = true;
        }

        // ---------------------------------------------------------------- engineers

        /// <summary>Counts the engineers tending each machine (sending away those whose machine is gone or full).</summary>
        void UpdateEngineers()
        {
            Buildings.ClearTenders();
            int max = Config.castes.engineersPerMachine;
            var ants = Ants.Ants;
            for (int i = 0; i < ants.Length; i++)
            {
                var a = ants[i];
                if (a.State != AntState.Tending) continue;
                int id = Buildings.At(a.Target);
                var m = id >= 0 && Buildings.Get(id).IsActive ? Buildings.MachineOf(id) : null;
                if (m == null || m.Tenders >= max || a.Possessed)
                {
                    a.State = AntState.Idle;
                    ants[i] = a;
                    continue;
                }
                m.Tenders++;
            }
            // Machines join or leave the engineers' field as they gain or lose a tender.
            int openings = 0;
            for (int id = 0; id < Buildings.All.Length; id++)
            {
                if (!Buildings.All[id].IsActive) continue;
                var m = Buildings.MachineOf(id);
                if (m != null) openings = openings * 31 + (m.Tenders < max ? id + 1 : 0);
            }
            if (openings != _machineOpenings)
            {
                _machineOpenings = openings;
                Fields.MarkDirty(FieldKind.Machines);
            }
        }

        // ---------------------------------------------------------------- scouts

        /// <summary>Twice a second every scout senses the rock around it: hidden cells there are revealed, ore found glows.</summary>
        void UpdateScouts(float dt)
        {
            _frontierTimer += dt;
            _senseTimer += dt;
            if (_senseTimer < 0.5f || _alive[(int)AntCaste.Scout] == 0) return;
            _senseTimer = 0f;
            int radius = Ants.Caste(AntCaste.Scout).SenseRadius;
            if (radius <= 0) return;
            int revealed = 0;
            foreach (var a in Ants.Ants)
                if (a.Caste == AntCaste.Scout && !a.IsDead && !a.IsMoving) revealed += Sense(a.Cell, radius);
            if (revealed > 0) _frontierDirty = true;
        }

        int Sense(int2 center, int radius)
        {
            int r2 = radius * radius, revealed = 0;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                int x = center.x + dx, y = center.y + dy;
                if (!World.InBounds(x, y)) continue;
                int i = World.Index(x, y);
                var cell = World.Cells[i];
                if (cell.IsRevealed) continue;
                cell.Flags |= CellFlags.Revealed;
                if (!cell.IsOpen && Content.MaterialSim[cell.Material].DropItem != ContentDB.NoItem) cell.Flags |= CellFlags.Scanned;
                World.Cells[i] = cell;
                World.MarkChanged(x, y, ChunkFlags.TerrainUpload);
                revealed++;
            }
            Colony.Stats.CellsScouted += revealed;
            return revealed;
        }

        // ---------------------------------------------------------------- rally (Pheromone Burst)

        /// <summary>Calls every ant within the ability's radius to <paramref name="cell"/>. Returns how many answered.</summary>
        int StartRally(int2 cell, AbilityDef def)
        {
            if (!World.InBounds(cell.x, cell.y)) return 0;
            int answered = 0;
            float r2 = def.radius * def.radius;
            var ants = Ants.Ants;
            for (int i = 0; i < ants.Length; i++)
            {
                var a = ants[i];
                if (a.IsDead || a.Possessed || math.distancesq(a.Pos, new float2(cell) + 0.5f) > r2) continue;
                a.State = AntState.Rallying;
                a.Timer = def.duration;
                ants[i] = a;
                answered++;
            }
            if (answered == 0) return 0;
            _rallyCell = cell;
            _rallyTimer = def.duration;
            _rallySpeed = 1f + def.magnitude;
            _rallyDirty = true;
            _forceFields = true;
            return answered;
        }

        void UpdateRally(float dt)
        {
            if (_rallyTimer <= 0f) return;
            _rallyTimer -= dt;
            if (_rallyTimer > 0f) return;
            _rallyTimer = 0f;
            _rallySpeed = 1f;
            _rallyDirty = true;
        }

        // ---------------------------------------------------------------- pheromones

        /// <summary>Each emitter keeps its kind of scent fresh around it.</summary>
        void SustainEmitters(PheromoneSystem pheromones)
        {
            Buildings.Collect(BuildingKind.Emitter, _emitters);
            foreach (int id in _emitters)
            {
                var b = Buildings.Get(id);
                if (b.Filter >= Antarium.Content.Pheromones.Count) continue;
                pheromones.Sustain((PheromoneKind)b.Filter, b.Origin, Content.Buildings[b.Def].Def.emitterRadius);
            }
        }

        /// <summary>Ants carrying something keep the trails they walk fresh.</summary>
        void ReinforceTrails()
        {
            if (Pheromones.Cells[(int)PheromoneKind.Trail] == 0) return;
            foreach (var a in Ants.Ants)
            {
                if (!a.IsMoving || a.Progress > 0f || !(a.IsCarrying || a.CarryingEgg)) continue;
                Pheromones.Reinforce(World.Index(a.Cell.x, a.Cell.y));
            }
        }

        // ---------------------------------------------------------------- fields

        void MarkCasteFieldsDirty()
        {
            int eggs = Colony.Eggs.Count;
            if (eggs != _eggCount)
            {
                _eggCount = eggs;
                _broodChanged = true; // eggs laid, hatched or moved by anyone
            }
            if (_broodChanged || Buildings.NurseriesChanged)
            {
                if (Buildings.NurseriesChanged) ValidateNurseryEggs();
                Fields.MarkDirty(FieldKind.Brood);
                Fields.MarkDirty(FieldKind.Nursery);
                _broodChanged = Buildings.NurseriesChanged = false;
            }
            if (Buildings.MachinesChanged)
            {
                Fields.MarkDirty(FieldKind.Machines);
                Buildings.MachinesChanged = false;
            }
            if (_alive[(int)AntCaste.Scout] > 0)
            {
                if (_terrainChanged) _frontierDirty = true;
                if (_frontierDirty && _frontierTimer >= Config.castes.scoutFieldSeconds)
                {
                    _frontierDirty = false;
                    _frontierTimer = 0f;
                    Fields.MarkDirty(FieldKind.Frontier);
                }
                // The beetle moves: chase it at the refresh rate; clear the field once it is gone.
                int critters = RandomEvents.Critters.Count;
                if (critters > 0 || _critterCount > 0) Fields.MarkDirty(FieldKind.Critters);
                _critterCount = critters;
            }
            if (_rallyDirty)
            {
                Fields.MarkDirty(FieldKind.Rally);
                _rallyDirty = false;
            }
        }

        void FillCasteSources()
        {
            if (Fields.IsDirty(FieldKind.Brood))
            {
                var sources = Fields.Sources(FieldKind.Brood);
                sources.Clear();
                if (NurseSlots() > 0)
                    foreach (var e in Colony.Eggs)
                        if (!e.InNursery) sources.Add(e.Cell);
            }
            if (Fields.IsDirty(FieldKind.Nursery))
            {
                var sources = Fields.Sources(FieldKind.Nursery);
                sources.Clear();
                foreach (int id in _nurseries)
                {
                    if (NurseryRoom(id) <= 0) continue;
                    var b = Buildings.Get(id);
                    for (int x = 0; x < b.Size.x; x++)
                    for (int y = 0; y < b.Size.y; y++)
                        sources.Add(b.Origin + new int2(x, y));
                }
            }
            if (Fields.IsDirty(FieldKind.Machines))
            {
                var sources = Fields.Sources(FieldKind.Machines);
                sources.Clear();
                if (Content.CasteDefined[(int)AntCaste.Engineer]) Buildings.AddMachineSources(sources, Config.castes.engineersPerMachine);
            }
            if (Fields.IsDirty(FieldKind.Frontier))
            {
                var sources = Fields.Sources(FieldKind.Frontier);
                sources.Clear();
                if (_alive[(int)AntCaste.Scout] > 0) FindFrontier(sources, math.max(2, Ants.Caste(AntCaste.Scout).SenseRadius - 2));
            }
            if (Fields.IsDirty(FieldKind.Critters))
            {
                var sources = Fields.Sources(FieldKind.Critters);
                sources.Clear();
                foreach (var c in RandomEvents.Critters) sources.Add(c.Cell);
            }
            if (Fields.IsDirty(FieldKind.Rally))
            {
                var sources = Fields.Sources(FieldKind.Rally);
                sources.Clear();
                if (_rallyTimer > 0f) sources.Add(World.FloorBelow(_rallyCell));
            }
        }

        /// <summary>Explored tunnel cells with hidden rock <paramref name="reach"/> cells away in some direction.</summary>
        void FindFrontier(NativeList<int2> sources, int reach)
        {
            _frontier.Clear();
            var found = new NativeQueue<int2>(Allocator.TempJob);
            new FrontierJob { Nav = new NavGrid(World), Reach = reach, Result = found.AsParallelWriter() }
                .Schedule(World.ChunkCount, 4).Complete();
            while (found.TryDequeue(out var c)) _frontier.Add(c);
            found.Dispose();
            // Chunks finish in any order: sort so the sources (and so the field's ties) do not depend on threading.
            _frontier.Sort(new CellOrder());
            sources.AddRange(_frontier.AsArray());
        }

        struct CellOrder : IComparer<int2>
        {
            public int Compare(int2 a, int2 b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);
        }

        /// <summary>One chunk per job index: explored walkable underground cells with an unrevealed cell <see cref="Reach"/> away.</summary>
        [BurstCompile]
        struct FrontierJob : IJobParallelFor
        {
            public NavGrid Nav;
            public int Reach;
            public NativeQueue<int2>.ParallelWriter Result;

            public void Execute(int chunk)
            {
                int2 origin = new int2(chunk % Nav.ChunksX, chunk / Nav.ChunksX) * Antarium.Core.WorldConstants.ChunkSize;
                int start = chunk << Antarium.Core.WorldConstants.ChunkCellShift;
                for (int i = 0; i < Antarium.Core.WorldConstants.ChunkCellCount; i++)
                {
                    var cell = Nav.Cells[start + i];
                    if ((cell.Flags & CellFlags.Explored) == 0 || !cell.IsOpen) continue;
                    int2 c = origin + new int2(i & Antarium.Core.WorldConstants.ChunkMask, i >> Antarium.Core.WorldConstants.ChunkShift);
                    if (!Nav.HasBackWall(c) || !Nav.IsWalkable(c)) continue;
                    for (int k = 0; k < NavGrid.DirectionCount; k++)
                    {
                        int2 p = c + NavGrid.Direction(k) * Reach;
                        if (!Nav.InBounds(p) || Nav.Cells[Nav.Index(p)].IsRevealed) continue;
                        Result.Enqueue(c);
                        break;
                    }
                }
            }
        }
    }
}
