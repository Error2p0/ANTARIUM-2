using System;
using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>One contract on the board. Progress is measured from <see cref="ColonyStats"/> since it started.</summary>
    public struct ActiveContract
    {
        /// <summary>Index into <see cref="ContentDB.Contracts"/>.</summary>
        public int Def;
        public long Target;
        /// <summary>Stat value when the contract started (0 for ReachDepth, which is absolute).</summary>
        public long Baseline;
        public long Progress;
        /// <summary>Rewards are multiplied by this (grows with the amount of repeated contracts).</summary>
        public float RewardScale;

        public bool IsDone => Progress >= Target;
    }

    /// <summary>
    /// The contract board: a few goals always on screen. Intro contracts come first, one at a time and in
    /// order; the other slots draw repeatable contracts at random (by weight), each bigger than the last.
    /// Finished contracts wait for the player to claim the reward.
    /// </summary>
    public sealed class ContractSystem
    {
        readonly ContentDB _content;
        readonly ProgressionConfig _config;
        readonly Simulation _sim;

        public readonly List<ActiveContract> Active = new();
        /// <summary>Per contract definition: times completed.</summary>
        public readonly int[] Completed;
        internal Unity.Mathematics.Random Rng;

        public ContractSystem(ContentDB content, ProgressionConfig config, Simulation sim, uint seed)
        {
            _content = content;
            _config = config;
            _sim = sim;
            Completed = new int[content.Contracts.Length];
            Rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, 0xC0D7u)) | 1u);
        }

        public int Slots => math.max(1, _config.contractSlots + (int)_sim.Colony.Mods.Flat(StatKind.ContractSlots));

        /// <summary>Updates progress and fills free slots. Runs every tick (a handful of contracts).</summary>
        internal void Tick()
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var c = Active[i];
                c.Progress = math.max(0, Measure(_content.Contracts[c.Def]) - c.Baseline);
                Active[i] = c;
            }
            Fill(-1);
        }

        /// <summary>Claims a finished contract: grants its rewards and draws a new one into the same slot.</summary>
        public bool Claim(int slot)
        {
            if (slot < 0 || slot >= Active.Count || !Active[slot].IsDone) return false;
            var c = Active[slot];
            var data = _content.Contracts[c.Def];
            var def = data.Def;
            _sim.GrantReward(data.RewardItems, data.RewardCounts, c.RewardScale, def.rewardFood * c.RewardScale, def.rewardKnowledge * c.RewardScale);
            Completed[c.Def]++;
            _sim.Colony.Stats.ContractsCompleted++;
            Active.RemoveAt(slot);
            _sim.Events.Publish(new ContractClaimedEvent(c.Def));

            int next = Pick(exclude: c.Def);
            if (next >= 0 && Active.Count < Slots) Active.Insert(slot, Create(next));
            Fill(-1);
            return true;
        }

        void Fill(int exclude)
        {
            while (Active.Count < Slots)
            {
                int d = Pick(exclude);
                if (d < 0) return;
                Active.Add(Create(d));
            }
        }

        ActiveContract Create(int index)
        {
            var data = _content.Contracts[index];
            var def = data.Def;
            if (def.kind == ContractKind.ReachDepth)
                return new ActiveContract { Def = index, Target = def.amount, Baseline = 0, RewardScale = 1f };
            float scale = def.repeatable ? math.pow(1f + def.growth, Completed[index]) : 1f;
            long target = math.max(1, (long)math.round(def.amount * scale));
            long baseline = Measure(data);
            return new ActiveContract { Def = index, Target = target, Baseline = baseline, RewardScale = target / (float)def.amount };
        }

        long Measure(ContractData data)
        {
            var stats = _sim.Colony.Stats;
            switch (data.Def.kind)
            {
                case ContractKind.Deliver: return stats.Delivered[data.Item];
                case ContractKind.Craft: return stats.Crafted[data.Item];
                case ContractKind.ReachDepth:
                case ContractKind.DigDeeper: return stats.MaxDepth;
                case ContractKind.HatchAnts: return stats.AntsHatched;
                case ContractKind.Build: return stats.Built[data.Building];
                case ContractKind.DigCells: return stats.CellsDug;
                case ContractKind.KillEnemies: return stats.EnemiesKilled;
                default: return 0;
            }
        }

        bool IsActive(int index)
        {
            foreach (var c in Active)
                if (c.Def == index) return true;
            return false;
        }

        bool IsAvailable(int index)
        {
            var data = _content.Contracts[index];
            var def = data.Def;
            if (!def.repeatable && Completed[index] > 0) return false;
            if (data.Requires >= 0 && !_sim.Research.Done[data.Requires]) return false;
            if (def.minDepth > _sim.Colony.Stats.MaxDepth) return false;
            if (data.Building >= 0 && !_sim.Colony.BuildingUnlocked[data.Building]) return false;
            return !IsActive(index);
        }

        /// <summary>Next contract to offer, or -1: the next intro contract if none is on the board, else a random one.</summary>
        int Pick(int exclude)
        {
            var contracts = _content.Contracts;
            bool introActive = false;
            foreach (var c in Active)
                if (contracts[c.Def].Def.introOrder > 0) introActive = true;
            if (!introActive)
            {
                // Contracts are sorted by intro order (0 first), so the first available intro one is next.
                for (int i = 0; i < contracts.Length; i++)
                    if (contracts[i].Def.introOrder > 0 && Completed[i] == 0 && IsAvailable(i)) return i;
            }

            float total = 0f, fallbackTotal = 0f;
            for (int i = 0; i < contracts.Length; i++)
            {
                if (contracts[i].Def.introOrder > 0 || !IsAvailable(i)) continue;
                fallbackTotal += contracts[i].Def.weight;
                if (i != exclude) total += contracts[i].Def.weight;
            }
            bool useExcluded = total <= 0f;
            if (useExcluded) total = fallbackTotal; // the only option left is the one just finished
            if (total <= 0f) return -1;
            float roll = Rng.NextFloat(total);
            int last = -1;
            for (int i = 0; i < contracts.Length; i++)
            {
                if (contracts[i].Def.introOrder > 0 || !IsAvailable(i) || (i == exclude && !useExcluded)) continue;
                last = i;
                roll -= contracts[i].Def.weight;
                if (roll < 0f) return i;
            }
            return last;
        }
    }
}
