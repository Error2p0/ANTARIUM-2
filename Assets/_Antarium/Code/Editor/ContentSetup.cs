using System.Collections.Generic;
using System.IO;
using System.Linq;
using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    /// <summary>Creates the default Milestone 1 content and keeps the ContentDatabase in sync.</summary>
    public static class ContentSetup
    {
        public const string Root = "Assets/_Antarium/Content";
        public const string MaterialsFolder = Root + "/Materials";
        public const string BiomesFolder = Root + "/Biomes";
        public const string ItemsFolder = Root + "/Items";
        public const string DatabasePath = Root + "/ContentDatabase.asset";
        public const string WorldGenPath = Root + "/WorldGenSettings.asset";
        public const string ConfigPath = Root + "/GameConfig.asset";

        /// <summary>
        /// Creates any missing default assets. Existing assets are left untouched so hand-tuned values survive.
        /// </summary>
        [MenuItem("Antarium/Setup/Create Default Content")]
        public static ContentDatabase CreateDefaultContent()
        {
            EnsureFolder(MaterialsFolder);
            EnsureFolder(BiomesFolder);
            EnsureFolder(ItemsFolder);

            var copperItem = Item("copper_ore", "Copper Ore", "#d47a3c");
            var ironItem = Item("iron_ore", "Iron Ore", "#cfc8c0");
            var coalItem = Item("coal", "Coal", "#2a282e");
            var amberItem = Item("amber", "Amber", "#e39b2b");
            var fossilItem = Item("fossil", "Fossil", "#e8dcc0");
            var crystalItem = Item("crystal_shard", "Crystal Shard", "#7fd8ff");
            var seedItem = Item("seed", "Seed", "#c8a85a", food: 2f);
            var grubItem = Item("grub", "Grub", "#f0e2c0", food: 5f);

            var air = Mat("air", "Air", 1f, PixelPattern.Soil, "#000000", "#000000", "#000000", "#000000", 0f, MaterialFlags.Air);
            var grass = Mat("grass", "Grass", 0.8f, PixelPattern.Soil, "#4c7a2e", "#33561f", "#6aa63c", "#2d4a1a", 0.2f, top: "#86c94e");
            var dirt = Mat("dirt", "Dirt", 1f, PixelPattern.Soil, "#6b4a2f", "#4a321f", "#86603d", "#3b2717", 0.2f);
            var pebbles = Mat("pebbles", "Pebbly Dirt", 2f, PixelPattern.Pebbles, "#6b4a2f", "#4a321f", "#86603d", "#9b948a", 0.6f);
            var copper = Mat("copper_ore", "Copper Ore", 3f, PixelPattern.Ore, "#5e4330", "#3f2c1e", "#7a573c", "#d47a3c", 0.7f, value: 2, flags: MaterialFlags.Conductive);
            var clay = Mat("clay", "Clay", 1.8f, PixelPattern.Strata, "#9a5a3c", "#74402a", "#b8714c", "#5e3322", 0.2f);
            var iron = Mat("iron_ore", "Iron Ore", 4f, PixelPattern.Ore, "#8a4e35", "#643622", "#a8654a", "#cfc8c0", 0.6f, value: 4, flags: MaterialFlags.Conductive);
            var stone = Mat("stone", "Stone", 5f, PixelPattern.Rock, "#6c6f78", "#4b4d55", "#8b8f99", "#3a3b41", 0.3f);
            var coal = Mat("coal", "Coal", 3f, PixelPattern.Ore, "#5d6068", "#41434a", "#7a7e87", "#1f1d22", 0.8f, value: 3, flags: MaterialFlags.Flammable);
            var amber = Mat("amber", "Amber", 3f, PixelPattern.Ore, "#5d6068", "#41434a", "#7a7e87", "#e39b2b", 0.5f, value: 10);
            var fossil = Mat("fossil", "Fossil", 4f, PixelPattern.Ore, "#5d6068", "#41434a", "#7a7e87", "#e8dcc0", 0.5f, value: 8);
            var crystal = Mat("glow_crystal", "Glow Crystal", 4f, PixelPattern.Crystal, "#28344f", "#1b2338", "#e0fbff", "#7fd8ff", 0.8f,
                value: 12, flags: MaterialFlags.LightEmitting, emission: "#7fd0ff", emissionStrength: 0.85f, glow: 0.6f);
            var grubNest = Mat("grub_nest", "Grub Nest", 0.8f, PixelPattern.Pebbles, "#6b4a2f", "#4a321f", "#86603d", "#f0e2c0", 0.7f);
            var bedrock = Mat("bedrock", "Bedrock", 1f, PixelPattern.Rock, "#2b2a30", "#1a191e", "#3d3c44", "#121115", 0.3f, MaterialFlags.Indestructible);

            var topsoil = Biome("topsoil", "Topsoil & Roots", 110, dirt, 0.028f, 0.62f,
                Dep(pebbles, 0.09f, 0.5f), Dep(copper, 0.14f, 0.72f, minDepth: 10), Dep(grubNest, 0.16f, 0.78f, minDepth: 6));
            EnsureDeposit(topsoil, grubNest, 0.16f, 0.78f, 6);

            // Fill drops on materials that have none (keeps hand-edited assets intact).
            EnsureDrop(copper, copperItem);
            EnsureDrop(iron, ironItem);
            EnsureDrop(coal, coalItem);
            EnsureDrop(amber, amberItem);
            EnsureDrop(fossil, fossilItem);
            EnsureDrop(crystal, crystalItem);
            EnsureDrop(grubNest, grubItem);
            var clayBiome = Biome("clay", "Clay & Groundwater", 180, clay, 0.022f, 0.56f,
                Dep(dirt, 0.05f, 0.6f), Dep(iron, 0.13f, 0.72f), Dep(copper, 0.15f, 0.8f));
            var strata = Biome("stone_strata", "Stone Strata", 400, stone, 0.018f, 0.5f,
                Dep(coal, 0.1f, 0.66f), Dep(iron, 0.14f, 0.78f), Dep(amber, 0.18f, 0.82f), Dep(fossil, 0.16f, 0.84f), Dep(crystal, 0.11f, 0.9f));

            var worldGen = LoadOrCreate<WorldGenSettings>(WorldGenPath, g =>
            {
                g.air = air;
                g.surfaceMaterial = grass;
                g.bedrock = bedrock;
                g.biomes = new List<BiomeDef> { topsoil, clayBiome, strata };
            });
            LoadOrCreate<GameConfig>(ConfigPath, _ => { });

            var database = LoadOrCreate<ContentDatabase>(DatabasePath, db => db.worldGen = worldGen);
            if (database.surfaceForage.Count == 0)
            {
                database.surfaceForage.Add(seedItem);
                EditorUtility.SetDirty(database);
            }
            RebuildDatabase();
            AssetDatabase.SaveAssets();
            return database;
        }

        /// <summary>Collects every TerrainMaterialDef in the project into the ContentDatabase.</summary>
        [MenuItem("Antarium/Content/Rebuild Database")]
        public static void RebuildDatabase()
        {
            var database = AssetDatabase.LoadAssetAtPath<ContentDatabase>(DatabasePath);
            if (database == null)
            {
                Debug.LogError($"No ContentDatabase at {DatabasePath}. Run Antarium/Setup/Create Default Content.");
                return;
            }
            database.materials = AssetDatabase.FindAssets("t:" + nameof(TerrainMaterialDef))
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<TerrainMaterialDef>)
                .ToList();
            database.items = AssetDatabase.FindAssets("t:" + nameof(ItemDef))
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<ItemDef>)
                .ToList();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            Debug.Log($"ContentDatabase rebuilt: {database.materials.Count} materials, {database.items.Count} items.");
        }

        static TerrainMaterialDef Mat(string id, string displayName, float hardness, PixelPattern pattern,
            string baseHex, string shadowHex, string highlightHex, string accentHex, float accentAmount,
            MaterialFlags flags = MaterialFlags.None, string top = null, int value = 0,
            string emission = null, float emissionStrength = 0f, float glow = 0f)
        {
            return LoadOrCreate<TerrainMaterialDef>($"{MaterialsFolder}/{id}.asset", m =>
            {
                m.EditorSetIdentity(id, displayName);
                m.hardness = hardness;
                m.flags = flags;
                m.value = value;
                m.pattern = pattern;
                m.baseColor = Hex(baseHex);
                m.shadowColor = Hex(shadowHex);
                m.highlightColor = Hex(highlightHex);
                m.accentColor = Hex(accentHex);
                m.accentAmount = accentAmount;
                m.topRimColor = top != null ? Hex(top) : new Color(0, 0, 0, 0);
                m.emissionColor = emission != null ? Hex(emission) : Color.black;
                m.emissionStrength = emissionStrength;
                m.selfGlow = glow;
            });
        }

        static BiomeDef Biome(string id, string displayName, int thickness, TerrainMaterialDef baseMaterial,
            float caveScale, float caveThreshold, params BiomeDef.Deposit[] deposits)
        {
            return LoadOrCreate<BiomeDef>($"{BiomesFolder}/{id}.asset", b =>
            {
                b.EditorSetIdentity(id, displayName);
                b.thickness = thickness;
                b.baseMaterial = baseMaterial;
                b.caveScale = caveScale;
                b.caveThreshold = caveThreshold;
                b.deposits = deposits.ToList();
            });
        }

        static ItemDef Item(string id, string displayName, string colorHex, float food = 0f)
        {
            return LoadOrCreate<ItemDef>($"{ItemsFolder}/{id}.asset", i =>
            {
                i.EditorSetIdentity(id, displayName);
                i.color = Hex(colorHex);
                i.foodValue = food;
            });
        }

        static void EnsureDrop(TerrainMaterialDef material, ItemDef item)
        {
            if (material.drop != null) return;
            material.drop = item;
            EditorUtility.SetDirty(material);
        }

        static void EnsureDeposit(BiomeDef biome, TerrainMaterialDef material, float scale, float threshold, int minDepth)
        {
            if (biome.deposits.Any(d => d.material == material)) return;
            biome.deposits.Add(Dep(material, scale, threshold, minDepth));
            EditorUtility.SetDirty(biome);
        }

        static BiomeDef.Deposit Dep(TerrainMaterialDef material, float scale, float threshold, int minDepth = 0, int maxDepth = 0) =>
            new() { material = material, scale = scale, threshold = threshold, minDepth = minDepth, maxDepth = maxDepth };

        static T LoadOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            init(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
