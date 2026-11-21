#ifndef ANTARIUM_TERRAIN_COMMON
#define ANTARIUM_TERRAIN_COMMON

// Per-pixel terrain reconstruction ("pixel marching squares").
// Cell densities live at cell centres. Every art pixel bilinearly interpolates the 4 surrounding
// cell densities and thresholds at 0.5, which yields rounded, organic tunnel outlines that are
// still crisp pixel art. All data comes from structured buffers mirrored from the simulation.

struct AntMaterial
{
    float4 topRim;    // rgb colour of top-facing edge pixels, a = 1 if set
    float4 edgeLight; // highlight for top-facing edges when no rim colour
    float4 edgeDark;  // outline for side/bottom-facing edges
    float4 params;    // x = is air, y = self glow
    float4 baseColor; // average colour, used to flatten background walls
    float4 oreColor;  // rgb = colour of the find it drops (ore overlay), a = 1 if it is a find
};

StructuredBuffer<uint> _AntCells;        // material(16) | density(8) | flags(8)
StructuredBuffer<uint> _AntLight;        // r | g | b (emitters) | sky level, 8 bits each
StructuredBuffer<AntMaterial> _AntMaterials;
TEXTURE2D_ARRAY(_AntMaterialTex);

float4 _AntWorld;      // x = width (cells), y = height (cells), z = chunks per row
float4 _AntPixel;      // x = pixels per unit, y = pixels per cell, z = light bands, w = edge noise
float4 _AntTile;       // x = material tile size - 1 (a power of two), y = patch contrast, z = patch tint
float4 _AntWall;       // x = wall brightness, y = wall occlusion, z = gamma exponent (2.2 in linear colour space, else 1), w = wall contrast
float4 _AntSkyTop;
float4 _AntSkyHorizon;
float4 _AntSkyRange;   // x = horizon world y, y = top world y
float4 _AntSkyLight;   // rgb = colour and strength of the sky light now (time of day, weather)
float4 _AntStars;      // rgb = star colour, a = how visible the stars are (night)
float4 _AntView;       // x = overlay: 0 none, 1 light, 2 ores; y = ore overlay dimming of everything else; z = light ramp size
float4 _AntLightRamp[8]; // light overlay colours, darkest first

static const float kBayer4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

float Bayer4(int2 p)
{
    int2 q = p & 3;
    return (kBayer4[q.y * 4 + q.x] + 0.5) / 16.0;
}

// Brightness values (light map, wall darkening) are authored perceptually; convert to the working space.
float Perceptual(float v) { return pow(max(v, 0.0), _AntWall.z); }
float3 Perceptual(float3 v) { return pow(max(v, 0.0), _AntWall.z); }

float Hash21(int2 p)
{
    uint h = (uint)p.x * 374761393u + (uint)p.y * 668265263u;
    h = (h ^ (h >> 13)) * 1274126177u;
    return (float)(h ^ (h >> 16)) / 4294967295.0;
}

int CellIndex(int2 c)
{
    c = clamp(c, int2(0, 0), int2(_AntWorld.xy) - 1);
    int2 ch = c >> 5;
    int2 l = c & 31;
    return ((ch.y * (int)_AntWorld.z + ch.x) << 10) | (l.y << 5) | l.x;
}

uint CellMaterial(uint w) { return w & 0xFFFFu; }
float CellDensity(uint w) { return ((w >> 16) & 0xFFu) / 255.0; }
float CellRevealed(uint w) { return (float)((w >> 24) & 1u); }

float3 CellLight(int2 c)
{
    uint w = _AntLight[CellIndex(c)];
    float3 emitted = float3(w & 0xFFu, (w >> 8) & 0xFFu, (w >> 16) & 0xFFu) / 255.0;
    return max(emitted, ((w >> 24) & 0xFFu) / 255.0 * _AntSkyLight.rgb);
}

// 3x3 block of cells around the pixel's cell; origin = bottom-left cell.
struct Neighborhood
{
    int2 origin;
    float density[9];
    uint material[9];
    float revealed[9];
    uint flags[9];
    float wall[9];   // 1 if the cell has a back wall (any material but Air, which is always index 0)
};

// Cell flag bits (must match Antarium.Sim.CellFlags).
#define ANT_FLAG_DIG_DESIGNATED 4u
#define ANT_FLAG_STORAGE 8u
#define ANT_FLAG_SCANNED 32u

float4 _AntOverlay; // rgb = mining order colour
float4 _AntStorageColor;

// Fluid plane: two cells per uint, each 16 bits = level (8) | kind (8). Kind: 1 water, 2 lava, 3 gas, 4 fire (on a solid cell),
// 5 smoke.
StructuredBuffer<uint> _AntFluid;
float4 _AntWater;        // rgb tint, a = cover
float4 _AntWaterSurface;
float4 _AntLava;
float4 _AntLavaHot;
float4 _AntGas;          // rgb tint, a = cover at full density
float4 _AntSmoke;        // rgb tint, a = cover at full density
float4 _AntFire;         // flames
float4 _AntFireHot;      // flame flicker
float4 _AntEmber;        // glowing embers inside a burning cell
float _AntTime;

#define ANT_FLUID_WATER 1u
#define ANT_FLUID_LAVA 2u
#define ANT_FLUID_GAS 3u
#define ANT_FLUID_FIRE 4u
#define ANT_FLUID_SMOKE 5u

uint FluidWord(int2 c)
{
    int i = CellIndex(c);
    uint w = _AntFluid[i >> 1];
    return (i & 1) != 0 ? (w >> 16) : (w & 0xFFFFu);
}

uint FluidKindOf(uint f) { return (f >> 8) & 0xFFu; }
float FluidLevelOf(uint f) { return (f & 0xFFu) / 255.0; }

