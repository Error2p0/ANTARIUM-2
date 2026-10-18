using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Content
{
    /// <summary>Blittable per-material data for Burst jobs.</summary>
    public struct MaterialSim
    {
        public float Hardness;
        public MaterialFlags Flags;
        /// <summary>Emitted light per channel as a fraction of the max light level.</summary>
        public float3 Emission;

        public bool IsAir => (Flags & MaterialFlags.Air) != 0;
        public bool IsIndestructible => (Flags & MaterialFlags.Indestructible) != 0;
    }

    /// <summary>
    /// Runtime content baked from authored definitions: dense ushort material indices (Air is always 0)
    /// plus native tables for jobs. Saves must store ids, not indices; see <see cref="TryGetMaterialIndex"/>.
    /// </summary>
    public sealed class ContentDB : IDisposable
    {
        public const ushort AirIndex = 0;

        public readonly TerrainMaterialDef[] Materials;
        public readonly WorldGenSettings WorldGen;
        public NativeArray<MaterialSim> MaterialSim;

        readonly Dictionary<string, ushort> _materialIndex = new();

        ContentDB(TerrainMaterialDef[] materials, WorldGenSettings worldGen)
        {
            Materials = materials;
            WorldGen = worldGen;
            MaterialSim = new NativeArray<MaterialSim>(materials.Length, Allocator.Persistent);
            for (int i = 0; i < materials.Length; i++)
            {
                var def = materials[i];
                _materialIndex.Add(def.Id, (ushort)i);
                var c = def.emissionColor;
                MaterialSim[i] = new MaterialSim
                {
                    Hardness = def.hardness,
                    Flags = def.flags,
                    Emission = new float3(c.r, c.g, c.b) * def.emissionStrength,
                };
            }
        }

        public static ContentDB Build(ContentDatabase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            return Build(database.materials, database.worldGen);
        }

        public static ContentDB Build(IReadOnlyList<TerrainMaterialDef> materials, WorldGenSettings worldGen)
        {
            if (materials == null) throw new ArgumentNullException(nameof(materials));
            if (worldGen == null) throw new ArgumentException("ContentDatabase has no WorldGenSettings.");

            var ordered = new List<TerrainMaterialDef>(materials.Count);
            var ids = new HashSet<string>();
            TerrainMaterialDef air = null;
            foreach (var def in materials)
            {
                if (def == null) throw new ArgumentException("Material list contains an empty entry.");
                if (string.IsNullOrEmpty(def.Id)) throw new ArgumentException($"Material '{def.name}' has no id.");
                if (!ids.Add(def.Id)) throw new ArgumentException($"Duplicate material id '{def.Id}'.");
                if (def.IsAir)
                {
                    if (air != null) throw new ArgumentException($"Both '{air.Id}' and '{def.Id}' are flagged Air.");
                    air = def;
                }
                else ordered.Add(def);
            }
            if (air == null) throw new ArgumentException("No material is flagged Air.");
            if (ordered.Count + 1 > ushort.MaxValue) throw new ArgumentException("Too many materials.");
            ordered.Insert(AirIndex, air);

            var db = new ContentDB(ordered.ToArray(), worldGen);
            db.ValidateWorldGen();
            return db;
        }

        public bool TryGetMaterialIndex(string id, out ushort index) => _materialIndex.TryGetValue(id, out index);

        public ushort GetMaterialIndex(TerrainMaterialDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!_materialIndex.TryGetValue(def.Id, out var index))
                throw new KeyNotFoundException($"Material '{def.Id}' is not registered in the ContentDatabase.");
            return index;
        }

        void ValidateWorldGen()
        {
            var gen = WorldGen;
            if (gen.air == null || !gen.air.IsAir) throw new ArgumentException("WorldGenSettings.air must be the Air material.");
            GetMaterialIndex(gen.surfaceMaterial);
            GetMaterialIndex(gen.bedrock);
            if (gen.biomes.Count == 0) throw new ArgumentException("WorldGenSettings has no biomes.");
            foreach (var biome in gen.biomes)
            {
                if (biome == null) throw new ArgumentException("WorldGenSettings.biomes contains an empty entry.");
                GetMaterialIndex(biome.baseMaterial);
                foreach (var deposit in biome.deposits)
                    GetMaterialIndex(deposit.material);
            }
        }

        public void Dispose()
        {
            if (MaterialSim.IsCreated) MaterialSim.Dispose();
        }
    }
}
