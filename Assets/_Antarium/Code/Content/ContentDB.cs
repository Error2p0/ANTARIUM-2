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
        public bool IsUnstable => (Flags & MaterialFlags.Unstable) != 0;
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
        /// <summary>Knowledge added per craft.</summary>
        public float Knowledge;
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
        /// <summary>Wall: the material the cell becomes when finished.</summary>
        public ushort FillMaterial;
    }

    /// <summary>Baked enemy: dense loot indices.</summary>
    public sealed class EnemyData
    {
        public EnemyDef Def;
        public ushort[] DropItems;
        public int[] DropCounts;
    }

    /// <summary>Baked biome extras used at runtime (creatures).</summary>
    public sealed class BiomeData
    {
        public BiomeDef Def;
        /// <summary>Indices into <see cref="ContentDB.Enemies"/>.</summary>
        public int[] Creatures;
    }

    /// <summary>Content of the incremental layer (research, abilities, contracts, random events).</summary>
    public sealed class ProgressionContent
    {
        public IReadOnlyList<ResearchDef> Research;
        public IReadOnlyList<AbilityDef> Abilities;
        public IReadOnlyList<ContractDef> Contracts;
        public IReadOnlyList<RandomEventDef> Events;
        public IReadOnlyList<EnemyDef> Enemies;
        public IReadOnlyList<EnemyDef> Raiders;
    }

    /// <summary>Baked research node: dense indices.</summary>
    public sealed class ResearchData
    {
        public ResearchDef Def;
        /// <summary>Indices into <see cref="ContentDB.Research"/>.</summary>
        public int[] Prerequisites;
        public ushort[] CostItems;
        public int[] CostCounts;
        /// <summary>Stat effects only; unlocks are in <see cref="UnlockBuildings"/> / <see cref="UnlockAbilities"/>.</summary>
        public ResearchEffect[] Stats;
        public int[] UnlockBuildings;
        public int[] UnlockAbilities;
        public AntCaste[] UnlockCastes;
    }

    public sealed class ContractData
    {
        public ContractDef Def;
        /// <summary>Item for Deliver / Craft, else <see cref="ContentDB.NoItem"/>.</summary>
        public ushort Item;
        /// <summary>Building for Build, else -1.</summary>
        public int Building;
        /// <summary>Research that must be done first, else -1.</summary>
        public int Requires;
        public ushort[] RewardItems;
        public int[] RewardCounts;
    }

    public sealed class RandomEventData
    {
        public RandomEventDef Def;
        public ushort[] Materials;
        public ushort[] RewardItems;
        public int[] RewardCounts;
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

        /// <summary>Research nodes ordered by tree position.</summary>
        public readonly ResearchData[] Research;
        /// <summary>Abilities in bar order.</summary>
        public readonly AbilityDef[] Abilities;
        public readonly ContractData[] Contracts;
        public readonly RandomEventData[] Events;
        public readonly EnemyData[] Enemies;
        /// <summary>Indices into <see cref="Enemies"/> of the creatures that raid from the surface.</summary>
        public readonly int[] Raiders;
        /// <summary>Biomes top to bottom (same order as <c>WorldGen.biomes</c>).</summary>
        public readonly BiomeData[] Biomes;
        /// <summary>Material lava turns into where it meets water.</summary>
        public readonly ushort FluidReactionMaterial;
        /// <summary>Per building / ability: true if some research unlocks it (it starts locked).</summary>
        public readonly bool[] BuildingStartsLocked;
        public readonly bool[] AbilityStartsLocked;

        readonly Dictionary<string, ushort> _materialIndex = new();
        readonly Dictionary<string, ushort> _itemIndex = new();
        readonly Dictionary<string, int> _buildingIndex = new();
        readonly Dictionary<string, int> _recipeIndex = new();
        readonly Dictionary<string, int> _researchIndex = new();
        readonly Dictionary<string, int> _abilityIndex = new();
        readonly Dictionary<string, int> _contractIndex = new();
        readonly Dictionary<string, int> _eventIndex = new();
        readonly Dictionary<string, int> _enemyIndex = new();

        ContentDB(TerrainMaterialDef[] materials, ItemDef[] items, IReadOnlyList<ItemDef> forage, WorldGenSettings worldGen,
            IReadOnlyList<BuildingDef> buildings, ProgressionContent progression)
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
            for (int i = 0; i < Recipes.Length; i++)
                _recipeIndex[Recipes[i].Def.Id] = i;

            progression ??= new ProgressionContent();
            Enemies = BakeEnemies(progression.Enemies, progression.Raiders, worldGen, out Raiders);
            Abilities = Sorted(progression.Abilities, "ability", (a, b) => a.order != b.order ? a.order.CompareTo(b.order) : string.CompareOrdinal(a.Id, b.Id));
            for (int i = 0; i < Abilities.Length; i++) _abilityIndex.Add(Abilities[i].Id, i);
            BuildingStartsLocked = new bool[Buildings.Length];
            AbilityStartsLocked = new bool[Abilities.Length];
            Research = BakeResearch(progression.Research);
            Contracts = BakeContracts(progression.Contracts);
            Events = BakeEvents(progression.Events);

            Biomes = new BiomeData[worldGen.biomes.Count];
            for (int i = 0; i < Biomes.Length; i++)
            {
                var biome = worldGen.biomes[i];
                var creatures = new List<int>();
                if (biome != null)
                    foreach (var c in biome.creatures)
                        if (c != null) creatures.Add(GetEnemyIndex(c));
                Biomes[i] = new BiomeData { Def = biome, Creatures = creatures.ToArray() };
            }
            FluidReactionMaterial = worldGen.fluidReactionMaterial != null && _materialIndex.TryGetValue(worldGen.fluidReactionMaterial.Id, out var reaction)
                ? reaction : AirIndex;
        }

        /// <summary>
        /// Enemies: every listed one, plus raiders and biome creatures that are not in the list (tests and
        /// hand-made databases only list them there).
        /// </summary>
        EnemyData[] BakeEnemies(IReadOnlyList<EnemyDef> listed, IReadOnlyList<EnemyDef> raiders, WorldGenSettings worldGen, out int[] raiderIndices)
        {
            var all = new List<EnemyDef>();
            var ids = new HashSet<string>();
            void Collect(EnemyDef def)
            {
                if (def != null && ids.Add(def.Id)) all.Add(def);
            }
            if (listed != null) foreach (var e in listed) Collect(e);
            if (raiders != null) foreach (var e in raiders) Collect(e);
            foreach (var b in worldGen.biomes)
                if (b != null) foreach (var e in b.creatures) Collect(e);
            all.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            var result = new EnemyData[all.Count];
            for (int i = 0; i < all.Count; i++)
            {
                _enemyIndex.Add(all[i].Id, i);
                var data = new EnemyData { Def = all[i] };
                Amounts(all[i].drops, $"enemy '{all[i].Id}' drops", out data.DropItems, out data.DropCounts);
                result[i] = data;
            }
            var raiderList = new List<int>();
            if (raiders != null)
                foreach (var e in raiders)
                    if (e != null && !raiderList.Contains(_enemyIndex[e.Id])) raiderList.Add(_enemyIndex[e.Id]);
            raiderIndices = raiderList.ToArray();
            return result;
        }

        public int GetEnemyIndex(EnemyDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!_enemyIndex.TryGetValue(def.Id, out var index))
                throw new KeyNotFoundException($"Enemy '{def.Id}' is not registered in the ContentDatabase.");
            return index;
        }

        static T[] Sorted<T>(IReadOnlyList<T> defs, string what, Comparison<T> order) where T : ContentDef
        {
            var list = new List<T>();
            var ids = new HashSet<string>();
            if (defs != null)
            {
                foreach (var def in defs)
                {
                    if (def == null) throw new ArgumentException($"The {what} list contains an empty entry.");
                    if (!ids.Add(def.Id)) throw new ArgumentException($"Duplicate {what} id '{def.Id}'.");
                    list.Add(def);
                }
            }
            list.Sort(order);
            return list.ToArray();
        }

        ResearchData[] BakeResearch(IReadOnlyList<ResearchDef> defs)
        {
            var sorted = Sorted(defs, "research", (a, b) =>
                a.treePosition.x != b.treePosition.x ? a.treePosition.x.CompareTo(b.treePosition.x)
                : a.treePosition.y != b.treePosition.y ? a.treePosition.y.CompareTo(b.treePosition.y)
                : string.CompareOrdinal(a.Id, b.Id));
            for (int i = 0; i < sorted.Length; i++) _researchIndex.Add(sorted[i].Id, i);

            var result = new ResearchData[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                var def = sorted[i];
                var data = new ResearchData { Def = def };
                Amounts(def.cost, $"research '{def.Id}' cost", out data.CostItems, out data.CostCounts);
                var prereqs = new List<int>();
                foreach (var p in def.prerequisites)
                {
                    if (p == null) continue;
                    if (!_researchIndex.TryGetValue(p.Id, out int pi))
                        throw new ArgumentException($"Research '{def.Id}' needs '{p.Id}', which is not registered.");
                    prereqs.Add(pi);
                }
                data.Prerequisites = prereqs.ToArray();

                var stats = new List<ResearchEffect>();
                var buildings = new List<int>();
                var abilities = new List<int>();
                var castes = new List<AntCaste>();
                foreach (var e in def.effects)
                {
                    switch (e.kind)
                    {
                        case ResearchEffectKind.Stat: stats.Add(e); break;
                        case ResearchEffectKind.UnlockBuilding:
                        {
                            int b = GetBuildingIndex(e.building);
                            buildings.Add(b);
                            BuildingStartsLocked[b] = true;
                            break;
                        }
                        case ResearchEffectKind.UnlockAbility:
                        {
                            if (e.ability == null || !_abilityIndex.TryGetValue(e.ability.Id, out int a))
                                throw new ArgumentException($"Research '{def.Id}' unlocks an ability that is not registered.");
                            abilities.Add(a);
                            AbilityStartsLocked[a] = true;
                            break;
                        }
                        case ResearchEffectKind.UnlockCaste:
                            if (!castes.Contains(e.caste)) castes.Add(e.caste);
                            break;
                    }
                }
                data.Stats = stats.ToArray();
                data.UnlockBuildings = buildings.ToArray();
                data.UnlockAbilities = abilities.ToArray();
                data.UnlockCastes = castes.ToArray();
                result[i] = data;
            }
            return result;
        }

        ContractData[] BakeContracts(IReadOnlyList<ContractDef> defs)
        {
            var sorted = Sorted(defs, "contract", (a, b) => a.introOrder != b.introOrder ? a.introOrder.CompareTo(b.introOrder) : string.CompareOrdinal(a.Id, b.Id));
            var result = new ContractData[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                var def = sorted[i];
                _contractIndex.Add(def.Id, i);
                var data = new ContractData { Def = def, Item = NoItem, Building = -1, Requires = -1 };
                if (def.kind == ContractKind.Deliver || def.kind == ContractKind.Craft)
                {
                    if (def.item == null) throw new ArgumentException($"Contract '{def.Id}' needs an item.");
                    data.Item = GetItemIndex(def.item);
                }
                if (def.kind == ContractKind.Build) data.Building = GetBuildingIndex(def.building);
                if (def.requires != null && !_researchIndex.TryGetValue(def.requires.Id, out data.Requires))
                    throw new ArgumentException($"Contract '{def.Id}' requires research '{def.requires.Id}', which is not registered.");
                Amounts(def.rewards, $"contract '{def.Id}' rewards", out data.RewardItems, out data.RewardCounts);
                result[i] = data;
            }
            return result;
        }

        RandomEventData[] BakeEvents(IReadOnlyList<RandomEventDef> defs)
        {
            var sorted = Sorted(defs, "event", (a, b) => string.CompareOrdinal(a.Id, b.Id));
            var result = new RandomEventData[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                var def = sorted[i];
                _eventIndex.Add(def.Id, i);
                var data = new RandomEventData { Def = def, Materials = new ushort[def.materials.Count] };
                for (int k = 0; k < def.materials.Count; k++)
                    data.Materials[k] = GetMaterialIndex(def.materials[k]);
                if ((def.kind == RandomEventKind.Meteorite || def.kind == RandomEventKind.RichVein) && data.Materials.Length == 0)
                    throw new ArgumentException($"Event '{def.Id}' needs at least one material.");
                Amounts(def.rewards, $"event '{def.Id}' rewards", out data.RewardItems, out data.RewardCounts);
                result[i] = data;
            }
            return result;
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
                if (def.kind == BuildingKind.Wall)
                {
                    if (def.fillMaterial == null) throw new ArgumentException($"Wall '{def.Id}' needs a fill material.");
                    data.FillMaterial = GetMaterialIndex(def.fillMaterial);
                }
                buildings[i] = data;
            }
            recipes = recipeList.ToArray();
        }

        RecipeData BakeRecipe(RecipeDef def)
        {
            var data = new RecipeData { Def = def, Duration = math.max(0.1f, def.duration), Knowledge = math.max(0f, def.knowledge) };
            Amounts(def.inputs, $"recipe '{def.Id}' inputs", out data.InputItems, out data.InputCounts);
            Amounts(def.outputs, $"recipe '{def.Id}' outputs", out data.OutputItems, out data.OutputCounts);
            if (data.OutputItems.Length == 0 && data.Knowledge <= 0f) throw new ArgumentException($"Recipe '{def.Id}' has no outputs.");
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
            return Build(database.materials, database.worldGen, database.items, database.surfaceForage, database.buildings,
                new ProgressionContent
                {
                    Research = database.research,
                    Abilities = database.abilities,
                    Contracts = database.contracts,
                    Events = database.events,
                    Enemies = database.enemies,
                    Raiders = database.raiders,
                });
        }

        public static ContentDB Build(IReadOnlyList<TerrainMaterialDef> materials, WorldGenSettings worldGen,
            IReadOnlyList<ItemDef> items = null, IReadOnlyList<ItemDef> surfaceForage = null, IReadOnlyList<BuildingDef> buildings = null,
            ProgressionContent progression = null)
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

            var db = new ContentDB(ordered.ToArray(), itemList.ToArray(), surfaceForage, worldGen, buildings, progression);
            db.ValidateWorldGen();
            return db;
        }

        public bool TryGetMaterialIndex(string id, out ushort index) => _materialIndex.TryGetValue(id, out index);
        public bool TryGetItemIndex(string id, out ushort index) => _itemIndex.TryGetValue(id, out index);
        public bool TryGetBuildingIndex(string id, out int index) => _buildingIndex.TryGetValue(id, out index);
        public bool TryGetRecipeIndex(string id, out int index) => _recipeIndex.TryGetValue(id, out index);
        public bool TryGetResearchIndex(string id, out int index) => _researchIndex.TryGetValue(id, out index);
        public bool TryGetAbilityIndex(string id, out int index) => _abilityIndex.TryGetValue(id, out index);
        public bool TryGetContractIndex(string id, out int index) => _contractIndex.TryGetValue(id, out index);
        public bool TryGetEventIndex(string id, out int index) => _eventIndex.TryGetValue(id, out index);
        public bool TryGetEnemyIndex(string id, out int index) => _enemyIndex.TryGetValue(id, out index);

        /// <summary>Index of the biome (top to bottom) at <paramref name="depth"/> cells below the surface, ignoring border warp.</summary>
        public int BiomeAtDepth(int depth)
        {
            var biomes = WorldGen.biomes;
            int top = 0;
            for (int i = 0; i < biomes.Count - 1; i++)
            {
                top += biomes[i].thickness;
                if (depth < top) return i;
            }
            return math.max(0, biomes.Count - 1);
        }

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
