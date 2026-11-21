using System;
using Antarium.Content;
using Antarium.Core;

namespace Antarium.Sim
{
    public enum EvolutionResult : byte
    {
        Ok,
        MaxLevel,
        NeedsPrerequisites,
        CantAfford,
        Unknown,
    }

    /// <summary>
    /// The queen's evolution tree (10.4): per colony, bought level by level with rare finds from the stock and food.
    /// Stat effects add their value per level; unlocks (castes, abilities) apply from the level they name.
    /// </summary>
    public sealed class EvolutionSystem
    {
        readonly ContentDB _content;
        readonly Colony _colony;
        readonly EventBus _events;

        /// <summary>Per evolution (index into <see cref="ContentDB.Evolutions"/>): levels bought.</summary>
        public readonly int[] Levels;

        public EvolutionSystem(ContentDB content, Colony colony, EventBus events)
        {
            _content = content;
            _colony = colony;
            _events = events;
            Levels = new int[content.Evolutions.Length];
        }

        /// <summary>Levels bought in all evolutions together.</summary>
        public int TotalLevels
        {
            get
            {
                int n = 0;
                foreach (int l in Levels) n += l;
                return n;
            }
        }

        public bool IsMaxed(int index) => Levels[index] >= _content.Evolutions[index].MaxLevel;

        /// <summary>Every prerequisite has at least one level.</summary>
        public bool IsAvailable(int index)
        {
            foreach (int p in _content.Evolutions[index].Prerequisites)
                if (Levels[p] <= 0) return false;
            return true;
        }

        /// <summary>The stock holds what the next level costs.</summary>
        public bool CanAfford(int index)
        {
            if (IsMaxed(index)) return false;
            var data = _content.Evolutions[index];
            int level = Levels[index];
            var inv = _colony.Inventory;
            if (inv.Food + 1e-3f < data.FoodCost[level]) return false;
            var items = data.CostItems[level];
            var counts = data.CostCounts[level];
            for (int i = 0; i < items.Length; i++)
                if (inv.Items[items[i]] < counts[i]) return false;
            return true;
        }

        public EvolutionResult Check(int index)
        {
            if (index < 0 || index >= Levels.Length) return EvolutionResult.Unknown;
            if (IsMaxed(index)) return EvolutionResult.MaxLevel;
            if (!IsAvailable(index)) return EvolutionResult.NeedsPrerequisites;
            return CanAfford(index) ? EvolutionResult.Ok : EvolutionResult.CantAfford;
        }

        /// <summary>Pays for and applies the next level. Refusals are published as <see cref="EvolutionFailedEvent"/>.</summary>
        public EvolutionResult Buy(int index)
        {
            var result = Check(index);
            if (result != EvolutionResult.Ok)
            {
                _events.Publish(new EvolutionFailedEvent(index, result));
                return result;
            }
            var data = _content.Evolutions[index];
            int level = Levels[index];
            var inv = _colony.Inventory;
            inv.Food = Math.Max(0f, inv.Food - data.FoodCost[level]);
            var items = data.CostItems[level];
            var counts = data.CostCounts[level];
            for (int i = 0; i < items.Length; i++) inv.Take(items[i], counts[i]);
            ApplyLevel(index, level + 1);
            _events.Publish(new EvolutionBoughtEvent(index, level + 1));
            return EvolutionResult.Ok;
        }

        /// <summary>Loading a save: applies levels up to <paramref name="level"/> without paying.</summary>
        internal void SetLevel(int index, int level)
        {
            level = Math.Min(level, _content.Evolutions[index].MaxLevel);
            while (Levels[index] < level) ApplyLevel(index, Levels[index] + 1);
        }

        void ApplyLevel(int index, int level)
        {
            Levels[index] = level;
            var data = _content.Evolutions[index];
            for (int k = 0; k < data.Effects.Length; k++)
            {
                var e = data.Effects[k];
                switch (e.kind)
                {
                    case EvolutionEffectKind.Stat:
                        _colony.Mods.Add(e.stat, e.value);
                        break;
                    case EvolutionEffectKind.UnlockCaste:
                        if (level == Math.Max(1, e.level)) _colony.UnlockCaste(e.caste);
                        break;
                    case EvolutionEffectKind.UnlockAbility:
                        if (level == Math.Max(1, e.level) && data.Targets[k] >= 0) _colony.AbilityUnlocked[data.Targets[k]] = true;
                        break;
                }
            }
            _colony.Inventory.RefreshCapacity();
        }

        /// <summary>The first level of <paramref name="index"/> that unlocks something, or 0.</summary>
        public int UnlockLevel(int index, AntCaste caste)
        {
            foreach (var e in _content.Evolutions[index].Effects)
                if (e.kind == EvolutionEffectKind.UnlockCaste && e.caste == caste) return Math.Max(1, e.level);
            return 0;
        }
    }
}
