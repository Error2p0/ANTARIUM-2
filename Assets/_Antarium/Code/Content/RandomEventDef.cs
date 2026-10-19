using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    public enum RandomEventKind : byte
    {
        /// <summary>A meteorite lands on the surface near the nest: a crater with a lump of <see cref="RandomEventDef.materials"/>[0].</summary>
        Meteorite,
        /// <summary>A blob of ore next to the tunnels is revealed: materials[layer] (top to bottom, last one repeats).</summary>
        RichVein,
        /// <summary>A beetle wanders the tunnels for <see cref="RandomEventDef.duration"/>; click it for the reward.</summary>
        GoldenBeetle,
        /// <summary><see cref="RandomEventDef.count"/> ants are trapped in a pocket near the tunnels; dig them out in time.</summary>
        TrappedAnts,
    }

    /// <summary>A random event that rewards attention. The event system picks one every few minutes by weight.</summary>
    [CreateAssetMenu(menuName = "Antarium/Random Event", fileName = "RandomEvent")]
    public sealed class RandomEventDef : ContentDef
    {
        public RandomEventKind kind;
        [TextArea] public string description;
        [Min(0f)] public float weight = 1f;
        [Tooltip("Only happens once the colony has dug this deep.")]
        [Min(0)] public int minDepth;

        [Header("Effect")]
        [Tooltip("Seconds the player has to act (beetle, trapped ants). 0 = no limit.")]
        [Min(0f)] public float duration;
        [Tooltip("Size in cells (meteorite lump, vein, trapped pocket distance).")]
        [Min(1f)] public float radius = 3f;
        [Tooltip("Trapped ants: how many.")]
        [Min(1)] public int count = 5;
        [Tooltip("Meteorite: the lump. Rich vein: ore per layer, top to bottom (the last repeats deeper down).")]
        public List<TerrainMaterialDef> materials = new();

        [Header("Rewards (beetle)")]
        [Min(0f)] public float rewardKnowledge;
        [Min(0f)] public float rewardFood;
        public List<ItemAmount> rewards = new();
    }
}
