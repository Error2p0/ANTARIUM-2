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
        public WorldGenSettings worldGen;
        [Tooltip("Food items that appear on the surface over time for workers to forage.")]
        public List<ItemDef> surfaceForage = new();
    }
}
