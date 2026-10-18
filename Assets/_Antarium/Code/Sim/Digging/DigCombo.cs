using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Active-play dig multiplier: each productive hit within the combo window raises the multiplier;
    /// a pause longer than the window resets it.
    /// </summary>
    public sealed class DigCombo
    {
        readonly DigConfig _config;
        double _lastHitTime = double.NegativeInfinity;

        public float Multiplier { get; private set; } = 1f;
        public int Streak { get; private set; }

        public DigCombo(DigConfig config) => _config = config;

        public void Update(double now)
        {
            if (now - _lastHitTime > _config.comboWindow)
            {
                Multiplier = 1f;
                Streak = 0;
            }
        }

        /// <summary>Call when a hit actually damaged terrain.</summary>
        public void RegisterHit(double now)
        {
            Update(now);
            _lastHitTime = now;
            Streak++;
            Multiplier = math.min(_config.maxCombo, 1f + (Streak - 1) * _config.comboPerHit);
        }
    }
}