Neighborhood LoadNeighborhood(int2 origin)
{
    Neighborhood n;
    n.origin = origin;
    [unroll] for (int y = 0; y < 3; y++)
    [unroll] for (int x = 0; x < 3; x++)
    {
        uint w = _AntCells[CellIndex(origin + int2(x, y))];
        n.density[y * 3 + x] = CellDensity(w);
        n.material[y * 3 + x] = CellMaterial(w);
        n.revealed[y * 3 + x] = CellRevealed(w);
        n.flags[y * 3 + x] = w >> 24;
        n.wall[y * 3 + x] = CellMaterial(w) != 0u ? 1.0 : 0.0;
    }
    return n;
}

// Dual-grid bilinear setup for a point p in cell space.
void Bilinear(Neighborhood n, float2 p, out int i00, out float4 weights)
{
    float2 q = p - 0.5 - (float2)n.origin;
    int2 i = clamp((int2)floor(q), int2(0, 0), int2(1, 1));
    float2 f = saturate(q - (float2)i);
    i00 = i.y * 3 + i.x;
    weights = float4((1 - f.x) * (1 - f.y), f.x * (1 - f.y), (1 - f.x) * f.y, f.x * f.y);
}

float FieldAt(Neighborhood n, float2 p)
{
    int i; float4 w;
    Bilinear(n, p, i, w);
    return n.density[i] * w.x + n.density[i + 1] * w.y + n.density[i + 3] * w.z + n.density[i + 4] * w.w;
}

float WallAt(Neighborhood n, float2 p)
{
    int i; float4 w;
    Bilinear(n, p, i, w);
    return n.wall[i] * w.x + n.wall[i + 1] * w.y + n.wall[i + 3] * w.z + n.wall[i + 4] * w.w;
}

// Material of a background-wall pixel: the neighbouring cell with a wall contributing the most.
uint WallMaterial(Neighborhood n, float2 p)
{
    int i; float4 w;
    Bilinear(n, p, i, w);
    float4 s = w * float4(n.wall[i], n.wall[i + 1], n.wall[i + 3], n.wall[i + 4]);
    int best = i; float bestScore = s.x;
    if (s.y > bestScore) { best = i + 1; bestScore = s.y; }
    if (s.z > bestScore) { best = i + 3; bestScore = s.z; }
    if (s.w > bestScore) { best = i + 4; }
    return n.material[best];
}

float RevealedAt(Neighborhood n, float2 p)
{
    int i; float4 w;
    Bilinear(n, p, i, w);
    return n.revealed[i] * w.x + n.revealed[i + 1] * w.y + n.revealed[i + 3] * w.z + n.revealed[i + 4] * w.w;
}

// The neighbouring cell contributing the most density to a solid pixel (index into the neighbourhood).
int SolidIndex(Neighborhood n, float2 p)
{
    int i; float4 w;
    Bilinear(n, p, i, w);
    float4 s = w * float4(n.density[i], n.density[i + 1], n.density[i + 3], n.density[i + 4]);
    int best = i; float bestScore = s.x;
    if (s.y > bestScore) { best = i + 1; bestScore = s.y; }
    if (s.z > bestScore) { best = i + 3; bestScore = s.z; }
    if (s.w > bestScore) { best = i + 4; }
    return best;
}

// Material of a solid pixel: the neighbouring cell contributing the most density.
uint SolidMaterial(Neighborhood n, float2 p)
{
    return n.material[SolidIndex(n, p)];
}

// Flicker in 0..1 that changes several times a second per art pixel.
float FireFlicker(int2 px)
{
    return Hash21(px + int2((int)(_AntTime * 9.0) * 131, (int)(_AntTime * 6.0) * 37));
}

float3 LightAt(float2 p)
{
    float2 q = p - 0.5;
    int2 i = (int2)floor(q);
    float2 f = q - (float2)i;
    float3 a = lerp(CellLight(i), CellLight(i + int2(1, 0)), f.x);
    float3 b = lerp(CellLight(i + int2(0, 1)), CellLight(i + int2(1, 1)), f.x);
    return lerp(a, b, f.y);
}

struct PixelInfo
{
    int2 px;          // art pixel coordinate
    float2 cellPos;   // art pixel centre in cell space
    float step;       // one art pixel in cell space
};

PixelInfo GetPixel(float2 positionWS)
{
    PixelInfo p;
    p.px = (int2)floor(positionWS * _AntPixel.x);
    p.step = 1.0 / _AntPixel.y;
    p.cellPos = ((float2)p.px + 0.5) * p.step;
    return p;
}

// Smooth value noise on a 4-pixel lattice: wobbles contours organically without salt-and-pepper speckle.
float ValueNoise4(int2 px)
{
    int2 c = px >> 2;
    float2 f = ((float2)(px & 3) + 0.5) / 4.0;
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash21(c), b = Hash21(c + int2(1, 0));
    float d = Hash21(c + int2(0, 1)), e = Hash21(c + int2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(d, e, f.x), f.y);
}

// Smooth value noise on a (1 << shift)-pixel lattice.
float ValueNoiseAt(int2 px, int shift)
{
    int size = 1 << shift;
    int2 c = px >> shift;
    float2 f = ((float2)(px & (size - 1)) + 0.5) / size;
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash21(c), b = Hash21(c + int2(1, 0));
    float d = Hash21(c + int2(0, 1)), e = Hash21(c + int2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(d, e, f.x), f.y);
}

// The ground runs in large flat patches, a little lighter or darker and warmer or cooler than the material tile.
// They are placed in world pixels, so unlike the tile they never repeat; flat steps keep it pixel art.
float3 PatchShade(float3 c, int2 px)
{
    float n = ValueNoiseAt(px, 5) * 0.6 + ValueNoiseAt(px + int2(911, 357), 4) * 0.4;
    float level = (floor(saturate(n * 1.5 - 0.25) * 4.999) - 2.0) * 0.5;   // five steps, -1 .. 1
    float w = ValueNoiseAt(px + int2(4513, 1291), 6);
    float warm = w > 0.58 ? 1.0 : w < 0.42 ? -1.0 : 0.0;
    c *= 1.0 + level * _AntTile.y;
    c.r *= 1.0 + warm * _AntTile.z;
    c.b *= 1.0 - warm * _AntTile.z;
    return c;
}

