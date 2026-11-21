using Antarium.Gameplay;
using UnityEditor;

namespace Antarium.Editor
{
    /// <summary>
    /// The demo in the editor (GDD 2.16): with *Play as Demo in Editor* checked, play mode is the demo (sealed colonies, full game
    /// locks, no achievements) and keeps its saves and settings in a <c>Demo</c> subfolder of the save folder. The full game in the
    /// editor offers to continue a colony from that folder, as the full game build does with the demo build's folder.
    /// </summary>
    public static class DemoMenu
    {
        const string Item = "Antarium/Demo/Play as Demo in Editor";

        [MenuItem(Item)]
        static void Toggle()
        {
            bool on = !EditorPrefs.GetBool(GameBootstrap.DemoInEditorPref, false);
            EditorPrefs.SetBool(GameBootstrap.DemoInEditorPref, on);
            UnityEngine.Debug.Log(on ? "Play mode is the demo now (saves in the Demo subfolder)." : "Play mode is the full game again.");
        }

        [MenuItem(Item, true)]
        static bool Validate()
        {
            Menu.SetChecked(Item, EditorPrefs.GetBool(GameBootstrap.DemoInEditorPref, false));
            return !EditorApplication.isPlaying;
        }
    }
}
