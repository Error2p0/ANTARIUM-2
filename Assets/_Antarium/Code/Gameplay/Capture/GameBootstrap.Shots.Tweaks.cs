using System.Collections;
using Antarium.Content;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Milestone 11 batch 3 review stills (<c>CaptureMenu.Run("tweaks", false)</c>): the giant worm in the soil and hit by the Dig
    /// tool, rival ants robbing the storage hall, a machine switched off with the configure panel's switch, the Mine tool's Vein
    /// Instinct switch, the Guiding Light at the pointer (with and without), and a caught shooting star bursting. Not store media.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>Captures: draw the Guiding Light at the shot pointer.</summary>
        bool _shotCursorLight;

        IEnumerator ShotTweaks()
        {
            LoadShowcase();
            Hud(true);
            var q = Queen;

            // The giant worm: burrowing near the tunnels, then hit along its body by the Dig tool.
            if (_sim.RandomEvents.TriggerKind(RandomEventKind.GiantWorm))
            {
                int w = _sim.Enemies.WormIndex();
                _camPath = _ => new CamPose(_sim.Enemies.WormIndex() >= 0 ? _sim.Enemies.Enemies[_sim.Enemies.WormIndex()].Pos : q, 4);
                yield return Seconds(9f);
                yield return Still("tweaks_worm");
                w = _sim.Enemies.WormIndex();
                if (w >= 0)
                {
                    var body = _sim.Enemies.WormCells;
                    float2 at = new float2(body[math.min(3, body.Count - 1)]) + 0.5f;
                    Pointer(at, true);
                    for (int i = 0; i < 12; i++) _sim.Enqueue(new DigCommand(at, 0.6f, 1.5f));
                    yield return Seconds(0.1f);
                    yield return Still("tweaks_worm_hit");
                    Pointer(at, false);
                }
            }

            // Rival ants come in near the storage hall: they rob it, not the royal chamber.
            var hall = _show.StorageHall;
            if (_content.Thieves.Length > 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    var c = _sim.World.OpenAbove(hall + new int2(10 + i * 2, 0));
                    _sim.Enemies.Spawn(_content.Thieves[0], c, EnemyState.Raid);
                }
                Cam((float2)hall + new float2(4f, 2f), 4);
                yield return Seconds(5f);
                yield return Still("tweaks_rivals");
            }

            // A machine switched off from the configure panel: dimmed, the switch reads Off.
            var smelter = _show.Smelter;
            Cam((float2)smelter + new float2(1f, 3f), 4);
            _hud.Configure.Open(_sim, smelter);
            _sim.EnqueueSwitch(smelter, false);
            yield return Seconds(1f);
            yield return Still("tweaks_switch_off");
            _sim.EnqueueSwitch(smelter, true);
            _hud.Configure.Hide();

            // The Mine tool's row with the Vein Instinct switch (researched).
            _sim.Colony.Features[(int)ResearchFeature.VeinInstinct] = true;
            _tools.SetMode(ToolMode.Mine);
            Cam(q + new float2(10f, -12f), 3);
            yield return Seconds(0.5f);
            yield return Still("tweaks_mine_row");
            _sim.EnqueueAutoMark(false);
            yield return Seconds(0.3f);
            yield return Still("tweaks_mine_row_off");
            _sim.EnqueueAutoMark(true);
            _tools.SetMode(ToolMode.Dig);

            // The Guiding Light in a dark place: without, then with.
            var deep = _show.Outposts[math.min(1, _show.Outposts.Length - 1)];
            float2 spot = (float2)deep + new float2(-6f, 2f);
            Cam(spot, 4);
            Pointer(spot, true);
            yield return Seconds(1f);
            yield return Still("tweaks_light_off");
            _sim.Colony.Features[(int)ResearchFeature.CursorLight] = true;
            _shotCursorLight = true;
            yield return Seconds(1f);
            yield return Still("tweaks_light_on");
            _shotCursorLight = false;
            Pointer(spot, false);

            // A starfall night: a shooting star caught bursts into sparkles.
            _sim.SkipToNight();
            yield return Seconds(0.2f); // the simulation sees the night first
            _sim.TriggerStarfall();
            Cam(q + new float2(0f, 26f), 3);
            yield return Seconds(2f);
            for (int tries = 0; tries < 120; tries++)
            {
                if (_surfaceView != null && _surfaceView.CatchAnyStar(q + new float2(0f, 26f), out var star))
                {
                    _sim.EnqueueShootingStar(star);
                    Pointer(star, true);
                    Cam(star + new float2(0f, -4f), 3); // the stars streak high up: bring the burst to the middle
                    Hud(false);
                    break;
                }
                yield return null;
            }
            yield return Frames(4);
            yield return Still("tweaks_star_caught");
            Hud(true);
        }
    }
}
