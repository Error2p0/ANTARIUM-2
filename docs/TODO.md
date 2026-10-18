# TODO

## Done
- Project check: Unity 6000.6.5f1, URP 17.6 2D Renderer, new Input System only, Linear colour space.
- Package cleanup (removed tilemap, tilemap.extras, visual scripting, collab proxy, tutorial framework); Burst/Collections/Mathematics are direct dependencies.
- `docs/GDD.md`, `CLAUDE.md`.
- Decisions: stay on Unity 6.6; UI Toolkit; pixel art; GPU per-pixel terrain; no terrain colliders; ShadowCaster2D only near the camera.
- Player Settings: Run In Background on (idle game keeps simulating when unfocused).

## Milestone 1 — Foundation
- [x] Assemblies: Core / Content / Sim / Presentation / Gameplay / Editor / Tests.EditMode
- [x] Core: EventBus (pull-based struct events), SimClock (fixed tick, hitch-safe), WorldConstants
- [x] Content: TerrainMaterialDef, BiomeDef, WorldGenSettings, GameConfig, ContentDatabase, baked ContentDB (Air = index 0)
- [x] World grid: chunk-major 32x32 chunks, 4-byte cells, chunk dirty flags
- [x] World generation (Burst): surface, 3 biomes with warped borders, deposits, caves, bedrock, starting nest
- [x] Terrain renderer: GPU buffers uploaded per dirty chunk + per-pixel "pixel marching squares" shader (rim/outline, bevel normals, dithered wall occlusion, sky)
- [x] Procedural 32x32 pixel textures per material (override with hand-drawn tiles later)
- [x] Pixel-perfect camera: integer zoom, pixel-snapped position, pan (WASD/arrows/stick, middle/right drag), zoom at cursor, pixel shake
- [x] Click/hold to dig: circular brush, hardness, combo multiplier, debris particles snapped to the pixel grid
- [x] Light map: Burst bucket-queue flood fill (8-way, RGB), sky + emissive materials, exact local updates
- [x] Fog of war: reveal radius around dug cells; breaching an unexplored cave reveals the whole cave
- [x] Editor setup: `Antarium/Setup/Create Default Content`, `Antarium/Setup/Create Main Scene`, `Antarium/Content/Rebuild Database`
- [x] 22 EditMode tests (grid, content, world gen determinism, digging, combo, light incl. local == full recompute, reveal)
- [ ] Hands-on feel pass in the editor (mouse dig, pan/zoom) — tuning in `GameConfig.asset`
- [ ] ShadowCaster2D near camera — deferred until the first dynamic Light2D exists (nothing to cast for in M1). URP 17.6 has no public shape setter: use a Collider2D shape provider or reflection on `m_ShapePath`.

## Known limits / follow-ups
- `LightSystem.UpdateDirty` unions all dirty chunks into one rect. Fine for one digging player; split into clusters before many ants dig at once (M2).
- Light falloff and fog use Bayer dithering; tune `lightBands` / fog spread if it reads too noisy.
- HUD is a debug label only; real UI Toolkit HUD comes with resources (M2).
- Sky has no day/night yet (GDD 2.8).

## Next: Milestone 2 — Living colony
Instanced ants, surface-crawling movement, flow fields, job system (dig/haul), queen + eggs + workers, storage, basic resources.
