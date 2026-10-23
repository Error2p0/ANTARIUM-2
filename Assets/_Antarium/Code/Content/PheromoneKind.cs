namespace Antarium.Content
{
    /// <summary>
    /// Scents the player paints onto the grid. The value is stored in saves and emitters: append, never reorder.
    /// </summary>
    public enum PheromoneKind : byte
    {
        /// <summary>Highway: ants walk faster on it and paths prefer it. Traffic keeps it fresh.</summary>
        Trail,
        /// <summary>Mining orders inside are dug before the others.</summary>
        Dig,
        /// <summary>Piles inside are hauled before the others.</summary>
        Gather,
        /// <summary>Idle soldiers patrol here.</summary>
        Defend,
        /// <summary>Ants route around it and take no jobs inside.</summary>
        Avoid,
    }

    public static class Pheromones
    {
        public const int Count = 5;
    }
}
