using System;
using Antarium.Content;

namespace Antarium.Sim
{
    /// <summary>
    /// Colony-wide stock. Food items become food points on delivery. Capacity comes from the royal chamber
    /// plus storage zone cells; items already being carried (or riding belts) count against it, so ants
    /// don't pick up what cannot be delivered. Machines that need an item add their free buffer space as
    /// <see cref="ConsumerRoom"/>, so items worth delivering to a machine are hauled even when the stock is full.
    /// </summary>
    public sealed class Inventory
    {
        readonly ContentDB _content;
        readonly ColonyConfig _config;

        public readonly int[] Items;
        public readonly int[] InTransit;
        /// <summary>Free input buffer space for each item over all machines (set by the building system).</summary>
        public readonly int[] ConsumerRoom;
        public float Food;
        public int StorageCells { get; private set; }
        public int ItemCapacity { get; private set; }
        public float FoodCapacity { get; private set; }

        /// <summary>Smoothed items (or food points for food) per minute delivered into the stock.</summary>
        public readonly float[] IncomePerMinute;
        public float FoodIncomePerMinute { get; private set; }
        readonly int[] _delivered;
        float _foodDelivered;
        float _rateTimer;

        /// <summary>Set whenever stock or capacity changes in a way that affects which piles are worth hauling.</summary>
        public bool AcceptanceChanged;

        public Inventory(ContentDB content, ColonyConfig config)
        {
            _content = content;
            _config = config;
            int n = content.Items.Length;
            Items = new int[n];
            InTransit = new int[n];
            ConsumerRoom = new int[n];
            IncomePerMinute = new float[n];
            _delivered = new int[n];
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

        /// <summary>Updates machine demand. Returns true if the item went from no demand to some or back.</summary>
        public bool SetConsumerRoom(ushort item, int room)
        {
            int old = ConsumerRoom[item];
            if (old == room) return false;
            ConsumerRoom[item] = room;
            bool crossed = (old == 0) != (room == 0);
            AcceptanceChanged |= crossed || room > old;
            return crossed;
        }

        float FoodInTransit()
        {
            float food = 0f;
            for (int i = 0; i < InTransit.Length; i++)
                if (InTransit[i] > 0) food += InTransit[i] * _content.Items[i].foodValue;
            return food;
        }

        /// <summary>
        /// True if one more unit of <paramref name="item"/> can be picked up and delivered (stock or machines).
        /// Stock room is clamped at zero: an over-full stock (refunds, cheats) must not cancel machine demand.
        /// </summary>
        public bool CanAccept(ushort item)
        {
            var def = _content.Items[item];
            if (def.IsFood)
                return FoodInTransit() + def.foodValue <= Math.Max(0f, FoodCapacity - Food) + ConsumerRoom[item] * def.foodValue + 1e-3f;
            return InTransit[item] < Math.Max(0, ItemCapacity - Items[item]) + ConsumerRoom[item];
        }

        /// <summary>True if the stock itself has room for one more <paramref name="item"/> right now.</summary>
        public bool StockAccepts(ushort item)
        {
            var def = _content.Items[item];
            return def.IsFood ? Food + def.foodValue <= FoodCapacity + 1e-3f : Items[item] < ItemCapacity;
        }

        public void BeginTransit(ushort item) => InTransit[item]++;

        /// <summary>Puts a carried item into the stock. Returns false (and keeps it in transit) if it no longer fits.</summary>
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
            if (def.IsFood)
            {
                Food += def.foodValue;
                _foodDelivered += def.foodValue;
            }
            else Items[item]++;
            _delivered[item]++;
            if (!CanAccept(item)) AcceptanceChanged = true; // just filled up: stop hauling this type
            return true;
        }

        /// <summary>A carried item left the colony's hands without reaching the stock; release the reservation.</summary>
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

        public bool CanAfford(BuildingData building)
        {
            for (int i = 0; i < building.CostItems.Length; i++)
                if (Items[building.CostItems[i]] < building.CostCounts[i]) return false;
            return Food + 1e-3f >= building.Def.foodCost;
        }

        public void Spend(BuildingData building)
        {
            for (int i = 0; i < building.CostItems.Length; i++)
                Items[building.CostItems[i]] -= building.CostCounts[i];
            Food = Math.Max(0f, Food - building.Def.foodCost);
            AcceptanceChanged = true;
        }

        /// <summary>Returns a share of a building's cost. May exceed capacity; nothing is thrown away.</summary>
        public void Refund(BuildingData building, float fraction)
        {
            for (int i = 0; i < building.CostItems.Length; i++)
                Items[building.CostItems[i]] += (int)Math.Floor(building.CostCounts[i] * fraction + 1e-4f);
            Food = Math.Min(FoodCapacity, Food + building.Def.foodCost * fraction);
            AcceptanceChanged = true;
        }

        /// <summary>Folds the deliveries of the last second into the income meters.</summary>
        public void UpdateRates(float dt, float smoothing)
        {
            _rateTimer += dt;
            if (_rateTimer < 1f) return;
            float scale = 60f / _rateTimer;
            for (int i = 0; i < _delivered.Length; i++)
            {
                IncomePerMinute[i] += (_delivered[i] * scale - IncomePerMinute[i]) * smoothing;
                _delivered[i] = 0;
            }
            FoodIncomePerMinute += (_foodDelivered * scale - FoodIncomePerMinute) * smoothing;
            _foodDelivered = 0f;
            _rateTimer = 0f;
        }
    }
}
