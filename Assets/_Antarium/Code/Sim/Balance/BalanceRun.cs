using System;
using System.Collections.Generic;
using System.Text;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>One row of a headless balance run: the colony at a point in game time.</summary>
    public struct BalanceSample
    {
        public float Minutes;
        public int Ants;
        public float Food;
        public float Knowledge;
        public int Depth;
        public int Research;
        public int Contracts;
        public long CellsDug;
        public int Stored;
        /// <summary>Genetic Memory a Nuptial Flight with one princess would earn now.</summary>
        public int Memory;
    }

    /// <summary>
    /// A deliberately simple player for balance runs: it orders a 3-wide shaft down from the royal chamber with a side
    /// branch every <see cref="BranchEvery"/> cells, paints storage along dug branches, claims finished contracts and
    /// buys the cheapest research it can afford. It never builds, so the numbers show the hand-run early game.
    /// </summary>
    public sealed class Autopilot
    {
        public const int ShaftStep = 12;
        public const int BranchEvery = 36;
        public const int BranchLength = 22;
        const float DigSeconds = 30f, ResearchSeconds = 10f, StorageSeconds = 60f;

        readonly Simulation _sim;
        readonly int2 _top;
        int _shaft;
        int _branches;
        float _digTimer, _researchTimer, _storageTimer = StorageSeconds;

        public Autopilot(Simulation sim)
        {
            _sim = sim;
            _top = sim.Colony.QueenCell + new int2(2, -1);
        }

        public void Step(float dt)
        {
            for (int i = 0; i < _sim.Contracts.Active.Count; i++)
                if (_sim.Contracts.Active[i].IsDone) _sim.EnqueueClaim(i);

            _digTimer -= dt;
            if (_digTimer <= 0f)
            {
                _digTimer = DigSeconds;
                ExtendShaft();
            }
            _researchTimer -= dt;
            if (_researchTimer <= 0f)
            {
                _researchTimer = ResearchSeconds;
                BuyResearch();
            }
            _storageTimer -= dt;
            if (_storageTimer <= 0f)
            {
                _storageTimer = StorageSeconds;
                PaintStorage();
            }
        }

        void ExtendShaft()
        {
            var world = _sim.World;
            for (int k = 0; k < ShaftStep; k++)
            {
                int y = _top.y - _shaft;
                if (y <= 2) return;
                _sim.EnqueueDesignate(new float2(_top.x + 0.5f, y + 0.5f), 1.6f, true);
                _shaft++;
                if (_shaft % BranchEvery != 0) continue;
                int side = (_branches++ & 1) == 0 ? 1 : -1;
                for (int x = 2; x < BranchLength; x += 2)
                {
                    int bx = _top.x + side * x;
                    if (bx > 2 && bx < world.Width - 3) _sim.EnqueueDesignate(new float2(bx + 0.5f, y + 0.5f), 1.2f, true);
                }
            }
        }

        void PaintStorage()
        {
            // Storage on the floor of the dug branches (the brush only paints open cells).
            for (int b = 0; b < _branches; b++)
            {
                int y = _top.y - (b + 1) * BranchEvery;
                int side = (b & 1) == 0 ? 1 : -1;
                for (int x = 4; x < BranchLength; x += 3)
                    _sim.EnqueueStorageZone(new float2(_top.x + side * x + 0.5f, y + 0.5f), 1.2f, true);
            }
        }

        void BuyResearch()
        {
            var research = _sim.Research;
            int best = -1;
            float cheapest = float.MaxValue;
            for (int r = 0; r < _sim.Content.Research.Length; r++)
            {
                if (research.Check(r) != ResearchResult.Ok) continue;
                float cost = _sim.Content.Research[r].Def.knowledgeCost;
                if (cost < cheapest)
                {
                    cheapest = cost;
                    best = r;
                }
            }
            if (best >= 0) _sim.EnqueueResearch(best);
        }
    }

    /// <summary>Runs a colony headless with the <see cref="Autopilot"/> and samples it once per game minute.</summary>
    public static class BalanceRun
    {
        /// <param name="progress">Called with 0..1 now and then (editor progress bars); return true to stop early.</param>
        public static List<BalanceSample> Run(Simulation sim, float minutes, float sampleSeconds = 60f, Func<float, bool> progress = null)
        {
            var samples = new List<BalanceSample> { Sample(sim) };
            var pilot = new Autopilot(sim);
            float dt = sim.TickDuration;
            int ticks = (int)math.ceil(minutes * 60f / dt);
            int perSample = math.max(1, (int)math.round(sampleSeconds / dt));
            for (int t = 1; t <= ticks; t++)
            {
                pilot.Step(dt);
                sim.Tick();
                sim.Events.Flush();
                if (t % 20 == 0) sim.UpdateDerived();
                if (t % perSample == 0)
                {
                    samples.Add(Sample(sim));
                    if (progress != null && progress(t / (float)ticks)) break;
                }
            }
            return samples;
        }

        public static BalanceSample Sample(Simulation sim)
        {
            var inv = sim.Colony.Inventory;
            var flight = sim.EstimateFlight();
            return new BalanceSample
            {
                Minutes = (float)(sim.PlayTime / 60.0),
                Ants = sim.Ants.Count,
                Food = inv.Food,
                Knowledge = inv.Knowledge,
                Depth = sim.Colony.Stats.MaxDepth,
                Research = sim.Research.DoneCount,
                Contracts = sim.Colony.Stats.ContractsCompleted,
                CellsDug = sim.Colony.Stats.CellsDug,
                Stored = inv.Stored,
                Memory = (int)math.floor(flight.Base * math.max(0f, flight.Gain) + 1e-4f),
            };
        }

        public static string ToCsv(IReadOnlyList<BalanceSample> samples)
        {
            var sb = new StringBuilder("minutes,ants,food,knowledge,depth,research,contracts,cells_dug,stored,memory_one_princess\n");
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var s in samples)
                sb.Append(s.Minutes.ToString("0.0", inv)).Append(',').Append(s.Ants).Append(',').Append(s.Food.ToString("0", inv)).Append(',')
                    .Append(s.Knowledge.ToString("0", inv)).Append(',').Append(s.Depth).Append(',').Append(s.Research).Append(',')
                    .Append(s.Contracts).Append(',').Append(s.CellsDug).Append(',').Append(s.Stored).Append(',').Append(s.Memory).Append('\n');
            return sb.ToString();
        }
    }
}
