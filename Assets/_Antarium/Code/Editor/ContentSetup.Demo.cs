using System.Collections.Generic;
using Antarium.Content;
using Antarium.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        public const string DemoConfigPath = Root + "/DemoConfig.asset";

        /// <summary>
        /// The Steam Next Fest demo (GDD 2.16): the Ancient Seal material and the <see cref="DemoConfig"/> (the demo ends at the bottom
        /// of the stone strata; obsidian, relics and tablets are full game items; the Nuptial Flight and Crystal Resonance research are
        /// full game only). An existing config keeps its values.
        /// </summary>
        [MenuItem("Antarium/Setup/Create Demo Content")]
        public static void CreateDemoContent()
        {
            var database = AssetDatabase.LoadAssetAtPath<ContentDatabase>(DatabasePath);
            if (database == null)
            {
                Debug.LogError($"No ContentDatabase at {DatabasePath}. Run Antarium/Setup/Create Default Content.");
                return;
            }
            CreateDemo(database);
            RebuildDatabase();
            AssetDatabase.SaveAssets();
        }

        static void CreateDemo(ContentDatabase database)
        {
            // Dark slate blocks with faint gold runes: it glows a little, so the end of the world reads in the dark.
            var seal = Mat("ancient_seal", "Ancient Seal", 1f, PixelPattern.Brick, "#2a2834", "#18161e", "#4a4658", "#e8b850", 0.35f,
                MaterialFlags.Indestructible | MaterialFlags.LightEmitting, emission: "#d8a848", emissionStrength: 0.35f, glow: 0.3f);
            T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
            var config = LoadOrCreate<DemoConfig>(DemoConfigPath, c =>
            {
                c.lastBiome = Load<BiomeDef>($"{BiomesFolder}/stone_strata.asset");
                c.sealMaterial = seal;
                c.fullGameItems = new List<ItemDef>
                {
                    Load<ItemDef>($"{ItemsFolder}/obsidian.asset"),
                    Load<ItemDef>($"{ItemsFolder}/relic.asset"),
                    Load<ItemDef>($"{ItemsFolder}/tablet.asset"),
                };
                c.fullGameItems.RemoveAll(i => i == null);
                c.fullGameResearch = new List<ResearchDef>
                {
                    Load<ResearchDef>($"{ResearchFolder}/nuptial_flight.asset"),
                    Load<ResearchDef>($"{ResearchFolder}/crystal_resonance.asset"),
                };
                c.fullGameResearch.RemoveAll(r => r == null);
                c.fullGameFeatures = new List<string>
                {
                    "Four deep biomes below the seal: the crystal caverns, the fungal depths, the magma zone and the ancient ruins, each with its own creatures and dangers",
                    "The story of the Old Colony: relics, tablets, twelve lore fragments and the Ancient Engine to restore",
                    "Fire ants, Crystal Resonators, obsidian plating and the ancient research tier",
                    "The Nuptial Flight: raise princesses, found new colonies and spend Genetic Memory on 23 permanent traits",
                    "Bloodlines in three save slots, world modifiers and biome variants",
                    "49 Steam achievements",
                };
                c.fullGameAppId = PlatformServices.SteamAppId;
            });
            if (config.sealMaterial == null)
            {
                config.sealMaterial = seal;
                EditorUtility.SetDirty(config);
            }
            if (database.demo != config)
            {
                database.demo = config;
                EditorUtility.SetDirty(database);
            }
        }
    }
}
