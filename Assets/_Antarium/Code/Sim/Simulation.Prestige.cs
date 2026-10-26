using System;
using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>What a Nuptial Flight would earn right now, part by part.</summary>
    public struct FlightEstimate
    {
        public float DepthScore, KnowledgeScore, ResearchScore, ContractScore;
        /// <summary>(score / divisor) ^ exponent, before princesses and gain.</summary>
        public float Base;
        public int Princesses, MaxPrincesses, MinPrincesses;
        /// <summary>1 + bonus per princess after the first (counted up to <see cref="MaxPrincesses"/>); 0 without princesses.</summary>
        public float PrincessFactor;
        /// <summary>Genetic Memory gain multiplier (traits, world modifiers).</summary>
        public float Gain;
        /// <summary>Genetic Memory the flight earns.</summary>
        public int Memory;

        public float Score => DepthScore + KnowledgeScore + ResearchScore + ContractScore;
        public bool EnoughPrincesses => Princesses >= MinPrincesses;
        public bool CanFly => EnoughPrincesses && Memory >= 1;
    }

    /// <summary>
    /// Prestige: the legacy (genetic traits) this colony inherited, its world modifiers, buying traits, princesses and
    /// the Nuptial Flight that hands the legacy on to the next colony.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Genetic Memory and traits; saved with the colony and carried into the next one.</summary>
        public Legacy Legacy { get; private set; }
        /// <summary>World modifiers of this colony (indices into <see cref="ContentDB.Modifiers"/>).</summary>
        public IReadOnlyList<int> WorldModifiers => _worldModifiers;
        /// <summary>How this colony's world modifiers changed world generation.</summary>
        public WorldTweaks Tweaks { get; private set; }
        int[] _worldModifiers = Array.Empty<int>();
        bool _veinInstinct;

        /// <summary>Biome index at a depth in this world (biome thickness follows the world modifiers).</summary>
        public int BiomeAtDepth(int depth) => Content.BiomeAtDepth(depth, Tweaks.BiomeScale);

        /// <summary>Buys a level of genetic trait <paramref name="trait"/> (index into <see cref="ContentDB.Genetics"/>).</summary>
        public void EnqueueGenetic(int trait) => _pending.Add(new Command(CommandKind.Genetic, trait, default, default, 0));

        void InitPrestige(Legacy legacy, IReadOnlyList<int> modifiers)
        {
            Legacy = legacy ?? new Legacy(Content);
            var list = new List<int>();
            if (modifiers != null)
                foreach (int m in modifiers)
                    if (m >= 0 && m < Content.Modifiers.Length && !list.Contains(m)) list.Add(m);
            _worldModifiers = list.ToArray();
            Tweaks = WorldTweaks.From(Content, _worldModifiers);
        }

        /// <summary>Stats of every bought trait level and of the world modifiers; princess limit; Vein Instinct.</summary>
        void ApplyLegacyEffects()
        {
            var mods = Colony.Mods;
            for (int g = 0; g < Legacy.Levels.Length; g++)
            {
                int level = Legacy.Levels[g];
                if (level == 0) continue;
                foreach (var e in Content.Genetics[g].Effects)
                    if (e.kind == GeneticEffectKind.Stat) mods.Add(e.stat, e.value * level);
            }
            foreach (int m in _worldModifiers)
                foreach (var s in Content.Modifiers[m].stats)
                    mods.Add(s.stat, s.value);
            RefreshLegacyFlags();
            Colony.Inventory.RefreshCapacity();
        }

        void RefreshLegacyFlags()
        {
            _veinInstinct = Legacy.Has(GeneticEffectKind.VeinInstinct);
            Colony.PrincessCap = Legacy.MaxPrincesses(Config.prestige);
        }

        /// <summary>A new colony: inherited research, starting food, knowledge and items, the revealed area around the nest.</summary>
        void ApplyStartBonuses()
        {
            for (int g = 0; g < Legacy.Levels.Length; g++)
            {
                if (Legacy.Levels[g] == 0) continue;
                var data = Content.Genetics[g];
                for (int k = 0; k < data.Effects.Length; k++)
                    if (data.Effects[k].kind == GeneticEffectKind.InheritResearch) Research.Apply(data.Targets[k]);
            }

            var inv = Colony.Inventory;
            inv.RefreshCapacity();
            inv.Food = math.min(inv.FoodCapacity, inv.Food + Legacy.Total(GeneticEffectKind.StartFood));
            inv.Knowledge += Legacy.Total(GeneticEffectKind.StartKnowledge); // not "earned": it does not raise the next flight
            for (int g = 0; g < Legacy.Levels.Length; g++)
            {
                int level = Legacy.Levels[g];
                if (level == 0) continue;
                var data = Content.Genetics[g];
                for (int k = 0; k < data.Effects.Length; k++)
                    if (data.Effects[k].kind == GeneticEffectKind.StartItems)
                        inv.Add((ushort)data.Targets[k], data.Effects[k].count * level, Colony.QueenCell);
            }

            int reveal = (int)Legacy.Total(GeneticEffectKind.RevealRadius);
            if (reveal > 0) Reveal.Around(World, Colony.QueenCell, reveal);
        }

        void BuyGenetic(int trait)
        {
            var result = Legacy.Buy(trait);
            if (result != GeneticResult.Ok)
            {
                Events.Publish(new GeneticFailedEvent(trait, result));
                return;
            }
            // Effects that work during a colony apply now; starting bonuses wait for the next colony.
            var data = Content.Genetics[trait];
            for (int k = 0; k < data.Effects.Length; k++)
            {
                var e = data.Effects[k];
                if (e.kind == GeneticEffectKind.Stat) Colony.Mods.Add(e.stat, e.value);
                else if (e.kind == GeneticEffectKind.InheritResearch && !Research.Done[data.Targets[k]])
                {
                    Research.Apply(data.Targets[k]);
                    Events.Publish(new ResearchCompletedEvent(data.Targets[k]));
                }
            }
            RefreshLegacyFlags();
            Colony.Inventory.RefreshCapacity();
            Colony.Inventory.AcceptanceChanged = true;
            Events.Publish(new GeneticBoughtEvent(trait, Legacy.Levels[trait]));
        }

        /// <summary>Genetic Memory a Nuptial Flight would earn now, with its parts.</summary>
        public FlightEstimate EstimateFlight()
        {
            var cfg = Config.prestige;
            var stats = Colony.Stats;
            var e = new FlightEstimate
            {
                DepthScore = stats.MaxDepth * cfg.depthScore,
                KnowledgeScore = (float)stats.KnowledgeEarned * cfg.knowledgeScore,
                ResearchScore = Research.DoneCount * cfg.researchScore,
                ContractScore = stats.ContractsCompleted * cfg.contractScore,
                Princesses = Colony.Princesses,
                MaxPrincesses = Colony.PrincessCap,
                MinPrincesses = math.max(1, cfg.minPrincesses),
                Gain = Colony.Mods.Mult(StatKind.GeneticGain),
            };
            e.Base = math.pow(math.max(0f, e.Score) / math.max(0.01f, cfg.scoreDivisor), math.clamp(cfg.scoreExponent, 0.1f, 1f));
            int counted = math.min(e.Princesses, e.MaxPrincesses);
            e.PrincessFactor = counted <= 0 ? 0f : 1f + cfg.princessBonus * (counted - 1);
            e.Memory = e.EnoughPrincesses ? (int)math.floor(e.Base * e.PrincessFactor * math.max(0f, e.Gain) + 1e-4f) : 0;
            return e;
        }

        /// <summary>
        /// The princesses fly: returns the legacy for the next colony (this one's plus the Genetic Memory earned, one
        /// more flight, records). This colony is left as it is. False if the flight is not possible yet.
        /// </summary>
        public bool TryNuptialFlight(out Legacy next, out int earned)
        {
            var estimate = EstimateFlight();
            if (!estimate.CanFly)
            {
                next = null;
                earned = 0;
                return false;
            }
            earned = estimate.Memory;
            next = Legacy.Clone();
            next.Memory += earned;
            next.MemoryEarned += earned;
            next.Flights++;
            next.BestDepth = math.max(next.BestDepth, Colony.Stats.MaxDepth);
            next.BestFlight = math.max(next.BestFlight, earned);
            return true;
        }

        /// <summary>Loading: the saved legacy and world modifiers replace the empty ones and their effects apply.</summary>
        internal void RestoreLegacy(Legacy legacy, IReadOnlyList<int> modifiers)
        {
            InitPrestige(legacy, modifiers);
            ApplyLegacyEffects();
        }

        /// <summary>Debug: adds princesses (up to the limit) and unspent Genetic Memory.</summary>
        public void DebugGivePrestige(int princesses, int memory)
        {
            Colony.Princesses = math.min(Colony.PrincessCap, Colony.Princesses + princesses);
            Legacy.Memory += memory;
        }

        /// <summary>Vein Instinct: solid ore around a cell that was just dug out is ordered for mining.</summary>
        void FollowVein(int2 c)
        {
            int r = math.max(1, Config.prestige.veinRadius);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r * r) continue;
                int x = c.x + dx, y = c.y + dy;
                if (!World.InBounds(x, y)) continue;
                int index = World.Index(x, y);
                var cell = World.Cells[index];
                if (cell.IsEmpty || !cell.IsRevealed || (cell.Flags & CellFlags.DigDesignated) != 0) continue;
                if (Content.MaterialSim[cell.Material].IsIndestructible || !WorldGenerator.IsOre(Content, cell.Material)) continue;
                cell.Flags |= CellFlags.DigDesignated;
                World.Cells[index] = cell;
                World.MarkChanged(x, y, ChunkFlags.TerrainUpload);
                Colony.Designated.Add(index);
                _designationsChanged = true;
            }
        }
    }
}
