using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Burning cells. A fire is a solid flammable cell with <see cref="FluidKind.Fire"/> in the fluid plane, the level being
    /// the fire steps it still burns (so fires are saved, uploaded and drawn with the fluids). Every fluid step each burning
    /// cell may light its flammable neighbours (only cells the colony has seen that touch open air), gives off smoke (gas)
    /// into the open cell above or beside it, is put out by water next to it, and burns down into ash (listed in
    /// <see cref="BurntOut"/> so the simulation can let it fall). Fires are few, so this is plain C# on the main thread.
    /// </summary>
    public sealed class FireSystem
    {
        readonly WorldGrid _world;
        readonly FireConfig _config;
        readonly NativeArray<MaterialSim> _materials;
        readonly List<int> _burning = new();
        readonly HashSet<int> _set = new();
        /// <summary>Cells that burnt out this step: never lit again in the same step (without ash they stay flammable).</summary>
        readonly HashSet<int> _out = new();
        readonly ushort _ash;
        uint _step;

        /// <summary>Cells that burnt out in the last step (now ash, or their old material if the content has no ash).</summary>
        public readonly List<int2> BurntOut = new();
        /// <summary>Fires started in the last step by lava, a meteorite or the player (not by spreading).</summary>
        public readonly List<int2> Started = new();
        /// <summary>Fires water put out in the last step.</summary>
        public readonly List<int2> Doused = new();

        /// <param name="ash">Material burnt-out cells turn into (<see cref="ContentDB.AirIndex"/> = they stay as they are).</param>
        public FireSystem(WorldGrid world, FireConfig config, NativeArray<MaterialSim> materials, ushort ash = ContentDB.AirIndex)
        {
            _world = world;
            _config = config ?? new FireConfig();
            _materials = materials;
            _ash = ash;
        }

        /// <summary>Cells burning now.</summary>
        public int Count => _burning.Count;

        /// <summary>Burning cells (world indices).</summary>
        public IReadOnlyList<int> Burning => _burning;

        /// <summary>Fire steps a fresh fire burns.</summary>
        public byte BurnLevels(float stepSeconds) => (byte)math.clamp((int)math.ceil(_config.burnSeconds / math.max(0.01f, stepSeconds)), 1, 255);

        public bool IsBurning(int2 c) => _world.InBounds(c.x, c.y) && _set.Contains(_world.Index(c.x, c.y));

        /// <summary>
        /// A solid, flammable cell the colony has seen that touches an open cell without water in it (a fire needs air; lava
        /// and gas don't smother it) and is not burning yet.
        /// </summary>
        public bool CanBurn(int2 c)
        {
            if (!_world.InBounds(c.x, c.y)) return false;
            int i = _world.Index(c.x, c.y);
            var cell = _world.Cells[i];
            if (cell.IsOpen || !cell.IsRevealed || _set.Contains(i) || _out.Contains(i) || !_materials[cell.Material].IsFlammable) return false;
            for (int d = 0; d < 4; d++)
            {
                var n = c + Dir(d);
                if (!_world.InBounds(n.x, n.y)) continue;
                int ni = _world.Index(n.x, n.y);
                if (!_world.Cells[ni].IsOpen) continue;
                var f = _world.Fluid[ni];
                if (f.Level > 0 && f.Kind == FluidKind.Water) continue; // under water: no air on that side
                return true;
            }
            return false;
        }

        /// <summary>Lights <paramref name="c"/> if it can burn (<see cref="CanBurn"/>). <paramref name="spread"/>: from a neighbour (no new-fire event).</summary>
        public bool Ignite(int2 c, float stepSeconds, bool spread = false)
        {
            if (_burning.Count >= _config.maxBurning || !CanBurn(c)) return false;
            int i = _world.Index(c.x, c.y);
            _world.Fluid[i] = new FluidCell(FluidKind.Fire, BurnLevels(stepSeconds));
            _world.MarkChanged(c.x, c.y, ChunkFlags.FluidUpload | ChunkFlags.LightRecompute);
            _burning.Add(i);
            _set.Add(i);
            if (!spread) Started.Add(c);
            return true;
        }

        /// <summary>Lights every cell that can burn within <paramref name="radius"/> of <paramref name="center"/> (a meteorite). Returns how many.</summary>
        public int IgniteAround(float2 center, float radius, float stepSeconds)
        {
            int r = (int)math.ceil(radius), lit = 0;
            var c0 = (int2)math.floor(center);
            for (int y = c0.y - r; y <= c0.y + r; y++)
            for (int x = c0.x - r; x <= c0.x + r; x++)
            {
                if (math.distance(new float2(x + 0.5f, y + 0.5f), center) > radius) continue;
                if (Ignite(new int2(x, y), stepSeconds, spread: lit > 0)) lit++;
            }
            return lit;
        }

        /// <summary>The cell <paramref name="c"/> was dug out or changed: it stops burning (its fire entry is cleared).</summary>
        public void Remove(int2 c)
        {
            if (!_world.InBounds(c.x, c.y)) return;
            int i = _world.Index(c.x, c.y);
            if (_world.Fluid[i].Kind == FluidKind.Fire)
            {
                _world.Fluid[i] = FluidCell.Empty;
                _world.MarkChanged(c.x, c.y, ChunkFlags.FluidUpload | ChunkFlags.LightRecompute);
            }
            if (_set.Remove(i)) _burning.Remove(i);
        }

        /// <summary>Re-reads the burning cells from the fluid plane (a loaded save).</summary>
        public void Rebuild()
        {
            _burning.Clear();
            _set.Clear();
            var fluid = _world.Fluid;
            for (int i = 0; i < fluid.Length; i++)
            {
                if (fluid[i].Kind != FluidKind.Fire || fluid[i].Level == 0) continue;
                if (_world.Cells[i].IsOpen)
                {
                    fluid[i] = FluidCell.Empty;
                    continue;
                }
                _burning.Add(i);
                _set.Add(i);
            }
        }

        /// <summary>One fire step (with the fluid step): spread, smoke, water, burning down.</summary>
        public void Step(FluidSystem fluids, float stepSeconds)
        {
            BurntOut.Clear();
            Doused.Clear();
            _out.Clear();
            if (_burning.Count == 0) return;
            _step++;
            uint spread = (uint)(math.saturate(_config.spreadPerSecond * stepSeconds) * 65535f);
            int count = _burning.Count; // fires lit this step start burning next step
            for (int k = 0; k < count; k++)
            {
                int i = _burning[k];
                var c = GridIndex.ToCell(i, _world.ChunksX);
                var f = _world.Fluid[i];
                if (f.Kind != FluidKind.Fire || f.Level == 0 || _world.Cells[i].IsOpen)
                {
                    // Dug out or changed meanwhile.
                    if (f.Kind == FluidKind.Fire)
                    {
                        _world.Fluid[i] = FluidCell.Empty;
                        _world.MarkChanged(c.x, c.y, ChunkFlags.FluidUpload | ChunkFlags.LightRecompute);
                    }
                    Drop(k--, ref count, i);
                    continue;
                }
                if (Douse(c))
                {
                    _world.Fluid[i] = FluidCell.Empty;
                    _world.MarkChanged(c.x, c.y, ChunkFlags.FluidUpload | ChunkFlags.LightRecompute);
                    Doused.Add(c);
                    Drop(k--, ref count, i);
                    continue;
                }
                for (int d = 0; d < 4; d++)
                    if (Roll(i, (uint)d, spread)) Ignite(c + Dir(d), stepSeconds, spread: true);
                Smoke(fluids, c);
                if (f.Level <= 1)
                {
                    _world.Fluid[i] = FluidCell.Empty;
                    var flags = ChunkFlags.FluidUpload | ChunkFlags.LightRecompute;
                    if (_ash != ContentDB.AirIndex)
                    {
                        var cell = _world.Cells[i];
                        _world.Cells[i] = new Cell(_ash, cell.Density, cell.Flags); // flags stay: mining orders too
                        flags |= ChunkFlags.TerrainUpload;
                    }
                    _world.MarkChanged(c.x, c.y, flags);
                    _out.Add(i);
                    BurntOut.Add(c);
                    Drop(k--, ref count, i);
                    continue;
                }
                _world.Fluid[i] = new FluidCell(FluidKind.Fire, (byte)(f.Level - 1));
            }
        }

        /// <summary>Clears the new-fire list (after the simulation announced them).</summary>
        public void ClearStarted() => Started.Clear();

        void Drop(int k, ref int count, int index)
        {
            // Swap with the last of this step's fires, then move the last entry (maybe lit this step) into that gap.
            int last = count - 1;
            _burning[k] = _burning[last];
            _burning[last] = _burning[_burning.Count - 1];
            _burning.RemoveAt(_burning.Count - 1);
            count--;
            _set.Remove(index);
        }

        /// <summary>Water next to a fire puts it out and loses some of itself as steam.</summary>
        bool Douse(int2 c)
        {
            for (int d = 0; d < 4; d++)
            {
                var n = c + Dir(d);
                if (!_world.InBounds(n.x, n.y)) continue;
                int ni = _world.Index(n.x, n.y);
                var w = _world.Fluid[ni];
                if (w.Kind != FluidKind.Water || w.Level < _config.extinguishLevel) continue;
                _world.Fluid[ni] = new FluidCell(FluidKind.Water, (byte)math.max(0, w.Level - _config.extinguishWater));
                _world.MarkChanged(n.x, n.y, ChunkFlags.FluidUpload);
                return true;
            }
            return false;
        }

        /// <summary>Smoke rises into the open cell above (else beside, else below) unless it holds a liquid.</summary>
        void Smoke(FluidSystem fluids, int2 c)
        {
            if (_config.smokePerStep <= 0) return;
            for (int k = 0; k < 4; k++)
            {
                var n = c + (k == 0 ? new int2(0, 1) : k == 1 ? new int2(1, 0) : k == 2 ? new int2(-1, 0) : new int2(0, -1));
                if (!_world.InBounds(n.x, n.y)) continue;
                int ni = _world.Index(n.x, n.y);
                if (!_world.Cells[ni].HoldsFluid) continue;
                var f = _world.Fluid[ni];
                if (f.Level > 0 && f.Kind != FluidKind.Gas) continue;
                int level = math.min(FluidCell.Full, f.Level + _config.smokePerStep);
                if (level == f.Level) return;
                _world.Fluid[ni] = new FluidCell(FluidKind.Gas, (byte)level);
                _world.MarkChanged(n.x, n.y, ChunkFlags.FluidUpload);
                fluids?.Wake(n);
                return;
            }
        }

        bool Roll(int index, uint salt, uint chance) =>
            chance > 0 && (math.hash(new uint3((uint)index, _step, 0x0F1E0000u + salt)) & 0xFFFF) < chance;

        static int2 Dir(int d) => d == 0 ? new int2(1, 0) : d == 1 ? new int2(-1, 0) : d == 2 ? new int2(0, 1) : new int2(0, -1);
    }
}
