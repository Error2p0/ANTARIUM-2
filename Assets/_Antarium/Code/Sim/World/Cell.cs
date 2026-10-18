using System;
using System.Runtime.InteropServices;

namespace Antarium.Sim
{
    [Flags]
    public enum CellFlags : byte
    {
        None = 0,
        /// <summary>Visible to the player (not hidden by fog of war).</summary>
        Revealed = 1 << 0,
        /// <summary>Open cell known to be connected to explored space. Unexplored open cells are undiscovered caves.</summary>
        Explored = 1 << 1,
        /// <summary>Player ordered this solid cell to be mined by workers.</summary>
        DigDesignated = 1 << 2,
        /// <summary>Open cell painted as a storage zone.</summary>
        Storage = 1 << 3,
    }

    /// <summary>
    /// One grid cell (4 bytes, mirrored 1:1 to the GPU). <see cref="Material"/> is kept after the cell is
    /// dug out so the renderer can draw the background wall; <see cref="Density"/> says how much is left.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Cell
    {
        public const byte Full = 255;
        /// <summary>Below this density the cell counts as open space (matches the shader's 0.5 iso level).</summary>
        public const byte OpenThreshold = 128;

        public ushort Material;
        public byte Density;
        public CellFlags Flags;

        public Cell(ushort material, byte density, CellFlags flags = CellFlags.None)
        {
            Material = material;
            Density = density;
            Flags = flags;
        }

        public bool IsEmpty => Density == 0;
        public bool IsOpen => Density < OpenThreshold;
        public bool IsRevealed => (Flags & CellFlags.Revealed) != 0;
    }
}
