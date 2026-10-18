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
};

StructuredBuffer<uint> _AntCells;        // material(16) | density(8) | flags(8)
StructuredBuffer<uint> _AntLight;        // r | g | b | unused, 8 bits each
StructuredBuffer<AntMaterial> _AntMaterials;
TEXTURE2D_ARRAY(_AntMaterialTex);

float4 _AntWorld;      // x = width (cells), y = height (cells), z = chunks per row
float4 _AntPixel;      // x = pixels per unit, y = pixels per cell, z = light bands, w = edge noise
float4 _AntWall;       // x = wall brightness, y = wall occlusion, z = gamma exponent (2.2 in linear colour space, else 1)
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
};

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

bool IsSolid(Neighborhood n, float2 cellPos, int2 px)
{
    return FieldAt(n, cellPos) + (Hash21(px) - 0.5) * _AntPixel.w > 0.5;
}

struct TerrainSurface
{
    float3 albedo;
    float glow;      // self illumination
    bool unlit;      // sky: output albedo directly
    bool hidden;     // fog of war
    float3 normalWS;
};

TerrainSurface EvaluateTerrain(float2 positionWS)
{
    TerrainSurface s;
    PixelInfo p = GetPixel(positionWS);
    Neighborhood n = LoadNeighborhood((int2)floor(p.cellPos) - 1);
    float st = p.step;

    bool solid = IsSolid(n, p.cellPos, p.px);
    s.glow = 0;
    s.unlit = false;
    s.normalWS = float3(0, 0, -1);

    if (solid)
    {
        uint m = SolidMaterial(n, p.cellPos);
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

        s.albedo = c;
        s.glow = mat.params.y;
    }
    else
    {
        uint m = n.material[4];
        AntMaterial mat = _AntMaterials[m];
        if (mat.params.x > 0.5)
        {
            // Sky: dithered vertical gradient.
            float t = saturate((positionWS.y - _AntSkyRange.x) / max(_AntSkyRange.y - _AntSkyRange.x, 1e-3));
            t = floor(t * 8.0 + Bayer4(p.px)) / 8.0;
            s.albedo = lerp(_AntSkyHorizon.rgb, _AntSkyTop.rgb, t);
            s.unlit = true;
        }
        else
        {
            // Background wall: darker material, with dithered occlusion near the tunnel edge.
            float3 c = LOAD_TEXTURE2D_ARRAY(_AntMaterialTex, p.px & 31, m).rgb * Perceptual(_AntWall.x);
            float occ = saturate(FieldAt(n, p.cellPos) * 2.0);
            occ = floor(occ * 3.0 + Bayer4(p.px)) / 3.0;
            s.albedo = c * Perceptual(1.0 - _AntWall.y * occ);
        }
    }

    s.hidden = RevealedAt(n, p.cellPos) + (Bayer4(p.px) - 0.5) * 0.7 < 0.5;
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
