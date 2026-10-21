using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    /// <summary>
    /// Colony stats that research (and later prestige) modifies. Each stat starts at 0 and effects add to it.
    /// Multiplier stats are read as 1 + total (0.25 = +25%); flat stats are read as the total itself.
    /// </summary>
    public enum StatKind : byte
    {
        /// <summary>Multiplier: worker walking speed.</summary>
        WorkerSpeed,
        /// <summary>Multiplier: worker digging power.</summary>
        WorkerDig,
        /// <summary>Multiplier: the player's own dig power (Dig tool).</summary>
        PlayerDig,
        /// <summary>Multiplier: construction work per worker.</summary>
        BuildSpeed,
        /// <summary>Multiplier: item speed on belts and lifts.</summary>
        BeltSpeed,
        /// <summary>Multiplier: machine crafting speed.</summary>
        MachineSpeed,
        /// <summary>Flat: extra items the royal chamber holds.</summary>
        RoyalCapacity,
        /// <summary>Flat: extra food capacity.</summary>
        FoodCapacity,
        /// <summary>Multiplier: how often the queen lays.</summary>
        LayingSpeed,
        /// <summary>Flat: extra eggs that can wait at once.</summary>
        MaxEggs,
        /// <summary>Multiplier: how often forage appears on the surface (and how much may lie around).</summary>
        ForageRate,
        /// <summary>Flat: added to the offline efficiency (0.25 = +25 percentage points).</summary>
        OfflineEfficiency,
        /// <summary>Flat: extra hours of offline progress.</summary>
        OfflineHours,
        /// <summary>Flat: extra contract slots.</summary>
        ContractSlots,
        /// <summary>Multiplier: knowledge from studying.</summary>
        KnowledgeGain,
        /// <summary>Multiplier: ability recharge speed.</summary>
        AbilityRecharge,
        /// <summary>Multiplier: hit points of every ant.</summary>
        AntHealth,
        /// <summary>Multiplier: damage ants deal to enemies.</summary>
        AntAttack,
        /// <summary>Multiplier: resistance to drowning, lava, gas and cave-ins (damage is divided by it).</summary>
        HazardResistance,
        /// <summary>Multiplier: gate hit points and trap damage.</summary>
        DefenseStrength,
    }

    /// <summary>Kinds of ant. Workers dig, haul and build; soldiers guard and fight.</summary>
    public enum AntCaste : byte
    {
        Worker,
        Soldier,
    }

    public enum ResearchEffectKind : byte
    {
        /// <summary>Adds <see cref="ResearchEffect.value"/> to <see cref="ResearchEffect.stat"/>.</summary>
        Stat,
        UnlockBuilding,
        UnlockAbility,
        /// <summary>The queen may lay eggs of <see cref="ResearchEffect.caste"/>.</summary>
        UnlockCaste,
    }

    [Serializable]
    public struct ResearchEffect
    {
        public ResearchEffectKind kind;
        public StatKind stat;
        public float value;
        public BuildingDef building;
        public AbilityDef ability;
        public AntCaste caste;

        public static ResearchEffect Add(StatKind stat, float value) => new() { kind = ResearchEffectKind.Stat, stat = stat, value = value };
        public static ResearchEffect Unlock(BuildingDef building) => new() { kind = ResearchEffectKind.UnlockBuilding, building = building };
        public static ResearchEffect Unlock(AbilityDef ability) => new() { kind = ResearchEffectKind.UnlockAbility, ability = ability };
        public static ResearchEffect Unlock(AntCaste caste) => new() { kind = ResearchEffectKind.UnlockCaste, caste = caste };
    }

    /// <summary>
    /// A node of the research tree. Paid once with knowledge (plus items / food); unlocks buildings or
    /// abilities, or improves colony stats. Buildings and abilities named by any research start locked.
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Research", fileName = "Research")]
    public sealed class ResearchDef : ContentDef
    {
        [TextArea] public string description;
        [Tooltip("Column (tier) and row in the research panel.")]
        public Vector2Int treePosition;

        [Header("Cost")]
        [Min(0f)] public float knowledgeCost = 10f;
        public List<ItemAmount> cost = new();
        [Min(0f)] public float foodCost;

        [Header("Tree")]
        [Tooltip("All of these must be researched first.")]
        public List<ResearchDef> prerequisites = new();
        public List<ResearchEffect> effects = new();
    }
}
