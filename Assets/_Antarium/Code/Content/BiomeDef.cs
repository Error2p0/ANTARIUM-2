using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    public enum FluidKind : byte
    {
        None,
        /// <summary>Falls and spreads; drowns ants in deep water; evaporates as a thin film.</summary>
        Water,
        /// <summary>Falls and spreads slowly, glows, burns ants; turns to obsidian where it meets water.</summary>
        Lava,
        /// <summary>Rises and spreads; harms ants in dense clouds; fades once it reaches the colony's tunnels.</summary>
        Gas,
    }

    /// <summary>A depth band of the world with its own materials, deposits, caves, fluids and creatures.</summary>
    [CreateAssetMenu(menuName = "Antarium/Biome", fileName = "Biome")]
    public sealed class BiomeDef : ContentDef
    {
        [Serializable]
        public sealed class Deposit
        {
            public TerrainMaterialDef material;
            [Tooltip("Noise frequency. Higher = smaller, more frequent blobs.")]
            [Min(0.001f)] public float scale = 0.1f;
            [Tooltip("Noise threshold in [-1, 1]. Higher = rarer.")]
            [Range(-1f, 1f)] public float threshold = 0.7f;
            [Tooltip("Depth range (cells below the surface) where this deposit can appear. Max 0 = unlimited.")]
            [Min(0)] public int minDepth;
            [Min(0)] public int maxDepth;
        }

        /// <summary>Cave regions pre-filled with a fluid (lakes, lava pools, gas pockets).</summary>
        [Serializable]
        public sealed class FluidPocket
        {
            public FluidKind kind = FluidKind.Water;
            [Tooltip("Noise frequency. Higher = smaller pockets.")]
            [Min(0.001f)] public float scale = 0.03f;
            [Tooltip("Noise threshold in [-1, 1]: cave cells above it start full. Higher = rarer.")]
            [Range(-1f, 1f)] public float threshold = 0.3f;
        }

        [Tooltip("Thickness in cells. The last biome in the world extends to the bottom.")]
        [Min(1)] public int thickness = 128;
        public TerrainMaterialDef baseMaterial;
        [Tooltip("Later entries override earlier ones where they overlap.")]
        public List<Deposit> deposits = new();

        [Header("Caves")]
        [Min(0.001f)] public float caveScale = 0.025f;
        [Tooltip("Noise threshold in [-1, 1]. Higher = fewer caves.")]
        [Range(-1f, 1.5f)] public float caveThreshold = 0.6f;

        [Header("Ruins")]
        [Tooltip("Carve a grid of rectangular rooms and corridors (ancient ruins) instead of noise caves.")]
        public bool rooms;
        [Tooltip("Room grid size in cells (walls included).")]
        public Vector2Int roomSize = new(14, 9);
        [Tooltip("Share of grid slots that hold a room.")]
        [Range(0f, 1f)] public float roomChance = 0.55f;

        [Header("Fluids")]
        public List<FluidPocket> fluidPockets = new();

        [Header("Creatures")]
        [Tooltip("Creatures that may live in this biome's caves; they wake when the colony breaks in.")]
        public List<EnemyDef> creatures = new();
        [Tooltip("One creature per this many discovered cave cells.")]
        [Min(1)] public int caveCellsPerCreature = 150;
        [Min(0)] public int maxCreaturesPerCave = 4;
    }
}
