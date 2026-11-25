using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Store media capture: instead of the title screen the game runs a list of scripted shots (see
    /// <c>GameBootstrap.Shots.cs</c>), each loading the showcase colony or a fresh one, moving the camera along a
    /// path, acting like a player (digging, abilities, possession) and recording frames offscreen through a
    /// <see cref="CaptureRig"/>. Time runs at a fixed rate, so every clip is smooth however slow capturing is.
    /// Nothing is saved, no achievements unlock and the player's settings are not used.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>A camera pose: focus point in cells and integer zoom.</summary>
        struct CamPose
        {
            public float2 Cell;
            public int Zoom;
            public CamPose(float2 cell, int zoom)
            {
                Cell = cell;
                Zoom = zoom;
            }
        }

        CaptureRequest _captureRequest;
        CaptureRig _rig;
        CaptureSound _captureSound;
        /// <summary>Seconds since the current shot began (fixed steps).</summary>
        float _shotTime;
        Func<float, CamPose> _camPath;
        /// <summary>Runs every frame before the simulation advances: the shot's "player" (time, dt).</summary>
        Action<float, float> _shotTick;
        bool _shotPaused;
        float2 _shotPointer;
        bool _showPointer;
        string _stillPath;
        int _fps = 60;
        readonly StringBuilder _captureLog = new();

        bool Capturing => _captureRequest != null;

        void BeginCapture(CaptureRequest request)
        {
            _captureRequest = request;
            Directory.CreateDirectory(request.OutputDirectory);
            _rig = new CaptureRig(worldCamera, _panelSettings);
            _rig.SetSize(1920, 1080);
            if (_audio != null)
            {
                _audio.Clock = () => Time.timeAsDouble; // fixed-rate game time, like the frames
                _captureSound = new CaptureSound(_audio, _rig, request.OutputDirectory);
            }
            _debugHud.Visible = false;
            _hud.QuietCaveIns = true;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Debug.Log($"Capture: shots [{string.Join(",", request.Shots)}] → {Path.GetFullPath(request.OutputDirectory)}");
            StartCoroutine(EndOfFrameCapture());
            StartCoroutine(RunCapture());
        }

        IEnumerator EndOfFrameCapture()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                if (_rig == null) yield break;
                _rig.Cursor = _showPointer ? _camera.WorldToScreen(_shotPointer) : null;
                if (_stillPath != null)
                {
                    _rig.WriteFrame(_stillPath);
                    _stillPath = null;
                }
                _rig.EndOfFrame();
            }
        }

        IEnumerator RunCapture()
        {
            yield return null; // let the UI document build its panel
            var started = DateTime.Now;
            foreach (var (name, shot, store) in Shots())
            {
                if (!_captureRequest.Wants(name, store)) continue;
                Debug.Log($"Capture: shot '{name}'");
                var t0 = DateTime.Now;
                ResetShot();
                IEnumerator run;
                try
                {
                    run = shot();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Capture: shot '{name}' failed to start: {e}");
                    continue;
                }
                yield return RunSafely(run, name);
                _rig.EndClip();
                _captureLog.AppendLine($"{name}\t{(DateTime.Now - t0).TotalSeconds:0.0}s");
            }
            _rig.Flush();
            _captureSound?.Dispose();
            File.WriteAllText(Path.Combine(_captureRequest.OutputDirectory, "capture_log.txt"), _captureLog.ToString());
            Debug.Log($"Capture: done in {(DateTime.Now - started).TotalSeconds:0} s.\n{_captureLog}");
            Time.captureFramerate = 0;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Runs a shot's coroutine (nested coroutines included); an exception ends the shot, not the capture.</summary>
        IEnumerator RunSafely(IEnumerator root, string name)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                try
                {
                    more = top.MoveNext();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Capture: shot '{name}' failed: {e}");
                    _captureLog.AppendLine($"{name}\tFAILED {e.Message}");
                    yield break;
                }
                if (!more)
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested) stack.Push(nested);
                else yield return top.Current;
            }
        }

        void ResetShot()
        {
            _shotTime = 0f;
            _camPath = null;
            _shotTick = null;
            _shotPaused = false;
            _showPointer = false;
            _rig.ShowUi = true;
            _rig.SetSize(1920, 1080);
            SetFps(60);
            SetOverlay(OverlayMode.None);
            _hud.HideModal();
            _hud.Intro?.Stop();
            _hud.SetTitle(false);
            Hud(true);
            _camera.ShakeScale = 1f;
            _content.DemoActive = false; // the demo shots turn it on (Shots.Demo.cs)
        }

        /// <summary>Called by Update in capture mode instead of the camera controller.</summary>
        void DriveShot(float dt, bool paused)
        {
            _shotTime += dt;
            if (DemoPass) KeepDemoFed();
            _shotTick?.Invoke(_shotTime, dt);
            if (_camPath != null)
            {
                var pose = _camPath(_shotTime);
                _camera.SetZoom(pose.Zoom);
                _camera.Position = pose.Cell * WorldConstants.CellSize;
            }
        }

        // ---------------------------------------------------------------- shot building blocks

        void SetFps(int fps)
        {
            _fps = fps;
            Time.captureFramerate = fps;
        }

        /// <summary>Renders <paramref name="frames"/> frames without recording them.</summary>
        IEnumerator Frames(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

        IEnumerator Seconds(float seconds) => Frames(math.max(1, (int)math.round(seconds * _fps)));

        /// <summary>
        /// Records <paramref name="seconds"/> of frames to <c>clips/&lt;name&gt;/NNNNN.png</c>. In a demo pass only the demo's own
        /// clips (<c>d_*</c>) are recorded; the store shots' clips just let the time pass.
        /// </summary>
        IEnumerator Clip(string name, float seconds)
        {
            if (DemoPass && !name.StartsWith("d_", StringComparison.Ordinal))
            {
                yield return Seconds(seconds);
                yield break;
            }
            _rig.BeginClip(Path.Combine(_captureRequest.OutputDirectory, "clips", name));
            int frames = math.max(1, (int)math.round(seconds * _fps));
            for (int i = 0; i < frames; i++) yield return null;
            _rig.EndClip();
        }

        /// <summary>Writes this frame to <c>stills/&lt;name&gt;.png</c> (in a demo pass <c>demo_stills/</c>).</summary>
        IEnumerator Still(string name)
        {
            _stillPath = Path.Combine(_captureRequest.OutputDirectory, DemoPass ? "demo_stills" : "stills", name + ".png");
            yield return null;
        }

        void Hud(bool visible)
        {
            var root = hudDocument != null ? hudDocument.rootVisualElement : null;
            if (root != null) root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Cam(float2 cell, int zoom)
        {
            var pose = new CamPose(cell, zoom);
            _camPath = _ => pose;
        }

        /// <summary>Moves the camera from <paramref name="from"/> to <paramref name="to"/> over <paramref name="seconds"/> (smooth start and stop), counting from now.</summary>
        void Pan(float2 from, float2 to, int zoom, float seconds, bool ease = true)
        {
            float start = _shotTime;
            _camPath = t =>
            {
                float k = math.saturate((t - start) / seconds);
                if (ease) k = k * k * (3f - 2f * k);
                return new CamPose(math.lerp(from, to, k), zoom);
            };
        }

        /// <summary>Mouse pointer over a cell (drawn in the frames when <paramref name="show"/>).</summary>
        void Pointer(float2 cell, bool show)
        {
            _shotPointer = cell * WorldConstants.CellSize;
            _showPointer = show;
        }

        /// <summary>
        /// Loads the showcase colony (built and saved once; see <see cref="ShowcaseColony"/>). In a demo pass the demo's
        /// showcase, built in demo mode in its own folder.
        /// </summary>
        void LoadShowcase()
        {
            string dir = ShowcaseDirectory;
            var store = new SaveStore(dir);
            if (_captureRequest.RebuildShowcase && _rebuiltShowcases.Add(dir) || !store.HasSave)
            {
                var t0 = DateTime.Now;
                var built = ShowcaseColony.Build(_content, config);
                store.Save(SaveGame.Capture(built, DateTime.UtcNow));
                built.Dispose();
                File.WriteAllText(LayoutPath, JsonUtility.ToJson(ShowcaseColony.Last, true));
                foreach (var w in ShowcaseColony.Warnings) Debug.LogWarning("Showcase: " + w);
                File.WriteAllText(Path.Combine(dir, "warnings.txt"), string.Join("\n", ShowcaseColony.Warnings));
                Debug.Log($"Capture: {(DemoPass ? "demo " : "")}showcase colony built in {(DateTime.Now - t0).TotalSeconds:0.0} s.");
            }
            BeginSession(store.Load(_content, config, out _));
            _freshColony = false;
            _show = JsonUtility.FromJson<ShowcaseColony.Layout>(File.ReadAllText(LayoutPath));
        }

        string ShowcaseDirectory => DemoPass ? _captureRequest.DemoShowcaseDirectory : _captureRequest.ShowcaseDirectory;
        string LayoutPath => Path.Combine(ShowcaseDirectory, "layout.json");
        /// <summary>Showcase folders already rebuilt in this run (a rebuild request rebuilds each once).</summary>
        readonly HashSet<string> _rebuiltShowcases = new();

        /// <summary>Where things are in the showcase colony (loaded with it).</summary>
        ShowcaseColony.Layout _show;

        /// <summary>A brand-new colony on <paramref name="seed"/> (the start of the game).</summary>
        void FreshColony(uint seed)
        {
            StartNewColony(seed, announce: false);
        }

        float2 Queen => (float2)_sim.Colony.QueenCell + 0.5f;
    }
}
