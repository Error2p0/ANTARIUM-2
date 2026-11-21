using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>The demo's state in a colony (GDD 2.16; saved in <c>DEMO</c>).</summary>
    public sealed class DemoState
    {
        /// <summary>This colony's world has the Ancient Seal (it was founded in the demo).</summary>
        public bool Sealed;
        /// <summary>The colony has dug a cell next to the seal.</summary>
        public bool SealReached;
        /// <summary>Where it first reached the seal.</summary>
        public int2 Contact;
        /// <summary>The end-of-demo screen has been shown (the timed offer comes only once).</summary>
        public bool EndShown;
        /// <summary>Per column: the highest row of the seal, or -1 (derived from the world, not saved).</summary>
        public int[] SealTop;
    }

    /// <summary>The colony first dug a cell next to the demo's seal; cells beyond it were revealed around <see cref="Glimpse"/>.</summary>
    public readonly struct DemoSealReachedEvent
    {
        public readonly int2 Cell;
        public readonly int2 Glimpse;
        public DemoSealReachedEvent(int2 cell, int2 glimpse) { Cell = cell; Glimpse = glimpse; }
    }

    /// <summary>A colony carried over from the demo was loaded by the full game and its seal was opened (<see cref="Cells"/> restored).</summary>
    public readonly struct DemoSealBrokenEvent
    {
        public readonly int Cells;
        public DemoSealBrokenEvent(int cells) { Cells = cells; }
    }

    /// <summary>
    /// The Steam Next Fest demo (GDD 2.16): in a demo run new colonies get the Ancient Seal at the bottom of the stone strata,
    /// research / evolution levels / contracts that need what lies below it are full game only, and the colony learns when it
    /// reaches the seal. The full game opens the seal of a colony carried over from the demo.
    /// </summary>
    public sealed partial class Simulation
    {
        public DemoState Demo { get; } = new();

        /// <summary>The demo's rules, when this run is the demo; else null.</summary>
        public DemoData DemoRules => Content.ActiveDemo;
        public bool IsDemo => DemoRules != null;

        /// <summary>Deepest dig a contract may ask for in the demo: the seal's depth less its border warp.</summary>
        public int DemoDepthLimit
        {
            get
            {
                var demo = DemoRules;
                if (demo == null) return int.MaxValue;
                return demo.SealDepth(Content.WorldGen, Tweaks.BiomeScale) - (int)math.ceil(Content.WorldGen.biomeWarpAmplitude) - 2;
            }
        }

        /// <summary>A new demo colony's world is generated with the seal.</summary>
        void PrepareDemoWorld(bool fresh)
        {
            if (fresh && IsDemo) Tweaks = Tweaks.WithSeal(Content, DemoRules);
        }

        /// <summary>After the systems exist: locks for a demo run; a fresh sealed world finds its seal.</summary>
        void InitDemo(bool fresh)
        {
            Research.FullGame = DemoRules?.FullGameResearch;
            Evolutions.FullGame = DemoRules?.FullGameEvolution;
            if (!fresh || !Tweaks.Seal) return;
            Demo.Sealed = true;
            FindSeal();
        }

        /// <summary>After a load: a demo run finds the seal again; the full game opens it.</summary>
        void FinishDemoLoad()
        {
            if (!Demo.Sealed) return;
            if (IsDemo) FindSeal();
            else BreakSeal();
        }

        /// <summary>The cell is the seal or below it, in a sealed world (mining orders are not placed there).</summary>
        public bool IsBeyondSeal(int x, int y)
        {
            var top = Demo.SealTop;
            return Demo.Sealed && top != null && x >= 0 && x < top.Length && top[x] >= 0 && y <= top[x];
        }

        /// <summary>The material at the cell is the demo's seal.</summary>
        public bool IsSeal(int2 c)
        {
            var demo = Content.Demo;
            return demo != null && World.InBounds(c.x, c.y) && World.Cells[World.Index(c.x, c.y)].Material == demo.SealMaterial;
        }

        /// <summary>The timed end-of-demo offer is due: played long enough, not shown yet, and no raid on.</summary>
        public bool DemoEndDue
        {
            get
            {
                var demo = DemoRules;
                return demo != null && !Demo.EndShown && PlayTime >= demo.Def.endScreenMinutes * 60.0 && Enemies.ActiveRaid == 0;
            }
        }

        /// <summary>The end-of-demo screen was shown (it is not offered again by time).</summary>
        public void MarkDemoEndShown() => Demo.EndShown = true;

        void FindSeal()
        {
            var demo = Content.Demo;
            int width = World.Width;
            var top = Demo.SealTop = new int[width];
            if (demo == null)
            {
                System.Array.Fill(top, -1);
                return;
            }
            // The seal lies around the bottom of its biome; start well above it (the surface line moves a little after generation).
            int from = demo.SealDepth(Content.WorldGen, Tweaks.BiomeScale) - (int)math.ceil(Content.WorldGen.biomeWarpAmplitude) - 40;
            for (int x = 0; x < width; x++)
            {
                top[x] = -1;
                for (int y = math.min(World.Height - 1, World.SurfaceY[x] - math.max(0, from)); y >= 0; y--)
                    if (World.Cells[World.Index(x, y)].Material == demo.SealMaterial)
                    {
                        top[x] = y;
                        break;
                    }
            }
        }

        /// <summary>Neighbours to look for the seal in: below first (the usual way down), then the sides, then diagonals.</summary>
        static readonly int2[] SealNeighbours =
        {
            new(0, -1), new(-1, 0), new(1, 0), new(-1, -1), new(1, -1), new(0, 1), new(-1, 1), new(1, 1),
        };

        /// <summary>A cell was dug out: is the seal next to it? The first time, reveal a window beyond it.</summary>
        void CheckSealContact(int2 c)
        {
            if (!Demo.Sealed || Demo.SealReached) return;
            foreach (var d in SealNeighbours)
            {
                var n = c + d;
                if (!IsSeal(n)) continue;
                ReachSeal(c, n.x);
                return;
            }
        }

        void ReachSeal(int2 c, int column)
        {
            Demo.SealReached = true;
            Demo.Contact = c;
            var cfg = Content.Demo.Def;
            // Below the seal in that column, then a round window of revealed cells: the crystal caverns glow through.
            int y = Demo.SealTop != null && Demo.SealTop[column] >= 0 ? Demo.SealTop[column] : c.y - 1;
            while (y > 0 && IsSeal(new int2(column, y))) y--;
            var glimpse = new int2(column, math.max(1, y - cfg.glimpseDepth));
            if (cfg.glimpseRadius > 0) Reveal.Around(World, glimpse, cfg.glimpseRadius);
            Events.Publish(new DemoSealReachedEvent(c, glimpse));
        }

        /// <summary>
        /// The full game opens the seal of a colony carried over from the demo: the world is generated again from its seed and
        /// world modifiers (deterministic), and every seal cell gets back what was there. Fog, fluids and light follow.
        /// </summary>
        void BreakSeal()
        {
            var demo = Content.Demo;
            Demo.Sealed = false;
            Demo.SealTop = null;
            if (demo == null) return; // nothing knows the seal: it stays as rock
            var tweaks = WorldTweaks.From(Content, _worldModifiers);
            var fresh = WorldGenerator.Create(Content, Seed, tweaks);
            int restored = 0;
            try
            {
                for (int i = 0; i < World.Cells.Length; i++)
                {
                    var cell = World.Cells[i];
                    if (cell.Material != demo.SealMaterial) continue;
                    var original = fresh.Cells[i];
                    original.Flags = cell.Flags & CellFlags.Revealed;
                    World.Cells[i] = original;
                    World.Fluid[i] = fresh.Fluid[i];
                    restored++;
                }
            }
            finally
            {
                fresh.Dispose();
            }
            if (restored == 0) return;
            World.MarkAll(ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute | ChunkFlags.FluidUpload);
            Light.RecomputeAll(World, Content.MaterialSim);
            Fluids.WakeAllWithFluid();
            _terrainChanged = true;
            Events.Publish(new DemoSealBrokenEvent(restored));
        }
    }
}
