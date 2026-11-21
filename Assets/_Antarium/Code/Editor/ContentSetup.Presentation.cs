using Antarium.Content;
using UnityEditor;

namespace Antarium.Editor
{
    /// <summary>Milestone 10.7: the ore overlay's map colours for every find (bright, one hue each, so finds tell apart on the dimmed map).</summary>
    public static partial class ContentSetup
    {
        static readonly (string Item, string Color)[] MapColors =
        {
            ("copper_ore", "#ff8c3a"), ("iron_ore", "#e8463c"), ("coal", "#4f7bff"), ("amber", "#ffd21f"),
            ("fossil", "#fff3dc"), ("crystal_shard", "#5ef0ff"), ("obsidian", "#b45cff"), ("relic", "#3ddc84"),
            ("tablet", "#ff5cc8"), ("star_fragment", "#b6ff3d"), ("root_fibre", "#b08a5a"),
        };

        /// <summary>Sets the map colour of each find that has none yet (a colour set by hand stays).</summary>
        static void CreatePresentation()
        {
            foreach (var (id, hex) in MapColors)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDef>($"{ItemsFolder}/{id}.asset");
                if (item == null || item.mapColor.a > 0f) continue;
                item.mapColor = Hex(hex);
                EditorUtility.SetDirty(item);
            }
        }

        [MenuItem("Antarium/Setup/Set Ore Map Colours")]
        static void SetMapColors()
        {
            CreatePresentation();
            AssetDatabase.SaveAssets();
        }
    }
}
