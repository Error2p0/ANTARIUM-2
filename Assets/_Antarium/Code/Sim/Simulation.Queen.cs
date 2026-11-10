using Antarium.Content;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Colony depth (10.4): the queen's evolutions, job priorities, ant experience and traits, heat, and the Quickening.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Lowest and highest job priority; 3 is normal.</summary>
        public const int MinPriority = 1, MaxPriority = 5, NormalPriority = 3;

        /// <summary>Buys the next level of queen evolution <paramref name="evolution"/> (index into <see cref="ContentDB.Evolutions"/>).</summary>
        public void EnqueueEvolution(int evolution) => _pending.Add(new Command(CommandKind.Evolve, evolution, default, default, 0));

        /// <summary>Sets the priority (1..5) of <paramref name="job"/> for the whole colony.</summary>
        public void EnqueuePriority(JobKind job, int priority) =>
            _pending.Add(new Command(CommandKind.Priority, priority, default, default, (ushort)job));

        void SetPriority(JobKind job, int priority)
        {
            if ((int)job >= Jobs.Count) return;
            Colony.JobPriority[(int)job] = (byte)math.clamp(priority, MinPriority, MaxPriority);
        }

        /// <summary>Distance multiplier of a priority (1..5) from the config table.</summary>
        float PriorityWeight(int priority)
        {
            var table = Config.growth.priorityWeights;
            int i = math.clamp(priority, MinPriority, MaxPriority) - 1;
            return table != null && i < table.Length ? math.max(0.01f, table[i]) : 1f;
        }

        JobWeights PriorityWeights()
        {
            var p = Colony.JobPriority;
            return new JobWeights
            {
                Dig = PriorityWeight(p[(int)JobKind.Dig]),
                Haul = PriorityWeight(p[(int)JobKind.Haul]),
                Build = PriorityWeight(p[(int)JobKind.Build]),
                Supply = PriorityWeight(p[(int)JobKind.Supply]),
                Tend = PriorityWeight(p[(int)JobKind.Tend]),
                Nurse = PriorityWeight(p[(int)JobKind.Nurse]),
            };
        }

        // ---------------------------------------------------------------- experience

        /// <summary>Level 1..5 of an ant in its skill.</summary>
        public int AntLevel(in Ant a) => _growth.Level(a.Xp);

        /// <summary>
        /// Work in <paramref name="job"/>: the ant's first job becomes its skill; only work in its skill gives experience.
        /// A new level is published as <see cref="AntLevelUpEvent"/>.
        /// </summary>
        void GainXp(ref Ant ant, JobKind job, float xp)
        {
            if (xp <= 0f || ant.Possessed) return;
            if (ant.Skill == Jobs.NoSkill) ant.Skill = (byte)job;
            if (ant.Skill != (byte)job) return;
            int before = _growth.Level(ant.Xp);
            ant.Xp += xp;
            int after = _growth.Level(ant.Xp);
            if (after > before) Events.Publish(new AntLevelUpEvent(ant.Cell, job, after));
        }

        /// <summary>Lucky ants sometimes get one more item out of a cell they break.</summary>
        void LuckyDrop(in Ant ant, int2 cell)
        {
            if (ant.Trait != AntTrait.Lucky || !World.InBounds(cell.x, cell.y)) return;
            var mat = Content.MaterialSim[World.Cells[World.Index(cell.x, cell.y)].Material];
            if (mat.DropItem == ContentDB.NoItem || _rng.NextFloat() >= Config.growth.luckyChance) return;
            DropItem(cell, mat.DropItem, 1);
        }

        float TraitChance => Config.growth.traitChance;

        // ---------------------------------------------------------------- heat

        /// <summary>Per world row: the heat of the biome there (rows are mapped to depth from the middle of the world).</summary>
        void BuildHeatRows()
        {
            if (Ants.HeatByRow.IsCreated) Ants.HeatByRow.Dispose();
            var rows = new NativeArray<float>(World.Height, Allocator.Persistent);
            var biomes = Content.WorldGen.biomes;
            int surface = World.SurfaceY[World.Width / 2];
            for (int y = 0; y < World.Height; y++)
            {
                int depth = surface - y;
                if (depth < 0) continue;
                int b = BiomeAtDepth(depth);
                var def = b >= 0 && b < biomes.Count ? biomes[b] : null;
                rows[y] = def != null ? math.max(0f, def.heat) : 0f;
            }
            Ants.HeatByRow = rows;
        }

        // ---------------------------------------------------------------- Quickening

        /// <summary>Every egg hatches now: by the queen, in nurseries and carried by nurses. Returns how many.</summary>
        int HatchAllEggs()
        {
            int count = Colony.Eggs.Count;
            foreach (var egg in Colony.Eggs) Hatch(egg.Cell, egg.Caste);
            Colony.Eggs.Clear();
            var ants = Ants.Ants;
            int n = ants.Length;
            for (int i = 0; i < n; i++)
            {
                var a = ants[i];
                if (!a.CarryingEgg) continue;
                a.CarryingEgg = false;
                if (a.State == AntState.CarryEgg) a.State = AntState.Idle;
                ants[i] = a;
                Hatch(a.Cell, a.EggCaste);
                count++;
            }
            if (count > 0) _broodChanged = true;
            return count;
        }
    }
}
