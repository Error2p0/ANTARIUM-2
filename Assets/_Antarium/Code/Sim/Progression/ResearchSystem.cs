using System;
using Antarium.Content;
using Antarium.Core;

namespace Antarium.Sim
{
    public enum ResearchResult : byte
    {
        Ok,
        AlreadyDone,
        NeedsPrerequisites,
        CantAfford,
        Unknown,
    }

    /// <summary>
    /// The research tree. A node is paid once (knowledge, items from the stock, food) and then applies its
    /// effects for good: stat modifiers, unlocked buildings and abilities.
    /// </summary>
    public sealed class ResearchSystem
    {
        readonly ContentDB _content;
        readonly Colony _colony;
        readonly EventBus _events;

        /// <summary>Per research node (index into <see cref="ContentDB.Research"/>).</summary>
        public readonly bool[] Done;

        public ResearchSystem(ContentDB content, Colony colony, EventBus events)
        {
            _content = content;
            _colony = colony;
            _events = events;
            Done = new bool[content.Research.Length];
        }

        public int DoneCount
        {
            get
            {
                int n = 0;
                foreach (bool d in Done) if (d) n++;
                return n;
            }
        }

        public ResearchResult Check(int index)
        {
            if (index < 0 || index >= Done.Length) return ResearchResult.Unknown;
            if (Done[index]) return ResearchResult.AlreadyDone;
            foreach (int p in _content.Research[index].Prerequisites)
                if (!Done[p]) return ResearchResult.NeedsPrerequisites;
            return CanAfford(index) ? ResearchResult.Ok : ResearchResult.CantAfford;
        }

        /// <summary>True when every prerequisite is done (the node can be bought once affordable).</summary>
        public bool IsAvailable(int index)
        {
            foreach (int p in _content.Research[index].Prerequisites)
                if (!Done[p]) return false;
            return true;
        }

        public bool CanAfford(int index)
        {
            var data = _content.Research[index];
            var inv = _colony.Inventory;
            if (inv.Knowledge + 1e-3f < data.Def.knowledgeCost) return false;
            if (inv.Food + 1e-3f < data.Def.foodCost) return false;
            for (int i = 0; i < data.CostItems.Length; i++)
                if (inv.Items[data.CostItems[i]] < data.CostCounts[i]) return false;
            return true;
        }

        /// <summary>Pays for and completes a node. Refusals are published as <see cref="ResearchFailedEvent"/>.</summary>
        public ResearchResult Research(int index)
        {
            var result = Check(index);
            if (result != ResearchResult.Ok)
            {
                _events.Publish(new ResearchFailedEvent(index, result));
                return result;
            }
            var data = _content.Research[index];
            var inv = _colony.Inventory;
            inv.Knowledge = Math.Max(0f, inv.Knowledge - data.Def.knowledgeCost);
            inv.Food = Math.Max(0f, inv.Food - data.Def.foodCost);
            for (int i = 0; i < data.CostItems.Length; i++)
                inv.Take(data.CostItems[i], data.CostCounts[i]);
            Apply(index);
            _events.Publish(new ResearchCompletedEvent(index));
            return ResearchResult.Ok;
        }

        /// <summary>Marks a node done and applies its effects without paying (also used when loading a save).</summary>
        internal void Apply(int index)
        {
            if (Done[index]) return;
            Done[index] = true;
            var data = _content.Research[index];
            foreach (var e in data.Stats) _colony.Mods.Add(e.stat, e.value);
            foreach (int b in data.UnlockBuildings) _colony.BuildingUnlocked[b] = true;
            foreach (int a in data.UnlockAbilities) _colony.AbilityUnlocked[a] = true;
            foreach (var caste in data.UnlockCastes) _colony.UnlockCaste(caste);
            _colony.Inventory.RefreshCapacity();
        }
    }
}
