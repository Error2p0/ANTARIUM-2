namespace Antarium.Presentation
{
    /// <summary>
    /// Keeps busy cues from flooding the mix: a cue may start again only after its cooldown and while fewer than
    /// its maximum voices are still sounding. Time is any monotonic clock in seconds.
    /// </summary>
    public sealed class VoiceLimiter
    {
        readonly float[] _cooldown;
        readonly double[] _last;
        /// <summary>Per cue: end times of its voices (a ring of maxVoices).</summary>
        readonly double[][] _ends;

        public VoiceLimiter(float[] cooldowns, int[] maxVoices)
        {
            _cooldown = cooldowns;
            _last = new double[cooldowns.Length];
            _ends = new double[cooldowns.Length][];
            for (int i = 0; i < cooldowns.Length; i++)
            {
                _last[i] = double.NegativeInfinity;
                _ends[i] = new double[System.Math.Max(1, maxVoices[i])];
            }
        }

        /// <summary>True (and the voice is counted until <paramref name="now"/> + <paramref name="length"/>) if the cue may play now.</summary>
        public bool TryStart(int cue, double now, float length)
        {
            if (now - _last[cue] < _cooldown[cue]) return false;
            var ends = _ends[cue];
            int free = -1;
            for (int i = 0; i < ends.Length; i++)
                if (ends[i] <= now) { free = i; break; }
            if (free < 0) return false;
            ends[free] = now + length;
            _last[cue] = now;
            return true;
        }
    }
}
