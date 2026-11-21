using System.Collections.Generic;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// A spot on a cave floor where a mushroom grows (Milestone 11). While grown it is an ordinary item pile of
    /// <see cref="Item"/> in <see cref="Cell"/> (workers haul it like any other); once picked it grows back after a while.
    /// </summary>
    public struct Growth
    {
        public int2 Cell;
        public ushort Item;
        /// <summary>The mushroom stands there (its pile is in the cell).</summary>
        public bool Grown;
        /// <summary>Seconds until it grows back (while not grown).</summary>
        public float Timer;
    }

    /// <summary>Wild food (Milestone 11): the cave growth spots (mushrooms, the Elder Tree's fruit) and the timers of berries, toadstools and apples. Saved (<c>WILD</c>).</summary>
    public sealed class WildState
    {
        public readonly List<Growth> Growths = new();
        readonly Dictionary<long, int> _byCell = new();

        /// <summary>Growth spots were placed in this world (a save from before Milestone 11 gets them when it loads).</summary>
        public bool Placed;
        public float BerryTimer;
        public float ToadstoolTimer;
        /// <summary>Seconds toward the next apple falling from an apple tree (batch 6).</summary>
        public float AppleTimer;
        /// <summary>The colony has broken into the Elder Tree's cavern (batch 6; announced once).</summary>
        public bool ElderFound;
        /// <summary>Seconds since the last rain ended (toadstools come faster for a while after).</summary>
        public float SinceRain = 1e6f;
        public float CheckTimer;

        static long Key(int2 c) => ((long)c.x << 32) | (uint)c.y;

        public void Add(in Growth g)
        {
            if (_byCell.ContainsKey(Key(g.Cell))) return;
            _byCell.Add(Key(g.Cell), Growths.Count);
            Growths.Add(g);
        }

        public void Clear()
        {
            Growths.Clear();
            _byCell.Clear();
        }

        /// <summary>Index of the growth spot in <paramref name="cell"/>, or -1.</summary>
        public int IndexAt(int2 cell) => _byCell.TryGetValue(Key(cell), out int i) ? i : -1;
    }
}
