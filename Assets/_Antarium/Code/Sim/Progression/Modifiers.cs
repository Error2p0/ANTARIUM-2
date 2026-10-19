using System;
using Antarium.Content;

namespace Antarium.Sim
{
    /// <summary>
    /// Colony-wide stat modifiers from research. Every stat starts at 0 and effects add to it; systems read
    /// multiplier stats as <see cref="Mult"/> (1 + total) and flat stats as <see cref="Flat"/>.
    /// </summary>
    public sealed class Modifiers
    {
        public static readonly int Count = Enum.GetValues(typeof(StatKind)).Length;

        readonly float[] _total = new float[Count];

        public float Mult(StatKind stat) => 1f + _total[(int)stat];
        public float Flat(StatKind stat) => _total[(int)stat];

        public void Add(StatKind stat, float value) => _total[(int)stat] += value;

        public void Clear() => Array.Clear(_total, 0, _total.Length);
    }
}
