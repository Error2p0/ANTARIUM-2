namespace Antarium.Sim
{
    /// <summary>Lifetime counters of the colony. Contracts measure progress against these.</summary>
    public sealed class ColonyStats
    {
        /// <summary>Per item: units delivered into the stock (food items too).</summary>
        public readonly long[] Delivered;
        /// <summary>Per item: units made by machines.</summary>
        public readonly long[] Crafted;
        /// <summary>Per building definition: how many were finished.</summary>
        public readonly int[] Built;
        /// <summary>Cells dug out by anyone.</summary>
        public long CellsDug;
        /// <summary>Depth (cells below the surface) of the deepest cell dug out.</summary>
        public int MaxDepth;
        public long AntsHatched;
        public int ContractsCompleted;
        public int EventsWon;
        public double KnowledgeEarned;

        public ColonyStats(int items, int buildings)
        {
            Delivered = new long[items];
            Crafted = new long[items];
            Built = new int[buildings];
        }
    }
}
