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
        [Tooltip("What a burnt-out flammable cell turns into (none = it stays as it is).")]
        public TerrainMaterialDef ashMaterial;

        [Header("Surface plants and roots")]
        [Tooltip("Columns between surface plants (random in this range). Each plant drops leaves and has roots.")]
        public Vector2Int plantGap = new(8, 20);
        [Tooltip("No plants this many columns either side of the nest shaft.")]
        [Min(0)] public int plantClearance = 6;
        [Tooltip("Tough material roots are made of (none = no roots).")]
        public TerrainMaterialDef rootMaterial;
        [Tooltip("Cells a plant's main root grows down (random in this range); branches are shorter.")]
        public Vector2Int rootLength = new(10, 32);
        [Tooltip("Roots stay in the topsoil: no deeper than this many cells.")]
        [Min(1)] public int rootMaxDepth = 60;
        [Tooltip("Share of surface plants that are berry bushes (Milestone 11): berries fall under them by day.")]
        [Range(0f, 1f)] public float berryBushShare = 0.25f;

        [Header("Lakes and rivers (Milestone 11)")]
        [Tooltip("Lakes on the surface of a world (random in this range): deep bowls of water.")]
        public Vector2Int lakeCount = new(2, 3);
        [Tooltip("Rivers on the surface (random in this range): wider, shallow channels with steep banks, seen end-on.")]
        public Vector2Int riverCount = new(1, 2);
        [Tooltip("Lake width in cells and depth below its lower bank (random in these ranges).")]
        public Vector2Int lakeWidth = new(14, 28);
        public Vector2Int lakeDepth = new(4, 7);
        [Tooltip("River width in cells and depth below its lower bank.")]
        public Vector2Int riverWidth = new(10, 18);
        public Vector2Int riverDepth = new(2, 3);
        [Tooltip("No water this many columns either side of the nest shaft: keep it beyond where raids and night hunters appear, so they can still walk to the nest.")]
        [Min(0)] public int waterClearance = 110;
        [Tooltip("Columns of dry ground at least between two bodies of water.")]
        [Min(2)] public int waterSpacing = 24;
        [Tooltip("Ground under and beside the water (none = grass).")]
        public TerrainMaterialDef waterBedMaterial;
        [Tooltip("Rows of bed material under the water.")]
        [Min(1)] public int waterBedDepth = 2;

        [Header("Wild growths in caves (Milestone 11)")]
        [Tooltip("What grows on natural cave floors, per biome (mushrooms, glowing mushrooms). The first rule that hits a cell wins.")]
        public List<GrowthRule> caveGrowths = new();
        [Tooltip("Size of the patches growths cluster in (noise frequency per cell; smaller = bigger patches).")]
        [Min(0.001f)] public float growthPatchScale = 0.07f;
        [Tooltip("At most this many growth spots in a world.")]
        [Min(0)] public int maxCaveGrowths = 2500;

        [Header("Ruins (10.5)")]
        [Tooltip("Unique find set into the floor of some ruin rooms (none = no tablets).")]
        public TerrainMaterialDef tabletMaterial;
        [Tooltip("Tablets in a world.")]
        [Min(0)] public int tabletCount = 4;
        [Tooltip("Tablets lie at least this many cells apart.")]
        [Min(1)] public int tabletSpacing = 60;

        [Header("Starting nest")]
        [Min(4)] public int nestDepth = 14;
        public Vector2 nestRadius = new(7f, 3.5f);
        [Min(1)] public int shaftHalfWidth = 1;

        [Header("Seed")]
        public uint defaultSeed = 1337;
    }

    /// <summary>A wild growth on cave floors (Milestone 11): <see cref="item"/> grows on floor cells of <see cref="biome"/>'s natural caves.</summary>
    [System.Serializable]
    public sealed class GrowthRule
    {
        public ItemDef item;
        [Tooltip("Biome whose caves it grows in (none = every biome).")]
        public BiomeDef biome;
        [Tooltip("Chance per cave floor cell, inside a patch (patches cover about half the floor).")]
        [Range(0f, 1f)] public float chance = 0.05f;
    }
}
