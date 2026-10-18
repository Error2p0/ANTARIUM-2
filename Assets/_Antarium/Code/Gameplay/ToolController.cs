using System;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    public enum ToolMode
    {
        /// <summary>Dig personally at the cursor.</summary>
        Dig,
        /// <summary>Paint mining orders for workers (right button erases).</summary>
        Mine,
        /// <summary>Paint storage zones (right button erases).</summary>
        Storage,
    }

    /// <summary>Routes pointer input to the active tool. A press that starts over the UI never reaches the world.</summary>
    public sealed class ToolController
    {
        readonly DigConfig _config;
        readonly DigTool _dig;
        bool _strokeBlocked;
        bool _hasLast;
        float2 _lastCellPos;

        public ToolMode Mode { get; private set; } = ToolMode.Dig;
        public DigTool Dig => _dig;
        public event Action<ToolMode> ModeChanged;

        public ToolController(DigConfig config)
        {
            _config = config;
            _dig = new DigTool(config);
        }

        public void SetMode(ToolMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;
            _hasLast = false;
            ModeChanged?.Invoke(mode);
        }

        public void Update(Simulation sim, GameInput input, float2 pointerWorld, bool pointerOverUi, float dt, double now)
        {
            if (input.ToolDig.WasPressedThisFrame()) SetMode(ToolMode.Dig);
            if (input.ToolMine.WasPressedThisFrame()) SetMode(ToolMode.Mine);
            if (input.ToolStorage.WasPressedThisFrame()) SetMode(ToolMode.Storage);

            bool primary = input.Primary.IsPressed();
            bool secondary = input.Secondary.IsPressed();
            bool pressed = input.Primary.WasPressedThisFrame() || input.Secondary.WasPressedThisFrame();
            if (pressed) _strokeBlocked = pointerOverUi;
            if (!primary && !secondary) _strokeBlocked = false;
            if (_strokeBlocked) { primary = secondary = false; }

            float2 cellPos = WorldGrid.WorldToCellSpace(pointerWorld);
            switch (Mode)
            {
                case ToolMode.Dig:
                    _dig.Update(sim, primary, pressed && !_strokeBlocked, pointerWorld, dt, now);
                    break;
                case ToolMode.Mine:
                case ToolMode.Storage:
                    Paint(sim, primary, secondary, cellPos);
                    break;
            }
        }

        void Paint(Simulation sim, bool primary, bool secondary, float2 cellPos)
        {
            if (!primary && !secondary)
            {
                _hasLast = false;
                return;
            }
            bool set = primary;
            float radius = _config.orderBrushRadius;

            // Fill the gap since last frame so fast strokes stay continuous.
            float2 from = _hasLast ? _lastCellPos : cellPos;
            float length = math.distance(from, cellPos);
            int steps = math.max(1, (int)math.ceil(length / (radius * 0.5f)));
            for (int i = 1; i <= steps; i++)
            {
                float2 p = math.lerp(from, cellPos, i / (float)steps);
                if (Mode == ToolMode.Mine) sim.EnqueueDesignate(p, radius, set);
                else sim.EnqueueStorageZone(p, radius, set);
            }
            _hasLast = true;
            _lastCellPos = cellPos;
        }

        public void Consume(EventBus events, double now) => _dig.Consume(events, now);
    }
}
