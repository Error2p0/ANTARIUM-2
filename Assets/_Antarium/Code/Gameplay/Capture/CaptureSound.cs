using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Antarium.Presentation;
using UnityEngine;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The sound of captured clips. The capture runs the game's audio muted (time runs at a fixed rate, so live sound
    /// would drift); instead every sound the <see cref="AudioDirector"/> starts while a clip records is logged with its
    /// frame to <c>clips/&lt;name&gt;/sounds.tsv</c> (frame, kind, clip, volume, pitch, pan; kind is <c>sfx</c> or
    /// <c>bed</c> for the ambience loop), and each clip's samples are written once to <c>clips/_sounds/&lt;clip&gt;.wav</c>.
    /// <c>tools/media/make_shorts.py</c> mixes them back in sync with the frames.
    /// </summary>
    public sealed class CaptureSound : IDisposable
    {
        readonly AudioDirector _audio;
        readonly CaptureRig _rig;
        readonly string _soundDirectory;
        readonly HashSet<string> _written = new();
        readonly StringBuilder _log = new();
        AudioClip _bed;
        float _bedVolume;

        public CaptureSound(AudioDirector audio, CaptureRig rig, string outputDirectory)
        {
            _audio = audio;
            _rig = rig;
            _soundDirectory = Path.Combine(outputDirectory, "clips", "_sounds");
            _audio.Started += OnStarted;
            _audio.AmbienceChanged += OnAmbience;
            _rig.ClipStarted += OnClipStarted;
            _rig.ClipEnded += OnClipEnded;
        }

        void OnClipStarted(string directory)
        {
            _log.Clear();
            if (_bed != null) Add("bed", _bed, _bedVolume, 1f, 0f);
        }

        void OnClipEnded(string directory)
        {
            File.WriteAllText(Path.Combine(directory, "sounds.tsv"), _log.ToString());
            _log.Clear();
        }

        void OnStarted(AudioClip clip, float volume, float pitch, float pan)
        {
            if (_rig.Recording) Add("sfx", clip, volume, pitch, pan);
        }

        void OnAmbience(AudioClip clip, float volume)
        {
            _bed = clip;
            _bedVolume = volume;
            if (_rig.Recording && clip != null) Add("bed", clip, volume, 1f, 0f);
        }

        void Add(string kind, AudioClip clip, float volume, float pitch, float pan)
        {
            if (clip == null || !Export(clip)) return;
            _log.Append(_rig.FrameIndex).Append('\t').Append(kind).Append('\t').Append(clip.name).Append('\t')
                .Append(volume.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                .Append(pitch.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                .Append(pan.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
        }

        /// <summary>Writes the clip's samples as 16-bit WAV once; false if they can't be read (a streamed asset).</summary>
        bool Export(AudioClip clip)
        {
            if (_written.Contains(clip.name)) return true;
            var samples = new float[clip.samples * clip.channels];
            if (samples.Length == 0 || !clip.GetData(samples, 0)) return false;
            Directory.CreateDirectory(_soundDirectory);
            WriteWav(Path.Combine(_soundDirectory, clip.name + ".wav"), samples, clip.channels, clip.frequency);
            _written.Add(clip.name);
            return true;
        }

        public static void WriteWav(string path, float[] samples, int channels, int sampleRate)
        {
            using var w = new BinaryWriter(File.Create(path));
            int bytes = samples.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + bytes);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(bytes);
            foreach (var s in samples) w.Write((short)Math.Clamp((int)Math.Round(s * 32767f), -32768, 32767));
        }

        public void Dispose()
        {
            _audio.Started -= OnStarted;
            _audio.AmbienceChanged -= OnAmbience;
            _rig.ClipStarted -= OnClipStarted;
            _rig.ClipEnded -= OnClipEnded;
        }
    }
}