bool IsSolid(Neighborhood n, float2 cellPos, int2 px)
{
    return FieldAt(n, cellPos) + (ValueNoise4(px) - 0.5) * 2.0 * _AntPixel.w > 0.5;
}

// Wall vs sky uses the same rounded, wobbly contour as solid terrain, so a dug cell at the surface
// does not show a square of back wall sticking into the sky. The threshold is a bit higher than the
// solid one, keeping the wall edge just inside the terrain edge: it never peeks above the surface.
bool HasWall(Neighborhood n, float2 cellPos, int2 px)
{
    return WallAt(n, cellPos) + (ValueNoise4(px) - 0.5) * 2.0 * _AntPixel.w > 0.65;
}

// Material boundaries between solid cells: sample the material at a smoothly jittered point so that
// patches (a pocket of dirt in stone) read as organic blobs instead of 8x8 squares.
int JitteredCell(Neighborhood n, float2 cellPos, int2 px)
{
    float2 jitter = float2(ValueNoise4(px + int2(1307, 271)), ValueNoise4(px + int2(523, 1949))) - 0.5;
    int2 c = clamp((int2)floor(cellPos + jitter * 0.8) - n.origin, int2(0, 0), int2(2, 2));
    return c.y * 3 + c.x;
}

struct TerrainSurface
{
    float3 albedo;
    float glow;      // self illumination
    bool unlit;      // sky and lava: output albedo directly
    bool hidden;     // fog of war
    bool fogOrder;   // hidden, but shows a mining-order stripe
    float3 normalWS;
    bool solid;      // a solid pixel (else open: sky, wall, fluid)
    uint material;   // material of a solid pixel
    bool scanned;    // its cell was found by Seismic Sense or a rich vein
    bool sky;        // open sky (no back wall)
};

// Falling liquid (Milestone 11): x = stream width per unit of level (in cells), y = streak speed (art pixels per second),
// z = streak length (art pixels), w = share of a column's length that is a light streak.
float4 _AntFall;
float4 _AntWaterFoam;

#define ANT_FULL_LIQUID 0.97

bool CellOpen(int2 c) { return CellDensity(_AntCells[CellIndex(c)]) < 0.5; }

float LiquidLevel(int2 c, uint kind)
{
    uint f = FluidWord(c);
    return FluidKindOf(f) == kind ? FluidLevelOf(f) : 0.0;
}

// An open cell beside c with room for more of the liquid (not full of it, and no other fluid in it).
bool RoomBeside(int2 c, uint kind)
{
    int2 l = c - int2(1, 0), r = c + int2(1, 0);
    uint fl = FluidWord(l), fr = FluidWord(r);
    bool roomL = CellOpen(l) && (FluidKindOf(fl) == 0u || (FluidKindOf(fl) == kind && FluidLevelOf(fl) < ANT_FULL_LIQUID));
    bool roomR = CellOpen(r) && (FluidKindOf(fr) == 0u || (FluidKindOf(fr) == kind && FluidLevelOf(fr) < ANT_FULL_LIQUID));
    return roomL || roomR;
}

// Liquid in c that is falling, drawn as a vertical stream instead of a cell filled from the bottom: nothing under it yet, or
// the cell under it is part of the fall too (it has open room under it), or c is fed from above with room beside it (a
// stream pouring into a pool). Liquid resting on a film of liquid (running down a tunnel) is not falling.
bool LiquidFalls(int2 c, uint kind)
{
    if (LiquidLevel(c, kind) <= 0.0) return false;
    int2 b = c - int2(0, 1);
    if (!CellOpen(b)) return false;
    float lb = LiquidLevel(b, kind);
    if (lb <= 0.0) return true;
    if (lb < ANT_FULL_LIQUID && CellOpen(b - int2(0, 1)) && LiquidLevel(b - int2(0, 1), kind) < ANT_FULL_LIQUID) return true;
    return LiquidLevel(c + int2(0, 1), kind) > 0.0 && RoomBeside(c, kind);
}

// Height of the liquid surface beside c (cells, relative to c's floor) for smoothing c's surface toward it: a neighbour's
// level (one cell more when liquid stands on it: the flow is deeper there), the step below when the liquid has run down to a
// lower cell, the cell above a solid neighbour when the liquid comes from higher up, else c's own level (flat against a wall,
// and beside a falling stream).
float NeighbourSurface(int2 c, int dx, uint kind, float own)
{
    int2 n = c + int2(dx, 0);
    float result = own;
    if (CellOpen(n))
    {
        float ln = LiquidLevel(n, kind);
        float lb = LiquidLevel(n - int2(0, 1), kind);
        if (ln > 0.0)
        {
            float lu = LiquidLevel(n + int2(0, 1), kind);
            if (!LiquidFalls(n, kind)) result = lu > 0.0 ? 1.0 + lu : ln;
        }
        else if (lb > 0.0) result = lb - 1.0;
    }
    else
    {
        float lu = CellOpen(n + int2(0, 1)) ? LiquidLevel(n + int2(0, 1), kind) : 0.0;
        if (lu > 0.0) result = 1.0 + lu;
    }
    return result;
}

// Surface height at this pixel's column: the cell's own level sloped toward the neighbour it leans to, so liquid running down
// a tunnel is a ramp instead of stairs. `own` may be below the cell's floor (an empty cell over liquid).
float SmoothSurface(int2 c, PixelInfo p, uint kind, float own)
{
    float side = frac(p.cellPos.x) - 0.5;
    return lerp(own, NeighbourSurface(c, side < 0.0 ? -1 : 1, kind, own), abs(side));
}

int StreamWidth(float level)
{
    float ppc = _AntPixel.y;
    return (int)clamp(ceil(level * ppc * _AntFall.x), 2.0, ppc);
}

