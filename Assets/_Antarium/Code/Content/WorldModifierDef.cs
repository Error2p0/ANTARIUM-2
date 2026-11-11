using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    [Serializable]
    public struct StatAmount
    {
        public StatKind stat;
        public float value;

        public StatAmount(StatKind stat, float value)
        {
            this.stat = stat;
            this.value = value;
        }
    }

    /// <summary>
    /// A biome variant (10.6): in a world with this modifier one biome is generated from another base material (a recoloured
    /// rock with the same rules) and its caves hold other creatures.
    /// </summary>
    [Serializable]
    public sealed class BiomeVariant
    {
        public BiomeDef biome;
        [Tooltip("Shown instead of the biome's name (empty = keep the name).")]
        public string displayName;
        [Tooltip("Replaces the biome's base material (empty = keep).")]
        public TerrainMaterialDef baseMaterial;
        [Tooltip("Replace the creatures of the biome's caves (empty = keep).")]
        public List<EnemyDef> creatures = new();
    }

    /// <summary>
    /// A rule for one whole colony, picked before a Nuptial Flight: it changes how the next world is generated and
    /// adds colony stats (threats, forage, Genetic Memory gain). Modifiers named by a genetic trait start locked.
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/World Modifier", fileName = "Modifier")]
    public sealed class WorldModifierDef : ContentDef
    {
        [TextArea] public string description;
        [Tooltip("Position in the flight screen (lower first).")]
        public int order;
        public Color color = Color.white;

        [Header("World generation")]
        [Tooltip("Added to the threshold of every ore deposit (items that are not food). Negative = more ore.")]
        [Range(-0.5f, 0.5f)] public float oreThreshold;
        [Tooltip("Added to every biome's cave threshold. Negative = more caves.")]
        [Range(-0.5f, 0.5f)] public float caveThreshold;
        [Tooltip("Added to the thresholds of water / lava / gas pockets. Negative = more of that fluid.")]
        [Range(-0.5f, 0.5f)] public float waterThreshold;
        [Range(-0.5f, 0.5f)] public float lavaThreshold;
        [Range(-0.5f, 0.5f)] public float gasThreshold;
        [Tooltip("Every biome's thickness is multiplied by this (0.75 = the deep layers come 25% sooner).")]
        [Range(0.25f, 2f)] public float biomeThickness = 1f;

        [Tooltip("Biome variants: a biome with another base material and other cave creatures.")]
        public List<BiomeVariant> variants = new();

        [Header("Colony")]
        [Tooltip("Colony stats for the whole colony (forage, raids, creatures, Genetic Memory gain...).")]
        public List<StatAmount> stats = new();
    }
}
