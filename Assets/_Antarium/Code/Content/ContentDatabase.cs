using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    /// <summary>
    /// Registry asset listing all authored content. Use "Antarium/Content/Rebuild Database" in the
    /// editor to collect every definition in the project.
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Content Database", fileName = "ContentDatabase")]
    public sealed class ContentDatabase : ScriptableObject
    {
        public List<TerrainMaterialDef> materials = new();
        public List<ItemDef> items = new();
        public List<BuildingDef> buildings = new();
        public List<RecipeDef> recipes = new();
        public List<ResearchDef> research = new();
        public List<AbilityDef> abilities = new();
        public List<ContractDef> contracts = new();
        public List<RandomEventDef> events = new();
        public List<EnemyDef> enemies = new();
        [Tooltip("Creatures that come from the surface in raids (picked at random).")]
        public List<EnemyDef> raiders = new();
        public WorldGenSettings worldGen;
        [Tooltip("Food items that appear on the surface over time for workers to forage.")]
        public List<ItemDef> surfaceForage = new();
    }
}
