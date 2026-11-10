using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    public enum EvolutionEffectKind : byte
    {
        /// <summary>Adds <see cref="EvolutionEffect.value"/> per level bought to <see cref="EvolutionEffect.stat"/>.</summary>
        Stat,
        /// <summary>From level <see cref="EvolutionEffect.level"/> on, the queen may lay <see cref="EvolutionEffect.caste"/> (no research needed).</summary>
        UnlockCaste,
        /// <summary>From level <see cref="EvolutionEffect.level"/> on, <see cref="EvolutionEffect.ability"/> can be used.</summary>
        UnlockAbility,
    }

    [Serializable]
    public struct EvolutionEffect
    {
        public EvolutionEffectKind kind;
        public StatKind stat;
        [Tooltip("Stat: amount per level.")]
        public float value;
        public AntCaste caste;
        public AbilityDef ability;
        [Tooltip("Unlocks: the level that unlocks it (1 = the first).")]
        [Min(1)] public int level;

        public static EvolutionEffect Add(StatKind stat, float value) => new() { kind = EvolutionEffectKind.Stat, stat = stat, value = value, level = 1 };
        public static EvolutionEffect Unlock(AntCaste caste, int level = 1) => new() { kind = EvolutionEffectKind.UnlockCaste, caste = caste, level = level };
        public static EvolutionEffect Unlock(AbilityDef ability, int level = 1) => new() { kind = EvolutionEffectKind.UnlockAbility, ability = ability, level = level };
    }

    /// <summary>What one level of an evolution costs: rare finds from the stock and food.</summary>
    [Serializable]
    public sealed class EvolutionLevel
    {
        public List<ItemAmount> cost = new();
        [Min(0f)] public float foodCost;
    }

    /// <summary>
    /// A node of the queen's evolution tree (10.4). Unlike genetic traits it belongs to one colony: bought with rare finds
    /// from the stock (honeydew, amber, star fragments, relics...) and lost with the colony at a Nuptial Flight. Each entry in
    /// <see cref="levels"/> is one level with its own price.
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Queen Evolution", fileName = "Evolution")]
    public sealed class EvolutionDef : ContentDef
    {
        [TextArea] public string description;
        [Tooltip("Icon in UI/Icons/ (without the folder), e.g. evolution_brood.")]
        public string icon;
        [Tooltip("Column and row in the queen panel.")]
        public Vector2Int treePosition;

        [Tooltip("One entry per level, in order.")]
        public List<EvolutionLevel> levels = new();

        [Tooltip("Each of these needs at least one level first.")]
        public List<EvolutionDef> prerequisites = new();
        public List<EvolutionEffect> effects = new();
    }
}
