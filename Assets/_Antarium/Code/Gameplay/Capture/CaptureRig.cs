using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Renders the game offscreen at a fixed size for store media (screenshots, GIF footage, trailer clips):
    /// the world camera and the UI panel each draw into a render texture, and every captured frame is read back,
    /// the UI laid over the world, an optional mouse cursor stamped on, and written as PNG on worker threads.
    /// Independent of the window size, focus and frame rate (the capture sets <see cref="Time.captureFramerate"/>).
    /// </summary>
    public sealed class CaptureRig : IDisposable
    {
        /// <summary>Encoded frames waiting to be written before capturing blocks (keeps memory bounded).</summary>
        const int MaxPending = 12;

        readonly Camera _camera;
        readonly PanelSettings _panel;
        readonly SemaphoreSlim _slots = new(MaxPending, MaxPending);
        readonly List<Task> _writes = new();
        RenderTexture _world, _ui;
        Texture2D _readWorld, _readUi;

        public int Width { get; private set; }
        public int Height { get; private set; }
        /// <summary>Frames go to <see cref="ClipDirectory"/>/<c>NNNNN.png</c> while set.</summary>
        public bool Recording { get; private set; }
        public string ClipDirectory { get; private set; }
        public int FrameIndex { get; private set; }
        /// <summary>Mouse cursor to draw, in image pixels from the bottom left (null = none).</summary>
        public float2? Cursor;
        /// <summary>Screen pixels per cursor pixel.</summary>
        public int CursorScale = 3;
        /// <summary>Show the UI layer.</summary>
        public bool ShowUi = true;

        public CaptureRig(Camera camera, PanelSettings panel)
        {
            _camera = camera;
            _panel = panel;
        }

        /// <summary>Renders world and UI at <paramref name="width"/> × <paramref name="height"/> from now on.</summary>
        public void SetSize(int width, int height)
        {
            if (width == Width && height == Height && _world != null) return;
            ReleaseTargets();
            Width = width;
            Height = height;
            _world = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "CaptureWorld", filterMode = FilterMode.Point };
            _ui = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "CaptureUi", filterMode = FilterMode.Point };
            _world.Create();
            _ui.Create();
            _readWorld = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            _readUi = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            _camera.targetTexture = _world;
            if (_panel != null)
            {
                _panel.targetTexture = _ui;
                _panel.clearColor = true;
                _panel.colorClearValue = Color.clear;
            }
        }

        public void BeginClip(string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (var old in Directory.GetFiles(directory, "*.png")) File.Delete(old);
            ClipDirectory = directory;
            FrameIndex = 0;
            Recording = true;
        }

        public void EndClip() => Recording = false;

        /// <summary>Call at the end of a frame (after rendering): writes the frame if a clip is recording.</summary>
        public void EndOfFrame()
        {
            if (!Recording || _world == null) return;
            WriteFrame(Path.Combine(ClipDirectory, FrameIndex.ToString("00000") + ".png"));
            FrameIndex++;
        }

        /// <summary>Reads back the current frame and writes it to <paramref name="path"/> (PNG).</summary>
        public void WriteFrame(string path)
        {
            var pixels = Compose();
            var w = (uint)Width;
            var h = (uint)Height;
            _slots.Wait();
            lock (_writes)
            {
                _writes.RemoveAll(t => t.IsCompleted);
                _writes.Add(Task.Run(() =>
                {
                    try
                    {
                        var png = ImageConversion.EncodeArrayToPNG(pixels, GraphicsFormat.R8G8B8A8_SRGB, w, h);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, png);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Capture: writing {path} failed: {e.Message}");
                    }
                    finally
                    {
                        _slots.Release();
                    }
                }));
            }
        }

        /// <summary>Waits until every frame is on disk.</summary>
        public void Flush()
        {
            Task[] pending;
            lock (_writes) pending = _writes.ToArray();
            Task.WaitAll(pending);
        }

        /// <summary>World + UI (premultiplied alpha) + cursor, as bottom-up RGBA rows (texture layout) ready for PNG.</summary>
        byte[] Compose()
        {
            Read(_world, _readWorld);
            var world = _readWorld.GetPixelData<Color32>(0);
            var result = new byte[Width * Height * 4];
            bool ui = ShowUi && _panel != null;
            Unity.Collections.NativeArray<Color32> over = default;
            if (ui)
            {
                Read(_ui, _readUi);
                over = _readUi.GetPixelData<Color32>(0);
            }
            for (int y = 0; y < Height; y++)
            {
                int src = y * Width;
                int dst = y * Width * 4; // bottom-up rows, like the texture (the encoder flips them)
                for (int x = 0; x < Width; x++, src++, dst += 4)
                {
                    var c = world[src];
                    if (ui)
                    {
                        var u = over[src];
                        if (u.a != 0)
                        {
                            int inv = 255 - u.a;
                            c.r = (byte)math.min(255, u.r + c.r * inv / 255);
                            c.g = (byte)math.min(255, u.g + c.g * inv / 255);
                            c.b = (byte)math.min(255, u.b + c.b * inv / 255);
                        }
                    }
                    result[dst] = c.r;
                    result[dst + 1] = c.g;
                    result[dst + 2] = c.b;
                    result[dst + 3] = 255;
                }
            }
            if (Cursor.HasValue) StampCursor(result, Cursor.Value);
            return result;
        }

        static void Read(RenderTexture rt, Texture2D into)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            into.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            into.Apply(false);
            RenderTexture.active = previous;
        }

        // A classic arrow pointer, 11 × 17: '#' outline, '.' fill.
        static readonly string[] CursorArt =
        {
            "#",
            "##",
            "#.#",
            "#..#",
            "#...#",
            "#....#",
            "#.....#",
            "#......#",
            "#.......#",
            "#........#",
            "#.....#####",
            "#..#..#",
            "#.##..#",
            "##  #..#",
            "#   #..#",
            "     #..#",
            "     ###",
        };

        void StampCursor(byte[] rgba, float2 at)
        {
            int s = math.max(1, CursorScale);
            int left = (int)math.round(at.x);
            int top = (int)math.round(at.y); // tip of the arrow; rows run bottom-up, so the arrow is drawn downward
            for (int row = 0; row < CursorArt.Length; row++)
            {
                string line = CursorArt[row];
                for (int col = 0; col < line.Length; col++)
                {
                    char ch = line[col];
                    if (ch != '#' && ch != '.') continue;
                    byte v = ch == '#' ? (byte)20 : (byte)250;
                    for (int dy = 0; dy < s; dy++)
                    for (int dx = 0; dx < s; dx++)
                    {
                        int x = left + col * s + dx, y = top - row * s - dy;
                        if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                        int i = (y * Width + x) * 4;
                        rgba[i] = rgba[i + 1] = rgba[i + 2] = v;
                    }
                }
            }
        }

        void ReleaseTargets()
        {
            if (_camera != null && _camera.targetTexture == _world) _camera.targetTexture = null;
            if (_panel != null && _panel.targetTexture == _ui) _panel.targetTexture = null;
            if (_world != null) _world.Release();
            if (_ui != null) _ui.Release();
            UnityEngine.Object.Destroy(_world);
            UnityEngine.Object.Destroy(_ui);
            UnityEngine.Object.Destroy(_readWorld);
            UnityEngine.Object.Destroy(_readUi);
            _world = _ui = null;
            _readWorld = _readUi = null;
        }

        public void Dispose()
        {
            Flush();
            ReleaseTargets();
        }
    }
}
