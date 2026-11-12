using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Click / hold to dig at the cursor, with a combo multiplier for sustained productive digging. Productive hits spend
    /// digging strength (<see cref="DigStamina"/>, more with a bigger brush); it comes back slowly while the tool rests.
    /// </summary>
    public sealed class DigTool
    {
        readonly DigConfig _config;
        readonly DigCombo _combo;
        readonly DigStamina _stamina;
        float _cooldown;
        int _size = BrushSizes.Default;
        /// <summary>Tool hits sent but not resolved yet (a possessed ant's bites are resolved the same way but cost nothing).</summary>
        int _pending;

        public float ComboMultiplier => _combo.Multiplier;
        public int ComboStreak => _combo.Streak;
        public DigStamina Stamina => _stamina;

        public DigTool(DigConfig config)
        {
            _config = config;
            _combo = new DigCombo(config);
            _stamina = new DigStamina(config);
        }

        /// <param name="size">Brush size 1..3.</param>
        public void Update(Simulation sim, bool held, bool pressedThisFrame, float2 pointerWorld, float deltaTime, double now, int size = BrushSizes.Default)
        {
            _combo.Update(now);
            _cooldown -= deltaTime;
            if (pressedThisFrame) _cooldown = 0f;
            if (!held || _cooldown > 0f || !_stamina.CanDig) return;

            _cooldown += 1f / _config.hitsPerSecond;
            if (_cooldown < 0f) _cooldown = 0f;
            _size = size;
            float2 cellPos = WorldGrid.WorldToCellSpace(pointerWorld);
            sim.Enqueue(new DigCommand(cellPos, BrushSizes.Radius(_config, _config.brushRadius, size), _config.power * _combo.Multiplier));
            _pending++;
        }

        /// <summary>Digging strength comes back while the tool rests (called every frame, whatever tool is active).</summary>
        public void Rest(float deltaTime) => _stamina.Update(deltaTime);

        /// <summary>Feeds back resolved hits so only productive hits build the combo and spend strength.</summary>
        public void Consume(EventBus events, double now)
        {
            foreach (var e in events.Read<DigResolvedEvent>())
            {
                bool mine = _pending > 0;
                if (mine) _pending--;
                if (e.CellsDamaged <= 0) continue;
                _combo.RegisterHit(now);
                if (mine) _stamina.Spend(_stamina.HitCost(_size));
            }
        }
    }
}
