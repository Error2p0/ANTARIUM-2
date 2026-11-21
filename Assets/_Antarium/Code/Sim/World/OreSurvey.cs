using System;
using System.Collections.Generic;
using Antarium.Content;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Antarium.Sim
{
    /// <summary>
    /// Counts the finds the player knows of (10.7, the ore overlay's legend): solid cells that are revealed or were found
    /// by Seismic Sense, per item they drop. A find is a material that drops a non-food item (<see cref="WorldGenerator.IsOre"/>):
    /// ores, coal, amber, fossils, crystals, relics, tablets. One Burst pass over the world; call it about once a second.
    /// </summary>
    public sealed class OreSurvey : IDisposable
    {
        NativeArray<ushort> _itemOf;
        NativeArray<int> _counts;

        /// <summary>Every item some material drops as a find, in material order (each once).</summary>
        public readonly ushort[] Finds;

        public OreSurvey(ContentDB content)
        {
            int materials = content.Materials.Length;
            _itemOf = new NativeArray<ushort>(materials, Allocator.Persistent);
            _counts = new NativeArray<int>(Math.Max(1, content.Items.Length), Allocator.Persistent);
            var finds = new List<ushort>();
            for (int m = 0; m < materials; m++)
            {
                ushort item = FindItem(content, (ushort)m);
                _itemOf[m] = item;
                if (item != ContentDB.NoItem && !finds.Contains(item)) finds.Add(item);
            }
            Finds = finds.ToArray();
        }

        /// <summary>The item a cell of <paramref name="material"/> drops if it is a find, else <see cref="ContentDB.NoItem"/>.</summary>
        public static ushort FindItem(ContentDB content, ushort material) =>
            WorldGenerator.IsOre(content, material) ? content.MaterialSim[material].DropItem : ContentDB.NoItem;

        /// <summary>Known cells of the find that drops <paramref name="item"/>, as of the last <see cref="Count"/>.</summary>
        public int Known(ushort item) => item < _counts.Length ? _counts[item] : 0;

        public void Count(WorldGrid world)
        {
            new CountJob { Cells = world.Cells, ItemOf = _itemOf, Counts = _counts }.Run();
        }

        [BurstCompile]
        struct CountJob : IJob
        {
            [ReadOnly] public NativeArray<Cell> Cells;
            [ReadOnly] public NativeArray<ushort> ItemOf;
            public NativeArray<int> Counts;

            public void Execute()
            {
                for (int i = 0; i < Counts.Length; i++) Counts[i] = 0;
                const CellFlags known = CellFlags.Revealed | CellFlags.Scanned;
                for (int i = 0; i < Cells.Length; i++)
                {
                    var c = Cells[i];
                    if (c.Density < Cell.OpenThreshold || (c.Flags & known) == 0 || c.Material >= ItemOf.Length) continue;
                    ushort item = ItemOf[c.Material];
                    if (item != ContentDB.NoItem) Counts[item]++;
                }
            }
        }

        public void Dispose()
        {
            if (_itemOf.IsCreated) _itemOf.Dispose();
            if (_counts.IsCreated) _counts.Dispose();
        }
    }
}