// The pixel's column inside a centred stream of `level` (optionally widened by `margin` pixels on each side).
bool InStream(PixelInfo p, int2 c, float level, int margin)
{
    int ppc = (int)_AntPixel.y;
    int lx = p.px.x - c.x * ppc;
    int w = StreamWidth(level);
    int x0 = (ppc - w) >> 1;
    return lx >= x0 - margin && lx < x0 + w + margin;
}

// A vertical stream of liquid through cell c, `level` wide (pixels of the cell's column, centred), with light streaks
// running down. False if the pixel is outside the stream.
bool DrawStream(inout TerrainSurface s, PixelInfo p, int2 c, uint kind, float level)
{
    if (!InStream(p, c, level, 0)) return false;
    int ppc = (int)_AntPixel.y;
    int lx = p.px.x - c.x * ppc;
    int w = StreamWidth(level);
    int x0 = (ppc - w) >> 1;
    bool rim = w > 3 && (lx == x0 || lx == x0 + w - 1);
    float phase = Hash21(int2(p.px.x, 9311));
    float speed = _AntFall.y * (0.8 + 0.4 * Hash21(int2(p.px.x, 4127)));
    bool streak = frac((p.px.y + floor(_AntTime * speed)) / max(_AntFall.z, 1.0) + phase) < _AntFall.w;
    if (kind == ANT_FLUID_WATER)
    {
        float3 body = lerp(s.albedo, _AntWater.rgb, max(_AntWater.a, 0.8));
        s.albedo = streak ? lerp(body, _AntWaterSurface.rgb, 0.65) : rim ? lerp(body, _AntWaterSurface.rgb, 0.35) : body;
    }
    else
    {
        s.albedo = streak || rim ? _AntLavaHot.rgb : _AntLava.rgb;
        s.unlit = true;
    }
    return true;
}

// Still or running liquid: the body, its bright surface row, foam (where a stream lands). Lava is self-lit and churns.
void DrawLiquid(inout TerrainSurface s, PixelInfo p, uint kind, bool surface, bool foam)
{
    if (kind == ANT_FLUID_WATER)
    {
        s.albedo = foam ? _AntWaterFoam.rgb : surface ? _AntWaterSurface.rgb : lerp(s.albedo, _AntWater.rgb, _AntWater.a);
    }
    else
    {
        float churn = ValueNoise4(p.px + int2((int)(_AntTime * 5.0), (int)(_AntTime * 3.0)));
        s.albedo = surface || foam || churn > 0.62 ? _AntLavaHot.rgb : _AntLava.rgb;
        s.unlit = true;
    }
}

// Foam flickering on and above a surface where a stream lands.
bool Foam(PixelInfo p, int salt)
{
    return Hash21(p.px + int2((int)(_AntTime * 12.0) * 53, salt)) > 0.45;
}

// The liquid falling out of the cell above, if any (its kind, else 0).
uint FallingAbove(int2 c, out float level)
{
    int2 a = c + int2(0, 1);
    uint fa = FluidWord(a);
    uint kind = FluidKindOf(fa);
    level = FluidLevelOf(fa);
    return (kind == ANT_FLUID_WATER || kind == ANT_FLUID_LAVA) && LiquidFalls(a, kind) ? kind : 0u;
}

// The open part of a cell above its liquid (or a cell with none): the stream falling into it from above, with foam thrown up
// just over the surface at `top` where it lands.
void AboveSurface(inout TerrainSurface s, PixelInfo p, int2 c, float local, float top)
{
    float la;
    uint kind = FallingAbove(c, la);
    if (kind == 0u) return;
    if (top > 0.0 && local <= top + 2.0 * p.step && InStream(p, c, la, 2) && Foam(p, 31))
    {
        DrawLiquid(s, p, kind, false, true);
        return;
    }
    DrawStream(s, p, c, kind, la);
}

