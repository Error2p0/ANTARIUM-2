using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// The Queen's Throne (10.6): a building only a genetic trait unlocks, one per colony, built around the queen (her cell in
    /// its bottom row). While it stands it adds colony stats and ants near it work faster (the simulation applies both).
    /// </summary>
    public sealed partial class BuildingSystem
    {
        int _throne = None;

        /// <summary>The Queen's Throne (blueprint or built), or <see cref="None"/>.</summary>
        public int Throne => _throne >= 0 && _throne < All.Length && All[_throne].Alive && All[_throne].Kind == BuildingKind.Throne ? _throne : None;

        /// <summary>The throne is built (not a blueprint).</summary>
        public bool ThroneStanding
        {
            get
            {
                int t = Throne;
                return t >= 0 && All[t].IsActive;
            }
        }

        /// <summary>Centre of the throne's footprint in cell units (cell centres are whole numbers).</summary>
        public float2 ThroneCenter(int id)
        {
            var b = All[id];
            var size = _content.Buildings[b.Def].Def.Footprint;
            return new float2(b.Origin) + (new float2(size.x, size.y) - 1f) * 0.5f;
        }

        /// <summary>The queen's cell is in the bottom row of the footprint.</summary>
        bool ThroneFits(int2 origin, int2 size)
        {
            var q = _colony.QueenCell;
            return q.y == origin.y && q.x >= origin.x && q.x < origin.x + size.x;
        }
    }
}
