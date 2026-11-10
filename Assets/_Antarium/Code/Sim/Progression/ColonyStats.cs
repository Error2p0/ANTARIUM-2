namespace Antarium.Sim
{
    /// <summary>Lifetime counters of the colony. Contracts measure progress against these.</summary>
    public sealed class ColonyStats
    {
        /// <summary>Per item: units delivered into the stock (food items too).</summary>
        public readonly long[] Delivered;
        /// <summary>Per item: units made by machines.</summary>
        public readonly long[] Crafted;
        /// <summary>Per item: units studied into knowledge (the Archive; relics and tablets find lore, 10.5).</summary>
        public readonly long[] Studied;
        /// <summary>Per building definition: how many were finished.</summary>
        public readonly int[] Built;
        /// <summary>Cells dug out by anyone.</summary>
        public long CellsDug;
        /// <summary>Depth (cells below the surface) of the deepest cell dug out.</summary>
        public int MaxDepth;
        public long AntsHatched;
        /// <summary>Per <see cref="Antarium.Content.AntCaste"/>: ants of that caste hatched.</summary>
        public readonly long[] HatchedCaste = new long[Antarium.Content.Castes.Count];
        public int ContractsCompleted;
        public int EventsWon;
        public double KnowledgeEarned;
        public long EnemiesKilled;
        public long AntsLost;
        public int RaidsRepelled;
        /// <summary>Hidden cells scouts revealed.</summary>
        public long CellsScouted;
        /// <summary>Princesses raised for the Nuptial Flight.</summary>
        public long PrincessesRaised;
        /// <summary>Raids of rival ants beaten without losing an item (10.5).</summary>
        public int RivalsRepelled;

        public ColonyStats(int items, int buildings)
        {
            Delivered = new long[items];
            Crafted = new long[items];
            Studied = new long[items];
            Built = new int[buildings];
        }
    }
}
