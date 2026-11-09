using System.Linq;
using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        /// <summary>
        /// Milestone 10.3: the living world. Pumps and water wheels (research row 9), irrigation for the Fungus Garden and the
        /// Aphid Ranch, magma heat for the Smelter (coal becomes fuel), fire (flammable mycelium and giant fungus, ash), the hot
        /// and conductive flags, contracts and the fire sound. Existing assets are only extended, never overwritten.
        /// </summary>
        static void CreateWaterworks(WorldGenSettings worldGen, ItemDef copper, ItemDef iron)
        {
            ResearchDef LoadResearch(string id) => AssetDatabase.LoadAssetAtPath<ResearchDef>($"{ResearchFolder}/{id}.asset");
            BuildingDef LoadBuilding(string id) => AssetDatabase.LoadAssetAtPath<BuildingDef>($"{BuildingsFolder}/{id}.asset");
            TerrainMaterialDef LoadMaterial(string id) => AssetDatabase.LoadAssetAtPath<TerrainMaterialDef>($"{MaterialsFolder}/{id}.asset");
            var fibre = AssetDatabase.LoadAssetAtPath<ItemDef>($"{ItemsFolder}/root_fibre.asset");
            var coal = AssetDatabase.LoadAssetAtPath<ItemDef>($"{ItemsFolder}/coal.asset");

            // Material flags: what burns, what is hot, what conducts (crystal resonance, 10.5).
            EnsureFlags(LoadMaterial("mycelium"), MaterialFlags.Flammable);
            EnsureFlags(LoadMaterial("giant_fungus"), MaterialFlags.Flammable);
            EnsureFlags(LoadMaterial("magma_rock"), MaterialFlags.Hot);
            EnsureFlags(LoadMaterial("glow_crystal"), MaterialFlags.Conductive);
            if (coal != null && !coal.fuel)
            {
                coal.fuel = true;
                EditorUtility.SetDirty(coal);
            }

            // Ash: what burnt cells turn into. Soft, grey, and it falls like sand once undermined.
            var ash = Mat("ash", "Ash", 0.4f, PixelPattern.Sand, "#5a5654", "#3e3b3a", "#78736f", "#2a2726", 0.55f, MaterialFlags.Unstable);
            if (worldGen.ashMaterial == null)
            {
                worldGen.ashMaterial = ash;
                EditorUtility.SetDirty(worldGen);
            }

            var pumpCost = fibre != null ? new[] { Amt(copper, 3), Amt(fibre, 2) } : new[] { Amt(copper, 4) };
            var pump = Building("pump", "Pump", BuildingKind.Pump, 9, "#5a9ad8", 3f, speed: 2f, cost: pumpCost,
                description: "Moves water or gas (not lava) from behind it to where its spout points, 2 cells a second. It reaches 8 cells " +
                             "back and pushes up to 8 cells ahead, so it drains a flooded shaft from above and lifts water up one. Fluid can't " +
                             "pass through it; ants can. R turns it.");
            var wheelCost = fibre != null ? new[] { Amt(copper, 4), Amt(iron, 2), Amt(fibre, 4) } : new[] { Amt(copper, 4), Amt(iron, 2) };
            var wheel = Building("water_wheel", "Water Wheel", BuildingKind.WaterWheel, 17, "#7a5a36", 6f, speed: 2f,
                size: new Vector2Int(2, 2), cost: wheelCost, art: PlaceholderArt.Wheel,
                description: "Put it where water pours down: water falling onto it passes through and turns it. Machines within 8 cells " +
                             "work up to 50% faster while it turns. Needs no floor; a pump can keep it going.");
            if (wheel.machineBoost <= 0f)
            {
                wheel.machineBoost = 0.5f;
                wheel.boostRadius = 8f;
                EditorUtility.SetDirty(wheel);
            }

            // Irrigation and heat on the existing machines.
            foreach (var id in new[] { "fungus_garden", "aphid_ranch" })
            {
                var farm = LoadBuilding(id);
                if (farm == null || farm.irrigationBonus > 0f) continue;
                farm.irrigationBonus = 0.5f;
                EditorUtility.SetDirty(farm);
            }
            var smelter = LoadBuilding("smelter");
            if (smelter != null && smelter.heatBonus <= 0f)
            {
                smelter.heatBonus = 1f;
                EditorUtility.SetDirty(smelter);
            }

            // Research row 9.
            var herding = LoadResearch("aphid_herding");
            var hydraulicsCost = fibre != null ? new[] { Amt(copper, 2), Amt(fibre, 3) } : new[] { Amt(copper, 4) };
            var hydraulics = Research("hydraulics", "Hydraulics", 2, 8, 25f,
                "Unlocks the Pump: drain flooded tunnels, fill moats and water your farms (water next to a Fungus Garden or Aphid Ranch: +50% output).",
                cost: hydraulicsCost, prerequisites: herding != null ? new[] { herding } : null, effects: new[] { ResearchEffect.Unlock(pump) });
            var mills = Research("water_mills", "Water Mills", 3, 8, 40f,
                "Unlocks the Water Wheel: falling water speeds up the machines around it.",
                cost: new[] { Amt(iron, 4) }, prerequisites: new[] { hydraulics }, effects: new[] { ResearchEffect.Unlock(wheel) });

            Contract("build_pumps", "Waterworks", ContractKind.Build, 2, building: pump, knowledge: 8f, growth: 0.3f, weight: 0.7f, requires: hydraulics,
                description: "Build pumps to move water where you want it.");
            Contract("build_water_wheels", "Mill Race", ContractKind.Build, 1, building: wheel, knowledge: 12f, growth: 0.3f, weight: 0.6f, requires: mills,
                description: "Build a water wheel where water falls.");
        }

        static void EnsureFlags(TerrainMaterialDef material, MaterialFlags flags)
        {
            if (material == null || (material.flags & flags) == flags) return;
            material.flags |= flags;
            EditorUtility.SetDirty(material);
        }
    }
}
