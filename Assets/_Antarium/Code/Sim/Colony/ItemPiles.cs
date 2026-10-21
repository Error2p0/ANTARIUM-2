using System.Collections.Generic;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public struct Pile
    {
        public int2 Cell;
        public ushort Item;
        public int Count;
    }

    /// <summary>Loose items lying in the world, keyed by cell and item type.</summary>
    public sealed class ItemPiles
    {
        readonly List<Pile> _piles = new();
        readonly Dictionary<long, int> _lookup = new();
        readonly int _itemTypes;

        /// <summary>Set when piles are added or removed; the Items flow field must be rebuilt.</summary>
        public bool Changed;

        public ItemPiles(int itemTypes) => _itemTypes = itemTypes;

        public int Count => _piles.Count;
        public Pile this[int index] => _piles[index];

        static long Key(int2 cell, ushort item) => ((long)cell.x << 40) | ((long)(uint)cell.y << 16) | item;

        public void Add(int2 cell, ushort item, int count)
        {
            if (count <= 0) return;
            long key = Key(cell, item);
            if (_lookup.TryGetValue(key, out int index))
            {
                var p = _piles[index];
                p.Count += count;
                _piles[index] = p;
                return;
            }
            _lookup.Add(key, _piles.Count);
            _piles.Add(new Pile { Cell = cell, Item = item, Count = count });
            Changed = true;
        }

        public int CountAt(int2 cell, ushort item) =>
            _lookup.TryGetValue(Key(cell, item), out int index) ? _piles[index].Count : 0;

        /// <summary>Takes one item at <paramref name="cell"/> that the inventory can accept.</summary>
        public bool TryTake(int2 cell, Inventory inventory, out ushort item)
        {
            for (int i = 0; i < _itemTypes; i++)
            {
                item = (ushort)i;
                if (!_lookup.TryGetValue(Key(cell, item), out int index)) continue;
                if (!inventory.CanAccept(item)) continue;
                var p = _piles[index];
                p.Count--;
                if (p.Count > 0) _piles[index] = p;
                else Remove(index);
                return true;
            }
            item = 0;
            return false;
        }

        void Remove(int index)
        {
            var removed = _piles[index];
            _lookup.Remove(Key(removed.Cell, removed.Item));
            int last = _piles.Count - 1;
            if (index != last)
            {
                var moved = _piles[last];
                _piles[index] = moved;
                _lookup[Key(moved.Cell, moved.Item)] = index;
            }
            _piles.RemoveAt(last);
            Changed = true;
        }

        /// <summary>Moves every pile at <paramref name="from"/> to <paramref name="to"/> (its cell became solid).</summary>
        public void Relocate(int2 from, int2 to)
        {
            if (from.Equals(to)) return;
            for (int i = _piles.Count - 1; i >= 0; i--)
            {
                if (i >= _piles.Count) continue;
                var p = _piles[i];
                if (!p.Cell.Equals(from)) continue;
                Remove(i);
                Add(to, p.Item, p.Count);
            }
        }

        public int CountItem(ushort item)
        {
            int total = 0;
            foreach (var p in _piles)
                if (p.Item == item) total += p.Count;
            return total;
        }
    }
}
