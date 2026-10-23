using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    [CreateAssetMenu(menuName = "Antarium/World Gen Settings", fileName = "WorldGenSettings")]
    public sealed class WorldGenSettings : ScriptableObject
    {
        [Header("Size (chunks of 32x32 cells)")]
        [Min(2)] public int widthInChunks = 32;
        [Min(2)] public int heightInChunks = 48;

        [Header("Surface")]
        [Tooltip("Rows of open sky above the average surface.")]
        [Min(8)] public int skyHeight = 48;
        public float surfaceAmplitude = 5f;
        public float surfaceFrequency = 0.012f;
        [Min(0)] public int grassDepth = 2;

        [Header("Materials")]
        public TerrainMaterialDef air;
        public TerrainMaterialDef surfaceMaterial;
        public TerrainMaterialDef bedrock;
        [Min(1)] public int bedrockThickness = 3;

        [Header("Biomes (top to bottom)")]
        public List<BiomeDef> biomes = new();
        public float biomeWarpAmplitude = 10f;
        public float biomeWarpFrequency = 0.02f;

        [Header("Caves")]
        [Tooltip("No caves above this depth, so the starting area stays intact.")]
        [Min(0)] public int caveMinDepth = 24;
        [Tooltip("Depth over which caves fade in below caveMinDepth.")]
        [Min(1)] public int caveFadeDepth = 24;

        [Header("Fluids")]
        [Tooltip("What lava turns into where it meets water.")]
        public TerrainMaterialDef fluidReactionMaterial;

        [Header("Starting nest")]
        [Min(4)] public int nestDepth = 14;
        public Vector2 nestRadius = new(7f, 3.5f);
        [Min(1)] public int shaftHalfWidth = 1;

        [Header("Seed")]
        public uint defaultSeed = 1337;
    }
}
