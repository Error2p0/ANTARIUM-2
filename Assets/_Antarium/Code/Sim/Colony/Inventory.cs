using System;
using Antarium.Content;

namespace Antarium.Sim
{
    /// <summary>
    /// Colony-wide stock. Food items become food points on delivery. Capacity comes from the royal chamber
    /// plus storage zone cells; items already being carried count against it, so ants don't pick up what
    /// cannot be delivered.
    /// </summary>
    public sealed class Inventory
    {
        readonly ContentDB _content;
        readonly ColonyConfig _config;

        public readonly int[] Items;
        public readonly int[] InTransit;
        public float Food;
        public int StorageCells { get; private set; }
        public int ItemCapacity { get; private set; }
        public float FoodCapacity { get; private set; }

        /// <summary>Set whenever stock or capacity changes in a way that affects which piles are worth hauling.</summary>
        public bool AcceptanceChanged;

        public Inventory(ContentDB content, ColonyConfig config)
        {
            _content = content;
            _config = config;
            Items = new int[content.Items.Length];
            InTransit = new int[content.Items.Length];
            Food = config.startingFood;
            SetStorageCells(0);
        }

        public void SetStorageCells(int cells)
        {
            StorageCells = cells;
            ItemCapacity = _config.baseItemCapacity + cells * _config.storageItemsPerCell;
            FoodCapacity = _config.baseFoodCapacity + cells * _config.storageFoodPerCell;
            AcceptanceChanged = true;
        }

        float FoodInTransit()
        {
            float food = 0f;
            for (int i = 0; i < InTransit.Length; i++)
                if (InTransit[i] > 0) food += InTransit[i] * _content.Items[i].foodValue;
            return food;
        }

        /// <summary>True if one more unit of <paramref name="item"/> can be picked up and delivered.</summary>
        public bool CanAccept(ushort item)
        {
            var def = _content.Items[item];
            if (def.IsFood) return Food + FoodInTransit() + def.foodValue <= FoodCapacity + 1e-3f;
            return Items[item] + InTransit[item] < ItemCapacity;
        }

        public void BeginTransit(ushort item) => InTransit[item]++;

        /// <summary>Puts down a carried item. Returns false (and keeps it in transit) if it no longer fits.</summary>
        public bool Deliver(ushort item)
        {
            var def = _content.Items[item];
            InTransit[item] = Math.Max(0, InTransit[item] - 1);
            bool fits = def.IsFood ? Food + def.foodValue <= FoodCapacity + 1e-3f : Items[item] < ItemCapacity;
            if (!fits)
            {
                InTransit[item]++;
                return false;
            }
            if (def.IsFood) Food += def.foodValue;
            else Items[item]++;
            if (!CanAccept(item)) AcceptanceChanged = true; // just filled up: stop hauling this type
            return true;
        }

        /// <summary>A carrying ant was lost or dropped its load; release the reservation.</summary>
        public void CancelTransit(ushort item) => InTransit[item] = Math.Max(0, InTransit[item] - 1);

        /// <summary>Consumes food; returns false if there wasn't enough (stock is clamped at zero).</summary>
        public bool ConsumeFood(float amount)
        {
            bool wasFull = Food >= FoodCapacity - 1e-3f;
            Food -= amount;
            if (wasFull) AcceptanceChanged = true;
            if (Food >= 0f) return true;
            Food = 0f;
            return false;
        }
    }
}
