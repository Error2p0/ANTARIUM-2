using System.IO;
using Antarium.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    /// <summary>
    /// Antarium → Capture: runs the store media capture in play mode (frames go to <c>Captures/</c> in the project
    /// root, git-ignored). Play mode only runs while the editor has focus, so keep it in front until it stops by itself.
    /// From code: <c>CaptureMenu.Run("nest,factory", rebuild: false)</c>.
    /// </summary>
    public static class CaptureMenu
    {
        public static string OutputDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures"));

        [MenuItem("Antarium/Capture/Run All Shots")]
        static void RunAll() => Run("all", rebuild: false);

        [MenuItem("Antarium/Capture/Rebuild Showcase and Run All Shots")]
        static void RebuildAll() => Run("all", rebuild: true);

        [MenuItem("Antarium/Capture/Open Output Folder")]
        static void OpenFolder()
        {
            Directory.CreateDirectory(OutputDirectory);
            EditorUtility.RevealInFinder(OutputDirectory);
        }

        /// <summary>Enters play mode running <paramref name="shots"/> (comma separated names, or "all").</summary>
        public static void Run(string shots, bool rebuild)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Capture: stop play mode first.");
                return;
            }
            var request = new CaptureRequest
            {
                Shots = shots.Split(','),
                OutputDirectory = OutputDirectory,
                RebuildShowcase = rebuild,
            };
            SessionState.SetString(CaptureRequest.EditorKey, request.Encode());
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        static void ClearWhenPlayModeEnds()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) SessionState.EraseString(CaptureRequest.EditorKey);
            };
        }
    }
}