// Fluid over an open pixel. Liquids fill a cell from the bottom up to their level (full when the same liquid is above), with
// a bright surface row that slopes toward the neighbouring cells' surfaces (also through the empty cell over a lower step);
// falling liquid is a stream with streaks running down and foam where it lands. Gas is a dithered haze.
void ApplyFluid(inout TerrainSurface s, Neighborhood n, PixelInfo p)
{
    int2 c = (int2)floor(p.cellPos);
    float local = frac(p.cellPos.y);
    uint f = FluidWord(c);
    if (FluidKindOf(f) == 0u && n.density[4] >= 0.5)
    {
        // An open-looking rim pixel of a solid cell: take the fluid of the open neighbour it leans toward (filled from the
        // bottom; never a stream, which keeps to the middle of its cell).
        int2 src = c + int2(frac(p.cellPos.x) < 0.5 ? -1 : 1, 0);
        if (FluidKindOf(FluidWord(src)) == 0u) src = c + int2(0, 1);
        f = FluidWord(src);
        uint rk = FluidKindOf(f);
        float rl = FluidLevelOf(f);
        if (rk == 0u || rk == ANT_FLUID_FIRE || rl <= 0.0) return;
        if (rk == ANT_FLUID_GAS || rk == ANT_FLUID_SMOKE)
        {
            float4 tint = rk == ANT_FLUID_GAS ? _AntGas : _AntSmoke;
            if (Bayer4(p.px + int2((int)(_AntTime * 2.0), 0)) < rl * tint.a * 1.6)
                s.albedo = lerp(s.albedo, tint.rgb, 0.55);
            return;
        }
        if (LiquidFalls(src, rk)) return;
        bool rfull = LiquidLevel(src + int2(0, 1), rk) > 0.0;
        float rtop = rfull ? 1.0 : max(rl, p.step);
        if (local <= rtop) DrawLiquid(s, p, rk, !rfull && local > rtop - p.step, false);
        return;
    }

    uint kind = FluidKindOf(f);
    float level = FluidLevelOf(f);
    if (kind == ANT_FLUID_FIRE) return;
    if (kind == 0u || level <= 0.0)
    {
        // An empty cell: the surface of liquid one step lower may slope up into it; a stream may fall through it.
        uint bk = FluidKindOf(FluidWord(c - int2(0, 1)));
        float top = 0.0;
        if ((bk == ANT_FLUID_WATER || bk == ANT_FLUID_LAVA) && !LiquidFalls(c - int2(0, 1), bk))
        {
            top = SmoothSurface(c, p, bk, LiquidLevel(c - int2(0, 1), bk) - 1.0);
            if (local <= top)
            {
                DrawLiquid(s, p, bk, local > top - p.step, false);
                return;
            }
        }
        AboveSurface(s, p, c, local, top);
        return;
    }

    if (kind == ANT_FLUID_GAS || kind == ANT_FLUID_SMOKE)
    {
        float4 tint = kind == ANT_FLUID_GAS ? _AntGas : _AntSmoke;
        if (Bayer4(p.px + int2((int)(_AntTime * 2.0), 0)) < level * tint.a * 1.6)
            s.albedo = lerp(s.albedo, tint.rgb, 0.55);
        return;
    }

    if (LiquidFalls(c, kind))
    {
        // Landing in a pool: the pool's surface beside the stream is in this row, and under it this cell is pool too.
        if (LiquidLevel(c - int2(0, 1), kind) >= ANT_FULL_LIQUID)
        {
            float pool = max(LiquidLevel(c - int2(1, 0), kind), LiquidLevel(c + int2(1, 0), kind));
            if (local <= pool)
            {
                bool top = local > pool - p.step;
                DrawLiquid(s, p, kind, top, top && InStream(p, c, level, 2) && Foam(p, 13));
                return;
            }
            if (pool > 0.0 && local <= pool + 2.0 * p.step && InStream(p, c, level, 2) && Foam(p, 31))
            {
                DrawLiquid(s, p, kind, false, true);
                return;
            }
        }
        // Taper toward the stream above so its width changes smoothly down the fall.
        float la;
        uint ka = FallingAbove(c, la);
        DrawStream(s, p, c, kind, lerp(level, ka == kind ? la : level, local));
        return;
    }

    bool full = LiquidLevel(c + int2(0, 1), kind) > 0.0;
    float top = full ? 1.0 : clamp(SmoothSurface(c, p, kind, level), p.step, 1.0);
    if (local > top)
    {
        AboveSurface(s, p, c, local, top);
        return;
    }
    bool surface = !full && local > top - p.step;
    float la;
    bool fallingIn = !full && FallingAbove(c, la) == kind;
    DrawLiquid(s, p, kind, surface, surface && fallingIn && InStream(p, c, la, 2) && Foam(p, 13));
}

// Pheromones: two cells per uint, each 16 bits = 2 bits per kind (0 none, 1..3 by strength).
StructuredBuffer<uint> _AntScent;
float4 _AntScentColors[8];
float _AntScentCover;   // share of pixels a full-strength scent tints

uint ScentWord(int2 c)
{
    int i = CellIndex(c);
    uint w = _AntScent[i >> 1];
    return (i & 1) != 0 ? (w >> 16) : (w & 0xFFFFu);
}

// Painted scents show as sparse speckles in their colour (static hash noise, not a regular grid), denser when fresh.
// Each kind hashes with its own offset, so overlapping scents mostly light different pixels.
void ApplyScent(inout TerrainSurface s, PixelInfo p)
{
    uint w = ScentWord((int2)floor(p.cellPos));
    if (w == 0u) return;
    [unroll] for (uint k = 0; k < 5; k++)
    {
        uint level = (w >> (k * 2u)) & 3u;
        if (level == 0u) continue;
        float cover = _AntScentCover * (0.4 + 0.2 * level);
        if (Hash21(p.px + int2((int)k * 7919, (int)k * 104729)) >= cover) continue;
        s.albedo = lerp(s.albedo, _AntScentColors[k].rgb, 0.75);
        s.glow = max(s.glow, 0.3);
    }
}

// Surface background (Milestone 11): up to 4 parallax layers behind the sky pixels, farthest first, and clouds.
float4 _AntScenery[4];       // x = shape (0 off, 1 peaks, 2 hills, 3 trees), y = foot above the horizon, z = height, w = feature width (cells)
float4 _AntSceneryColor[4];  // rgb = colour, a = haze toward the horizon colour
float4 _AntSceneryMore[4];   // x = parallax (0 far .. 1 with the world), y = snow line above the horizon (cells, 0 = none), z = mist at the foot, w = tree spacing (art pixels)
float4 _AntSceneryInfo;      // x = layer count, y = seed, w = how much the layers follow the camera up and down
float4 _AntSnow;
float4 _AntCloud;            // x = cover (0..1), y = haze, z = drift (art pixels per second), w = parallax
float4 _AntCloudBand;        // x, y = lowest and highest cloud (cells above the horizon), z = cloud size (cells), w = star parallax
float4 _AntCloudColor;
float4 _AntCloudShade;

float LineNoise(float x, int seed, bool smooth)
{
    float i = floor(x);
    float f = x - i;
    if (smooth) f = f * f * (3.0 - 2.0 * f);
    return lerp(Hash21(int2((int)i, seed)), Hash21(int2((int)i + 1, seed)), f);
}

float PlaneNoise(float2 q, int seed)
{
    float2 i = floor(q);
    float2 f = q - i;
    f = f * f * (3.0 - 2.0 * f);
    int2 c = (int2)i + int2(seed * 7, seed * 13);
    return lerp(lerp(Hash21(c), Hash21(c + int2(1, 0)), f.x), lerp(Hash21(c + int2(0, 1)), Hash21(c + int2(1, 1)), f.x), f.y);
}

// Crowns of a row of trees (art pixels above the ground line): spruces (stepped triangles) and round broadleaves.
float TreeCrowns(int lx, float spacing, int seed)
{
    int sp = max((int)spacing, 3);
    int t0 = (int)floor((float)lx / sp);
    float best = 0.0;
    [unroll] for (int k = -1; k <= 1; k++)
    {
        int t = t0 + k;
        float h1 = Hash21(int2(t, seed)), h2 = Hash21(int2(t, seed + 1)), h3 = Hash21(int2(t, seed + 2));
        float cx = t * sp + sp * 0.5 + (h1 - 0.5) * sp * 0.6;
        float dx = abs(lx + 0.5 - cx);
        float height = sp * (1.0 + h2 * 1.2);
        float r = sp * 0.62;
        float crown = h3 < 0.55 ? height - floor(dx) * 2.0 : (dx < r ? height - r + sqrt(r * r - dx * dx) : -1.0);
        best = max(best, crown);
    }
    return best;
}

