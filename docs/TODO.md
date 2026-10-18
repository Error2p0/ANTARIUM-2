# TODO

## Done
- Project check: Unity 6000.6.5f1, URP 17.6 2D Renderer, new Input System only.
- Package cleanup (removed tilemap, tilemap.extras, visual scripting, collab proxy, tutorial framework); Burst/Collections/Mathematics are direct dependencies.
- `docs/GDD.md`, `CLAUDE.md`.
- Decisions: stay on Unity 6.6; UI Toolkit; pixel art; GPU per-pixel terrain; no terrain colliders; ShadowCaster2D only near the camera.

## Milestone 1 — Foundation (in progress)
- [ ] Assemblies + folder structure
- [ ] Core: IDs, EventBus, SimClock
- [ ] Content: MaterialDef, BiomeDef, WorldGenSettings, baked ContentDB
- [ ] World grid (chunks, cells, edits, dirty tracking) + EditMode tests
- [ ] World generation (biome bands, ores, caves, surface) + determinism tests
- [ ] Terrain renderer: data texture upload + pixel marching-squares shader
- [ ] Pixel-perfect camera (pan, integer zoom)
- [ ] Click/hold to dig + debris particles
- [ ] Light map (sky + sources BFS) + fog of war
- [ ] ShadowCaster2D near camera
- [ ] Editor setup script for the main scene + default content
