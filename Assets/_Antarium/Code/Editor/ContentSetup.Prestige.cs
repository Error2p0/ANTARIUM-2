using System.Collections.Generic;
using System.Linq;
using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        /// <summary>
        /// Milestone 10.6: prestige-only mechanics. The Queen's Throne (a building only a genetic trait unlocks), two traits that
        /// let every colony lay glow or fire ants from the start, and three biome variants (world modifiers that swap a biome's
        /// base rock for a recoloured one and its cave creatures) behind the trait Wild Lineages. Existing assets are only
        /// extended, never overwritten.
        /// </summary>
        static void CreatePrestige(WorldGenSettings worldGen)
        {
            ItemDef LoadItem(string id) => AssetDatabase.LoadAssetAtPath<ItemDef>($"{ItemsFolder}/{id}.asset");
            ResearchDef LoadResearch(string id) => AssetDatabase.LoadAssetAtPath<ResearchDef>($"{ResearchFolder}/{id}.asset");
            GeneticDef LoadGenetic(string id) => AssetDatabase.LoadAssetAtPath<GeneticDef>($"{GeneticsFolder}/{id}.asset");
            EnemyDef LoadEnemy(string id) => AssetDatabase.LoadAssetAtPath<EnemyDef>($"{EnemiesFolder}/{id}.asset");
            BiomeDef FindBiome(string id) => worldGen.biomes.FirstOrDefault(b => b != null && b.Id == id);
            var amber = LoadItem("amber");
            var copper = LoadItem("copper");
            var iron = LoadItem("iron");

            // The Queen's Throne: built around the queen; she lays faster, more eggs wait, and ants near her work faster.
            var throne = Building("queens_throne", "Queen's Throne", BuildingKind.Throne, 20, "#f0c850", 25f,
                size: new Vector2Int(4, 2), cost: new[] { Amt(amber, 8), Amt(copper, 10), Amt(iron, 4) }, art: PlaceholderArt.Throne,
                description: "Built around the queen (her cell in its bottom row), on the royal chamber floor. While it stands the queen lays " +
                             "30% faster, 2 more eggs can wait, and ants within 12 cells dig and build 15% faster. One per colony. Known only " +
                             "to bloodlines with the trait Royal Throne.");
            if (throne.stats.Count == 0)
            {
                throne.unique = true;
                throne.stats = new List<StatAmount> { Stat(StatKind.LayingSpeed, 0.3f), Stat(StatKind.MaxEggs, 2f) };
                throne.auraRadius = 12f;
                throne.auraWork = 0.15f;
                EditorUtility.SetDirty(throne);
            }

            // Biome variants: the same rules as the original rock, another palette (Terrain/<id>.png), other creatures.
            var ember = VariantMaterial("ember_stone", "Ember Stone", "stone");
            var gley = VariantMaterial("gley_clay", "Gley Clay", "clay");
            var amethyst = VariantMaterial("amethyst", "Amethyst", "quartzite");
            var emberStrata = Modifier("ember_strata", "Ember Strata", 9, "#c0502a",
                "the stone strata are rust-red ember stone, and fire beetles live in their caves instead of centipedes. +20% Genetic Memory.",
                null, Stat(StatKind.GeneticGain, 0.2f));
            Variant(emberStrata, FindBiome("stone_strata"), "Ember Strata", ember, LoadEnemy("fire_beetle"));
            var drownedClay = Modifier("drowned_clay", "Drowned Clay", 10, "#5a8aa0",
                "the clay layer is waterlogged blue gley, and centipedes hunt in its caves. +15% Genetic Memory.",
                null, Stat(StatKind.GeneticGain, 0.15f));
            Variant(drownedClay, FindBiome("clay"), "Drowned Clay", gley, LoadEnemy("centipede"));
            var amethystCaverns = Modifier("amethyst_caverns", "Amethyst Caverns", 11, "#9468bc",
                "the crystal caverns are deep purple amethyst, and swarms of fungal mites live there instead of cave spiders. +15% Genetic Memory.",
                null, Stat(StatKind.GeneticGain, 0.15f));
            Variant(amethystCaverns, FindBiome("crystal_caverns"), "Amethyst Caverns", amethyst, LoadEnemy("fungal_mite"));

            // Genetic traits: the fifth tier (lineages and the throne) and Wild Lineages under Strange Worlds.
            var knowledge = LoadGenetic("ancient_knowledge");
            var deepMemory = LoadGenetic("deep_memory");
            var dormant = LoadGenetic("dormant_wisdom");
            var strange = LoadGenetic("strange_worlds");
            var glowAnts = LoadResearch("glow_ants");
            var fireAnts = LoadResearch("fire_ants");
            if (glowAnts != null)
                Genetic("glow_lineage", "Glow Lineage", 4, 0, 8, 0, 1, "Every colony can lay glow ants from the start.",
                    knowledge != null ? new[] { knowledge } : null, GeneticEffect.Inherit(glowAnts));
            Genetic("royal_throne", "Royal Throne", 4, 1, 8, 0, 1,
                "Your bloodline remembers how to build the Queen's Throne: in every colony, build it around the queen.",
                deepMemory != null ? new[] { deepMemory } : null, GeneticEffect.Unlock(throne));
            if (fireAnts != null)
                Genetic("fire_lineage", "Fire Lineage", 4, 2, 10, 0, 1, "Every colony can lay fire ants from the start.",
                    dormant != null ? new[] { dormant } : null, GeneticEffect.Inherit(fireAnts));
            Genetic("wild_lineages", "Wild Lineages", 3, 4, 6, 0, 1,
                "Unlocks three biome variants: world modifiers that turn a layer into another rock with other creatures, for more Genetic Memory.",
                strange != null ? new[] { strange } : null,
                GeneticEffect.Unlock(emberStrata), GeneticEffect.Unlock(drownedClay), GeneticEffect.Unlock(amethystCaverns));

            int achievementOrder = AssetDatabase.FindAssets("t:" + nameof(AchievementDef)).Length;
            Achievement("long_live_the_queen", "ACH_THRONE", "Long Live the Queen", "Build the Queen's Throne.", AchievementKind.Build, 1,
                achievementOrder, building: throne);
        }

        /// <summary>A copy of an existing material (same hardness, flags, drops) under a new id: its tile comes from Terrain/&lt;id&gt;.png.</summary>
        static TerrainMaterialDef VariantMaterial(string id, string displayName, string sourceId)
        {
            string path = $"{MaterialsFolder}/{id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TerrainMaterialDef>(path);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<TerrainMaterialDef>($"{MaterialsFolder}/{sourceId}.asset");
            if (source == null)
            {
                Debug.LogWarning($"Biome variant '{id}': no material '{sourceId}' to copy.");
                return null;
            }
            var copy = Object.Instantiate(source);
            copy.EditorSetIdentity(id, displayName);
            copy.textureOverride = null; // Import Overrides assigns Terrain/<id>.png and its colours
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        static void Variant(WorldModifierDef modifier, BiomeDef biome, string displayName, TerrainMaterialDef baseMaterial, EnemyDef creature)
        {
            if (modifier == null || biome == null || modifier.variants.Count > 0) return;
            modifier.variants.Add(new BiomeVariant
            {
                biome = biome,
                displayName = displayName,
                baseMaterial = baseMaterial,
                creatures = creature != null ? new List<EnemyDef> { creature } : new List<EnemyDef>(),
            });
            EditorUtility.SetDirty(modifier);
        }
    }
}