// Rough 1D fractal noise in 0..1: broad swells with finer and finer jags on top.
float Fbm1(float x, int seed)
{
    return LineNoise(x, seed, true) * 0.5 + LineNoise(x * 2.13, seed + 1, true) * 0.25
         + LineNoise(x * 4.37, seed + 2, false) * 0.15 + LineNoise(x * 9.1, seed + 3, false) * 0.1;
}

// Mountains: one peak per feature width (random height, place, and a steepness of its own on each side), the highest one
// wins. Real mountains rather than pyramids: the flanks are concave (steep under the summit, spreading into a broad foot),
// the summit is blunt, and the ridgeline is rough rock, more jagged the higher it is. `face` is -1 on the sunlit side of
// the peak's ridge and 1 on the shaded side; the ridge runs down from the summit at a slant and wanders like an arête, so
// for a pixel `yh` high (in the same units as the peak heights) it is offset from the summit by the slant times the drop.
float Peaks(float x, float yh, int seed, out float face)
{
    float i0 = floor(x);
    float best = -1.0;
    face = 1.0;
    [unroll] for (int k = -1; k <= 1; k++)
    {
        int i = (int)i0 + k;
        float h = 0.35 + 0.65 * Hash21(int2(i, seed));
        float cx = i + 0.5 + (Hash21(int2(i, seed + 1)) - 0.5) * 0.6;
        float left = 1.0 + 0.9 * Hash21(int2(i, seed + 2));
        float right = 1.0 + 0.9 * Hash21(int2(i, seed + 4));
        float slant = (Hash21(int2(i, seed + 3)) - 0.5) * 0.5;
        float dx = x - cx;
        // A blunt top: distance from the summit eased over a small radius.
        float r = 0.075;
        float d = (sqrt(dx * dx + r * r) - r) * (dx < 0.0 ? left : right) / h;
        float v = h * pow(saturate(1.0 - d * 0.62), 1.75);
        if (v > best)
        {
            best = v;
            float drop = max(h - yh, 0.0);
            float arete = (LineNoise(yh * 7.0, seed + i * 31, true) - 0.5) * 0.26 + (LineNoise(yh * 31.0, seed + i * 17, false) - 0.5) * 0.06;
            face = dx < drop * (slant + arete) ? -1.0 : 1.0;
        }
    }
    best = max(best, 0.0);
    // Shoulders and lesser summits along the ridges, then rock along the ridgeline: notches and crags, stronger toward
    // the summits than on the low foothills.
    best += (LineNoise(x * 2.6, seed + 29, true) - 0.5) * 0.16 * best;
    return best + (Fbm1(x * 7.0, seed + 17) - 0.5) * (0.03 + 0.11 * best);
}

// Top of layer k at layer column lx, in art pixels above the horizon; `ly` is the pixel's height (for the peaks' faces).
float SceneryRidge(int k, int lx, float ly, out float face)
{
    float4 sh = _AntScenery[k];
    float cell = _AntPixel.y;
    int seed = (int)_AntSceneryInfo.y + k * 7919;
    float w = max(sh.w * cell, 1.0);
    face = 0.0;
    float h;
    if (sh.x < 1.5)
    {
        // Big peaks with smaller ones on their shoulders.
        float yh = (ly / cell - sh.y) / max(sh.z, 1e-3);
        float small;
        h = Peaks(lx / w, yh, seed, face);
        float h2 = Peaks(lx / (w * 0.42) + 0.37, yh / 0.55, seed + 101, small) * 0.55;
        if (h2 > h) { h = h2; face = small; }
    }
    else h = LineNoise(lx / w, seed, true) * 0.8 + LineNoise(lx / (w * 0.35), seed + 17, true) * 0.2;
    float ridge = (sh.y + sh.z * h) * cell;
    if (sh.x > 2.5) ridge += TreeCrowns(lx, _AntSceneryMore[k].w, seed + 31);
    return floor(ridge);
}

// Pixels a layer moves by relative to the world, for a camera at `cam` (art pixels): far layers follow the camera.
float2 SceneryShift(float parallax)
{
    float ppu = _AntPixel.x;
    float2 cam = _WorldSpaceCameraPos.xy * ppu;
    float horizon = _AntSkyRange.x * ppu;
    return round(float2(cam.x, (cam.y - horizon) * _AntSceneryInfo.w) * (1.0 - parallax));
}

// The nearest background layer covering a sky pixel, lit by the sky and fading into the horizon colour with distance and
// toward its foot. False if the pixel is open sky.
bool ApplyScenery(inout float3 albedo, int2 px)
{
    float cell = _AntPixel.y;
    float horizon = _AntSkyRange.x * _AntPixel.x;
    for (int k = (int)_AntSceneryInfo.x - 1; k >= 0; k--)
    {
        if (_AntScenery[k].x < 0.5) continue;
        float4 more = _AntSceneryMore[k];
        float2 shift = SceneryShift(more.x);
        int lx = px.x - (int)shift.x;
        float ly = px.y - shift.y - horizon;
        float face;
        float ridge = SceneryRidge(k, lx, ly, face);
        if (ly > ridge) continue;

        float4 sh = _AntScenery[k];
        float3 c = _AntSceneryColor[k].rgb;
        // The snow line dips down the gullies and rises over the ribs, a little lower on the shaded side.
        float snowLine = more.y * cell + (LineNoise(lx / (3.0 * cell), 5, true) - 0.5) * 3.0 * cell + (Hash21(int2(lx >> 1, 5)) - 0.5) * 3.0
                       + (face > 0.0 ? -0.6 * cell : 0.0);
        bool snow = sh.x < 1.5 && more.y > 0.0 && ly > snowLine;
        if (snow) c = _AntSnow.rgb;
        if (sh.x < 1.5) c *= face < 0.0 ? 1.12 : 0.8;      // sunlit left slopes, shaded right ones
        else if (ly > ridge - 1.0) c *= 1.18;            // a lit rim along the top of hills and trees
        // Mist rises from the foot, up to half the layer's height (in eighths, ordered dither between them; flat above).
        float mist = saturate(1.0 - ly / max((sh.y + sh.z * 0.5) * cell, 1.0));
        float haze = saturate(_AntSceneryColor[k].a + floor(more.z * mist * 8.0 + Bayer4(px)) / 8.0);
        albedo = lerp(c * _AntSkyLight.rgb, _AntSkyHorizon.rgb, haze);
        return true;
    }
    return false;
}

