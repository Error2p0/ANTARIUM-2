using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>What a Crystal Resonator measured in the conductive rock around it (refreshed every climate check).</summary>
    public struct Resonance
    {
        /// <summary>Conductive cells in the cluster it touches (up to the building's limit).</summary>
        public int Cells;
        /// <summary>Cells in the cluster that add resonance (glow crystals; ores only carry it).</summary>
        public int Crystals;
        /// <summary>Summed material resonance.</summary>
        public float Total;
        /// <summary>0..1: <see cref="Total"/> over the building's full resonance.</summary>
        public float Power;
    }

    /// <summary>
    /// The deep endgame's buildings (10.5). A <b>Crystal Resonator</b> touches conductive rock: the cluster of conductive cells
    /// it reaches (glow crystals add resonance, copper and iron ore only carry it) gives it power, which makes knowledge and speeds
    /// up machines near it (the best resonator counts, like water wheels). Mining the cluster weakens it. The <b>Ancient
    /// Engine</b> is built once, in the ruins, and restored step by step: each step is paid from the stock and built by workers
    /// like a blueprint; when the last one is done it runs, makes knowledge and is the colony's goal (more Genetic Memory).
    /// </summary>
    public sealed partial class BuildingSystem
    {
        /// <summary>Finished resonators (rebuilt with the network).</summary>
        readonly List<int> _resonators = new();
        readonly Dictionary<int, Resonance> _resonance = new();
        readonly Queue<int2> _flood = new();
        readonly HashSet<int> _seen = new();
        int _engine = None;
        float _ruinKnowledge;

        public IReadOnlyList<int> Resonators => _resonators;

        /// <summary>The Ancient Engine (blueprint, ruin or running), or <see cref="None"/>.</summary>
        public int Engine => _engine >= 0 && _engine < All.Length && All[_engine].Alive && All[_engine].Kind == BuildingKind.Engine ? _engine : None;

        /// <summary>The Ancient Engine is fully restored and runs.</summary>
        public bool EngineRunning
        {
            get
            {
                int e = Engine;
                if (e < 0) return false;
                var b = All[e];
                return b.IsActive && b.Stage >= _content.Buildings[b.Def].StageCount;
            }
        }

        /// <summary>The cluster a resonator measured at its last check (default if it is not a finished resonator).</summary>
        public Resonance ResonanceOf(int id) => _resonance.TryGetValue(id, out var r) ? r : default;

        /// <summary>Knowledge per second the resonators and the running engine make now (before knowledge gain).</summary>
        public float RuinKnowledgeRate
        {
            get
            {
                float rate = 0f;
                foreach (int id in _resonators)
                    rate += All[id].Craft * _content.Buildings[All[id].Def].Def.knowledgePerSecond;
                if (EngineRunning) rate += _content.Buildings[All[Engine].Def].Def.knowledgePerSecond;
                return rate;
            }
        }

        /// <summary>A footprint cell or a cell right around it is solid conductive rock.</summary>
        public bool TouchesConductive(int2 origin, int2 size)
        {
            for (int y = -1; y <= size.y; y++)
            for (int x = -1; x <= size.x; x++)
            {
                var c = origin + new int2(x, y);
                if (IsConductiveRock(c)) return true;
            }
            return false;
        }

        bool IsConductiveRock(int2 c)
        {
            if (!_world.InBounds(c.x, c.y)) return false;
            var cell = _world[c.x, c.y];
            return !cell.IsOpen && _content.MaterialSim[cell.Material].IsConductive;
        }

        /// <summary>
        /// Follows the conductive cells touching the resonator's footprint (4-way, up to its cell limit) and sums their
        /// resonance.
        /// </summary>
        Resonance Measure(in Building b, BuildingDef def)
        {
            _flood.Clear();
            _seen.Clear();
            var r = new Resonance();
            int limit = math.max(1, def.maxClusterCells);
            for (int y = -1; y <= b.Size.y; y++)
            for (int x = -1; x <= b.Size.x; x++)
            {
                var c = b.Origin + new int2(x, y);
                if (!IsConductiveRock(c) || !_seen.Add(_world.Index(c.x, c.y))) continue;
                _flood.Enqueue(c);
            }
            var materials = _content.MaterialSim;
            while (_flood.Count > 0 && r.Cells < limit)
            {
                var c = _flood.Dequeue();
                var mat = materials[_world[c.x, c.y].Material];
                r.Cells++;
                if (mat.Resonance > 0f)
                {
                    r.Crystals++;
                    r.Total += mat.Resonance;
                }
                for (int d = 0; d < 4; d++)
                {
                    var n = c + Cardinals[d];
                    if (!IsConductiveRock(n) || !_seen.Add(_world.Index(n.x, n.y))) continue;
                    _flood.Enqueue(n);
                }
            }
            r.Power = math.saturate(r.Total / math.max(0.1f, def.fullResonance));
            return r;
        }

        /// <summary>Every climate check: each resonator measures its cluster; its power is kept in <see cref="Building.Craft"/>.</summary>
        void UpdateResonators()
        {
            _resonance.Clear();
            foreach (int id in _resonators)
            {
                ref var b = ref Get(id);
                var r = Measure(b, _content.Buildings[b.Def].Def);
                _resonance[id] = r;
                b.Craft = r.Power;
                b.Status = r.Power > 0f ? MachineStatus.Working : MachineStatus.Idle;
            }
        }

        /// <summary>Extra machine speed from the best resonator within its boost radius of <paramref name="machine"/>.</summary>
        float ResonatorBoost(in Building machine)
        {
            float boost = 0f;
            foreach (int id in _resonators)
            {
                var rb = All[id];
                if (rb.Craft <= 0f) continue;
                var def = _content.Buildings[rb.Def].Def;
                if (RectDistance(machine, rb) <= def.boostRadius) boost = math.max(boost, rb.Craft * def.machineBoost);
            }
            return boost;
        }

        /// <summary>Machines a resonating resonator speeds up now (hover text).</summary>
        public int MachinesNear(int id)
        {
            var rb = All[id];
            if (rb.Craft <= 0f) return 0;
            float radius = _content.Buildings[rb.Def].Def.boostRadius;
            int n = 0;
            foreach (var m in _machines)
                if (m != null && All[m.Building].IsActive && RectDistance(All[m.Building], rb) <= radius) n++;
            return n;
        }

        /// <summary>Knowledge from resonators and the running engine, paid out in whole points.</summary>
        void TickRuins(float dt)
        {
            if (_resonators.Count == 0 && _engine < 0) return;
            _ruinKnowledge += RuinKnowledgeRate * dt;
            if (_ruinKnowledge < 1f) return;
            float whole = math.floor(_ruinKnowledge);
            _ruinKnowledge -= whole;
            _colony.Inventory.AddKnowledge(whole * _colony.Mods.Mult(StatKind.KnowledgeGain), studied: true);
        }

        // ---------------------------------------------------------------- the Ancient Engine

        /// <summary>The next restoration step of the engine at <paramref name="cell"/> (index into its stages), or -1 when done / none.</summary>
        public int NextStage(int2 cell)
        {
            int id = At(cell);
            if (id < 0 || All[id].Kind != BuildingKind.Engine) return -1;
            var b = All[id];
            int stages = _content.Buildings[b.Def].StageCount;
            return b.Stage < stages ? b.Stage : -1;
        }

        public bool CanAffordStage(BuildingData data, int stage)
        {
            if (stage < 0 || stage >= data.StageCount) return false;
            var inv = _colony.Inventory;
            var items = data.StageCostItems[stage];
            var counts = data.StageCostCounts[stage];
            for (int i = 0; i < items.Length; i++)
                if (inv.Items[items[i]] < counts[i]) return false;
            return inv.Food + 1e-3f >= data.Def.stages[stage].foodCost;
        }

        /// <summary>
        /// Pays the engine's next restoration step and turns it into a blueprint for workers (they build the step like a new
        /// building). Refusals are published as <see cref="StageFailedEvent"/>.
        /// </summary>
        public bool BeginStage(int2 cell)
        {
            int id = At(cell);
            if (id < 0 || All[id].Kind != BuildingKind.Engine) return false;
            ref var b = ref Get(id);
            var data = _content.Buildings[b.Def];
            int stage = b.Stage;
            StageResult result = !b.IsActive ? StageResult.Busy
                : stage >= data.StageCount ? StageResult.Finished
                : !CanAffordStage(data, stage) ? StageResult.CantAfford
                : StageResult.Ok;
            if (result != StageResult.Ok)
            {
                _events.Publish(new StageFailedEvent(id, b.Def, result));
                return false;
            }
            var inv = _colony.Inventory;
            var items = data.StageCostItems[stage];
            var counts = data.StageCostCounts[stage];
            for (int i = 0; i < items.Length; i++) inv.Take(items[i], counts[i]);
            inv.Food = math.max(0f, inv.Food - data.Def.stages[stage].foodCost);
            inv.AcceptanceChanged = true;
            b.Restoring = true;
            b.State = BuildingState.Blueprint;
            b.WorkLeft = b.WorkTotal = math.max(0.05f, data.Def.stages[stage].buildWork);
            b.Status = MachineStatus.Idle;
            ForEachCell(b.Origin, b.Size, index =>
            {
                var c = _world.Cells[index];
                c.Flags |= CellFlags.Blueprint;
                _world.Cells[index] = c;
            });
            _networkDirty = true;
            BlueprintsChanged = true;
            RecountBlueprints();
            _events.Publish(new EngineStageEvent(id, stage, EngineStagePhase.Started, b.Origin));
            return true;
        }

        /// <summary>Removing an engine that is being restored cancels the step (refunded in full); a finished engine stays.</summary>
        bool CancelStage(int id)
        {
            ref var b = ref Get(id);
            if (!b.Restoring) return false;
            var data = _content.Buildings[b.Def];
            int stage = b.Stage;
            var inv = _colony.Inventory;
            _leftover.Clear();
            var items = data.StageCostItems[stage];
            var counts = data.StageCostCounts[stage];
            for (int i = 0; i < items.Length; i++)
            {
                int missing = inv.Add(items[i], counts[i], b.Origin);
                for (int n = 0; n < missing; n++) _leftover.Add(items[i]);
            }
            inv.Food = math.min(inv.FoodCapacity, inv.Food + data.Def.stages[stage].foodCost);
            inv.AcceptanceChanged = true;
            foreach (ushort item in _leftover) DropPile(b.Origin, item, 1);
            b.Restoring = false;
            b.State = BuildingState.Active;
            b.WorkLeft = 0f;
            ForEachCell(b.Origin, b.Size, index =>
            {
                var c = _world.Cells[index];
                c.Flags &= ~CellFlags.Blueprint;
                _world.Cells[index] = c;
            });
            _networkDirty = true;
            BlueprintsChanged = true;
            RecountBlueprints();
            _events.Publish(new EngineStageEvent(id, stage, EngineStagePhase.Cancelled, b.Origin));
            return true;
        }

        /// <summary>Called when an engine blueprint is finished: a restoration step completes (the last one starts it).</summary>
        void FinishStage(ref Building b, int id, bool notify)
        {
            if (!b.Restoring) return;
            b.Restoring = false;
            b.Stage++;
            if (!notify) return;
            bool running = b.Stage >= _content.Buildings[b.Def].StageCount;
            _events.Publish(new EngineStageEvent(id, b.Stage - 1, running ? EngineStagePhase.Awakened : EngineStagePhase.Finished, b.Origin));
        }
    }

    public enum StageResult : byte
    {
        Ok,
        /// <summary>A step is already being built (or the engine itself is still a blueprint).</summary>
        Busy,
        /// <summary>Every step is done: the engine runs.</summary>
        Finished,
        CantAfford,
    }
}
