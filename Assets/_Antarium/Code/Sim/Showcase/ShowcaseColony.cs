using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// The colony shown in store media: a grown nest with a storage hall and nurseries, a factory hall fed by an ore
    /// room beside it (miners dig ore that rains onto a belt, a root lift carries it up to the smelter, copper rides on
    /// to the archive), every machine, a shaft down through every biome with a working outpost in each, all castes and
    /// all research. Built from a fixed seed with the instant showcase edits, then run for a while so ants, belts and
    /// machines are busy. Everything near the surface has light wells: sky light only reaches ~32 open cells.
    /// Positions are relative to the queen (<c>q</c>; y grows upward).
    /// </summary>
    public static class ShowcaseColony
    {
        public const uint Seed = 20261108u;

        /// <summary>Depth below the surface of each deep outpost (crystal caverns, fungal depths, magma zone, ruins).</summary>
        public static readonly int[] OutpostDepths = { 770, 950, 1140, 1330 };

        /// <summary>Placements that failed during the last build (for the log).</summary>
        public static readonly List<string> Warnings = new();

        /// <summary>Key positions of a build, for camera shots (cells).</summary>
        [System.Serializable]
        public struct Layout
        {
            public int2 Queen;
            public int2 StorageHall, Nursery, Factory, OreRoom, Lift, Smelter, Archive, Princess;
            public int ShaftX;
            public int2[] Outposts;
            public int Surface;
        }

        public static Layout Last;

        public static Simulation Build(ContentDB content, GameConfig config, float settleSeconds = 90f)
        {
            Warnings.Clear();
            var sim = new Simulation(content, config, Seed, new Legacy(content));
            new Builder(sim).Run(settleSeconds);
            return sim;
        }

        sealed class Builder
        {
            readonly Simulation _sim;
            readonly ContentDB _c;
            readonly int2 _q;
            readonly int _surface;
            Layout _layout;
            Random _spread = new(0x5EEDu);

            public Builder(Simulation sim)
            {
                _sim = sim;
                _c = sim.Content;
                _q = sim.Colony.QueenCell;
                _surface = sim.World.SurfaceY[_q.x];
            }

            int2 Q(int dx, int dy) => _q + new int2(dx, dy);
            float2 Qf(float dx, float dy) => (float2)_q + new float2(dx + 0.5f, dy + 0.5f);

            public void Run(float settleSeconds)
            {
                _sim.ResearchAllNow();
                _layout.Queen = _q;
                _layout.Surface = _surface;

                CarveNest();
                int shaftX = FindClearColumn(_q.x - 62, _q.x - 58, _q.y - 12, _surface - OutpostDepths[^1] - 4);
                _layout.ShaftX = shaftX;
                CarveShaft(shaftX);
                _sim.ClearPilesNow();

                FillOre();
                PlaceBuildings();
                PaintZones();
                Stock();
                Populate();

                // Let the colony settle into its routines: ants spread to their jobs, belts and machines fill up.
                _sim.FastForward(settleSeconds);
                // Fresh work for the shots: the ore ceiling ordered for mining, crews at the deep outposts.
                DesignateOre();
                CrewOutposts();
                _sim.FastForward(12f);
                Last = _layout;
            }

            // ------------------------------------------------------------ terrain

            void CarveNest()
            {
                // Left wing: storage hall, then the nurseries (the deep shaft starts there).
                _sim.CarveLineNow(Qf(-6, 0), Qf(-18, -1), 1.5f);
                CarveRoom(Q(-48, -4), Q(-18, 2));
                _layout.StorageHall = Q(-33, -2);
                _sim.CarveLineNow(Qf(-48, -2), Qf(-56, -3), 1.5f);
                CarveRoom(Q(-76, -6), Q(-56, -1));
                _layout.Nursery = Q(-66, -4);
                LightWell(-26, 2);
                LightWell(-40, 2);
                LightWell(-68, -1);

                // Right wing: the factory hall, and the ore room beside it one floor lower; a lift joins them.
                _sim.CarveLineNow(Qf(6, 0), Qf(18, 5), 1.5f);
                CarveRoom(Q(18, 4), Q(88, 11));
                _layout.Factory = Q(53, 7);
                CarveRoom(Q(90, -2), Q(124, 4));
                _layout.OreRoom = Q(107, 1);
                _sim.CarveRectNow(Q(89, -2), Q(89, 4));
                _layout.Lift = Q(89, 1);
                LightWell(30, 11);
                LightWell(50, 11);
                LightWell(72, 11);

                // A mature colony has explored around its nest: no fog patches between the wells.
                for (int x = -132; x <= 140; x += 8)
                for (int y = -40; y <= 24; y += 8)
                    _sim.RevealNow(Q(x, y), 6);
            }

            /// <summary>A 2-cell shaft from the room top at <paramref name="dx"/> straight up to the sky.</summary>
            void LightWell(int dx, int top)
            {
                int x = _q.x + dx;
                int sky = _sim.World.SurfaceY[x] + 2;
                _sim.CarveLineNow(new float2(x + 1f, _q.y + top + 0.5f), new float2(x + 1f, sky + 0.5f), 1.05f);
            }

            /// <summary>A room with a flat floor (row <c>min.y</c>) and rounded ends.</summary>
            void CarveRoom(int2 min, int2 max)
            {
                _sim.CarveRectNow(min, max);
                float h = max.y - min.y + 1;
                float r = math.min(h * 0.5f, 3f);
                _sim.CarveEllipseNow(new float2(min.x, min.y + h * 0.5f), new float2(r, h * 0.45f));
                _sim.CarveEllipseNow(new float2(max.x + 1, min.y + h * 0.5f), new float2(r, h * 0.45f));
                // A flat solid floor for machines.
                var floor = SolidMaterialNear(min + new int2(0, -2));
                _sim.FillRectNow(new int2(min.x - 1, min.y - 1), new int2(max.x + 1, min.y - 1), floor);
            }

            ushort SolidMaterialNear(int2 c)
            {
                var cell = _sim.World[c.x, c.y];
                return cell.IsOpen ? Mat("dirt") : cell.Material;
            }

            void CarveShaft(int x)
            {
                int top = _q.y - 6;
                int bottom = _surface - OutpostDepths[^1] - 2;
                _sim.CarveLineNow(new float2(x + 0.5f, top + 0.5f), new float2(x + 0.5f, bottom + 0.5f), 1.5f);
                _sim.ClearFluidNow(new int2(x - 3, bottom), new int2(x + 3, top));

                _layout.Outposts = new int2[OutpostDepths.Length];
                for (int i = 0; i < OutpostDepths.Length; i++)
                {
                    int y = _surface - OutpostDepths[i];
                    int side = (i & 1) == 0 ? 1 : -1;
                    // A gallery off the shaft that breaks into whatever caves lie there, ending in a work chamber.
                    var end = new float2(x + side * 46 + 0.5f, y + 0.5f);
                    _sim.CarveLineNow(new float2(x + 0.5f, y + 0.5f), end, 1.6f);
                    _sim.CarveEllipseNow(end, new float2(9f, 4.5f));
                    _sim.CarveLineNow(new float2(x + 0.5f, y + 0.5f), new float2(x - side * 18 + 0.5f, y + 3.5f), 1.4f);
                    _layout.Outposts[i] = (int2)end;
                }
            }

            /// <summary>The column in [x0, x1] with the least fluid within 3 cells along the depth range.</summary>
            int FindClearColumn(int x0, int x1, int top, int bottom)
            {
                var world = _sim.World;
                int best = (x0 + x1) / 2, bestScore = int.MaxValue;
                for (int x = x0; x <= x1; x++)
                {
                    int score = 0;
                    for (int y = bottom; y <= top; y++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        if (!world.InBounds(x + dx, y)) continue;
                        if (!world.Fluid[world.Index(x + dx, y)].IsEmpty) score++;
                    }
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = x;
                    }
                }
                return best;
            }

            void FillOre()
            {
                // The ore room's ceiling: copper ore in dirt (only copper, so one smelter keeps up), with light wells through it.
                ushort copper = Mat("copper_ore"), dirt = Mat("dirt");
                var rng = new Random(Seed | 1u);
                int2 min = Q(90, 5), max = Q(124, 10);
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    float roll = rng.NextFloat();
                    ushort m = roll < 0.6f ? copper : dirt;
                    _sim.FillRectNow(new int2(x, y), new int2(x, y), m);
                }
                LightWell(97, 4);
                LightWell(107, 4);
                LightWell(117, 4);
                _sim.ClearPilesNow();
            }

            void DesignateOre()
            {
                int2 min = Q(90, 5), max = Q(124, 10);
                for (int x = min.x + 1; x <= max.x; x += 3)
                    _sim.EnqueueDesignate(new float2(x + 0.5f, min.y + 2.5f), 3f, true);
                _sim.Tick();
                Spread(92, 122, 3, 12, AntCaste.Miner);
            }

            // ------------------------------------------------------------ buildings

            void PlaceBuildings()
            {
                int belt = Building("silk_belt"), lift = Building("root_lift");
                int ff = _q.y + 4; // factory floor row
                int of = _q.y - 2; // ore room floor row
                int lx = _q.x + 89;

                // Ore room: a belt along the floor to the lift, which climbs to the factory floor.
                for (int x = _q.x + 124; x > lx; x--) Place(belt, new int2(x, of), new int2(-1, 0));
                for (int y = of; y < ff; y++) Place(lift, new int2(lx, y), new int2(0, 1));
                // Factory floor: from the lift top to the smelter...
                for (int x = lx; x >= _q.x + 83; x--) Place(belt, new int2(x, ff), new int2(-1, 0));
                Place(Building("smelter"), Q(80, 4), default);
                _layout.Smelter = Q(81, 5);
                // ...and the smelter's copper on to the archive.
                for (int x = _q.x + 79; x >= _q.x + 69; x--) Place(belt, new int2(x, ff), new int2(-1, 0));
                Place(Building("archive"), Q(66, 4), default);
                _layout.Archive = Q(67, 5);

                Place(Building("princess_chamber"), Q(58, 4), default);
                _layout.Princess = Q(59, 5);
                Place(Building("fungus_garden"), Q(46, 4), default);
                Place(Building("pheromone_emitter"), Q(-50, -2), default, (ushort)PheromoneKind.Trail);
                Place(Building("fungus_garden"), Q(37, 4), default);
                Place(Building("smelter"), Q(24, 4), default);

                Place(Building("nursery"), Q(-75, -6), default);
                Place(Building("nursery"), Q(-71, -6), default);
                Place(Building("nursery"), Q(-67, -6), default);

                Place(Building("guard_post"), Q(-10, -1), default);
                foreach (var o in _layout.Outposts)
                    Place(Building("guard_post"), _sim.World.FloorBelow(o), default);
            }

            void PaintZones()
            {
                for (int x = _q.x - 46; x <= _q.x - 20; x += 2)
                    _sim.EnqueueStorageZone(new float2(x + 0.5f, _q.y - 3.5f), 1.3f, true);
                foreach (var o in _layout.Outposts)
                    _sim.EnqueueStorageZone(new float2(o.x + 0.5f, _sim.World.FloorBelow(o).y + 1.5f), 2.5f, true);

                // A trail along the left wing's floor (the scent is drawn as speckles, so keep it to one route).
                for (int x = -54; x <= -6; x += 2) _sim.EnqueuePheromone(PheromoneKind.Trail, Qf(x, -3), 0.8f, true);
                _sim.Tick();
            }

            void Stock()
            {
                _sim.GiveResources(40, 1000f);
                _sim.Colony.Inventory.AddKnowledge(240f, studied: false);
                _sim.Colony.Princesses = 2;
            }

            void Populate()
            {
                var col = _sim.Colony;
                col.SetCasteShare(AntCaste.Soldier, 0.15f);
                col.SetCasteShare(AntCaste.Miner, 0.15f);
                col.SetCasteShare(AntCaste.Hauler, 0.1f);
                col.SetCasteShare(AntCaste.Nurse, 0.05f);
                col.SetCasteShare(AntCaste.Builder, 0.05f);
                col.SetCasteShare(AntCaste.Scout, 0.05f);
                col.SetCasteShare(AntCaste.Engineer, 0.05f);

                _sim.SpawnAnts(60, AntCaste.Worker);
                _sim.SpawnAnts(8, AntCaste.Soldier);
                _sim.SpawnAnts(5, AntCaste.Scout);
                Spread(-46, -20, -3, 30, AntCaste.Worker);
                Spread(-46, -20, -3, 8, AntCaste.Hauler);
                Spread(-74, -58, -5, 8, AntCaste.Nurse);
                Spread(20, 86, 6, 30, AntCaste.Worker);
                Spread(20, 86, 6, 8, AntCaste.Hauler);
                Spread(20, 86, 6, 5, AntCaste.Engineer);
                Spread(20, 86, 6, 4, AntCaste.Builder);
                Spread(-30, 30, 0, 10, AntCaste.Soldier);


                // Eggs on the royal chamber floor.
                for (int i = 0; i < 6; i++)
                    _sim.Colony.LayEggNow(_sim.Colony.QueenCell + new int2(i % 2 == 0 ? -2 - i / 2 : 2 + i / 2, 0));
                _sim.Tick();
            }

            void CrewOutposts()
            {
                foreach (var o in _layout.Outposts)
                {
                    int dx = o.x - _q.x, dy = o.y - _q.y;
                    Spread(dx - 8, dx + 8, dy, 10, AntCaste.Miner);
                    Spread(dx - 8, dx + 8, dy, 10, AntCaste.Worker);
                    Spread(dx - 8, dx + 8, dy, 6, AntCaste.Soldier);
                    Spread(dx - 8, dx + 8, dy, 2, AntCaste.Scout);
                    // Rock around the work chamber to keep the miners busy.
                    _sim.EnqueueDesignate(new float2(o.x + 11.5f, o.y + 0.5f), 4f, true);
                    _sim.EnqueueDesignate(new float2(o.x - 11.5f, o.y + 0.5f), 4f, true);
                    _sim.EnqueueDesignate(new float2(o.x + 0.5f, o.y + 6.5f), 3.5f, true);
                }
                _sim.Tick();
            }

            /// <summary>Spawns ants on the floor at random x in [<paramref name="dx0"/>, <paramref name="dx1"/>] (queen-relative), dropping from row <paramref name="dy"/>.</summary>
            void Spread(int dx0, int dx1, int dy, int count, AntCaste caste)
            {
                for (int i = 0; i < count; i++)
                {
                    var c = Q(_spread.NextInt(dx0, dx1 + 1), dy);
                    if (!_sim.World.InBounds(c.x, c.y) || !_sim.World[c.x, c.y].IsOpen) continue;
                    _sim.SpawnAntsAt(_sim.World.FloorBelow(c), 1, caste);
                }
            }

            // ------------------------------------------------------------ helpers

            void Place(int def, int2 origin, int2 dir, ushort filter = 0)
            {
                if (def < 0) return;
                var result = _sim.PlaceNow(def, origin, dir, filter);
                if (result != PlaceResult.Ok) Warnings.Add($"{_c.Buildings[def].Def.Id} at {origin - _q} (q-relative): {result}");
            }

            int Building(string id)
            {
                if (_c.TryGetBuildingIndex(id, out int index)) return index;
                Warnings.Add($"no building '{id}'");
                return -1;
            }

            ushort Mat(string id) => _c.TryGetMaterialIndex(id, out ushort m) ? m : (ushort)0;
        }
    }
}
