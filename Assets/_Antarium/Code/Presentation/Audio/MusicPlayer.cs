using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>What the game is doing, as far as the music cares.</summary>
    public enum MusicMode : byte
    {
        /// <summary>Normal play: biome music in waves with rests between.</summary>
        Play,
        Title,
        Raid,
        /// <summary>The genetic tree is open.</summary>
        Genetics,
        /// <summary>The Nuptial Flight: the flight cue plays, everything else fades out.</summary>
        Flight,
    }

    /// <summary>
    /// Background music (<see cref="MusicSettings"/>). Recorded tracks are used wherever they are assigned: biome
    /// tracks in waves with rests between (a new track is picked by the biome under the camera when the last one ends),
    /// looping sets for the title, raids and the genetic tree (crossfaded in and out), one-shot cues. Everywhere else
    /// the generative music plays: an open-chord pad that changes every two bars and sparse plucked notes on a
    /// pentatonic scale, scheduled on the DSP clock, in waves, its key dropping (and turning minor) with depth.
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
        int _biome;

        // Recorded tracks
        /// <summary>Per biome + 1 (0 = surface): the clips assigned to it (may be empty).</summary>
        readonly List<AudioClip>[] _biomeSets;
        readonly AudioSource[] _tracks = new AudioSource[2];
        readonly float[] _fade = new float[2];
        readonly float[] _fadeTarget = new float[2];
        readonly AudioSource _cue;
        int _active;
        /// <summary>The looping set now playing (title, raid, genetics), or null while biome tracks / nothing play.</summary>
        List<AudioClip> _loopSet;
        AudioClip _last;
        float _rest;
        /// <summary>DSP time when the active biome track ends (not asked from the source: a paused or stalled source reports not playing).</summary>
        double _endsAt;
        /// <summary>0..1: how loud the generative music is (it fades out under recorded tracks).</summary>
        float _generative = 1f;

        /// <param name="pluck">Rendered from <see cref="PluckPatch"/>.</param>
        /// <param name="pad">Rendered from <see cref="PadPatch"/>.</param>
        /// <param name="biomeIds">Ids of the biomes top to bottom (to find each biome's tracks).</param>
        public MusicPlayer(MusicSettings settings, AudioClip pluck, AudioClip pad, IReadOnlyList<string> biomeIds, Transform parent)
        {
            _settings = settings;
            _pluck = pluck;
            _pad = pad;
            for (int i = 0; i < _plucks.Length; i++) _plucks[i] = Source(parent, "MusicPluck");
            for (int i = 0; i < _pads.Length; i++) _pads[i] = Source(parent, "MusicPad");
            for (int i = 0; i < _tracks.Length; i++) _tracks[i] = Source(parent, "MusicTrack");
            _cue = Source(parent, "MusicCue");
            _phaseLeft = settings.firstDelay;
            _rest = settings.firstDelay;
            _biomeSets = BiomeSets(settings, biomeIds);
        }

        /// <summary>The clips per biome + 1 (0 = surface, from entries without a biome).</summary>
        public static List<AudioClip>[] BiomeSets(MusicSettings settings, IReadOnlyList<string> biomeIds)
        {
            var sets = new List<AudioClip>[biomeIds.Count + 1];
            for (int i = 0; i < sets.Length; i++) sets[i] = new List<AudioClip>();
            foreach (var entry in settings.biomeTracks)
            {
                if (entry == null) continue;
                int slot = 0;
                if (entry.biome != null)
                {
                    slot = -1;
                    for (int b = 0; b < biomeIds.Count; b++)
                        if (biomeIds[b] == entry.biome.Id) slot = b + 1;
                    if (slot < 0) continue;
                }
                foreach (var clip in entry.tracks)
                    if (clip != null) sets[slot].Add(clip);
            }
            return sets;
        }

        /// <summary>
        /// The set to play for <paramref name="slot"/> (biome + 1, 0 = surface): its own tracks, else the nearest set
        /// above it, else the nearest below. -1 if no biome has tracks.
        /// </summary>
        public static int PickSet(IReadOnlyList<List<AudioClip>> sets, int slot)
        {
            slot = math.clamp(slot, 0, sets.Count - 1);
            for (int s = slot; s >= 0; s--)
                if (sets[s].Count > 0) return s;
            for (int s = slot + 1; s < sets.Count; s++)
                if (sets[s].Count > 0) return s;
            return -1;
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

        /// <summary>Plays a one-shot cue over the music (flight, a new biome reached).</summary>
        public void PlayCue(AudioClip clip, float volume)
        {
            if (clip == null || volume <= 0f) return;
            _cue.PlayOneShot(clip, volume * _settings.cueVolume);
        }

        /// <param name="volume">Music bus volume (master included).</param>
        /// <param name="biome">Biome index at the camera, -1 above ground.</param>
        public void Update(float dt, float volume, int biome, MusicMode mode)
        {
            _biome = math.max(0, biome);
            bool recorded = UpdateTracks(dt, volume, biome, mode);

            // The generative music only plays where no recorded track does (and never during the flight).
            float target = recorded || mode == MusicMode.Flight ? 0f : 1f;
            _generative = Approach(_generative, target, dt / math.max(0.1f, _settings.crossfadeSeconds));
            foreach (var s in _plucks) s.volume = volume * _settings.pluckVolume * _generative;
            foreach (var s in _pads) s.volume = volume * _settings.padVolume * _generative;
            UpdateGenerative(dt, volume, schedule: target > 0f);
        }

        /// <summary>Recorded tracks; true while one plays (or a biome set exists and is resting between tracks).</summary>
        bool UpdateTracks(float dt, float volume, int biome, MusicMode mode)
        {
            float step = dt / math.max(0.1f, _settings.crossfadeSeconds);
            var loop = mode switch
            {
                MusicMode.Title => _settings.titleTracks,
                MusicMode.Raid => _settings.raidTracks,
                MusicMode.Genetics => _settings.geneticsTracks,
                _ => null,
            };
            if (loop != null && !HasClips(loop)) loop = null;
            // Without tracks of its own, the title / raid / genetics fall back to the biome tracks.
            int set = mode == MusicMode.Flight ? -1 : PickSet(_biomeSets, biome + 1);
            bool recorded = loop != null || set >= 0;

            if (mode == MusicMode.Flight) FadeOutAll();
            else if (loop != null)
            {
                if (_loopSet != loop) Start(Pick(loop), true);
                _loopSet = loop;
            }
            else
            {
                if (_loopSet != null)
                {
                    // Back from a looping set: let it fade, then a short rest before the biome music.
                    FadeOutAll();
                    _loopSet = null;
                    _rest = math.min(_rest, _settings.crossfadeSeconds * 2f);
                }
                if (set >= 0)
                {
                    bool playing = _fadeTarget[_active] > 0f;
                    if (playing && AudioSettings.dspTime >= _endsAt - _settings.crossfadeSeconds)
                    {
                        _fadeTarget[_active] = 0f;
                        var range = _settings.restSeconds;
                        _rest = _rng.NextFloat(math.min(range.x, range.y), math.max(range.x, range.y) + 0.01f);
                    }
                    else if (!playing)
                    {
                        _rest -= dt;
                        if (_rest <= 0f && volume > 0f) Start(Pick(_biomeSets[set]), false);
                    }
                }
                else FadeOutAll();
            }

            for (int i = 0; i < _tracks.Length; i++)
            {
                _fade[i] = Approach(_fade[i], _fadeTarget[i], step);
                _tracks[i].volume = volume * _settings.trackVolume * _fade[i];
                if (_fade[i] <= 0f && _fadeTarget[i] <= 0f && _tracks[i].isPlaying) _tracks[i].Stop();
            }
            return recorded;
        }

        static bool HasClips(List<AudioClip> clips)
        {
            foreach (var c in clips)
                if (c != null) return true;
            return false;
        }

        AudioClip Pick(List<AudioClip> clips)
        {
            AudioClip pick = null;
            for (int tries = 0; tries < 4; tries++)
            {
                pick = clips[_rng.NextInt(clips.Count)];
                if (pick != null && (pick != _last || clips.Count == 1)) break;
            }
            return pick;
        }

        /// <summary>Crossfades to <paramref name="clip"/> on the other track source.</summary>
        void Start(AudioClip clip, bool loop)
        {
            if (clip == null) return;
            _fadeTarget[_active] = 0f;
            _active = 1 - _active;
            var s = _tracks[_active];
            s.clip = clip;
            s.loop = loop;
            s.time = 0f;
            s.Play();
            _endsAt = loop ? double.MaxValue : AudioSettings.dspTime + clip.length;
            _fade[_active] = 0f;
            _fadeTarget[_active] = 1f;
            _last = clip;
        }

        void FadeOutAll()
        {
            _fadeTarget[0] = 0f;
            _fadeTarget[1] = 0f;
        }

        static float Approach(float value, float target, float step) =>
            value < target ? math.min(target, value + step) : math.max(target, value - step);

        void UpdateGenerative(float dt, float volume, bool schedule)
        {
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
            if (!_playing || !schedule || volume <= 0f) return;

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
