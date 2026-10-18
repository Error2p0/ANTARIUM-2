using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>Click / hold to dig at the cursor, with a combo multiplier for sustained productive digging.</summary>
    public sealed class DigTool
    {
        readonly DigConfig _config;
        readonly DigCombo _combo;
        float _cooldown;

        public float ComboMultiplier => _combo.Multiplier;
        public int ComboStreak => _combo.Streak;

        public DigTool(DigConfig config)
        {
            _config = config;
            _combo = new DigCombo(config);
        }

        public void Update(Simulation sim, bool held, bool pressedThisFrame, float2 pointerWorld, float deltaTime, double now)
        {
            _combo.Update(now);
            _cooldown -= deltaTime;
            if (pressedThisFrame) _cooldown = 0f;
            if (!held || _cooldown > 0f) return;

            _cooldown += 1f / _config.hitsPerSecond;
            if (_cooldown < 0f) _cooldown = 0f;
            float2 cellPos = WorldGrid.WorldToCellSpace(pointerWorld);
            sim.Enqueue(new DigCommand(cellPos, _config.brushRadius, _config.power * _combo.Multiplier));
        }

        /// <summary>Feeds back resolved hits so only productive hits build the combo.</summary>
        public void Consume(EventBus events, double now)
        {
            foreach (var e in events.Read<DigResolvedEvent>())
                if (e.CellsDamaged > 0)
                    _combo.RegisterHit(now);
        }
    }
}
