using System;
using System.IO;
using UnityEngine;

namespace Antarium.Gameplay
{
    /// <summary>
    /// A request to run the store media capture instead of the game: which shots, where the frames go, and whether
    /// the showcase colony is rebuilt. Comes from the command line of a development build
    /// (<c>-capture nest,factory -captureOut D:/media -rebuildShowcase</c>) or, in the editor, from
    /// <i>Antarium → Capture</i> (a session value read when play mode starts).
    /// </summary>
    public sealed class CaptureRequest
    {
        public const string EditorKey = "Antarium.CaptureRequest";

        /// <summary>Shot names (see <c>GameBootstrap.Shots</c>); empty or "all" = every shot.</summary>
        public string[] Shots = Array.Empty<string>();
        public string OutputDirectory;
        public bool RebuildShowcase;

        /// <summary>Profile, settings and saves of the capture run: never the player's own.</summary>
        public string ScratchDirectory => Path.Combine(OutputDirectory, "_scratch");
        public string ShowcaseDirectory => Path.Combine(OutputDirectory, "_showcase");

        public bool Wants(string shot)
        {
            if (Shots.Length == 0) return true;
            foreach (var s in Shots)
                if (s == "all" || string.Equals(s, shot, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The request for this run, or null to play normally.</summary>
        public static CaptureRequest Get()
        {
#if UNITY_EDITOR
            string encoded = UnityEditor.SessionState.GetString(EditorKey, "");
            if (string.IsNullOrEmpty(encoded)) return null;
            return Decode(encoded);
#else
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-capture");
            if (i < 0) return null;
            var request = new CaptureRequest
            {
                Shots = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? Split(args[i + 1]) : Array.Empty<string>(),
                OutputDirectory = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Captures"),
                RebuildShowcase = Array.IndexOf(args, "-rebuildShowcase") >= 0,
            };
            int o = Array.IndexOf(args, "-captureOut");
            if (o >= 0 && o + 1 < args.Length) request.OutputDirectory = args[o + 1];
            return request;
#endif
        }

        /// <summary>"shots|output|rebuild" (editor session value).</summary>
        public string Encode() => string.Join(",", Shots) + "|" + OutputDirectory + "|" + (RebuildShowcase ? "1" : "0");

        public static CaptureRequest Decode(string encoded)
        {
            var parts = encoded.Split('|');
            return new CaptureRequest
            {
                Shots = parts.Length > 0 ? Split(parts[0]) : Array.Empty<string>(),
                OutputDirectory = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : "Captures",
                RebuildShowcase = parts.Length > 2 && parts[2] == "1",
            };
        }

        static string[] Split(string list) => list.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
