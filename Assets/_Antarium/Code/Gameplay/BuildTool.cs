using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Presentation;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Places buildings from the palette. Belts and lifts are laid by dragging (locked to their axis; the
    /// drag direction sets the flow); walls and gates are painted by dragging; everything else is placed with a
    /// click. Clicking a placed filter with the filter selected changes its item. Holding the right button
    /// removes buildings.
    /// </summary>
    public sealed class BuildTool
    {
        readonly ContentDB _content;
        bool _stroke;
        bool _strokeOnFilter;
        int2 _anchor;
        int2 _last;
        bool _hasLast;
        int2 _lastErase;
        bool _erasing;

        public int Selected { get; private set; }
        public int2 Dir { get; private set; } = new(1, 0);
        /// <summary>Item new filters are set to (the last one chosen).</summary>
        public ushort FilterItem { get; private set; }
        /// <summary>Pheromone new emitters give off (R cycles it).</summary>
        public PheromoneKind EmitterKind { get; private set; }

        /// <summary>What a new building of the selected kind is set to: the filter item, or the emitter's pheromone.</summary>
        ushort NewFilter => SelectedDef != null && SelectedDef.kind == BuildingKind.Emitter ? (ushort)EmitterKind : FilterItem;
        public event Action<int> SelectionChanged;

        public BuildTool(ContentDB content) => _content = content;

        public BuildingDef SelectedDef => _content.Buildings.Length > 0 ? _content.Buildings[Selected].Def : null;

        public void Select(int def)
        {
            if (_content.Buildings.Length == 0) return;
            def = (def % _content.Buildings.Length + _content.Buildings.Length) % _content.Buildings.Length;
            if (def == Selected) return;
            Selected = def;
            Dir = BuildingSystem.NormalizeDir(SelectedDef.kind, Dir);
            SelectionChanged?.Invoke(def);
        }

        public void Cycle(int delta) => Select(Selected + delta);

        /// <summary>Belts and lifts flip; filters turn a quarter clockwise; emitters switch to the next pheromone.</summary>
        public void Rotate()
        {
            var def = SelectedDef;
            if (def == null) return;
            if (def.kind == BuildingKind.Emitter)
            {
                EmitterKind = (PheromoneKind)(((int)EmitterKind + 1) % Pheromones.Count);
                return;
            }
            Dir = def.kind switch
            {
                BuildingKind.Belt => new int2(-Dir.x, 0),
                BuildingKind.Lift => new int2(0, -Dir.y),
                BuildingKind.Filter => new int2(Dir.y, -Dir.x),
                _ => Dir,
            };
            Dir = BuildingSystem.NormalizeDir(def.kind, Dir);
        }

        /// <summary>Bottom-left cell for the selected building with the cursor at <paramref name="cell"/> (machines: bottom centre).</summary>
        public int2 OriginFor(int2 cell)
        {
            var def = SelectedDef;
            if (def == null || def.IsTransport) return cell;
            return cell - new int2(def.Footprint.x / 2, 0);
        }

        public void Cancel()
        {
            _stroke = _hasLast = _erasing = false;
        }

        public void Update(Simulation sim, bool primary, bool primaryPressed, bool secondary, float2 cellPos, List<BuildGhost> ghosts)
        {
            var def = SelectedDef;
            if (def == null) return;
            int2 cell = (int2)math.floor(cellPos);

            if (secondary && !primary)
            {
                if (!_erasing || !cell.Equals(_lastErase)) sim.EnqueueRemove(cell);
                _erasing = true;
                _lastErase = cell;
            }
            else _erasing = false;

            if (primaryPressed)
            {
                _stroke = true;
                _anchor = cell;
                _hasLast = false;
                _strokeOnFilter = false;
                int existing = sim.Buildings.At(cell);
                if (def.kind == BuildingKind.Filter && existing >= 0 && sim.Buildings.Get(existing).Kind == BuildingKind.Filter)
                {
                    // Click on a placed filter: next item.
                    int items = _content.Items.Length;
                    var next = (ushort)((sim.Buildings.Get(existing).Filter + 1) % math.max(1, items));
                    sim.EnqueueSetFilter(cell, next);
                    FilterItem = next;
                    _strokeOnFilter = true;
                }
                else if (def.kind == BuildingKind.Emitter && existing >= 0 && sim.Buildings.Get(existing).Kind == BuildingKind.Emitter)
                {
                    // Click on a placed emitter: next pheromone.
                    var next = (PheromoneKind)((sim.Buildings.Get(existing).Filter + 1) % Pheromones.Count);
                    sim.EnqueueSetFilter(cell, (ushort)next);
                    EmitterKind = next;
                    _strokeOnFilter = true;
                }
                else if (def.kind != BuildingKind.Wall && def.kind != BuildingKind.Gate
                         && (!def.IsTransport || def.kind == BuildingKind.Splitter || def.kind == BuildingKind.Filter))
                {
                    sim.EnqueueBuild(Selected, OriginFor(cell), Dir, NewFilter);
                }
            }
            if (!primary) _stroke = false;

            bool line = def.kind == BuildingKind.Belt || def.kind == BuildingKind.Lift;
            bool paint = def.kind == BuildingKind.Wall || def.kind == BuildingKind.Gate;
            if (_stroke && paint)
            {
                // Every cell between the previous and the current pointer cell, so a fast drag leaves no gaps.
                if (!_hasLast)
                {
                    sim.EnqueueBuild(Selected, cell, Dir, NewFilter);
                    _last = cell;
                    _hasLast = true;
                }
                while (!cell.Equals(_last))
                {
                    _last += (int2)math.sign(cell - _last);
                    sim.EnqueueBuild(Selected, _last, Dir, FilterItem);
                }
            }
            if (_stroke && line && !_strokeOnFilter)
            {
                // Lock the line to its axis so a slightly wobbly drag stays on one row / column.
                int2 target = def.kind == BuildingKind.Belt ? new int2(cell.x, _anchor.y) : new int2(_anchor.x, cell.y);
                if (!_hasLast)
                {
                    sim.EnqueueBuild(Selected, target, Dir, FilterItem);
                    _last = target;
                    _hasLast = true;
                }
                while (!target.Equals(_last))
                {
                    int2 step = (int2)math.sign(target - _last);
                    Dir = BuildingSystem.NormalizeDir(def.kind, step);
                    sim.EnqueueBuild(Selected, _last, Dir, FilterItem); // re-orients the previous cell to follow the drag
                    _last += step;
                    sim.EnqueueBuild(Selected, _last, Dir, FilterItem);
                }
            }

            ghosts.Clear();
            if (!secondary)
            {
                int2 origin = _stroke && line && _hasLast ? _last : OriginFor(cell);
                var result = sim.Buildings.CanPlace(Selected, origin, Dir);
                ghosts.Add(new BuildGhost
                {
                    Def = Selected,
                    Origin = origin,
                    Dir = Dir,
                    Filter = NewFilter,
                    Valid = result == PlaceResult.Ok || result == PlaceResult.Rotated || result == PlaceResult.Unchanged,
                });
            }
        }
    }
}