float CloudDensity(float lx, float ly, float lo, float hi, float size, int seed)
{
    float n = PlaneNoise(float2(lx / (size * 2.4), ly / size), seed) * 0.65
            + PlaneNoise(float2(lx / (size * 0.8), ly / (size * 0.45)), seed + 3) * 0.35;
    float b = saturate((ly - lo) / max(hi - lo, 1.0));
    return n * saturate(b * 8.0) * saturate((1.0 - b) * 2.5);   // flat-ish bottoms, rounded tops
}

// Drifting pixel clouds: a highlight on their tops and a shade along their bottoms.
void ApplyClouds(inout float3 albedo, int2 px)
{
    float cover = _AntCloud.x;
    if (cover <= 0.0) return;
    float cell = _AntPixel.y;
    float horizon = _AntSkyRange.x * _AntPixel.x;
    float2 shift = SceneryShift(_AntCloud.w);
    float ly = px.y - shift.y - horizon;
    float lo = _AntCloudBand.x * cell, hi = _AntCloudBand.y * cell;
    if (ly < lo || ly > hi) return;
    float lx = px.x - shift.x - floor(_AntTime * _AntCloud.z);
    float size = max(_AntCloudBand.z * cell, 2.0);
    int seed = (int)_AntSceneryInfo.y + 4441;
    float th = lerp(0.78, 0.3, saturate(cover));
    if (CloudDensity(lx, ly, lo, hi, size, seed) <= th) return;
    bool top = CloudDensity(lx, ly + 1.0, lo, hi, size, seed) <= th;
    bool bottom = CloudDensity(lx, ly - 2.0, lo, hi, size, seed) <= th;
    float3 c = bottom ? _AntCloudShade.rgb : _AntCloudColor.rgb * (top ? 1.06 : 1.0);
    albedo = lerp(c * _AntSkyLight.rgb, _AntSkyHorizon.rgb, _AntCloud.y);
}

