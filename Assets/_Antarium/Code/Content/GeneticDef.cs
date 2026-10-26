using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    public enum GeneticEffectKind : byte
    {
        /// <summary>Adds <see cref="GeneticEffect.value"/> per level to <see cref="GeneticEffect.stat"/>, right away and in every colony after.</summary>
        Stat,
        /// <summary>Extra workers per level when a colony is founded.</summary>
        StartWorkers,
        /// <summary>Extra food per level when a colony is founded.</summary>
        StartFood,
        /// <summary>Knowledge per level when a colony is founded.</summary>
        StartKnowledge,
        /// <summary><see cref="GeneticEffect.count"/> of <see cref="GeneticEffect.item"/> per level when a colony is founded.</summary>
        StartItems,
        /// <summary>The research node is done in every colony (and right away when bought).</summary>
        InheritResearch,
        /// <summary>Extra world modifier slots per level.</summary>
        ModifierSlots,
        /// <summary>The world modifier can be chosen for the next colonies.</summary>
        UnlockModifier,
        /// <summary>Extra princesses per level that count toward a Nuptial Flight.</summary>
        MaxPrincesses,
        /// <summary>Cells around the nest per level that new colonies start with revealed.</summary>
        RevealRadius,
        /// <summary>Ore next to a cell that was dug out is ordered for mining automatically.</summary>
        VeinInstinct,
    }

    [Serializable]
    public struct GeneticEffect
    {
        public GeneticEffectKind kind;
        public StatKind stat;
        [Tooltip("Per level: stat amount, workers, food, knowledge, slots, princesses or reveal cells.")]
        public float value;
        public ItemDef item;
        [Min(0)] public int count;
        public ResearchDef research;
        public WorldModifierDef modifier;

        public static GeneticEffect Add(StatKind stat, float value) => new() { kind = GeneticEffectKind.Stat, stat = stat, value = value };
        public static GeneticEffect Of(GeneticEffectKind kind, float value = 1f) => new() { kind = kind, value = value };
        public static GeneticEffect Items(ItemDef item, int count) => new() { kind = GeneticEffectKind.StartItems, item = item, count = count };
        public static GeneticEffect Inherit(ResearchDef research) => new() { kind = GeneticEffectKind.InheritResearch, research = research };
        public static GeneticEffect Unlock(WorldModifierDef modifier) => new() { kind = GeneticEffectKind.UnlockModifier, modifier = modifier };
    }

    /// <summary>
    /// A node of the Genetic Memory tree: bought with Genetic Memory from Nuptial Flights and kept forever, across
    /// colonies. Nodes can have several levels; each level costs <see cref="cost"/> plus <see cref="costPerLevel"/>
    /// for every level already bought.
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Genetic Trait", fileName = "Genetic")]
    public sealed class GeneticDef : ContentDef
    {
        [TextArea] public string description;
        [Tooltip("Column (tier) and row in the genetic tree panel.")]
        public Vector2Int treePosition;

        [Header("Cost (Genetic Memory)")]
        [Min(0)] public int cost = 1;
        [Min(0)] public int costPerLevel = 1;
        [Min(1)] public int maxLevel = 1;

        [Header("Tree")]
        [Tooltip("Each of these needs at least one level first.")]
        public List<GeneticDef> prerequisites = new();
        public List<GeneticEffect> effects = new();
    }
}
