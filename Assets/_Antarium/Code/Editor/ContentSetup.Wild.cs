using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        /// <summary>
        /// Milestone 11 batch 4: berries (from berry bushes), toadstools (surface mushrooms), cave mushrooms and glowing
        /// mushrooms (1.5x the food), the cave growth rules per biome and the sand under lakes and rivers. Existing assets are
        /// only extended (a rule list or a bed material already set is left as it is).
        /// </summary>
        static void CreateWild(ContentDatabase database, WorldGenSettings worldGen)
        {
            var berry = WildItem("berry", "Berry", "#b02848", 3f, PileArt.Berries);
            var toadstool = WildItem("toadstool", "Toadstool", "#e05a40", 4f, PileArt.Toadstool);
            var mushroom = WildItem("mushroom", "Cave Mushroom", "#c59a62", 4f, PileArt.Mushroom);
            var glow = WildItem("glow_mushroom", "Glowing Mushroom", "#8ef0d0", 6f, PileArt.GlowMushroom, new Color(0.3f, 0.95f, 0.8f, 0.6f));

            bool dirty = false;
            if (!database.bushForage.Contains(berry)) { database.bushForage.Add(berry); dirty = true; }
            if (!database.surfaceMushrooms.Contains(toadstool)) { database.surfaceMushrooms.Add(toadstool); dirty = true; }
            if (dirty) EditorUtility.SetDirty(database);

            if (worldGen == null) return;
            BiomeDef Biome(string id) => AssetDatabase.LoadAssetAtPath<BiomeDef>($"{BiomesFolder}/{id}.asset");
            if (worldGen.caveGrowths.Count == 0)
            {
                void Rule(ItemDef item, string biome, float chance)
                {
                    var b = Biome(biome);
                    if (b != null) worldGen.caveGrowths.Add(new GrowthRule { item = item, biome = b, chance = chance });
                }
                Rule(mushroom, "topsoil", 0.05f);
                Rule(mushroom, "clay", 0.06f);
                Rule(glow, "stone_strata", 0.012f);
                Rule(mushroom, "stone_strata", 0.04f);
                Rule(glow, "crystal_caverns", 0.03f);
                Rule(glow, "fungal_depths", 0.12f);
                Rule(mushroom, "ancient_ruins", 0.025f);
                EditorUtility.SetDirty(worldGen);
            }
            if (worldGen.waterBedMaterial == null)
            {
                worldGen.waterBedMaterial = AssetDatabase.LoadAssetAtPath<TerrainMaterialDef>($"{MaterialsFolder}/sand.asset");
                EditorUtility.SetDirty(worldGen);
            }
        }

        static ItemDef WildItem(string id, string name, string color, float food, PileArt art, Color light = default)
        {
            var item = Item(id, name, color, food);
            if (item.pileArt == art && item.growthLight == light) return item;
            item.pileArt = art;
            item.growthLight = light;
            EditorUtility.SetDirty(item);
            return item;
        }
    }
}
