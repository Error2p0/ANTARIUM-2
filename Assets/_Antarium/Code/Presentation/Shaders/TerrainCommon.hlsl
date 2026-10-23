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
};

StructuredBuffer<uint> _AntCells;        // material(16) | density(8) | flags(8)
StructuredBuffer<uint> _AntLight;        // r | g | b | unused, 8 bits each
StructuredBuffer<AntMaterial> _AntMaterials;
TEXTURE2D_ARRAY(_AntMaterialTex);

float4 _AntWorld;      // x = width (cells), y = height (cells), z = chunks per row
float4 _AntPixel;      // x = pixels per unit, y = pixels per cell, z = light bands, w = edge noise
float4 _AntWall;       // x = wall brightness, y = wall occlusion, z = gamma exponent (2.2 in linear colour space, else 1), w = wall contrast
float4 _AntSkyTop;
float4 _AntSkyHorizon;
float4 _AntSkyRange;   // x = horizon world y, y = top world y

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
    return float3(w & 0xFFu, (w >> 8) & 0xFFu, (w >> 16) & 0xFFu) / 255.0;
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

// Fluid plane: two cells per uint, each 16 bits = level (8) | kind (8). Kind: 1 water, 2 lava, 3 gas.
StructuredBuffer<uint> _AntFluid;
float4 _AntWater;        // rgb tint, a = cover
float4 _AntWaterSurface;
float4 _AntLava;
float4 _AntLavaHot;
float4 _AntGas;          // rgb tint, a = cover at full density
float _AntTime;

#define ANT_FLUID_WATER 1u
#define ANT_FLUID_LAVA 2u
#define ANT_FLUID_GAS 3u

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

// Material of a solid pixel: the neighbouring cell contributing the most density.
uint SolidMaterial(Neighborhood n, float2 p)
{
    int i; float4 w;
    Bilinear(n, p, i, w);
    float4 s = w * float4(n.density[i], n.density[i + 1], n.density[i + 3], n.density[i + 4]);
    int best = i; float bestScore = s.x;
    if (s.y > bestScore) { best = i + 1; bestScore = s.y; }
    if (s.z > bestScore) { best = i + 3; bestScore = s.z; }
    if (s.w > bestScore) { best = i + 4; }
    return n.material[best];
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
};

// Fluid over an open pixel. Liquids fill a cell from the bottom up to their level (full when the same liquid
// is above), with a bright surface row; lava is self-lit and churns; gas is a dithered haze.
void ApplyFluid(inout TerrainSurface s, Neighborhood n, PixelInfo p)
{
    int2 c = (int2)floor(p.cellPos);
    uint f = FluidWord(c);
    if (FluidKindOf(f) == 0u && n.density[4] >= 0.5)
    {
        // An open-looking rim pixel of a solid cell: take the fluid of the open neighbour it leans toward.
        float2 fr = frac(p.cellPos);
        uint side = FluidWord(c + int2(fr.x < 0.5 ? -1 : 1, 0));
        f = FluidKindOf(side) != 0u ? side : FluidWord(c + int2(0, 1));
    }
    uint kind = FluidKindOf(f);
    float level = FluidLevelOf(f);
    if (kind == 0u || level <= 0.0) return;

    if (kind == ANT_FLUID_GAS)
    {
        if (Bayer4(p.px + int2((int)(_AntTime * 2.0), 0)) < level * _AntGas.a * 1.6)
            s.albedo = lerp(s.albedo, _AntGas.rgb, 0.55);
        return;
    }

    uint above = FluidWord(c + int2(0, 1));
    bool full = FluidKindOf(above) == kind && FluidLevelOf(above) > 0.0;
    float top = full ? 1.0 : max(level, p.step);
    float local = frac(p.cellPos.y);
    if (local > top) return;
    bool surface = !full && local > top - p.step;

    if (kind == ANT_FLUID_WATER)
    {
        s.albedo = surface ? _AntWaterSurface.rgb : lerp(s.albedo, _AntWater.rgb, _AntWater.a);
    }
    else
    {
        float churn = ValueNoise4(p.px + int2((int)(_AntTime * 5.0), (int)(_AntTime * 3.0)));
        s.albedo = surface || churn > 0.62 ? _AntLavaHot.rgb : _AntLava.rgb;
        s.unlit = true;
    }
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

    if (solid)
    {
        int jc = JitteredCell(n, p.cellPos, p.px);
        uint m = n.density[jc] >= 0.5 ? n.material[jc] : SolidMaterial(n, p.cellPos);
        AntMaterial mat = _AntMaterials[m];
        float3 c = LOAD_TEXTURE2D_ARRAY(_AntMaterialTex, p.px & 31, m).rgb;

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
            s.unlit = true;
            ApplyFluid(s, n, p);
        }
        else
        {
            // Background wall: darker material, with stepped occlusion bands following the tunnel edge.
            // Low-contrast background keeps the foreground (terrain edges, ants, items) readable.
            float3 tex = LOAD_TEXTURE2D_ARRAY(_AntMaterialTex, p.px & 31, m).rgb;
            float3 c = lerp(mat.baseColor.rgb, tex, _AntWall.w) * Perceptual(_AntWall.x);
            float occ = floor(saturate(FieldAt(n, p.cellPos) * 2.0 - 0.2) * 3.0) / 3.0;
            s.albedo = c * Perceptual(1.0 - _AntWall.y * occ);

            // Storage zone: checker tint on the background wall.
            if ((n.flags[4] & ANT_FLAG_STORAGE) != 0 && ((p.px.x ^ p.px.y) & 2) == 0)
                s.albedo = lerp(s.albedo, _AntStorageColor.rgb, 0.35);
            ApplyFluid(s, n, p);
        }
    }

    ApplyScent(s, p);
    s.hidden = RevealedAt(n, p.cellPos) + (Bayer4(p.px) - 0.5) * 0.7 < 0.5;
    // Mining orders stay visible as faint stripes over the fog so the player can see the plan.
    s.fogOrder = s.hidden && (n.flags[4] & ANT_FLAG_DIG_DESIGNATED) != 0 && ((p.px.x + p.px.y) & 3) == 0;
    return s;
}

float3 QuantizedGridLight(float2 positionWS)
{
    PixelInfo p = GetPixel(positionWS);
    float3 l = LightAt(p.cellPos);
    float bands = _AntPixel.z;
    return Perceptual(floor(l * bands + Bayer4(p.px)) / bands);
}

#endif
