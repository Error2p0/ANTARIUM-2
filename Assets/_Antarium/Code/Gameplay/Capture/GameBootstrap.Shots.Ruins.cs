using System.Collections;
using Antarium.Content;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// 10.5 review stills (<c>CaptureMenu.Run("ruins", false)</c>): a Crystal Resonator by the crystal outpost with its hover text,
    /// the Ancient Engine in the ruins at every restoration stage and its configure panel, the Codex, the ancient research tier,
    /// rival ants stealing from the royal chamber and a mole cricket tunnelling toward the nest. Not store media.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        IEnumerator ShotRuins()
        {
            LoadShowcase();
            Hud(true);
            var q = Queen;
            for (int i = 0; i < _sim.Colony.BuildingUnlocked.Length; i++) _sim.Colony.BuildingUnlocked[i] = true;
            _sim.GiveResources(40, 300f);

            // A Crystal Resonator at the crystal outpost, a smelter next to it.
            var crystalOutpost = _show.Outposts[0];
            int resonatorDef = BuildingIndex("crystal_resonator");
            int2 resonator = FlatSpot(crystalOutpost + new int2(0, 10), 2, 2, search: false);
            ushort glow = _content.TryGetMaterialIndex("glow_crystal", out var g) ? g : (ushort)0;
            for (int y = -1; y <= 3; y++)
            for (int x = -3; x <= -1; x++)
            {
                var c = resonator + new int2(x, y);
                if (!_sim.World[c.x, c.y].IsOpen || x == -1)
                    _sim.SetCell(c, new Cell(glow, Cell.Full, CellFlags.Revealed));
            }
            _sim.EnqueueBuild(resonatorDef, resonator, default);
            int smelterDef = BuildingIndex("smelter");
            int2 smelter = FlatSpot(resonator + new int2(4, 0), 3, 3, search: false);
            _sim.EnqueueBuild(smelterDef, smelter, default);
            yield return Seconds(0.3f);
            _sim.Buildings.CompleteAllBlueprints();
            Cam((float2)resonator + new float2(2f, 2f), 4);
            Pointer((float2)resonator + 1f, true);
            yield return Seconds(2f);
            yield return Still("ruins_resonator");
            Pointer(q, false);

            // The Ancient Engine in the ruins: placed, then restored step by step.
            var ruins = _show.Outposts[math.min(3, _show.Outposts.Length - 1)];
            int engineDef = BuildingIndex("ancient_engine");
            int2 engine = FlatSpot(ruins, 4, 3);
            _sim.EnqueueBuild(engineDef, engine, default);
            yield return Seconds(0.3f);
            _sim.Buildings.CompleteAllBlueprints();
            Cam((float2)engine + new float2(2f, 3f), 5);
            yield return Seconds(0.5f);
            yield return Still("ruins_engine_0");
            for (int stage = 1; stage <= 3; stage++)
            {
                _sim.GiveResources(40, 300f);
                _sim.EnqueueStage(engine);
                yield return Seconds(0.3f);
                if (stage == 1)
                {
                    _hud.Configure.Open(_sim, engine);
                    yield return Seconds(0.3f);
                    yield return Still("ruins_engine_restoring");
                }
                _sim.Buildings.CompleteAllBlueprints();
                yield return Seconds(0.5f);
                yield return Still("ruins_engine_" + stage);
            }
            _hud.Configure.Open(_sim, engine);
            yield return Seconds(0.3f);
            yield return Still("ruins_engine_panel");
            _hud.Configure.Hide();

            // The Codex with most fragments found (relics and a tablet studied).
            if (_content.TryGetItemIndex("relic", out ushort relic)) _sim.Colony.Stats.Studied[relic] = 12;
            if (_content.TryGetItemIndex("tablet", out ushort tablet)) _sim.Colony.Stats.Studied[tablet] = 1;
            yield return Seconds(0.3f);
            _hud.ShowModal(_hud.Codex.Root);
            _hud.Codex.Select(5);
            yield return Seconds(0.5f);
            yield return Still("ruins_codex");
            _hud.Codex.Select(10);
            yield return Seconds(0.3f);
            yield return Still("ruins_codex_locked");
            _hud.HideModal();

            // The research tree with the ancient tier.
            _hud.ShowModal(_hud.Research.Root);
            yield return Seconds(0.5f);
            yield return Still("ruins_research");
            _hud.HideModal();

            // Rival ants: a raid on its way (the card) and thieves already in the royal chamber.
            _sim.EnqueueTriggerRaid(rival: true);
            if (_content.Thieves.Length > 0)
                for (int i = 0; i < 4; i++)
                    _sim.Enemies.Spawn(_content.Thieves[0], _sim.Colony.QueenCell + new int2(i * 2 - 4, 0), EnemyState.Raid);
            Cam(q + new float2(0f, 1f), 6);
            yield return Seconds(5f);
            yield return Still("ruins_rivals");

            // A mole cricket digging toward the nest: its tunnel shows through the dark.
            _sim.EnqueueTriggerDigger();
            yield return Seconds(0.2f);
            int mole = -1;
            for (int i = 0; i < _sim.Enemies.Count; i++)
                if (_content.Enemies[_sim.Enemies.Enemies[i].Def].Def.behavior == EnemyBehavior.Digger) mole = i;
            if (mole >= 0)
            {
                _shotTick = (_, _) =>
                {
                    for (int i = 0; i < _sim.Enemies.Count; i++)
                        if (_content.Enemies[_sim.Enemies.Enemies[i].Def].Def.behavior == EnemyBehavior.Digger)
                            Cam(_sim.Enemies.Enemies[i].Pos, 4);
                };
                yield return Seconds(4f);
                yield return Still("ruins_mole");
                yield return Seconds(8f);
                yield return Still("ruins_mole_later");
                _shotTick = null;
            }
        }

        int BuildingIndex(string id) => _content.TryGetBuildingIndex(id, out int i) ? i : -1;

        /// <summary>
        /// The origin of a <paramref name="w"/> x <paramref name="h"/> open, revealed room with a solid floor near <paramref name="near"/>;
        /// carved (keeping the materials) if there is no such spot already.
        /// </summary>
        int2 FlatSpot(int2 near, int w, int h, bool search = true)
        {
            var world = _sim.World;
            for (int r = 0; r <= (search ? 8 : -1); r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var o = near + new int2(dx, dy);
                bool ok = true;
                for (int y = -1; y < h && ok; y++)
                for (int x = 0; x < w && ok; x++)
                {
                    var c = o + new int2(x, y);
                    if (!world.InBounds(c.x, c.y)) { ok = false; break; }
                    var cell = world[c.x, c.y];
                    ok = y < 0 ? !cell.IsOpen : cell.IsOpen && cell.IsRevealed && _sim.Buildings.At(c) < 0
                                                && world.Fluid[world.Index(c.x, c.y)].Level == 0;
                }
                if (ok) return o;
            }
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = near + new int2(x, y);
                _sim.SetCell(c, new Cell(world[c.x, c.y].Material, 0, CellFlags.Revealed | CellFlags.Explored));
            }
            for (int x = 0; x < w; x++)
            {
                var c = near + new int2(x, -1);
                if (world[c.x, c.y].IsOpen) _sim.SetCell(c, new Cell(world[c.x, c.y].Material, Cell.Full, CellFlags.Revealed));
            }
            return near;
        }
    }
}