TerrainSurface EvaluateTerrain(float2 positionWS)
{
    TerrainSurface s;
    PixelInfo p = GetPixel(positionWS);
    Neighborhood n = LoadNeighborhood((int2)floor(p.cellPos) - 1);
    float st = p.step;

    bool solid = IsSolid(n, p.cellPos, p.px);
    s.albedo = 0;
    s.glow = 0;
    s.unlit = false;
    s.hidden = false;
    s.fogOrder = false;
    s.normalWS = float3(0, 0, -1);
    s.solid = solid;
    s.material = 0u;
    s.scanned = false;
    s.sky = false;

    if (solid)
    {
        int jc = JitteredCell(n, p.cellPos, p.px);
        int mi = n.density[jc] >= 0.5 ? jc : SolidIndex(n, p.cellPos);
        uint m = n.material[mi];
        AntMaterial mat = _AntMaterials[m];
        float3 c = PatchShade(LOAD_TEXTURE2D_ARRAY(_AntMaterialTex, p.px & (int)_AntTile.x, m).rgb, p.px);
        s.material = m;
        s.scanned = (n.flags[mi] & ANT_FLAG_SCANNED) != 0;

        bool up = IsSolid(n, p.cellPos + float2(0, st), p.px + int2(0, 1));
        bool down = IsSolid(n, p.cellPos - float2(0, st), p.px - int2(0, 1));
        bool left = IsSolid(n, p.cellPos - float2(st, 0), p.px - int2(1, 0));
        bool right = IsSolid(n, p.cellPos + float2(st, 0), p.px + int2(1, 0));

        if (!up)
            c = mat.topRim.a > 0.5 ? mat.topRim.rgb : mat.edgeLight.rgb;
        else if (!down || !left || !right)
            c = mat.edgeDark.rgb;

        // Bevel normal from the density gradient so normal-mapped Light2Ds catch the edges.
        float gx = FieldAt(n, p.cellPos + float2(st, 0)) - FieldAt(n, p.cellPos - float2(st, 0));
        float gy = FieldAt(n, p.cellPos + float2(0, st)) - FieldAt(n, p.cellPos - float2(0, st));
        s.normalWS = normalize(float3(-gx * 6.0, -gy * 6.0, -1.0));

        // Mining order: diagonal stripes on the solid pixels of designated cells.
        if ((n.flags[4] & ANT_FLAG_DIG_DESIGNATED) != 0 && ((p.px.x + p.px.y) & 3) == 0)
        {
            c = lerp(c, _AntOverlay.rgb, 0.75);
            s.glow = 0.15;
        }

        // Found by Seismic Sense / a rich vein: a faint glow so it stands out in unlit rock.
        if ((n.flags[4] & ANT_FLAG_SCANNED) != 0)
            s.glow = max(s.glow, 0.3);

        s.albedo = c;
        s.glow = max(s.glow, mat.params.y);

        // Burning cell: flickering flames on its open edges, a charred face with sparkling embers inside.
        if (FluidKindOf(FluidWord(n.origin + int2(mi % 3, mi / 3))) == ANT_FLUID_FIRE)
        {
            float flick = FireFlicker(p.px);
            if (!up || !down || !left || !right)
            {
                s.albedo = flick > 0.5 ? _AntFireHot.rgb : _AntFire.rgb;
                s.unlit = true;
            }
            else
            {
                s.albedo = lerp(s.albedo, _AntEmber.rgb, 0.7);
                if (flick > 0.82) s.albedo = _AntFire.rgb;
                s.glow = max(s.glow, 0.6);
            }
        }
    }
    else
    {
        bool wallHere = HasWall(n, p.cellPos, p.px);
        int jc = JitteredCell(n, p.cellPos, p.px);
        uint m = n.wall[jc] > 0.5 ? n.material[jc] : WallMaterial(n, p.cellPos);
        AntMaterial mat = _AntMaterials[m];
        if (!wallHere)
        {
            // Sky: dithered vertical gradient.
            float t = saturate((positionWS.y - _AntSkyRange.x) / max(_AntSkyRange.y - _AntSkyRange.x, 1e-3));
            t = floor(t * 8.0 + Bayer4(p.px)) / 8.0;
            s.albedo = lerp(_AntSkyHorizon.rgb, _AntSkyTop.rgb, t);
            // Stars at night: sparse pixels fixed to the sky, the farthest layer (they barely move with the world, less
            // than the clouds and mountains), a few twinkling, fading toward the horizon.
            if (_AntStars.a > 0.0)
            {
                int2 sp = p.px - (int2)SceneryShift(_AntCloudBand.w);
                float star = Hash21(sp + int2(9173, 3511));
                float twinkle = Hash21(sp + int2((int)(_AntTime * 1.5), 77)) > 0.15 ? 1.0 : 0.4;
                if (star > 0.9965) s.albedo = lerp(s.albedo, _AntStars.rgb, saturate(_AntStars.a * t * 1.6) * twinkle);
            }
            // Mountains, hills and trees in front of the clouds.
            if (!ApplyScenery(s.albedo, p.px)) ApplyClouds(s.albedo, p.px);
            s.unlit = true;
            s.sky = true;
            ApplyFluid(s, n, p);
        }
        else
        {
            // Background wall: darker material, with stepped occlusion bands following the tunnel edge.
            // Low-contrast background keeps the foreground (terrain edges, ants, items) readable.
            float3 tex = PatchShade(LOAD_TEXTURE2D_ARRAY(_AntMaterialTex, p.px & (int)_AntTile.x, m).rgb, p.px);
            float3 c = lerp(mat.baseColor.rgb, tex, _AntWall.w) * Perceptual(_AntWall.x);
            float occ = floor(saturate(FieldAt(n, p.cellPos) * 2.0 - 0.2) * 3.0) / 3.0;
            s.albedo = c * Perceptual(1.0 - _AntWall.y * occ);

            // Storage zone: checker tint on the background wall.
            if ((n.flags[4] & ANT_FLAG_STORAGE) != 0 && ((p.px.x ^ p.px.y) & 2) == 0)
                s.albedo = lerp(s.albedo, _AntStorageColor.rgb, 0.35);
            ApplyFluid(s, n, p);
        }

        // Flames lick up from a burning cell below (a ragged band a few art pixels high).
        int2 below = (int2)floor(p.cellPos) - int2(0, 1);
        if (FluidKindOf(FluidWord(below)) == ANT_FLUID_FIRE)
        {
            float flick = FireFlicker(int2(p.px.x, 0));
            float height = (1.0 + 3.0 * flick) * p.step;
            float local = frac(p.cellPos.y);
            if (local < height && Hash21(p.px + int2((int)(_AntTime * 9.0), 7)) > 0.25)
            {
                s.albedo = local < height * 0.5 ? _AntFireHot.rgb : _AntFire.rgb;
                s.unlit = true;
            }
        }
    }

    ApplyScent(s, p);
    s.hidden = RevealedAt(n, p.cellPos) + (Bayer4(p.px) - 0.5) * 0.7 < 0.5;
    // Mining orders stay visible as faint stripes over the fog so the player can see the plan.
    s.fogOrder = s.hidden && (n.flags[4] & ANT_FLAG_DIG_DESIGNATED) != 0 && ((p.px.x + p.px.y) & 3) == 0;
    return s;
}

float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

// Light overlay (10.7): the light reaching a pixel, in flat colour bands from dark to bright; the terrain's own
// brightness only shades the bands a little so tunnels stay readable.
float3 LightOverlay(TerrainSurface s, float3 light, float2 positionWS)
{
    float level = s.unlit ? (s.sky ? Luma(_AntSkyLight.rgb) : 1.0) : Luma(light);
    level = pow(saturate(level), 1.0 / max(_AntWall.z, 1.0));   // back to perceptual steps
    int count = max((int)_AntView.z, 1);
    int band = min((int)floor(level * count), count - 1);
    float shade = 0.7 + 0.3 * saturate(Luma(s.albedo) * 4.0);
    return _AntLightRamp[band].rgb * shade;
}

// Ore overlay (10.7): finds in their map colour, flat and unlit; everything else grey and dim, unlit too so the map reads
// the same in dark rock. Finds hidden in the fog show at half strength if Seismic Sense found them.
half4 OreOverlay(TerrainSurface s, float2 positionWS)
{
    AntMaterial mat = _AntMaterials[s.material];
    bool find = s.solid && mat.oreColor.a > 0.5;
    float3 c = find ? mat.oreColor.rgb : saturate(Luma(s.albedo) * 1.5 * _AntView.y).xxx;
    if (s.hidden)
    {
        PixelInfo p = GetPixel(positionWS);
        c = find && s.scanned && Bayer4(p.px) < 0.5 ? mat.oreColor.rgb * 0.6 : float3(0, 0, 0);
    }
    return half4(c, 1);
}

float3 QuantizedGridLight(float2 positionWS)
{
    PixelInfo p = GetPixel(positionWS);
    float3 l = LightAt(p.cellPos);
    float bands = _AntPixel.z;
    return Perceptual(floor(l * bands + Bayer4(p.px)) / bands);
}

#endif
