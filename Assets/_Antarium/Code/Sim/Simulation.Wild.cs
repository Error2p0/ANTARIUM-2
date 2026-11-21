using System;
using Antarium.Content;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Wild food (Milestone 11): berries falling from berry bushes, toadstools popping up on the surface (more after rain),
    /// and mushrooms growing on the floors of natural caves. A grown cave mushroom is an item pile like any other, so
    /// workers pick it up once the tunnels reach it; it grows back after a few minutes. Glowing mushrooms light the cave
    /// around them while they stand.
    /// </summary>
    public sealed partial class Simulation
    {
        public readonly WildState Wild = new();

        /// <summary>
        /// Puts growth spots on the floors of natural caves not explored yet (a new world, or a save from before Milestone 11),
        /// by the content's cave growth rules: in noise patches, by chance per floor cell, the first rule that hits wins.
        /// Each starts grown.
        /// </summary>
        internal void PlaceCaveGrowths()
        {
            Wild.Placed = true;
            PlaceElderFruit();
            var rules = Content.CaveGrowths;
            var gen = Content.WorldGen;
            if (rules.Length == 0 || gen.maxCaveGrowths <= 0) return;
            float scale = math.max(0.001f, gen.growthPatchScale);
            var offset = new float2(math.hash(new uint2(Seed, 0x6A0Bu)) % 10000u, math.hash(new uint2(Seed, 0x6A0Cu)) % 10000u);
            int edge = gen.bedrockThickness + 1;
            ushort wood = gen.elderWood != null ? Content.GetMaterialIndex(gen.elderWood) : ContentDB.AirIndex;
            ushort leaves = gen.elderLeaves != null ? Content.GetMaterialIndex(gen.elderLeaves) : ContentDB.AirIndex;
            for (int x = edge; x < World.Width - edge; x++)
            for (int y = World.SurfaceY[x] - gen.caveMinDepth; y > edge; y--)
            {
                var cell = World[x, y];
                if (!cell.IsOpen || (cell.Flags & CellFlags.Explored) != 0 || World[x, y - 1].IsOpen) continue;
                ushort floor = World[x, y - 1].Material;
                if (floor != ContentDB.AirIndex && (floor == wood || floor == leaves) || Wild.IndexAt(new int2(x, y)) >= 0) continue; // the Elder Tree's own
                if (World.Fluid[World.Index(x, y)].Level > 0) continue;
                if (noise.snoise(new float2(x, y) * scale + offset) <= 0f) continue; // outside a patch
                int biome = BiomeAtDepth(World.SurfaceY[x] - y);
                for (int r = 0; r < rules.Length; r++)
                {
                    var rule = rules[r];
                    if (rule.Biome >= 0 && rule.Biome != biome) continue;
                    if ((math.hash(new uint4((uint)x, (uint)y, Seed, (uint)r)) & 0xFFFF) >= rule.Chance * 65536f) continue;
                    var c = new int2(x, y);
                    Wild.Add(new Growth { Cell = c, Item = rule.Item, Grown = true });
                    Colony.Piles.Add(c, rule.Item, 1);
                    Glow(c, rule.Item, true);
                    if (Wild.Growths.Count >= gen.maxCaveGrowths) return;
                    break;
                }
            }
        }

        /// <summary>The Elder Tree's fruit (batch 6): growth spots on top of its canopy and branches, grown from the start.</summary>
        void PlaceElderFruit()
        {
            if (!Surface.HasElderTree || Content.ElderFruit == ContentDB.NoItem) return;
            foreach (var c in ElderTree.FruitSpots(World, Content, Surface.ElderTree, Seed))
            {
                if (Wild.IndexAt(c) >= 0) continue;
                Wild.Add(new Growth { Cell = c, Item = Content.ElderFruit, Grown = true });
                Colony.Piles.Add(c, Content.ElderFruit, 1);
                Glow(c, Content.ElderFruit, true);
            }
        }

        /// <summary>The first time a breach reveals part of the Elder Tree's cavern, the colony has found it (announced once, saved).</summary>
        void CheckElderFound(NativeList<int2> discovered)
        {
            if (Wild.ElderFound || !Surface.HasElderTree) return;
            var site = Surface.ElderTree;
            for (int i = 0; i < discovered.Length; i++)
            {
                if (!site.Contains(discovered[i])) continue;
                Wild.ElderFound = true;
                RevealElderCavern(site);
                Events.Publish(new ElderTreeFoundEvent(discovered[i]));
                return;
            }
        }

        /// <summary>The canopy can wall off parts of the cavern: finding the tree reveals all of it at once.</summary>
        void RevealElderCavern(in ElderSite site)
        {
            var starts = new NativeList<int2>(1024, Allocator.TempJob);
            try
            {
                for (int x = site.Center.x - site.Half.x; x <= site.Center.x + site.Half.x; x++)
                for (int y = site.Center.y - site.Half.y; y <= site.Center.y + site.Half.y; y++)
                    if (Reveal.IsUnexploredOpen(World, x, y)) starts.Add(new int2(x, y));
                if (starts.Length > 0) Reveal.Flood(World, starts.AsArray(), Config.dig.revealRadius, Config.dig.caveRevealLimit);
                ElderTree.RevealTree(World, Content, site);
            }
            finally
            {
                starts.Dispose();
            }
        }

        /// <summary>After a load: glowing mushrooms that stand light their caves again (the light map keeps no emitters).</summary>
        internal void RestoreWildLights()
        {
            foreach (var g in Wild.Growths)
                if (g.Grown) Glow(g.Cell, g.Item, true);
        }

        void Glow(int2 cell, ushort item, bool on)
        {
            var rgb = Content.GrowthLight[item];
            if (math.cmax(rgb) <= 0f) return;
            Light.SetEmitter(World, cell, on ? rgb : float3.zero);
        }

        void UpdateWild(float dt)
        {
            var w = Wild;
            var c = Config.wild;
            w.SinceRain = Surface.Weather == WeatherKind.Rain ? 0f : math.min(1e6f, w.SinceRain + dt);
            UpdateBerries(dt);
            UpdateApples(dt);
            UpdateToadstools(dt);

            w.CheckTimer += dt;
            if (w.CheckTimer < c.checkInterval) return;
            float step = w.CheckTimer;
            w.CheckTimer = 0f;
            var list = w.Growths;
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                bool standing = Colony.Piles.CountAt(g.Cell, g.Item) > 0;
                if (standing)
                {
                    if (g.Grown) continue;
                    g.Grown = true;
                    Glow(g.Cell, g.Item, true);
                }
                else if (g.Grown)
                {
                    // Picked: it grows back after a while.
                    g.Grown = false;
                    float lo = math.max(1f, c.regrowSeconds.x), hi = math.max(lo, c.regrowSeconds.y);
                    float roll = (math.hash(new uint4((uint)g.Cell.x, (uint)g.Cell.y, Seed, (uint)(PlayTime * 10.0))) & 0xFFFF) / 65535f;
                    g.Timer = math.lerp(lo, hi, roll);
                    Glow(g.Cell, g.Item, false);
                }
                else
                {
                    g.Timer -= step;
                    if (g.Timer > 0f) { list[i] = g; continue; }
                    if (!CanGrow(g.Cell))
                    {
                        g.Timer = c.checkInterval * 10f; // look again later
                        list[i] = g;
                        continue;
                    }
                    g.Grown = true;
                    Colony.Piles.Add(g.Cell, g.Item, 1);
                    Glow(g.Cell, g.Item, true);
                }
                list[i] = g;
            }
        }

        /// <summary>A picked mushroom grows back only on an open, dry-enough floor that nothing was built on or painted.</summary>
        bool CanGrow(int2 c)
        {
            if (!World.InBounds(c.x, c.y - 1)) return false;
            var cell = World[c.x, c.y];
            if (!cell.IsOpen || World[c.x, c.y - 1].IsOpen) return false;
            if ((cell.Flags & (CellFlags.Storage | CellFlags.Blueprint | CellFlags.FluidBlock)) != 0) return false;
            var f = World.Fluid[World.Index(c.x, c.y)];
            if (f.Level > 0 && (f.Kind == FluidKind.Lava || f.Kind == FluidKind.Fire || f.Level >= Config.wild.maxWater)) return false;
            return Buildings.At(c) < 0;
        }

        /// <summary>
        /// True if ants can walk from the nest to <paramref name="c"/> (the Home field reaches it). Forage is only put where it
        /// can be fetched: a lake or river cuts the surface in two. While the field is being rebuilt, everything counts.
        /// </summary>
        bool ReachableFromNest(int2 c) =>
            Fields.IsDirty(FieldKind.Home) || Fields.Get(FieldKind.Home, World, c) != FlowFields.Unreachable;

        int ForageOnSurface(ushort[] items)
        {
            int n = 0;
            for (int i = 0; i < Colony.Piles.Count; i++)
            {
                var p = Colony.Piles[i];
                if (p.Cell.y > World.SurfaceY[p.Cell.x] - 2 && Array.IndexOf(items, p.Item) >= 0) n += p.Count;
            }
            return n;
        }

        /// <summary>By day (and on a starfall night) berries fall under the berry bushes near the nest, up to <c>maxSurfaceBerries</c>.</summary>
        void UpdateBerries(float dt)
        {
            var s = Surface;
            var c = Config.wild;
            if (Content.BushForage.Length == 0 || s.Plants.Count == 0 || !s.ForageGrows) return;
            float rate = Colony.Mods.Mult(StatKind.ForageRate) * WeatherForage;
            if (rate <= 0f) return;
            Wild.BerryTimer += dt;
            if (Wild.BerryTimer < c.berryInterval / rate) return;
            Wild.BerryTimer = 0f;
            if (ForageOnSurface(Content.BushForage) >= c.maxSurfaceBerries) return;

            int range = Config.colony.forageRange;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var plant = s.Plants[s.Rng.NextInt(s.Plants.Count)];
                if (plant.Kind != SurfacePlants.BerryBush || math.abs(plant.X - Colony.QueenCell.x) > range || !SurfacePlants.IsAlive(World, plant)) continue;
                int x = math.clamp(plant.X + s.Rng.NextInt(-1, 2), 1, World.Width - 2);
                int2 top = new int2(x, math.min(World.Height - 1, World.SurfaceY[x] + 3));
                if (!World[top.x, top.y].IsOpen || !ReachableFromNest(World.FloorBelow(top))) continue;
                DropItem(top, Content.BushForage[s.Rng.NextInt(Content.BushForage.Length)], 1);
                return;
            }
        }

        /// <summary>By day (and on a starfall night) apples fall under the apple trees near the nest (batch 6), up to <c>maxSurfaceApples</c>.</summary>
        void UpdateApples(float dt)
        {
            var s = Surface;
            var c = Config.wild;
            if (Content.TreeForage.Length == 0 || s.Plants.Count == 0 || !s.ForageGrows) return;
            float rate = Colony.Mods.Mult(StatKind.ForageRate) * WeatherForage;
            if (rate <= 0f) return;
            Wild.AppleTimer += dt;
            if (Wild.AppleTimer < c.appleInterval / rate) return;
            Wild.AppleTimer = 0f;
            if (ForageOnSurface(Content.TreeForage) >= c.maxSurfaceApples) return;

            int range = Config.colony.forageRange;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var plant = s.Plants[s.Rng.NextInt(s.Plants.Count)];
                if (plant.Kind != SurfacePlants.AppleTree || math.abs(plant.X - Colony.QueenCell.x) > range || !SurfacePlants.IsAlive(World, plant)) continue;
                int x = math.clamp(plant.X + s.Rng.NextInt(-c.appleSpread, c.appleSpread + 1), 1, World.Width - 2);
                int2 top = new int2(x, math.min(World.Height - 1, World.SurfaceY[x] + 3));
                if (!World[top.x, top.y].IsOpen || !ReachableFromNest(World.FloorBelow(top))) continue;
                DropItem(top, Content.TreeForage[s.Rng.NextInt(Content.TreeForage.Length)], 1);
                return;
            }
        }

        /// <summary>
        /// Toadstools pop up on open ground near the nest, day and night, <c>rainToadstoolFactor</c> times as often while it rains
        /// and for <c>afterRainSeconds</c> after; at most <c>maxSurfaceToadstools</c> at a time.
        /// </summary>
        void UpdateToadstools(float dt)
        {
            var c = Config.wild;
            var w = Wild;
            if (Content.SurfaceMushrooms.Length == 0) return;
            bool wet = Surface.Weather == WeatherKind.Rain || w.SinceRain < c.afterRainSeconds;
            float rate = Colony.Mods.Mult(StatKind.ForageRate) * (wet ? c.rainToadstoolFactor : 1f);
            if (rate <= 0f) return;
            w.ToadstoolTimer += dt * rate;
            if (w.ToadstoolTimer < c.toadstoolInterval) return;
            w.ToadstoolTimer = 0f;
            if (ForageOnSurface(Content.SurfaceMushrooms) >= c.maxSurfaceToadstools) return;

            var s = Surface;
            int range = Config.colony.forageRange, keep = Config.colony.royalRadius + 2;
            var nav = new NavGrid(World);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int dx = s.Rng.NextInt(keep, range + 1) * (s.Rng.NextBool() ? 1 : -1);
                int x = math.clamp(Colony.QueenCell.x + dx, 2, World.Width - 3);
                int y = World.Height - 1;
                while (y > 0 && World[x, y - 1].IsOpen) y--;
                var cell = new int2(x, y);
                if (y <= World.SurfaceY[x] - 2 || !nav.IsWalkable(cell) || World.Fluid[World.Index(x, y)].Level >= c.maxWater) continue;
                if (Buildings.At(cell) >= 0 || !ReachableFromNest(cell)) continue;
                Colony.Piles.Add(cell, Content.SurfaceMushrooms[s.Rng.NextInt(Content.SurfaceMushrooms.Length)], 1);
                return;
            }
        }
    }
}
