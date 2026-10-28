using Antarium.Content;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Generative music (<see cref="MusicSettings"/>): an open-chord pad that changes every two bars and sparse
    /// plucked notes on a pentatonic scale, scheduled on the DSP clock. It plays in waves with quiet rests between,
    /// and its key drops (and turns minor) the deeper the camera is.
    /// </summary>
    public sealed class MusicPlayer
    {
        static readonly int[] Major = { 0, 2, 4, 7, 9 };
        static readonly int[] Minor = { 0, 3, 5, 7, 10 };
        static readonly int[] MajorChords = { 0, -3, 5, 7 };
        static readonly int[] MinorChords = { 0, -4, 3, -2 };
        const double LookAhead = 0.25;
        const int ReferenceNote = 57; // the pluck and pad clips are rendered at A3

        readonly MusicSettings _settings;
        readonly AudioClip _pluck, _pad;
        readonly AudioSource[] _plucks = new AudioSource[6];
        readonly AudioSource[] _pads = new AudioSource[2];
        Unity.Mathematics.Random _rng = new(0x5EEDu);
        double _nextBeat;
        int _beat, _nextPluck, _nextPad, _melody = 5;
        float _phaseLeft;
        bool _playing;
        float _volume = 1f;
        int _biome;

        /// <param name="pluck">Rendered from <see cref="PluckPatch"/>.</param>
        /// <param name="pad">Rendered from <see cref="PadPatch"/>.</param>
        public MusicPlayer(MusicSettings settings, AudioClip pluck, AudioClip pad, Transform parent)
        {
            _settings = settings;
            _pluck = pluck;
            _pad = pad;
            for (int i = 0; i < _plucks.Length; i++) _plucks[i] = Source(parent, "MusicPluck");
            for (int i = 0; i < _pads.Length; i++) _pads[i] = Source(parent, "MusicPad");
            _phaseLeft = settings.firstDelay;
        }

        /// <summary>The pluck voice at the reference note (rendered with <see cref="Synth.Render"/>).</summary>
        public static SynthPatch PluckPatch(MusicSettings settings)
        {
            var pluck = settings.pluck;
            pluck.frequency = MidiToHz(ReferenceNote);
            return pluck;
        }

        /// <summary>The pad an octave below the reference note, long enough for two bars (rendered with <see cref="Synth.RenderPad"/>).</summary>
        public static SynthPatch PadPatch(MusicSettings settings)
        {
            var pad = settings.pad;
            pad.frequency = MidiToHz(ReferenceNote - 12);
            pad.duration = math.max(pad.duration, BeatSeconds(settings) * 8f + 2f);
            return pad;
        }

        static float BeatSeconds(MusicSettings settings) => 60f / math.max(20f, settings.bpm);

        static float MidiToHz(int note) => 440f * math.pow(2f, (note - 69) / 12f);

        static AudioSource Source(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        /// <param name="volume">Music bus volume (master included).</param>
        /// <param name="biome">Biome index at the camera (0 = topsoil / surface).</param>
        public void Update(float dt, float volume, int biome)
        {
            _volume = volume;
            _biome = math.max(0, biome);
            foreach (var s in _plucks) s.volume = volume * _settings.pluckVolume;
            foreach (var s in _pads) s.volume = volume * _settings.padVolume;

            _phaseLeft -= dt;
            if (_phaseLeft <= 0f)
            {
                _playing = !_playing && volume > 0f;
                var range = _playing ? _settings.playSeconds : _settings.restSeconds;
                _phaseLeft = _rng.NextFloat(math.min(range.x, range.y), math.max(range.x, range.y) + 0.01f);
                if (_playing)
                {
                    _beat = 0;
                    _nextBeat = AudioSettings.dspTime + 0.1;
                }
            }
            if (!_playing || volume <= 0f) return;

            double now = AudioSettings.dspTime;
            if (_nextBeat < now) _nextBeat = now + 0.05; // after a hitch or a pause
            while (_nextBeat < now + LookAhead)
            {
                ScheduleBeat(_nextBeat);
                _nextBeat += BeatSeconds(_settings);
                _beat++;
            }
        }

        void ScheduleBeat(double at)
        {
            bool minor = _biome >= _settings.minorFromBiome;
            int root = _settings.rootNote - _biome * _settings.dropPerBiome;
            var chords = minor ? MinorChords : MajorChords;
            int chord = chords[(_beat / 8) % chords.Length];
            if (_beat % 8 == 0)
            {
                var pad = _pads[_nextPad];
                _nextPad = (_nextPad + 1) % _pads.Length;
                pad.clip = _pad;
                pad.pitch = math.pow(2f, (root + chord - ReferenceNote) / 12f);
                pad.PlayScheduled(at);
            }

            // Melody: a random walk over two octaves of the scale, more likely on the downbeat.
            float chance = _settings.noteChance * (_beat % 2 == 0 ? 1.2f : 0.6f);
            if (_rng.NextFloat() >= chance) return;
            var scale = minor ? Minor : Major;
            _melody = math.clamp(_melody + _rng.NextInt(-2, 3), 0, scale.Length * 2 - 1);
            int note = root + 12 + scale[_melody % scale.Length] + 12 * (_melody / scale.Length);
            var src = _plucks[_nextPluck];
            _nextPluck = (_nextPluck + 1) % _plucks.Length;
            src.clip = _pluck;
            src.pitch = math.pow(2f, (note - ReferenceNote) / 12f);
            src.PlayScheduled(at + _rng.NextFloat(0f, 0.02f));
        }
    }
}
