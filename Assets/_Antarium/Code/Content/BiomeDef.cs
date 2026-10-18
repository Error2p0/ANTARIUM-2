using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    /// <summary>A depth band of the world with its own materials, deposits and caves.</summary>
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

        [Tooltip("Thickness in cells. The last biome in the world extends to the bottom.")]
        [Min(1)] public int thickness = 128;
        public TerrainMaterialDef baseMaterial;
        [Tooltip("Later entries override earlier ones where they overlap.")]
        public List<Deposit> deposits = new();

        [Header("Caves")]
        [Min(0.001f)] public float caveScale = 0.025f;
        [Tooltip("Noise threshold in [-1, 1]. Higher = fewer caves.")]
        [Range(-1f, 1.5f)] public float caveThreshold = 0.6f;
    }
}
