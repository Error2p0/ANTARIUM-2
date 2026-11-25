using System;
using System.Collections;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The demo's store media (capture set <c>demo</c>; GDD 2.16): the store shots again with the demo's rules on, on the demo's
    /// own showcase colony (the Ancient Seal in the world, nothing from the full game researched, placed or stocked), so the HUD,
    /// research tree and queen panel show the demo as players see it; and the Ancient Seal itself, which only the demo has.
    /// Stills go to <c>demo_stills/</c> under the store names (the shots beyond the seal are skipped), the seal's clip to
    /// <c>clips/d_seal</c>; the store shots' own clips are not recorded again.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>A demo shot is running: the demo's rules are on (ResetShot turns them off before every shot).</summary>
        bool DemoPass => Capturing && _content.DemoActive;

        /// <summary>Runs a store shot with the demo's rules on.</summary>
        IEnumerator AsDemo(Func<IEnumerator> shot)
        {
            _content.DemoActive = true;
            yield return shot();
        }

        /// <summary>
        /// Demo pass: the colony stays fed. The demo's showcase has less food storage than the full game's, and a starving
        /// banner would sit in every still.
        /// </summary>
        void KeepDemoFed()
        {
            var inv = _sim.Colony.Inventory;
            inv.Food = math.max(inv.Food, inv.FoodCapacity * 0.75f);
        }

        /// <summary>
        /// Reveals a round window of <paramref name="radius"/> around <paramref name="center"/> that stops at the seal: every
        /// column down to the seal's last cell, nothing beyond it (that is the glimpse's job).
        /// </summary>
        void RevealDownToTheSeal(int2 center, int radius)
        {
            var world = _sim.World;
            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                bool inSeal = false;
                for (int y = center.y + radius; y > 0; y--)
                {
                    var c = new int2(x, y);
                    bool seal = _sim.IsSeal(c);
                    if (inSeal && !seal) break;
                    inSeal |= seal;
                    if (world.InBounds(x, y) && math.lengthsq((float2)(c - center)) <= radius * radius * 1.6f) _sim.RevealNow(c, 0);
                }
            }
            world.MarkAll(ChunkFlags.TerrainUpload);
        }

        /// <summary>
        /// The bottom of the demo: the shaft ends just above the Ancient Seal; the player digs the last rock, the seal is reached
        /// and the crystal caverns glow through the window beyond it; then the end screen.
        /// </summary>
        IEnumerator ShotDemoSeal()
        {
            _content.DemoActive = true;
            LoadShowcase();
            var s = _show.SealShaft;
            if (s.Equals(default(int2))) throw new InvalidOperationException("the demo showcase has no seal shaft (rebuild it)");

            // Glow ants light the landing at the bottom of the shaft; a few workers wait for the way down. Any water that
            // found its way in during the settle is taken out.
            _sim.ClearFluidNow(s + new int2(-16, -12), s + new int2(16, 34));
            _sim.ClearPilesNow();
            RevealDownToTheSeal(s, 15);
            for (int i = 0; i < 6; i++) _sim.Ants.Spawn(s + new int2(i * 2 - 5, 1), (uint)(900 + i), AntCaste.Glow);
            for (int i = 0; i < 4; i++) _sim.Ants.Spawn(s + new int2(i * 2 - 3, 1), (uint)(910 + i), AntCaste.Worker);
            var landing = (float2)s + new float2(0.5f, 0.5f);

            // The seal under the shaft, before anyone has touched it.
            Hud(true);
            Cam(landing + new float2(0f, -4f), 4);
            _hud.ClearToast(); // the settle's own messages (a cave-in somewhere) don't belong in this still
            yield return Seconds(1.5f);
            yield return Still("seal_1_below_the_shaft");

            // The player digs the last rock by hand; whatever is left after a few seconds goes at once, so the clip always
            // reaches the seal. Digging next to it reveals the window beyond.
            Hud(false);
            Cam(landing + new float2(0f, -10f), 3);
            yield return Seconds(0.3f);
            float start = _shotTime, carveAt = 4.5f, reachedAt = -1f;
            var s0 = _show.SealShaft;
            _shotTick = (time, dt) =>
            {
                float t = time - start;
                if (_sim.Demo.SealReached)
                {
                    if (reachedAt < 0f) reachedAt = t;
                    Pointer(landing + new float2(0f, -2f), t < reachedAt + 1.2f);
                    return;
                }
                // The pointer rides on the topmost rock under the landing, swaying a little.
                int y = s0.y - 1;
                while (y > 0 && _sim.World[s0.x, y].IsOpen) y--;
                var p = new float2(s0.x + 0.5f + math.sin(t * 3f) * 1.2f, y + 0.5f);
                Pointer(p, true);
                _tools.Dig.Update(_sim, true, false, p * WorldConstants.CellSize, dt, UnityEngine.Time.timeAsDouble);
                // Whatever rock is left after a few seconds goes at once, a row a frame, until the seal is reached.
                if (t >= carveAt && !_sim.IsSeal(new int2(s0.x, y))) _sim.CarveRectNow(new int2(s0.x - 1, y), new int2(s0.x + 1, y));
            };
            yield return Clip("d_seal", 8f);
            _shotTick = null;
            Pointer(new float2(-50f, -50f), false); // off the map, so no hover panel in the stills
            if (!_sim.Demo.SealReached) throw new InvalidOperationException("the shot did not reach the seal");

            // The glimpse: the whole window in view, with the message the player gets.
            Hud(true);
            _hud.ShowToast("Your colony reached the Ancient Seal. Beyond it, the deep glows...");
            Cam(landing + new float2(0f, -14f), 2);
            yield return Seconds(1f);
            yield return Still("seal_2_the_deep_glows");
            Cam(landing + new float2(0f, -12f), 3);
            yield return Seconds(0.5f);
            yield return Still("seal_3_close");

            // The end screen it opens.
            OpenDemoDialog(DemoDialogKind.Seal, fromMenu: false);
            yield return Seconds(0.5f);
            yield return Still("seal_4_end_screen");
            _hud.HideModal();
        }
    }
}
