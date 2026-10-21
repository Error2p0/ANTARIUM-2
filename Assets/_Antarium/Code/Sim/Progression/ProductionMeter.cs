using System;

namespace Antarium.Sim
{
    /// <summary>
    /// Rolling record of the colony's production over the last few minutes, in one-minute buckets: per item
    /// the net change of the stock caused by the colony running (deliveries minus what workers took out for
    /// machines), gross food delivered and knowledge studied. Offline progress replays these rates.
    /// </summary>
    public sealed class ProductionMeter
    {
        public const int Minutes = 5;
        const float BucketSeconds = 60f;

        /// <summary>[bucket][item].</summary>
        internal readonly float[][] Items;
        internal readonly float[] Food = new float[Minutes];
        internal readonly float[] Knowledge = new float[Minutes];
        internal int Current;
        /// <summary>Completed buckets (up to <see cref="Minutes"/> - 1 kept besides the current one).</summary>
        internal int Filled;
        internal float CurrentSeconds;

        public ProductionMeter(int itemTypes)
        {
            Items = new float[Minutes][];
            for (int i = 0; i < Minutes; i++) Items[i] = new float[itemTypes];
        }

        public void AddItem(ushort item, float amount) => Items[Current][item] += amount;
        public void AddFood(float amount) => Food[Current] += amount;
        public void AddKnowledge(float amount) => Knowledge[Current] += amount;

        public void Update(float dt)
        {
            CurrentSeconds += dt;
            if (CurrentSeconds < BucketSeconds) return;
            CurrentSeconds -= BucketSeconds;
            Current = (Current + 1) % Minutes;
            Filled = Math.Min(Minutes - 1, Filled + 1);
            Array.Clear(Items[Current], 0, Items[Current].Length);
            Food[Current] = 0f;
            Knowledge[Current] = 0f;
        }

        /// <summary>Minutes of history the rates are averaged over.</summary>
        public float WindowMinutes => Filled + CurrentSeconds / BucketSeconds;

        public float ItemPerMinute(ushort item)
        {
            float sum = 0f;
            for (int i = 0; i < Minutes; i++) sum += Items[i][item];
            return Rate(sum);
        }

        public float FoodPerMinute => Rate(Sum(Food));
        public float KnowledgePerMinute => Rate(Sum(Knowledge));

        float Rate(float sum)
        {
            float window = WindowMinutes;
            return window < 0.25f ? 0f : sum / window; // too little history to say anything
        }

        static float Sum(float[] values)
        {
            float sum = 0f;
            foreach (float v in values) sum += v;
            return sum;
        }
    }
}
