namespace Antarium.Core
{
    /// <summary>
    /// Fixed world-scale constants. These define the grid and pixel layout, not balance,
    /// and are shared by the simulation, the shaders and the camera.
    /// </summary>
    public static class WorldConstants
    {
        public const int ChunkShift = 5;
        public const int ChunkSize = 1 << ChunkShift;          // 32 cells
        public const int ChunkMask = ChunkSize - 1;
        public const int ChunkCellShift = ChunkShift * 2;
        public const int ChunkCellCount = ChunkSize * ChunkSize; // 1024 cells

        public const int PixelsPerUnit = 16;
        public const int PixelsPerCell = 8;
        public const float CellSize = (float)PixelsPerCell / PixelsPerUnit; // 0.5 world units

        /// <summary>Edge length in pixels of each terrain material texture slice.</summary>
        public const int MaterialTextureSize = 32;
    }
}
