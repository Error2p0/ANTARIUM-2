using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    [Serializable]
    public struct ItemAmount
    {
        public ItemDef item;
        [Min(1)] public int count;

        public ItemAmount(ItemDef item, int count)
        {
            this.item = item;
            this.count = count;
        }
    }

    /// <summary>One processing step of a building: consumes inputs, waits, produces outputs.</summary>
    [CreateAssetMenu(menuName = "Antarium/Recipe", fileName = "Recipe")]
    public sealed class RecipeDef : ContentDef
    {
        public List<ItemAmount> inputs = new();
        public List<ItemAmount> outputs = new();
        [Tooltip("Seconds per craft.")]
        [Min(0.1f)] public float duration = 5f;
        [Tooltip("Knowledge added to the colony per craft (studying). A recipe needs outputs or knowledge.")]
        [Min(0f)] public float knowledge;
    }
}
