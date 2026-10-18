using System;

namespace Antarium.Core
{
    /// <summary>
    /// Fixed-step accumulator. Converts variable frame time into a whole number of simulation ticks
    /// and exposes the interpolation factor for rendering.
    /// </summary>
    public sealed class SimClock
    {
        public readonly double TickDuration;
        public readonly int MaxTicksPerFrame;

        double _accumulator;

        public long Tick { get; private set; }

        /// <summary>Fraction of the next tick that has elapsed, in [0, 1).</summary>
        public float Alpha => (float)(_accumulator / TickDuration);

        public SimClock(int ticksPerSecond, int maxTicksPerFrame)
        {
            if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            if (maxTicksPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(maxTicksPerFrame));
            TickDuration = 1.0 / ticksPerSecond;
            MaxTicksPerFrame = maxTicksPerFrame;
        }

        /// <summary>
        /// Adds frame time and returns how many ticks should run now. If the frame was too long,
        /// surplus time is dropped instead of spiralling.
        /// </summary>
        public int Advance(double deltaTime)
        {
            _accumulator += Math.Max(0.0, deltaTime);
            int ticks = 0;
            while (_accumulator >= TickDuration && ticks < MaxTicksPerFrame)
            {
                _accumulator -= TickDuration;
                ticks++;
            }
            if (_accumulator >= TickDuration)
                _accumulator = 0.0; // hitch: drop the surplus (modulo can leave ~1 tick due to rounding)
            Tick += ticks;
            return ticks;
        }
    }
}
