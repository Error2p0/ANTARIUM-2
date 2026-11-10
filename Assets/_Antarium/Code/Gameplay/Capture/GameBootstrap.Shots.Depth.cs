using System.Collections;
using Antarium.Content;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// 10.4 review stills (<c>CaptureMenu.Run("depth", false)</c>): glow ants lighting a dark tunnel (with and without their
    /// lights), fire ants and workers in the magma zone, the Queen panel, the priority panel, an ant's hover text and a
    /// busy route with lane jitter. Not store media.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        IEnumerator ShotColonyDepth()
        {
            LoadShowcase();
            Hud(true);
            var q = Queen;

            // Glow ants in the ruins (the darkest outpost): lights on, then off for comparison.
            int ruins = math.min(3, _show.Outposts.Length - 1);
            var dark = _show.Outposts[ruins];
            for (int i = 0; i < 8; i++) _sim.Ants.Spawn(dark + new int2(i - 4, 0), (uint)(900 + i), AntCaste.Glow);
            Cam((float2)dark + new float2(0f, 2f), 4);
            yield return Seconds(1.5f);
            yield return Still("depth_glow_lights");
            int lights = config.glow.maxLights;
            config.glow.maxLights = 0;
            yield return Seconds(0.2f);
            yield return Still("depth_glow_halo_only");
            config.glow.maxLights = lights;
            bool shadows = config.glow.shadows;
            config.glow.shadows = false;
            yield return Seconds(0.2f);
            yield return Still("depth_glow_no_shadows");
            config.glow.shadows = shadows;

            // Fire ants and workers by the lava: the workers' health bars drop, the fire ants' don't.
            int magma = math.min(2, _show.Outposts.Length - 1);
            var hot = _show.Outposts[magma];
            for (int i = 0; i < 4; i++)
            {
                _sim.Ants.Spawn(hot + new int2(i * 2 - 4, 0), (uint)(950 + i), AntCaste.Fire);
                _sim.Ants.Spawn(hot + new int2(i * 2 - 3, 0), (uint)(960 + i), AntCaste.Worker);
            }
            Cam((float2)hot + new float2(0f, 2f), 4);
            yield return Seconds(3f);
            yield return Still("depth_fire_ants");

            // The Queen panel with a few evolutions bought.
            Cam(q + new float2(12f, 2f), 3);
            _sim.GiveResources(20, 200f);
            foreach (var id in new[] { "bigger_broods", "royal_cradles", "hardy_brood", "bigger_broods" })
                if (_content.TryGetEvolutionIndex(id, out int e)) _sim.EnqueueEvolution(e);
            yield return Seconds(0.5f);
            _hud.ShowModal(_hud.Queen.Root);
            yield return Seconds(0.5f);
            yield return Still("depth_queen_panel");
            _hud.HideModal();

            // Job priorities beside the castes panel.
            _sim.EnqueuePriority(JobKind.Dig, 5);
            _sim.EnqueuePriority(JobKind.Haul, 4);
            _sim.EnqueuePriority(JobKind.Tend, 2);
            _hud.Castes?.Toggle();
            _hud.Priorities?.Toggle();
            yield return Seconds(0.5f);
            yield return Still("depth_priorities");
            _hud.Castes?.Toggle();
            _hud.Priorities?.Toggle();

            // An experienced, swift worker under the pointer.
            int pick = -1;
            for (int i = 0; i < _sim.Ants.Count && pick < 0; i++)
                if (_sim.Ants.Ants[i].Caste == AntCaste.Worker && math.distance(_sim.Ants.Ants[i].Pos, q) < 20f) pick = i;
            if (pick >= 0)
            {
                var a = _sim.Ants.Ants[pick];
                a.Skill = (byte)JobKind.Dig;
                a.Xp = 60f;
                a.Trait = AntTrait.Swift;
                _sim.Ants.Ants[pick] = a;
                _shotTick = (_, _) =>
                {
                    if (pick < _sim.Ants.Count) Pointer(_sim.Ants.Ants[pick].Pos, true);
                };
                Cam(_sim.Ants.Ants[pick].Pos, 5);
                yield return Seconds(0.3f);
                yield return Still("depth_hover_ant");
                _shotTick = null;
                Pointer(q, false);
            }

            // A busy route by the queen: lane jitter spreads the column.
            Cam(q + new float2(-14f, 2f), 5);
            yield return Seconds(2f);
            yield return Still("depth_lanes");
        }
    }
}
