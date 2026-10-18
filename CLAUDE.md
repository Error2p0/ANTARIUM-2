# Antarium 2 — Claude Code guide

Active incremental + colony automation sim, 2D side-view ant colony cross-section. PC/Steam, Unity C#.
Design: `docs/GDD.md`. Progress / next steps: `docs/TODO.md`.

## Engineering rules

- Unity 6.6 (6000.6.x), URP 2D Renderer, new Input System, C#. No Tilemaps.
- Pixel art: PPU 16, 1 cell = 8×8 px = 0.5 units. Everything snaps to the art-pixel grid; camera zoom is integer only. Terrain is drawn per-pixel on the GPU from a cell data texture (see GDD 3.1), not with meshes.
- UI: UI Toolkit.
- Simulation logic in plain C# classes/structs, independent of MonoBehaviours where possible, so it can be unit-tested (EditMode tests for world gen, digging, jobs, recipes, save/load).
- Hot paths use Burst + Jobs + NativeArrays; avoid per-frame GC allocations; no LINQ in hot loops.
- Content is data-driven (ScriptableObjects). No magic numbers for balance values in code.
- Rendering is separate from simulation; sim runs on a fixed tick.
- Keep systems decoupled (events or a simple message bus); avoid singleton spaghetti.
- Profile before optimizing, but respect the performance targets (60 FPS, thousands of ants, large world).
- Small, focused commits with clear messages.
- **Always keep the documents up to date, in the same change as the code** (never leave it for later):
  - `docs/GDD.md`: design changes, and the status markers (✅ done, 🟡 partly done with what's missing, no mark = not started).
  - `docs/TODO.md`: done / next per milestone, known limits, and the "Missing / not built yet" list at the bottom.
  - `docs/HOW_TO_PLAY.md`: every control, tool, HUD element and player-visible rule or number. Update it whenever input bindings, tools or gameplay rules change.
- When something needs to be done in the Unity Editor by hand (creating assets, setting up a scene, assigning references), give exact step-by-step instructions, or better, write an editor script that does it.
- If a design idea turns out to be bad in practice, say so and propose an alternative instead of silently forcing it.

## Tooling

- The `unity` CLI is installed and can drive the running Editor (via the `com.unity.pipeline` package):
  - `unity status` — connected editors
  - `unity recompile` — recompile and report compile errors (run after every code change)
  - `unity test` — run EditMode/PlayMode tests
  - `unity list` / `unity command <name> ...` — editor commands (create assets, scenes, capture game view, console, etc.)
  - `unity command run_tests --mode EditMode` runs tests in the open Editor (`unity test` refuses while it is open).
  - `unity command menu --path "Antarium/Setup/Create Main Scene"` rebuilds the wired main scene.
  - Play mode does not free-run while the Editor is unfocused (autotick doesn't help). Drive frames with
    `unity command eval --code 'UnityEditor.EditorApplication.Step(); return 0;'` after `editor_play`.
  - `capture_game_view --save_path X` always writes under `Assets/X`; move the PNG out and delete the folder + `.meta` right after.
- Git LFS is configured via `.gitattributes` for binary assets. Keep Version Control mode = Visible Meta Files.
