using System.Collections.Generic;
using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        /// <summary>
        /// Milestone 11 batch 6: hills on the surface, trees with big roots, apples, rare surface structures (boulders, a rock
        /// overhang, a hollow log, an abandoned anthill, standing stones) and the Elder Tree in a great cavern of the stone strata
        /// with its golden apples. New materials and items are created; the world generation settings are only filled in where
        /// they are still at their "off" defaults, so tuning in the asset is kept.
        /// </summary>
        static void CreateTrees(ContentDatabase database, WorldGenSettings worldGen)
        {
            T Load<T>(string folder, string id) where T : Object => AssetDatabase.LoadAssetAtPath<T>($"{folder}/{id}.asset");
            var fibre = Load<ItemDef>(ItemsFolder, "root_fibre");
            var leaf = Load<ItemDef>(ItemsFolder, "leaf");

            var apple = WildItem("apple", "Apple", "#dc4430", 5f, PileArt.Apple);
            var golden = WildItem("golden_apple", "Golden Apple", "#ffd45a", 12f, PileArt.GoldenApple, new Color(1f, 0.82f, 0.38f, 0.55f));

            var bigRoot = Mat("tree_root", "Tree Root", 6f, PixelPattern.Strata, "#4a2e16", "#2c1a0c", "#86582e", "#b07c44", 0.4f, MaterialFlags.Flammable);
            var elderwood = Mat("elderwood", "Elderwood", 8f, PixelPattern.Strata, "#5a4030", "#36261a", "#8c6c50", "#c8a878", 0.35f, MaterialFlags.Flammable);
            var elderLeaves = Mat("elder_leaves", "Elder Leaves", 1.2f, PixelPattern.Mycelium, "#2e5a2a", "#1a3a1c", "#7cc04a", "#e8f0a0", 0.35f,
                MaterialFlags.Flammable | MaterialFlags.LightEmitting, emission: "#c8e070", emissionStrength: 0.75f, glow: 0.45f);
            var deadwood = Mat("deadwood", "Deadwood", 2f, PixelPattern.Strata, "#5a4a36", "#3a2e20", "#8a7458", "#a89070", 0.3f, MaterialFlags.Flammable);
            if (fibre != null)
            {
                if (bigRoot.drop == null) { EnsureDrop(bigRoot, fibre, 2); }
                if (elderwood.drop == null) { EnsureDrop(elderwood, fibre); elderwood.dropChance = 0.5f; EditorUtility.SetDirty(elderwood); }
                if (deadwood.drop == null) { EnsureDrop(deadwood, fibre); deadwood.dropChance = 0.25f; EditorUtility.SetDirty(deadwood); }
            }
            if (leaf != null && elderLeaves.drop == null)
            {
                EnsureDrop(elderLeaves, leaf);
                elderLeaves.dropChance = 0.5f;
                EditorUtility.SetDirty(elderLeaves);
            }

            if (!database.treeForage.Contains(apple))
            {
                database.treeForage.Add(apple);
                EditorUtility.SetDirty(database);
            }
            if (worldGen == null) return;

            bool dirty = false;
            if (worldGen.hillAmplitude <= 0f)
            {
                worldGen.hillAmplitude = 19f;
                worldGen.hillFrequency = 0.0055f;
                worldGen.bumpAmplitude = 1.5f;
                worldGen.bumpFrequency = 0.09f;
                worldGen.terraceThreshold = 0.25f;
                worldGen.terraceFrequency = 0.004f;
                worldGen.terraceStep = 5;
                worldGen.nestFlatRadius = 16;
                worldGen.nestFlatBlend = 28;
                worldGen.waterFlatBlend = 14;
                dirty = true;
            }
            if (worldGen.treeGap.x <= 0)
            {
                worldGen.treeGap = new Vector2Int(26, 60);
                worldGen.treeClearance = 10;
                worldGen.appleTreeShare = 0.45f;
                worldGen.treeRoots = new Vector2Int(3, 4);
                worldGen.treeRootLength = new Vector2Int(28, 56);
                worldGen.treeRootMaxDepth = 90;
                dirty = true;
            }
            if (worldGen.treeRootMaterial == null) { worldGen.treeRootMaterial = bigRoot; dirty = true; }
            if (worldGen.surfaceFeatures.Count == 0)
            {
                var stone = Load<TerrainMaterialDef>(MaterialsFolder, "stone");
                void Rule(SurfaceFeatureKind kind, Vector2Int count, Vector2Int width, TerrainMaterialDef material, string find = null, int finds = 0)
                {
                    if (material == null) return;
                    worldGen.surfaceFeatures.Add(new SurfaceFeatureRule
                    {
                        kind = kind, count = count, width = width, material = material,
                        find = find != null ? Load<TerrainMaterialDef>(MaterialsFolder, find) : null, finds = finds,
                    });
                }
                Rule(SurfaceFeatureKind.Boulder, new Vector2Int(3, 6), new Vector2Int(4, 8), stone);
                Rule(SurfaceFeatureKind.Overhang, new Vector2Int(0, 1), new Vector2Int(12, 16), stone);
                Rule(SurfaceFeatureKind.HollowLog, new Vector2Int(1, 2), new Vector2Int(12, 18), deadwood, "grub_nest", 2);
                Rule(SurfaceFeatureKind.Anthill, new Vector2Int(0, 1), new Vector2Int(16, 22), Load<TerrainMaterialDef>(MaterialsFolder, "packed_earth"), "amber", 3);
                Rule(SurfaceFeatureKind.StandingStones, new Vector2Int(0, 1), new Vector2Int(11, 14), Load<TerrainMaterialDef>(MaterialsFolder, "ruin_brick"), "relic", 1);
                worldGen.featureClearance = 30;
                dirty = true;
            }
            if (worldGen.elderWood == null)
            {
                worldGen.elderBiome = AssetDatabase.LoadAssetAtPath<BiomeDef>($"{BiomesFolder}/stone_strata.asset");
                worldGen.elderCaveSize = new Vector2Int(92, 58);
                worldGen.elderDepthShare = 0.3f;
                worldGen.elderMinDistance = 110;
                worldGen.elderWood = elderwood;
                worldGen.elderLeaves = elderLeaves;
                worldGen.elderFruit = golden;
                worldGen.elderFruitChance = 0.25f;
                worldGen.elderFruitMax = 16;
                dirty = true;
            }
            if (dirty) EditorUtility.SetDirty(worldGen);
        }
    }
}
