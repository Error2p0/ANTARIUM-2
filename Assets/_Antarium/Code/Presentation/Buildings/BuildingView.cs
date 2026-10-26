using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>A placement preview drawn under the cursor.</summary>
    public struct BuildGhost
    {
        public int Def;
        public int2 Origin;
        public int2 Dir;
        public ushort Filter;
        public bool Valid;
    }

    /// <summary>
    /// Draws all buildings, the items riding belts and placement previews in one dynamic mesh. A Burst job
    /// writes four quads per building (sprite, two belt items, filter dot). Blueprints and previews are
    /// dithered; the throughput overlay tints belts by flow (red = jammed) and machines by status.
    /// </summary>
    public sealed class BuildingView : IDisposable
    {
        const int QuadsPerBuilding = 4;

        struct DefVisual
        {
            public BuildingKind Kind;
            public int2 SizePx;
            public int Rect;
            public int Frames;
            public float Speed;
            /// <summary>Gates: hit points when intact (damage tints them).</summary>
            public float MaxHealth;
        }

        readonly ContentDB _content;
        readonly RenderConfig _render;
        readonly BuildingSprites _sprites;
        readonly SpriteMesh _mesh;
        readonly SpriteMesh _stockMesh;
        readonly NativeArray<Color32> _itemColors;
        readonly Color32[] _stockColors;
        readonly NativeArray<DefVisual> _defs;
        readonly NativeArray<Color32> _scentColors;
        readonly OverlayColors _colors;

        public readonly List<BuildGhost> Ghosts = new();

        struct OverlayColors
        {
            public Color32 Idle, Flow, Jam, Working, Starved, GhostValid, GhostInvalid;
            public float FullRate;
            public byte GhostAlpha;
        }

        public BuildingView(ContentDB content, RenderConfig render, Material spriteMaterial, Transform parent, float2 worldSize)
        {
            _content = content;
            _render = render;
            _sprites = new BuildingSprites();
            _mesh = new SpriteMesh("Buildings", spriteMaterial, _sprites.Texture, parent, worldSize, 5);
            _stockMesh = new SpriteMesh("Stock", spriteMaterial, _sprites.Texture, parent, worldSize, 3);
            _itemColors = ColonyView.ItemColors(content);
            // Stored goods sit on the back wall: a little darker than loose items so they read as background.
            _stockColors = new Color32[content.Items.Length];
            for (int i = 0; i < _stockColors.Length; i++)
                _stockColors[i] = TerrainView.ToWorking(content.Items[i].color * new Color(render.stockBrightness, render.stockBrightness, render.stockBrightness, 1f));

            _defs = new NativeArray<DefVisual>(math.max(1, content.Buildings.Length), Allocator.Persistent);
            for (int i = 0; i < content.Buildings.Length; i++)
                _defs[i] = Visual(content.Buildings[i].Def);
            _scentColors = PheromoneColors(render);

            _colors = new OverlayColors
            {
                Idle = TerrainView.ToWorking(render.overlayIdleColor),
                Flow = TerrainView.ToWorking(render.overlayFlowColor),
                Jam = TerrainView.ToWorking(render.overlayJamColor),
                Working = TerrainView.ToWorking(render.overlayWorkingColor),
                Starved = TerrainView.ToWorking(render.overlayStarvedColor),
                GhostValid = TerrainView.ToWorking(render.ghostValidColor),
                GhostInvalid = TerrainView.ToWorking(render.ghostInvalidColor),
                FullRate = render.overlayFullRate,
                GhostAlpha = (byte)math.round(render.ghostAlpha * 255f),
            };
        }

        /// <summary>Pheromone colours (in <see cref="PheromoneKind"/> order) in the working colour space.</summary>
        public static NativeArray<Color32> PheromoneColors(RenderConfig render)
        {
            var colors = new NativeArray<Color32>(Pheromones.Count, Allocator.Persistent);
            for (int i = 0; i < colors.Length; i++)
                colors[i] = TerrainView.ToWorking(render.pheromoneColors != null && i < render.pheromoneColors.Length ? render.pheromoneColors[i] : Color.white);
            return colors;
        }

        static DefVisual Visual(BuildingDef def)
        {
            var size = def.Footprint;
            var v = new DefVisual
            {
                Kind = def.kind, Frames = 1, Speed = def.speed, SizePx = new int2(size.x, size.y) * BuildingSprites.Cell, MaxHealth = def.maxHealth,
            };
            switch (def.kind)
            {
                case BuildingKind.Belt: v.Rect = BuildingSprites.BeltRect; v.Frames = BuildingSprites.AnimFrames; break;
                case BuildingKind.Lift: v.Rect = BuildingSprites.LiftRect; v.Frames = BuildingSprites.AnimFrames; break;
                case BuildingKind.Splitter: v.Rect = BuildingSprites.SplitterRect; break;
                case BuildingKind.Filter: v.Rect = BuildingSprites.FilterRect; break;
                case BuildingKind.Gate: v.Rect = BuildingSprites.GateRect; break;
                case BuildingKind.Trap: v.Rect = BuildingSprites.TrapRect; v.Frames = 2; break;
                case BuildingKind.GuardPost: v.Rect = BuildingSprites.GuardPostRect; break;
                case BuildingKind.Wall: v.Rect = BuildingSprites.WallRect; break;
                case BuildingKind.Nursery: v.Rect = BuildingSprites.NurseryRect; break;
                case BuildingKind.Emitter: v.Rect = BuildingSprites.EmitterRect; break;
                default:
                    v.Rect = def.art switch
                    {
                        PlaceholderArt.Kiln => BuildingSprites.SmelterRect,
                        PlaceholderArt.Garden => BuildingSprites.GardenRect,
                        PlaceholderArt.Archive => BuildingSprites.ArchiveRect,
                        PlaceholderArt.PrincessChamber => BuildingSprites.PrincessChamberRect,
                        _ => BuildingSprites.GenericRect,
                    };
                    v.Frames = def.art == PlaceholderArt.Block ? 1 : 2;
                    break;
            }
            return v;
        }

        public void Render(Simulation sim, float alpha, float time, bool overlay)
        {
            var buildings = sim.Buildings.All;
            int count = buildings.Length;
            int quads = count * QuadsPerBuilding + Ghosts.Count * 2;
            _mesh.EnsureCapacity(math.max(1, quads));
            var vertices = _mesh.Vertices;

            if (count > 0)
            {
                new BuildingQuadJob
                {
                    Buildings = buildings.AsArray(),
                    Defs = _defs,
                    Rects = _sprites.Rects,
                    ItemColors = _itemColors,
                    ScentColors = _scentColors,
                    Vertices = vertices,
                    Alpha = alpha,
                    Time = time,
                    Overlay = overlay,
                    Colors = _colors,
                }.Schedule(count, 64).Complete();
            }

            int quad = count * QuadsPerBuilding;
            foreach (var ghost in Ghosts)
            {
                var color = ghost.Valid ? _colors.GhostValid : _colors.GhostInvalid;
                color.a = _colors.GhostAlpha;
                var b = new Building
                {
                    Def = ghost.Def,
                    Kind = _defs[ghost.Def].Kind,
                    Origin = ghost.Origin,
                    Dir = BuildingSystem.NormalizeDir(_defs[ghost.Def].Kind, ghost.Dir),
                    Filter = ghost.Filter,
                    State = BuildingState.Active,
                };
                WriteSprite(vertices, quad, quad + 1, b, _defs[ghost.Def], _sprites.Rects, _itemColors, _scentColors, color, time);
                quad += 2;
            }
            _mesh.Apply(quad);
            RenderStock(sim);
        }

        /// <summary>
        /// Every stored item as a small lump on the back wall: up to 8 per storage cell (2 across, 4 high,
        /// grouped by type), and the royal chamber's share spread over its floor-level cells.
        /// </summary>
        void RenderStock(Simulation sim)
        {
            const int PerCell = 8;
            var inv = sim.Colony.Inventory;
            var royalCells = sim.Colony.RoyalDisplayCells;
            _stockMesh.EnsureCapacity(math.max(1, inv.Stored));
            var vertices = _stockMesh.Vertices;
            var rect = _sprites.Rects[BuildingSprites.StoredRect];
            var size = new int2(BuildingSprites.StoredWidth, BuildingSprites.StoredHeight);
            int quad = 0;
            foreach (var container in inv.Containers)
            {
                int k = 0;
                for (int item = 0; item < container.Counts.Length; item++)
                {
                    for (int n = 0; n < container.Counts[item]; n++, k++)
                    {
                        int cellIndex = k / PerCell;
                        int2 cell;
                        if (container.IsRoyal)
                        {
                            if (cellIndex >= royalCells.Count) continue;
                            cell = royalCells[cellIndex];
                        }
                        else
                        {
                            if (cellIndex > 0) continue; // a cell holding more than 8 shows 8
                            cell = container.Cell;
                        }
                        int slot = k % PerCell;
                        float2 px = new float2(cell) * BuildingSprites.Cell + new float2((slot & 1) * 4, (slot >> 1) * 2);
                        float2 world = px / WorldConstants.PixelsPerUnit;
                        SpriteQuads.WriteUpright(vertices, quad++, world, size, rect, _stockColors[item],
                            WorldGrid.CellCenterToWorld(cell));
                    }
                }
            }
            _stockMesh.Apply(quad);
        }

        /// <summary>Building sprite into <paramref name="quad"/>; the filter's item dot into <paramref name="dotQuad"/>.</summary>
        static void WriteSprite(NativeArray<SpriteVertex> v, int quad, int dotQuad, in Building b, in DefVisual vis,
            NativeArray<float4> rects, NativeArray<Color32> itemColors, NativeArray<Color32> scentColors, Color32 color, float time)
        {
            float2 origin = new float2(b.Origin) * WorldConstants.CellSize;
            float2 cellCenter = origin + WorldConstants.CellSize * 0.5f;
            float2 light = origin + new float2(vis.SizePx) * 0.5f / WorldConstants.PixelsPerUnit;
            int anim = (int)(time * vis.Speed * BuildingSprites.Cell) & (BuildingSprites.AnimFrames - 1);
            var cell = new int2(BuildingSprites.Cell);
            SpriteQuads.WriteEmpty(v, dotQuad);

            switch (vis.Kind)
            {
                case BuildingKind.Belt:
                    SpriteQuads.WriteQuad(v, quad, cellCenter, cell, new int2(0, 1), b.Dir.x < 0 ? -1 : 1, rects[vis.Rect + anim], color, light);
                    break;
                case BuildingKind.Lift:
                {
                    int frame = b.Dir.y >= 0 ? anim : (BuildingSprites.AnimFrames - anim) & (BuildingSprites.AnimFrames - 1);
                    SpriteQuads.WriteUpright(v, quad, origin, cell, rects[vis.Rect + frame], color, light);
                    break;
                }
                case BuildingKind.Splitter:
                case BuildingKind.Gate:
                case BuildingKind.GuardPost:
                case BuildingKind.Wall:
                    SpriteQuads.WriteUpright(v, quad, origin, cell, rects[vis.Rect], color, light);
                    break;
                case BuildingKind.Trap:
                    SpriteQuads.WriteUpright(v, quad, origin, cell, rects[vis.Rect + (b.Rearm > 0f ? 1 : 0)], color, light);
                    break;
                case BuildingKind.Filter:
                {
                    // The arrow points along Dir: rotate the sprite's "up" a quarter turn from it.
                    var up = new int2(-b.Dir.y, b.Dir.x);
                    SpriteQuads.WriteQuad(v, quad, cellCenter, cell, up, 1, rects[vis.Rect], color, light);
                    var dot = itemColors[math.min(b.Filter, itemColors.Length - 1)];
                    dot.a = color.a;
                    float2 dotCenter = cellCenter - new float2(b.Dir) * (1f / WorldConstants.PixelsPerUnit);
                    SpriteQuads.WriteQuad(v, dotQuad, dotCenter, new int2(BuildingSprites.DotSize), new int2(0, 1), 1, rects[BuildingSprites.DotRect], dot, light);
                    break;
                }
                case BuildingKind.Emitter:
                {
                    SpriteQuads.WriteUpright(v, quad, origin, cell, rects[vis.Rect], color, light);
                    // The scent it gives off: a dot that pulses slowly over the pod.
                    var dot = scentColors[math.min(b.Filter, scentColors.Length - 1)];
                    dot.a = color.a;
                    float pulse = ((int)(time * 2f) & 1) == 0 ? 0f : 1f / WorldConstants.PixelsPerUnit;
                    float2 dotCenter = cellCenter + new float2(0f, pulse);
                    SpriteQuads.WriteQuad(v, dotQuad, dotCenter, new int2(BuildingSprites.DotSize), new int2(0, 1), 1, rects[BuildingSprites.DotRect], dot, light);
                    break;
                }
                default:
                {
                    int frame = vis.Frames > 1 && b.State == BuildingState.Active && b.Status == MachineStatus.Working ? 1 : 0;
                    SpriteQuads.WriteUpright(v, quad, origin, vis.SizePx, rects[vis.Rect + frame], color, light);
                    break;
                }
            }
        }

        public void Dispose()
        {
            _mesh.Dispose();
            _stockMesh.Dispose();
            _sprites.Dispose();
            if (_itemColors.IsCreated) _itemColors.Dispose();
            if (_defs.IsCreated) _defs.Dispose();
            if (_scentColors.IsCreated) _scentColors.Dispose();
        }

        [BurstCompile]
        struct BuildingQuadJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Building> Buildings;
            [ReadOnly] public NativeArray<DefVisual> Defs;
            [ReadOnly] public NativeArray<float4> Rects;
            [ReadOnly] public NativeArray<Color32> ItemColors;
            [ReadOnly] public NativeArray<Color32> ScentColors;
            [NativeDisableParallelForRestriction] public NativeArray<SpriteVertex> Vertices;
            public float Alpha;
            public float Time;
            public bool Overlay;
            public OverlayColors Colors;

            public void Execute(int index)
            {
                int quad = index * QuadsPerBuilding;
                var b = Buildings[index];
                if (!b.Alive)
                {
                    for (int k = 0; k < QuadsPerBuilding; k++) SpriteQuads.WriteEmpty(Vertices, quad + k);
                    return;
                }
                var vis = Defs[b.Def];
                WriteSprite(Vertices, quad, quad + 3, b, vis, Rects, ItemColors, ScentColors, Tint(b), Time);
                WriteItem(quad + 1, b, b.Front);
                WriteItem(quad + 2, b, b.Back);
            }

            Color32 Tint(in Building b)
            {
                if (b.State == BuildingState.Blueprint)
                {
                    // Dithered ghost that fills in as workers build it.
                    float progress = b.WorkTotal > 0f ? 1f - b.WorkLeft / b.WorkTotal : 0f;
                    return new Color32(255, 255, 255, (byte)math.round(math.lerp(0.3f, 0.85f, progress) * 255f));
                }
                if (b.Kind == BuildingKind.Gate)
                {
                    // A battered gate darkens toward red.
                    float hurt = 1f - math.saturate(b.Health / math.max(1f, Defs[b.Def].MaxHealth));
                    return Lerp(new Color32(255, 255, 255, 255), new Color32(255, 110, 90, 255), hurt);
                }
                if (b.Kind > BuildingKind.Processor) return new Color32(255, 255, 255, 255);
                if (!Overlay) return new Color32(255, 255, 255, 255);
                if (b.Kind == BuildingKind.Processor)
                    return b.Status == MachineStatus.Working ? Colors.Working : b.Status == MachineStatus.Idle ? Colors.Starved : Colors.Jam;
                if (b.Jammed) return Colors.Jam;
                return Lerp(Colors.Idle, Colors.Flow, math.saturate(b.Rate / Colors.FullRate));
            }

            static Color32 Lerp(Color32 a, Color32 b, float t)
            {
                float4 c = math.lerp(new float4(a.r, a.g, a.b, a.a), new float4(b.r, b.g, b.b, b.a), t);
                return new Color32((byte)c.x, (byte)c.y, (byte)c.z, (byte)c.w);
            }

            void WriteItem(int quad, in Building b, in ItemSlot slot)
            {
                if (slot.IsEmpty)
                {
                    SpriteQuads.WriteEmpty(Vertices, quad);
                    return;
                }
                float2 cellPos = math.lerp(slot.Prev, slot.Pos, Alpha);
                // Items ride on top of a belt's silk band (bottom of the cell); elsewhere they sit in the middle.
                float lift = b.Kind == BuildingKind.Belt ? 0.5f / BuildingSprites.Cell : 0f;
                float2 world = (cellPos + new float2(0f, lift)) * WorldConstants.CellSize;
                SpriteQuads.WriteQuad(Vertices, quad, world, new int2(BuildingSprites.ItemSize), new int2(0, 1), 1,
                    Rects[BuildingSprites.ItemRect], ItemColors[slot.Item], world);
            }
        }
    }
}
