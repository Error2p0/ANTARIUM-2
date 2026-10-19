using System;
using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// One place the stock is physically kept: the royal chamber (index 0 in <see cref="Inventory.Containers"/>)
    /// or a single storage zone cell. Holds up to <see cref="Capacity"/> items of any type.
    /// </summary>
    public sealed class StockContainer
    {
        /// <summary>The storage cell, or the queen's cell for the royal chamber.</summary>
        public int2 Cell;
        /// <summary>World cell index of the storage cell; -1 for the royal chamber.</summary>
        public int WorldIndex;
        public int Capacity;
        public int Total;
        public readonly int[] Counts;

        public StockContainer(int2 cell, int worldIndex, int capacity, int itemTypes)
        {
            Cell = cell;
            WorldIndex = worldIndex;
            Capacity = capacity;
            Counts = new int[itemTypes];
        }

        public bool IsRoyal => WorldIndex < 0;
        public int Free => Capacity - Total;
    }

    /// <summary>
    /// Colony-wide stock, kept physically in containers: the royal chamber and every storage zone cell each
    /// hold a number of items of any type (shown in the world). Food items become food points on delivery and
    /// have their own capacity. Items already being carried (or riding belts) count against the free space,
    /// so ants don't pick up what cannot be delivered. Machines that need an item add their free buffer space
    /// as <see cref="ConsumerRoom"/>, so items worth delivering to a machine are hauled even when the stock is full.
    /// </summary>
    public sealed class Inventory
    {
        readonly ContentDB _content;
        readonly ColonyConfig _config;
        readonly Modifiers _mods;
        readonly ColonyStats _stats;
        readonly ProductionMeter _meter;
        readonly Dictionary<int, int> _containerAt = new();

        /// <summary>Total stored per item over all containers. Read only: change the stock through Store/Add/Take.</summary>
        public readonly int[] Items;
        public readonly int[] InTransit;
        /// <summary>Free input buffer space for each item over all machines (set by the building system).</summary>
        public readonly int[] ConsumerRoom;
        /// <summary>Royal chamber first, then one per storage cell.</summary>
        public readonly List<StockContainer> Containers = new();
        public float Food;
        /// <summary>Research currency: from studying in the Archive, contracts and events.</summary>
        public float Knowledge;
        public int Capacity { get; private set; }
        public int Stored { get; private set; }
        public float FoodCapacity { get; private set; }
        public int StorageCells => Containers.Count - 1;
        public StockContainer Royal => Containers[0];

        /// <summary>Smoothed items (or food points for food) per minute delivered into the stock.</summary>
        public readonly float[] IncomePerMinute;
        public float FoodIncomePerMinute { get; private set; }
        readonly int[] _delivered;
        float _foodDelivered;
        float _rateTimer;

        /// <summary>Set whenever stock or capacity changes in a way that affects which piles are worth hauling.</summary>
        public bool AcceptanceChanged;
        /// <summary>Bit per item whose set of containers holding it changed (supply flow fields must be rebuilt).</summary>
        public ulong LocationsChanged = ulong.MaxValue;

        /// <summary>A stand-alone stock without research or stats (tests, tools).</summary>
        public Inventory(ContentDB content, ColonyConfig config)
            : this(content, config, new Modifiers(), new ColonyStats(content.Items.Length, content.Buildings.Length), new ProductionMeter(content.Items.Length)) { }

        public Inventory(ContentDB content, ColonyConfig config, Modifiers mods, ColonyStats stats, ProductionMeter meter)
        {
            _content = content;
            _config = config;
            _mods = mods;
            _stats = stats;
            _meter = meter;
            int n = content.Items.Length;
            Items = new int[n];
            InTransit = new int[n];
            ConsumerRoom = new int[n];
            IncomePerMinute = new float[n];
            _delivered = new int[n];
            Food = config.startingFood;
            Containers.Add(new StockContainer(default, -1, 0, n));
            RefreshCapacity();
        }

        public void SetRoyalCell(int2 cell) => Royal.Cell = cell;

        /// <summary>Re-reads capacities from the config and research (royal chamber, per storage cell, food).</summary>
        public void RefreshCapacity()
        {
            int total = 0;
            int royal = _config.baseItemCapacity + (int)_mods.Flat(StatKind.RoyalCapacity);
            foreach (var c in Containers)
            {
                c.Capacity = c.IsRoyal ? royal : _config.storageItemsPerCell;
                total += c.Capacity;
            }
            Capacity = total;
            FoodCapacity = _config.baseFoodCapacity + _mods.Flat(StatKind.FoodCapacity) + StorageCells * _config.storageFoodPerCell;
            AcceptanceChanged = true;
        }

        // ---------------------------------------------------------------- containers

        public void AddStorageCell(int2 cell, int worldIndex)
        {
            if (_containerAt.ContainsKey(worldIndex)) return;
            _containerAt.Add(worldIndex, Containers.Count);
            Containers.Add(new StockContainer(cell, worldIndex, _config.storageItemsPerCell, Items.Length));
            LocationsChanged = ulong.MaxValue;
            RefreshCapacity();
        }

        /// <summary>
        /// Removes a storage cell. Its items move to other containers with room (nearest first); what does not
        /// fit is added to <paramref name="evicted"/> (one entry per unit) for the caller to drop as piles.
        /// </summary>
        public void RemoveStorageCell(int worldIndex, List<ushort> evicted)
        {
            if (!_containerAt.TryGetValue(worldIndex, out int index)) return;
            var removed = Containers[index];
            int last = Containers.Count - 1;
            if (index != last)
            {
                Containers[index] = Containers[last];
                _containerAt[Containers[index].WorldIndex] = index;
            }
            Containers.RemoveAt(last);
            _containerAt.Remove(worldIndex);
            RefreshCapacity();
            LocationsChanged = ulong.MaxValue;

            for (ushort item = 0; item < removed.Counts.Length; item++)
            {
                for (int n = 0; n < removed.Counts[item]; n++)
                {
                    Items[item]--;
                    Stored--;
                    if (!Store(item, removed.Cell)) evicted.Add(item);
                }
            }
        }

        public StockContainer ContainerAt(int worldIndex) =>
            _containerAt.TryGetValue(worldIndex, out int i) ? Containers[i] : null;

        /// <summary>
        /// Container for one more <paramref name="item"/> delivered at <paramref name="near"/>: nearest with room,
        /// preferring containers that already hold the item, then empty ones, so storage stays sorted by type.
        /// </summary>
        int Pick(ushort item, int2 near)
        {
            int best = -1;
            int bestScore = int.MaxValue;
            for (int i = 0; i < Containers.Count; i++)
            {
                var c = Containers[i];
                if (c.Total >= c.Capacity) continue;
                int2 d = math.abs(c.Cell - near);
                int score = d.x + d.y;
                if (c.IsRoyal) score = math.max(0, score - _config.royalRadius);
                if (c.Counts[item] == 0) score += c.Total > 0 ? 12 : 4;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        void Put(StockContainer c, ushort item, int count)
        {
            if (c.Counts[item] == 0 && count > 0) LocationsChanged |= 1ul << item;
            c.Counts[item] += count;
            c.Total += count;
            Items[item] += count;
            Stored += count;
            if (c.Counts[item] == 0) LocationsChanged |= 1ul << item;
        }

        /// <summary>Puts one non-food item into the stock near <paramref name="near"/>. False if the stock is full.</summary>
        public bool Store(ushort item, int2 near)
        {
            int i = Pick(item, near);
            if (i < 0) return false;
            Put(Containers[i], item, 1);
            AcceptanceChanged = true;
            return true;
        }

        /// <summary>Adds items straight to the stock (refunds, cheats, tests). Returns how many did not fit.</summary>
        public int Add(ushort item, int count, int2 near)
        {
            if (_content.Items[item].IsFood)
            {
                Food = Math.Min(FoodCapacity, Food + count * _content.Items[item].foodValue);
                return 0;
            }
            int n = 0;
            while (n < count && Store(item, near)) n++;
            return count - n;
        }

        /// <summary>Takes one <paramref name="item"/> from a specific container.</summary>
        public bool Take(StockContainer container, ushort item)
        {
            if (container == null || container.Counts[item] <= 0) return false;
            Put(container, item, -1);
            AcceptanceChanged = true;
            return true;
        }

        /// <summary>Takes <paramref name="count"/> items from wherever they are (storage cells first, newest first).</summary>
        public bool Take(ushort item, int count)
        {
            if (Items[item] < count) return false;
            for (int i = Containers.Count - 1; i >= 0 && count > 0; i--)
            {
                var c = Containers[i];
                int n = Math.Min(count, c.Counts[item]);
                if (n == 0) continue;
                Put(c, item, -n);
                count -= n;
            }
            AcceptanceChanged = true;
            return true;
        }

        /// <summary>
        /// A worker at <paramref name="cell"/> takes <paramref name="item"/> out of the storage cell it stands on,
        /// or out of the royal chamber when it stands in it.
        /// </summary>
        public bool TryWithdraw(ushort item, int worldIndex, bool inRoyalChamber)
        {
            var c = ContainerAt(worldIndex);
            bool taken = c != null && c.Counts[item] > 0 ? Take(c, item) : inRoyalChamber && Take(Royal, item);
            if (taken) _meter.AddItem(item, -1f);
            return taken;
        }

        /// <summary>Adds knowledge. <paramref name="studied"/> = produced by the colony (counts toward offline rates).</summary>
        public void AddKnowledge(float amount, bool studied)
        {
            if (amount <= 0f) return;
            Knowledge += amount;
            _stats.KnowledgeEarned += amount;
            if (studied) _meter.AddKnowledge(amount);
        }

        // ---------------------------------------------------------------- demand and acceptance

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

        int NonFoodInTransit()
        {
            int n = 0;
            for (int i = 0; i < InTransit.Length; i++)
                if (!_content.Items[i].IsFood) n += InTransit[i];
            return n;
        }

        /// <summary>
        /// True if one more unit of <paramref name="item"/> can be picked up and delivered: there is free stock
        /// space not yet claimed by items on the way, or a machine wants more of it than is already on the way.
        /// </summary>
        public bool CanAccept(ushort item)
        {
            var def = _content.Items[item];
            if (def.IsFood)
                return FoodInTransit() + def.foodValue <= Math.Max(0f, FoodCapacity - Food) + ConsumerRoom[item] * def.foodValue + 1e-3f;
            return Capacity - Stored - NonFoodInTransit() > 0 || InTransit[item] < ConsumerRoom[item];
        }

        /// <summary>True if the stock itself has room for one more <paramref name="item"/> right now.</summary>
        public bool StockAccepts(ushort item)
        {
            var def = _content.Items[item];
            return def.IsFood ? Food + def.foodValue <= FoodCapacity + 1e-3f : Stored < Capacity;
        }

        public void BeginTransit(ushort item) => InTransit[item]++;

        /// <summary>
        /// Puts a carried item into the stock (non-food items into the container nearest to <paramref name="at"/>).
        /// Returns false (and keeps it in transit) if it no longer fits.
        /// </summary>
        public bool Deliver(ushort item, int2 at)
        {
            var def = _content.Items[item];
            if (def.IsFood)
            {
                if (Food + def.foodValue > FoodCapacity + 1e-3f) return false;
                Food += def.foodValue;
                _foodDelivered += def.foodValue;
                _meter.AddFood(def.foodValue);
            }
            else if (!Store(item, at)) return false;
            else _meter.AddItem(item, 1f);
            InTransit[item] = Math.Max(0, InTransit[item] - 1);
            _delivered[item]++;
            _stats.Delivered[item]++;
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

        // ---------------------------------------------------------------- building costs

        public bool CanAfford(BuildingData building)
        {
            for (int i = 0; i < building.CostItems.Length; i++)
                if (Items[building.CostItems[i]] < building.CostCounts[i]) return false;
            return Food + 1e-3f >= building.Def.foodCost;
        }

        public void Spend(BuildingData building)
        {
            for (int i = 0; i < building.CostItems.Length; i++)
                Take(building.CostItems[i], building.CostCounts[i]);
            Food = Math.Max(0f, Food - building.Def.foodCost);
            AcceptanceChanged = true;
        }

        /// <summary>
        /// Returns a share of a building's cost to the stock near <paramref name="near"/>. Units that do not fit
        /// are added to <paramref name="leftover"/> (one entry per unit) for the caller to drop as piles.
        /// </summary>
        public void Refund(BuildingData building, float fraction, int2 near, List<ushort> leftover)
        {
            for (int i = 0; i < building.CostItems.Length; i++)
            {
                ushort item = building.CostItems[i];
                int missing = Add(item, (int)Math.Floor(building.CostCounts[i] * fraction + 1e-4f), near);
                for (int n = 0; n < missing; n++) leftover.Add(item);
            }
            Food = Math.Min(FoodCapacity, Food + building.Def.foodCost * fraction);
            AcceptanceChanged = true;
        }

        // ---------------------------------------------------------------- save / load

        /// <summary>Empties every container and removes all storage cells (before loading a save).</summary>
        internal void ResetForLoad()
        {
            for (int i = Containers.Count - 1; i >= 1; i--) Containers.RemoveAt(i);
            _containerAt.Clear();
            Array.Clear(Royal.Counts, 0, Royal.Counts.Length);
            Royal.Total = 0;
            Array.Clear(Items, 0, Items.Length);
            Array.Clear(InTransit, 0, InTransit.Length);
            Stored = 0;
            LocationsChanged = ulong.MaxValue;
            RefreshCapacity();
        }

        /// <summary>Puts <paramref name="count"/> items straight into a container, ignoring its capacity (loading a save).</summary>
        internal void RestoreCount(StockContainer container, ushort item, int count)
        {
            if (count > 0) Put(container, item, count);
        }

        /// <summary>Recomputed after loading: items carried by ants or riding belts.</summary>
        internal void SetInTransit(int[] counts)
        {
            Array.Copy(counts, InTransit, Math.Min(counts.Length, InTransit.Length));
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
