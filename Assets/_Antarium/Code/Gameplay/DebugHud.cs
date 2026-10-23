using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>Developer readout (F3). Refreshes a few times per second to avoid per-frame garbage.</summary>
    public sealed class DebugHud
    {
        const float RefreshInterval = 0.25f;

        readonly Label _label;
        float _timer;
        int _frames;
        float _frameTime;

        public bool Visible
        {
            get => _label != null && _label.style.display != DisplayStyle.None;
            set { if (_label != null) _label.style.display = value ? DisplayStyle.Flex : DisplayStyle.None; }
        }

        public DebugHud(Label label) => _label = label;

        static string Castes(Simulation sim)
        {
            var alive = sim.AliveByCaste;
            var parts = new string[alive.Count];
            for (int c = 0; c < alive.Count; c++) parts[c] = alive[c].ToString();
            return string.Join("/", parts);
        }

        static string Scents(Simulation sim) => string.Join("/", sim.Pheromones.Cells);

        public void Update(float deltaTime, Simulation sim, float2 pointerWorld, int zoom, ToolController tools)
        {
            if (_label == null || !Visible) return;
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
                $"FPS {fps:0}   zoom x{zoom}   seed {sim.Seed}   tick {sim.Clock.Tick}\n" +
                $"cell {cell.x},{cell.y}   depth {depth}   {under}   light {math.cmax(light):0.00}\n" +
                $"tool {tools.Mode}   combo x{tools.Dig.ComboMultiplier:0.00} ({tools.Dig.ComboStreak})\n" +
                $"ants {sim.Ants.Count}   piles {sim.Colony.Piles.Count}   orders {sim.Colony.Designated.Count}   storage {sim.Colony.StorageCells.Count}\n" +
                $"buildings {sim.Buildings.ActiveCount}   blueprints {sim.Buildings.BlueprintCount}   belt cells {sim.Buildings.TransportOrder.Count}\n" +
                $"knowledge {sim.Colony.Inventory.Knowledge:0.0}   research {sim.Research.DoneCount}/{sim.Content.Research.Length}   next event {sim.RandomEvents.SecondsToNext:0}s\n" +
                $"soldiers {sim.Soldiers}   enemies {sim.Enemies.Count}   raid in {sim.Enemies.SecondsToRaid:0}s (size {sim.Enemies.NextRaidSize()})   fluid chunks {sim.Fluids.ActiveChunks}   lost {sim.Colony.Stats.AntsLost}\n" +
                $"castes {Castes(sim)}   scent cells {Scents(sim)}   scouted {sim.Colony.Stats.CellsScouted}   possessed {sim.PossessedAnt}\n" +
                "F3 toggle   F6 +50 ants   F7 finish blueprints   F8 +20 items, food, knowledge   F10 random event   F11 raid";
        }
    }
}
