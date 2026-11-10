using System.Collections.Generic;
using System.IO;
using System.Linq;
using Antarium.Content;
using Antarium.Core;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    /// <summary>
    /// <i>Antarium → Art → Import Overrides</i>: makes the PNGs under <c>Assets/_Antarium/Art/</c> usable by the game.
    /// Sets their import settings (point filter, no compression, readable, no mip maps), lists them by name in the
    /// <see cref="ArtLibrary"/> (the path under <c>Art/</c> without the extension), assigns <c>Art/Terrain/&lt;material
    /// id&gt;.png</c> to that material's texture override and takes the material's base, shadow and highlight colours
    /// (terrain edges, background walls) from the tile. Safe to re-run; run it after adding or renaming a PNG.
    /// The naming convention and sizes are in <c>docs/ART_STYLE.md</c>.
    /// </summary>
    public static class ArtImport
    {
        public const string ArtFolder = "Assets/_Antarium/Art";
        public const string LibraryPath = ArtFolder + "/ArtLibrary.asset";
        const string TerrainPrefix = "Terrain/";

        [MenuItem("Antarium/Art/Import Overrides")]
        public static void ImportOverrides()
        {
            ContentSetup.EnsureFolder(ArtFolder);
            AssetDatabase.Refresh();
            var library = AssetDatabase.LoadAssetAtPath<ArtLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ArtLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { ArtFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();
            foreach (string path in paths) Configure(path);

            library.entries.Clear();
            foreach (string path in paths)
                library.entries.Add(new ArtLibrary.Entry { name = NameOf(path), texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path) });
            library.Rebuild();
            EditorUtility.SetDirty(library);

            int terrain = AssignTerrain(library, out var problems);

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ContentSetup.ConfigPath);
            if (config != null && config.art != library)
            {
                config.art = library;
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
            foreach (string problem in problems) Debug.LogWarning("Art import: " + problem);
            Debug.Log($"Art import: {library.entries.Count} PNGs in the library, {terrain} terrain tiles assigned.");
        }

        /// <summary>The library name of a PNG: its path under the art folder, without the extension.</summary>
        public static string NameOf(string assetPath)
        {
            string relative = assetPath.Substring(ArtFolder.Length + 1).Replace('\\', '/');
            return relative.Substring(0, relative.Length - Path.GetExtension(relative).Length);
        }

        static void Configure(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) return;
            bool changed = false;
            void Set<T>(T current, T wanted, System.Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
                apply(wanted);
                changed = true;
            }
            Set(importer.textureType, TextureImporterType.Default, v => importer.textureType = v);
            Set(importer.filterMode, FilterMode.Point, v => importer.filterMode = v);
            Set(importer.mipmapEnabled, false, v => importer.mipmapEnabled = v);
            Set(importer.alphaIsTransparency, true, v => importer.alphaIsTransparency = v);
            Set(importer.textureCompression, TextureImporterCompression.Uncompressed, v => importer.textureCompression = v);
            Set(importer.npotScale, TextureImporterNPOTScale.None, v => importer.npotScale = v);
            Set(importer.isReadable, true, v => importer.isReadable = v);
            Set(importer.sRGBTexture, true, v => importer.sRGBTexture = v);
            bool tile = NameOf(path).StartsWith(TerrainPrefix);
            Set(importer.wrapMode, tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, v => importer.wrapMode = v);
            if (changed) importer.SaveAndReimport();
        }

        /// <summary>Terrain tiles: <c>Terrain/&lt;material id&gt;</c> becomes that material's texture override.</summary>
        static int AssignTerrain(ArtLibrary library, out List<string> problems)
        {
            problems = new List<string>();
            var materials = AssetDatabase.FindAssets("t:TerrainMaterialDef")
                .Select(g => AssetDatabase.LoadAssetAtPath<TerrainMaterialDef>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(m => m != null)
                .ToDictionary(m => m.Id);
            int assigned = 0;
            foreach (var entry in library.entries)
            {
                if (!entry.name.StartsWith(TerrainPrefix)) continue;
                string id = entry.name.Substring(TerrainPrefix.Length);
                if (!materials.TryGetValue(id, out var material))
                {
                    problems.Add($"'{entry.name}.png' matches no terrain material id.");
                    continue;
                }
                const int size = WorldConstants.MaterialTextureSize;
                if (entry.texture.width != size || entry.texture.height != size)
                {
                    problems.Add($"'{entry.name}.png' is {entry.texture.width}x{entry.texture.height}; terrain tiles are {size}x{size}. Not assigned.");
                    continue;
                }
                Antarium.Presentation.ArtOverrides.TileColors(entry.texture.GetPixels32(), out var base32, out var shadow32, out var highlight32);
                Color baseColor = base32, shadow = shadow32, highlight = highlight32;
                if (material.textureOverride != entry.texture || material.baseColor != baseColor
                    || material.shadowColor != shadow || material.highlightColor != highlight)
                {
                    material.textureOverride = entry.texture;
                    material.baseColor = baseColor;
                    material.shadowColor = shadow;
                    material.highlightColor = highlight;
                    EditorUtility.SetDirty(material);
                }
                assigned++;
            }
            return assigned;
        }
    }
}
