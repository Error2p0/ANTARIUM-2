using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// The player's digging strength (Milestone 11): every productive hit of the Dig tool spends some (more with a bigger
    /// brush); a while after the last hit it slowly comes back. Run dry and the tool rests until it is back to
    /// <see cref="DigConfig.staminaResume"/>. Not saved: it is a feel of the tool, not colony state.
    /// </summary>
    public sealed class DigStamina
    {
        readonly DigConfig _config;
        float _sinceUse = float.MaxValue;

        /// <summary>0 (empty) .. 1 (full).</summary>
        public float Value { get; private set; } = 1f;
        /// <summary>Ran dry and is not yet back to the resume level: no digging.</summary>
        public bool Exhausted { get; private set; }
        public bool CanDig => !Exhausted && Value > 0f;

        public DigStamina(DigConfig config) => _config = config;

        /// <summary>Strength one productive hit costs at brush size <paramref name="size"/> (1..3): by the brush's area.</summary>
        public float HitCost(int size)
        {
            float scale = BrushSizes.Scale(_config, size);
            return _config.staminaPerHit * scale * scale;
        }

        public void Spend(float cost)
        {
            if (cost <= 0f) return;
            Value = math.max(0f, Value - cost);
            _sinceUse = 0f;
            if (Value <= 0f) Exhausted = true;
        }

        public void Update(float dt)
        {
            _sinceUse += dt;
            if (_sinceUse >= _config.staminaRegenDelay) Value = math.min(1f, Value + _config.staminaRegen * dt);
            if (Exhausted && Value >= _config.staminaResume) Exhausted = false;
        }

        /// <summary>Back to full (a new colony).</summary>
        public void Reset()
        {
            Value = 1f;
            Exhausted = false;
            _sinceUse = float.MaxValue;
        }
    }

    /// <summary>Brush sizes 1..3 of the painting tools (dig, mine orders, storage, scents); 2 is each tool's base radius.</summary>
    public static class BrushSizes
    {
        public const int Min = 1, Max = 3, Default = 2;

        public static float Scale(DigConfig config, int size)
        {
            var scales = config.brushSizeScales;
            int i = math.clamp(size, Min, Max) - 1;
            return scales != null && i < scales.Length ? math.max(0.1f, scales[i]) : 1f;
        }

        public static float Radius(DigConfig config, float baseRadius, int size) => baseRadius * Scale(config, size);
    }
}
