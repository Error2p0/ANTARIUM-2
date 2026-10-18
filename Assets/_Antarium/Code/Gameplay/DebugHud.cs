using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>Temporary developer readout (UI Toolkit). Refreshes a few times per second to avoid per-frame garbage.</summary>
    public sealed class DebugHud
    {
        const float RefreshInterval = 0.25f;

        readonly Label _label;
        float _timer;
        int _frames;
        float _frameTime;

        public DebugHud(UIDocument document)
        {
            if (document == null) return;
            _label = new Label();
            _label.style.position = Position.Absolute;
            _label.style.left = 8;
            _label.style.top = 8;
            _label.style.color = Color.white;
            _label.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _label.style.paddingLeft = 6;
            _label.style.paddingRight = 6;
            _label.style.paddingTop = 4;
            _label.style.paddingBottom = 4;
            _label.style.fontSize = 12;
            _label.pickingMode = PickingMode.Ignore;
            document.rootVisualElement.Add(_label);
        }

        public void Update(float deltaTime, Simulation sim, float2 pointerWorld, int zoom, DigTool dig)
        {
            if (_label == null) return;
            _frames++;
            _frameTime += deltaTime;
            _timer -= deltaTime;
            if (_timer > 0f) return;
            _timer = RefreshInterval;

            float fps = _frames / math.max(_frameTime, 1e-5f);
            _frames = 0;
            _frameTime = 0f;

            var world = sim.World;
            int2 cell = WorldGrid.WorldToCell(pointerWorld);
            string under = "-";
            int depth = 0;
            float3 light = 0f;
            if (world.InBounds(cell.x, cell.y))
            {
                var c = world[cell.x, cell.y];
                under = c.IsRevealed ? $"{sim.Content.Materials[c.Material].DisplayName} ({c.Density})" : "???";
                depth = world.Depth(cell.x, cell.y);
                light = sim.Light.Sample(world, cell.x, cell.y);
            }

            _label.text =
                $"FPS {fps:0}   zoom x{zoom}   seed {sim.Seed}\n" +
                $"cell {cell.x},{cell.y}   depth {depth}\n" +
                $"{under}   light {math.cmax(light):0.00}\n" +
                $"combo x{dig.ComboMultiplier:0.00} ({dig.ComboStreak})";
        }
    }
}
