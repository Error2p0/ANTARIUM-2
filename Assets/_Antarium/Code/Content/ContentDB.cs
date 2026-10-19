using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Content
{
    /// <summary>Blittable per-material data for Burst jobs.</summary>
    public struct MaterialSim
    {
        public float Hardness;
        public MaterialFlags Flags;
        /// <summary>Emitted light per channel as a fraction of the max light level.</summary>
        public float3 Emission;
        /// <summary>Item index dropped when dug out, or <see cref="ContentDB.NoItem"/>.</summary>
        public ushort DropItem;
        public ushort DropAmount;
        public float DropChance;

        public bool IsAir => (Flags & MaterialFlags.Air) != 0;
        public bool IsIndestructible => (Flags & MaterialFlags.Indestructible) != 0;
    }

    /// <summary>Baked recipe: dense item indices instead of asset references.</summary>
    public sealed class RecipeData
    {
        public RecipeDef Def;
        public ushort[] InputItems;
        public int[] InputCounts;
        public ushort[] OutputItems;
        public int[] OutputCounts;
        public float Duration;
    }

    /// <summary>Baked building: dense cost and recipe indices.</summary>
    public sealed class BuildingData
    {
        public BuildingDef Def;
        public ushort[] CostItems;
        public int[] CostCounts;
        /// <summary>Indices into <see cref="ContentDB.Recipes"/>.</summary>
        public int[] Recipes;
        /// <summary>Distinct items consumed by any of the recipes, and the buffer size for each.</summary>
        public ushort[] InputItems;
        public int[] InputCaps;
        /// <summary>Bit per item index: items this building accepts as input.</summary>
        public ulong InputMask;
    }

    /// <summary>
    /// Runtime content baked from authored definitions: dense ushort indices (Air is always material 0)
    /// plus native tables for jobs. Saves must store ids, not indices; see <see cref="TryGetMaterialIndex"/>.
    /// </summary>
    public sealed class ContentDB : IDisposable
    {
        public const ushort AirIndex = 0;
        public const ushort NoItem = ushort.MaxValue;
        /// <summary>Items are tracked in 64-bit masks by the transport network.</summary>
        public const int MaxItems = 64;

        public readonly TerrainMaterialDef[] Materials;
        public readonly ItemDef[] Items;
        public readonly ushort[] SurfaceForage;
        public readonly WorldGenSettings WorldGen;
        public NativeArray<MaterialSim> MaterialSim;

        /// <summary>Buildings in palette order.</summary>
        public readonly BuildingData[] Buildings;
        public readonly RecipeData[] Recipes;
        /// <summary>Per item: index of its consumer flow field, or -1 if no building consumes it.</summary>
        public readonly int[] ConsumerSlot;
        public readonly int ConsumerSlotCount;

        readonly Dictionary<string, ushort> _materialIndex = new();
        readonly Dictionary<string, ushort> _itemIndex = new();
        readonly Dictionary<string, int> _buildingIndex = new();

        ContentDB(TerrainMaterialDef[] materials, ItemDef[] items, IReadOnlyList<ItemDef> forage, WorldGenSettings worldGen,
            IReadOnlyList<BuildingDef> buildings)
        {
            Materials = materials;
            Items = items;
            WorldGen = worldGen;
            for (int i = 0; i < items.Length; i++)
                _itemIndex.Add(items[i].Id, (ushort)i);

            MaterialSim = new NativeArray<MaterialSim>(materials.Length, Allocator.Persistent);
            for (int i = 0; i < materials.Length; i++)
            {
                var def = materials[i];
                _materialIndex.Add(def.Id, (ushort)i);
                var c = def.emissionColor;
                MaterialSim[i] = new MaterialSim
                {
                    Hardness = def.hardness,
                    Flags = def.flags,
                    Emission = new float3(c.r, c.g, c.b) * def.emissionStrength,
                    DropItem = def.drop != null ? GetItemIndex(def.drop) : NoItem,
                    DropAmount = (ushort)math.clamp(def.dropAmount, 1, ushort.MaxValue),
                    DropChance = def.dropChance,
                };
            }

            SurfaceForage = new ushort[forage?.Count ?? 0];
            for (int i = 0; i < SurfaceForage.Length; i++)
                SurfaceForage[i] = GetItemIndex(forage[i]);

            BakeBuildings(buildings, out Buildings, out Recipes);
            ConsumerSlot = new int[items.Length];
            for (int i = 0; i < ConsumerSlot.Length; i++) ConsumerSlot[i] = -1;
            foreach (var b in Buildings)
            foreach (ushort item in b.InputItems)
                if (ConsumerSlot[item] < 0) ConsumerSlot[item] = ConsumerSlotCount++;
        }

        void BakeBuildings(IReadOnlyList<BuildingDef> defs, out BuildingData[] buildings, out RecipeData[] recipes)
        {
            var ordered = new List<BuildingDef>();
            var ids = new HashSet<string>();
            if (defs != null)
            {
                foreach (var def in defs)
                {
                    if (def == null) throw new ArgumentException("Building list contains an empty entry.");
                    if (!ids.Add(def.Id)) throw new ArgumentException($"Duplicate building id '{def.Id}'.");
                    ordered.Add(def);
                }
            }
            ordered.Sort((a, b) => a.paletteOrder != b.paletteOrder ? a.paletteOrder.CompareTo(b.paletteOrder) : string.CompareOrdinal(a.Id, b.Id));

            var recipeList = new List<RecipeData>();
            var recipeIndex = new Dictionary<RecipeDef, int>();
            buildings = new BuildingData[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                var def = ordered[i];
                _buildingIndex.Add(def.Id, i);
                var data = new BuildingData { Def = def };
                Amounts(def.cost, $"building '{def.Id}' cost", out data.CostItems, out data.CostCounts);

                var used = new List<int>();
                var inputItems = new List<ushort>();
                var inputCaps = new List<int>();
                if (def.kind == BuildingKind.Processor)
                {
                    foreach (var recipe in def.recipes)
                    {
                        if (recipe == null) throw new ArgumentException($"Building '{def.Id}' has an empty recipe entry.");
                        if (!recipeIndex.TryGetValue(recipe, out int r))
                        {
                            r = recipeList.Count;
                            recipeIndex.Add(recipe, r);
                            recipeList.Add(BakeRecipe(recipe));
                        }
                        used.Add(r);
                        var baked = recipeList[r];
                        for (int k = 0; k < baked.InputItems.Length; k++)
                        {
                            int cap = baked.InputCounts[k] * math.max(1, def.inputBatches);
                            int at = inputItems.IndexOf(baked.InputItems[k]);
                            if (at < 0)
                            {
                                inputItems.Add(baked.InputItems[k]);
                                inputCaps.Add(cap);
                            }
                            else inputCaps[at] = math.max(inputCaps[at], cap);
                        }
                    }
                }
                data.Recipes = used.ToArray();
                data.InputItems = inputItems.ToArray();
                data.InputCaps = inputCaps.ToArray();
                foreach (ushort item in data.InputItems) data.InputMask |= 1ul << item;
                buildings[i] = data;
            }
            recipes = recipeList.ToArray();
        }

        RecipeData BakeRecipe(RecipeDef def)
        {
            var data = new RecipeData { Def = def, Duration = math.max(0.1f, def.duration) };
            Amounts(def.inputs, $"recipe '{def.Id}' inputs", out data.InputItems, out data.InputCounts);
            Amounts(def.outputs, $"recipe '{def.Id}' outputs", out data.OutputItems, out data.OutputCounts);
            if (data.OutputItems.Length == 0) throw new ArgumentException($"Recipe '{def.Id}' has no outputs.");
            return data;
        }

        void Amounts(List<ItemAmount> list, string what, out ushort[] items, out int[] counts)
        {
            int n = list?.Count ?? 0;
            items = new ushort[n];
            counts = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (list[i].item == null) throw new ArgumentException($"Empty item in {what}.");
                items[i] = GetItemIndex(list[i].item);
                counts[i] = math.max(1, list[i].count);
            }
        }

        public static ContentDB Build(ContentDatabase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            return Build(database.materials, database.worldGen, database.items, database.surfaceForage, database.buildings);
        }

        public static ContentDB Build(IReadOnlyList<TerrainMaterialDef> materials, WorldGenSettings worldGen,
            IReadOnlyList<ItemDef> items = null, IReadOnlyList<ItemDef> surfaceForage = null, IReadOnlyList<BuildingDef> buildings = null)
        {
            if (materials == null) throw new ArgumentNullException(nameof(materials));
            if (worldGen == null) throw new ArgumentException("ContentDatabase has no WorldGenSettings.");

            var ordered = new List<TerrainMaterialDef>(materials.Count);
            var ids = new HashSet<string>();
            TerrainMaterialDef air = null;
            foreach (var def in materials)
            {
                if (def == null) throw new ArgumentException("Material list contains an empty entry.");
                if (string.IsNullOrEmpty(def.Id)) throw new ArgumentException($"Material '{def.name}' has no id.");
                if (!ids.Add(def.Id)) throw new ArgumentException($"Duplicate material id '{def.Id}'.");
                if (def.IsAir)
                {
                    if (air != null) throw new ArgumentException($"Both '{air.Id}' and '{def.Id}' are flagged Air.");
                    air = def;
                }
                else ordered.Add(def);
            }
            if (air == null) throw new ArgumentException("No material is flagged Air.");
            if (ordered.Count + 1 > ushort.MaxValue) throw new ArgumentException("Too many materials.");
            ordered.Insert(AirIndex, air);

            var itemList = new List<ItemDef>();
            var itemIds = new HashSet<string>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null) throw new ArgumentException("Item list contains an empty entry.");
                    if (!itemIds.Add(item.Id)) throw new ArgumentException($"Duplicate item id '{item.Id}'.");
                    itemList.Add(item);
                }
            }
            if (itemList.Count > MaxItems) throw new ArgumentException($"Too many items (max {MaxItems}).");

            var db = new ContentDB(ordered.ToArray(), itemList.ToArray(), surfaceForage, worldGen, buildings);
            db.ValidateWorldGen();
            return db;
        }

        public bool TryGetMaterialIndex(string id, out ushort index) => _materialIndex.TryGetValue(id, out index);
        public bool TryGetItemIndex(string id, out ushort index) => _itemIndex.TryGetValue(id, out index);
        public bool TryGetBuildingIndex(string id, out int index) => _buildingIndex.TryGetValue(id, out index);

        public int GetBuildingIndex(BuildingDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!_buildingIndex.TryGetValue(def.Id, out var index))
                throw new KeyNotFoundException($"Building '{def.Id}' is not registered in the ContentDatabase.");
            return index;
        }

        public ushort GetMaterialIndex(TerrainMaterialDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!_materialIndex.TryGetValue(def.Id, out var index))
                throw new KeyNotFoundException($"Material '{def.Id}' is not registered in the ContentDatabase.");
            return index;
        }

        public ushort GetItemIndex(ItemDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!_itemIndex.TryGetValue(def.Id, out var index))
                throw new KeyNotFoundException($"Item '{def.Id}' is not registered in the ContentDatabase.");
            return index;
        }

        void ValidateWorldGen()
        {
            var gen = WorldGen;
            if (gen.air == null || !gen.air.IsAir) throw new ArgumentException("WorldGenSettings.air must be the Air material.");
            GetMaterialIndex(gen.surfaceMaterial);
            GetMaterialIndex(gen.bedrock);
            if (gen.biomes.Count == 0) throw new ArgumentException("WorldGenSettings has no biomes.");
            foreach (var biome in gen.biomes)
            {
                if (biome == null) throw new ArgumentException("WorldGenSettings.biomes contains an empty entry.");
                GetMaterialIndex(biome.baseMaterial);
                foreach (var deposit in biome.deposits)
                    GetMaterialIndex(deposit.material);
            }
        }

        public void Dispose()
        {
            if (MaterialSim.IsCreated) MaterialSim.Dispose();
        }
    }
}
