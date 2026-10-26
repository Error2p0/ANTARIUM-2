using System;
using Antarium.Content;

namespace Antarium.Sim
{
    public enum GeneticResult : byte
    {
        Ok,
        MaxLevel,
        NeedsPrerequisites,
        CantAfford,
        Unknown,
    }

    /// <summary>
    /// What survives a Nuptial Flight: unspent Genetic Memory, the levels of every genetic trait, the number of
    /// flights and a few records. Every colony carries its legacy (and saves it); a flight hands a copy with the new
    /// memory to the next colony. Plain bookkeeping: the simulation applies the traits' effects.
    /// </summary>
    public sealed class Legacy
    {
        readonly ContentDB _content;

        /// <summary>Genetic Memory not spent yet.</summary>
        public int Memory;
        /// <summary>Genetic Memory earned by all flights together.</summary>
        public long MemoryEarned;
        public int Flights;
        /// <summary>Per genetic trait (index into <see cref="ContentDB.Genetics"/>): levels bought.</summary>
        public readonly int[] Levels;
        /// <summary>Deepest dig of any colony that flew.</summary>
        public int BestDepth;
        /// <summary>Most Genetic Memory from one flight.</summary>
        public int BestFlight;

        public Legacy(ContentDB content)
        {
            _content = content;
            Levels = new int[content.Genetics.Length];
        }

        public Legacy Clone()
        {
            var copy = new Legacy(_content)
            {
                Memory = Memory,
                MemoryEarned = MemoryEarned,
                Flights = Flights,
                BestDepth = BestDepth,
                BestFlight = BestFlight,
            };
            Array.Copy(Levels, copy.Levels, Levels.Length);
            return copy;
        }

        /// <summary>True once the player has ever had Genetic Memory (the genetic tree is shown).</summary>
        public bool Awakened => MemoryEarned > 0 || Memory > 0;

        /// <summary>Sum over all bought levels of effects of <paramref name="kind"/> (value per level).</summary>
        public float Total(GeneticEffectKind kind)
        {
            float total = 0f;
            for (int g = 0; g < Levels.Length; g++)
            {
                if (Levels[g] == 0) continue;
                foreach (var e in _content.Genetics[g].Effects)
                    if (e.kind == kind) total += e.value * Levels[g];
            }
            return total;
        }

        public bool Has(GeneticEffectKind kind)
        {
            for (int g = 0; g < Levels.Length; g++)
            {
                if (Levels[g] == 0) continue;
                foreach (var e in _content.Genetics[g].Effects)
                    if (e.kind == kind) return true;
            }
            return false;
        }

        /// <summary>A world modifier can be chosen: it starts unlocked or a bought trait unlocks it.</summary>
        public bool ModifierUnlocked(int modifier)
        {
            if (modifier < 0 || modifier >= _content.Modifiers.Length) return false;
            if (!_content.ModifierStartsLocked[modifier]) return true;
            for (int g = 0; g < Levels.Length; g++)
            {
                if (Levels[g] == 0) continue;
                var data = _content.Genetics[g];
                for (int k = 0; k < data.Effects.Length; k++)
                    if (data.Effects[k].kind == GeneticEffectKind.UnlockModifier && data.Targets[k] == modifier) return true;
            }
            return false;
        }

        public int ModifierSlots(PrestigeConfig config) => Math.Max(0, config.modifierSlots + (int)Total(GeneticEffectKind.ModifierSlots));
        public int MaxPrincesses(PrestigeConfig config) => Math.Max(1, config.maxPrincesses + (int)Total(GeneticEffectKind.MaxPrincesses));

        /// <summary>Genetic Memory the next level of <paramref name="trait"/> costs.</summary>
        public int Cost(int trait) => _content.Genetics[trait].Cost(Levels[trait]);

        public bool IsAvailable(int trait)
        {
            foreach (int p in _content.Genetics[trait].Prerequisites)
                if (Levels[p] == 0) return false;
            return true;
        }

        public GeneticResult Check(int trait)
        {
            if (trait < 0 || trait >= Levels.Length) return GeneticResult.Unknown;
            if (Levels[trait] >= _content.Genetics[trait].MaxLevel) return GeneticResult.MaxLevel;
            if (!IsAvailable(trait)) return GeneticResult.NeedsPrerequisites;
            return Memory >= Cost(trait) ? GeneticResult.Ok : GeneticResult.CantAfford;
        }

        /// <summary>Pays for and adds one level (no effects applied here).</summary>
        public GeneticResult Buy(int trait)
        {
            var result = Check(trait);
            if (result != GeneticResult.Ok) return result;
            Memory -= Cost(trait);
            Levels[trait]++;
            return GeneticResult.Ok;
        }
    }
}
