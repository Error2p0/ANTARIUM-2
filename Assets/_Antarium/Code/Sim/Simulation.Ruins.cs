using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>The deep endgame (10.5): what creatures that dig do to the world.</summary>
    public sealed partial class Simulation
    {
        readonly Queue<int2> _tunnel = new();

        /// <summary>
        /// A creature (the mole cricket) digs a cell out: it opens without a drop or a cell-dug count, and it and the cells
        /// around it show through the fog so the player sees the tunnel coming. Fluids, cave-ins and fire react as to any
        /// dug cell.
        /// </summary>
        internal void DigByCreature(int2 c)
        {
            if (!World.InBounds(c.x, c.y)) return;
            int i = World.Index(c.x, c.y);
            var cell = World.Cells[i];
            if (cell.IsEmpty) return;
            cell.Density = 0;
            cell.Flags &= ~CellFlags.DigDesignated;
            World.Cells[i] = cell;
            World.MarkChanged(c.x, c.y, ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute);
            if (Colony.Designated.Remove(i)) _designationsChanged = true;
            _terrainChanged = true;
            Reveal.Around(World, c, 1);
            Fire.Remove(c);
            Fluids.Wake(c);
            CheckUnstableAbove(c);
        }

        /// <summary>
        /// A digger broke into the colony: its tunnel (the unexplored open cells connected to <paramref name="from"/>) becomes
        /// part of the explored tunnels, so ants use it and it never counts as a new cave.
        /// </summary>
        internal void ExploreTunnel(int2 from)
        {
            const int Limit = 2000;
            _tunnel.Clear();
            _tunnel.Enqueue(from);
            int n = 0;
            while (_tunnel.Count > 0 && n < Limit)
            {
                var c = _tunnel.Dequeue();
                if (!Reveal.IsUnexploredOpen(World, c.x, c.y)) continue;
                int i = World.Index(c.x, c.y);
                var cell = World.Cells[i];
                cell.Flags |= CellFlags.Explored | CellFlags.Revealed;
                World.Cells[i] = cell;
                World.MarkChanged(c.x, c.y, ChunkFlags.TerrainUpload);
                n++;
                for (int d = 0; d < 4; d++)
                {
                    var next = c + (d == 0 ? new int2(1, 0) : d == 1 ? new int2(-1, 0) : d == 2 ? new int2(0, 1) : new int2(0, -1));
                    if (Reveal.IsUnexploredOpen(World, next.x, next.y)) _tunnel.Enqueue(next);
                }
            }
            if (n > 0) _terrainChanged = true;
        }
    }
}
