using System;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Antarium.Presentation
{
    /// <summary>
    /// Plays the game's sounds. Turns this frame's simulation events into cues (busy ones are merged: one sound per
    /// cue and frame, a little louder when many happened), places world sounds by the camera view (panned, quieter at
    /// the edges, silent outside), keeps an ambience bed for the biome under the camera and runs the music.
    /// Clips come from the <see cref="SoundBank"/>: recorded ones if assigned, else synthesized on a worker thread at
    /// startup (about 1.5 s of work); sounds start once that is done.
    /// </summary>
    public sealed class AudioDirector : IDisposable
    {
        static readonly int CueCount = Enum.GetValues(typeof(SoundCue)).Length;

        readonly SoundBank _bank;
        readonly ContentDB _content;
        readonly GameObject _root;
        readonly SoundEntry[] _entries;
        readonly AudioClip[][] _clips;
        readonly int _sampleRate;
        /// <summary>Synthesized samples being rendered on a worker thread; null once the clips exist.</summary>
        System.Threading.Tasks.Task<Rendered> _rendering;
        readonly VoiceLimiter _limiter;
        readonly AudioSource[] _voices;
        readonly AudioSource[] _ambience = new AudioSource[2];
        /// <summary>Per biome (index + 1; 0 = surface): ambience loop or null.</summary>
        readonly AudioClip[] _ambienceClips;
        MusicPlayer _music;
        readonly System.Collections.Generic.List<AudioClip> _owned = new();
        Unity.Mathematics.Random _rng = new(0xA0D10u);
        int _nextVoice;

        // per-frame merging
        readonly int[] _frameCount;
        readonly float2[] _framePos;
        readonly float[] _frameDist;

        float _master = 1f, _music01 = 1f, _sfx = 1f, _ambience01 = 1f, _ui = 1f;
        float2 _viewCenter, _viewHalf = new(1f, 1f);
        float _zoomVolume = 1f;
        int _ambienceSlot, _ambienceKey = int.MinValue;
        float _fade = 1f;

        public AudioDirector(SoundBank bank, ContentDB content, Transform parent)
        {
            _bank = bank;
            _content = content;
            _root = new GameObject("Audio");
            _root.transform.SetParent(parent, false);
            if (Object.FindAnyObjectByType<AudioListener>() == null) _root.AddComponent<AudioListener>();

            _entries = new SoundEntry[CueCount];
            _clips = new AudioClip[CueCount][];
            var cooldowns = new float[CueCount];
            var maxVoices = new int[CueCount];
            _sampleRate = math.max(8000, bank.sampleRate);
            var job = new Job { SampleRate = _sampleRate, AmbienceSeconds = bank.ambienceSeconds, Cues = new SynthPatch[CueCount], Variants = new int[CueCount] };
            for (int c = 0; c < CueCount; c++)
            {
                var e = bank.Find((SoundCue)c);
                _entries[c] = e;
                cooldowns[c] = e?.cooldown ?? 0f;
                maxVoices[c] = e?.maxVoices ?? 1;
                if (e == null) continue;
                if (e.clip != null) _clips[c] = new[] { e.clip };
                else
                {
                    job.Cues[c] = e.synth;
                    job.Variants[c] = math.clamp(e.variants, 1, 6);
                }
            }
            _limiter = new VoiceLimiter(cooldowns, maxVoices);
            _frameCount = new int[CueCount];
            _framePos = new float2[CueCount];
            _frameDist = new float[CueCount];

            _voices = new AudioSource[math.max(4, bank.voices)];
            for (int i = 0; i < _voices.Length; i++) _voices[i] = Source("Voice", loop: false);
            for (int i = 0; i < _ambience.Length; i++) _ambience[i] = Source("Ambience", loop: true);

            _ambienceClips = new AudioClip[content.Biomes.Length + 1];
            job.Ambience = new AmbiencePatch?[_ambienceClips.Length];
            foreach (var a in bank.ambience)
            {
                if (a == null) continue;
                int slot = 0;
                if (a.biome != null)
                {
                    slot = -1;
                    for (int b = 0; b < content.Biomes.Length; b++)
                        if (content.Biomes[b].Def != null && content.Biomes[b].Def.Id == a.biome.Id) slot = b + 1;
                    if (slot < 0) continue;
                }
                if (a.clip != null) _ambienceClips[slot] = a.clip;
                else job.Ambience[slot] = a.synth;
            }
            job.Pluck = MusicPlayer.PluckPatch(bank.music);
            job.Pad = MusicPlayer.PadPatch(bank.music);
            _rendering = System.Threading.Tasks.Task.Run(() => job.Render());
        }

        /// <summary>Everything that has to be synthesized, rendered on a worker thread (Synth is plain C#).</summary>
        sealed class Job
        {
            public int SampleRate;
            public float AmbienceSeconds;
            public SynthPatch[] Cues;
            public int[] Variants;
            public AmbiencePatch?[] Ambience;
            public SynthPatch Pluck, Pad;

            public Rendered Render()
            {
                var r = new Rendered { Cues = new float[Cues.Length][][], Ambience = new float[Ambience.Length][] };
                for (int c = 0; c < Cues.Length; c++)
                {
                    if (Variants[c] == 0) continue;
                    r.Cues[c] = new float[Variants[c]][];
                    for (int v = 0; v < Variants[c]; v++)
                        r.Cues[c][v] = Synth.Render(Cues[c], SampleRate, (uint)(c * 7919 + v * 104729 + 1));
                }
                for (int slot = 0; slot < Ambience.Length; slot++)
                    if (Ambience[slot].HasValue)
                        r.Ambience[slot] = Synth.RenderAmbience(Ambience[slot].Value, SampleRate, AmbienceSeconds, (uint)(slot * 31 + 5));
                r.Pluck = Synth.Render(Pluck, SampleRate, 7u);
                r.Pad = Synth.RenderPad(Pad, SampleRate, 11u);
                return r;
            }
        }

        sealed class Rendered
        {
            public float[][][] Cues;
            public float[][] Ambience;
            public float[] Pluck, Pad;
        }

        /// <summary>True once every clip exists.</summary>
        public bool Ready => _rendering == null;

        /// <summary>Turns the rendered samples into clips (main thread) once the worker is done.</summary>
        void FinishRendering()
        {
            if (_rendering == null || !_rendering.IsCompleted) return;
            var task = _rendering;
            _rendering = null;
            if (task.IsFaulted)
            {
                Debug.LogError($"Synthesizing the sounds failed: {task.Exception}");
                return;
            }
            var r = task.Result;
            for (int c = 0; c < CueCount; c++)
            {
                if (r.Cues[c] == null) continue;
                _clips[c] = new AudioClip[r.Cues[c].Length];
                for (int v = 0; v < r.Cues[c].Length; v++)
                    _clips[c][v] = Own(Synth.ToClip($"{(SoundCue)c}_{v}", r.Cues[c][v], _sampleRate));
            }
            for (int slot = 0; slot < r.Ambience.Length; slot++)
                if (r.Ambience[slot] != null) _ambienceClips[slot] = Own(Synth.ToClip($"Ambience_{slot}", r.Ambience[slot], _sampleRate));
            _music = new MusicPlayer(_bank.music, Own(Synth.ToClip("MusicPluck", r.Pluck, _sampleRate)), Own(Synth.ToClip("MusicPad", r.Pad, _sampleRate)), _root.transform);
            _ambienceKey = int.MinValue; // start the bed for the current biome
        }

        AudioClip Own(AudioClip clip)
        {
            _owned.Add(clip);
            return clip;
        }

        AudioSource Source(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            return s;
        }

        /// <summary>Bus volumes, 0..1 each (settings).</summary>
        public void SetVolumes(float master, float music, float sfx, float ambience, float ui)
        {
            _master = master;
            _music01 = music;
            _sfx = sfx;
            _ambience01 = ambience;
            _ui = ui;
        }

        /// <param name="center">World position at the centre of the view.</param>
        /// <param name="halfSize">Half the view's size in world units.</param>
        /// <param name="zoomStepsOut">Zoom steps out from the closest zoom (far views are quieter).</param>
        public void SetView(float2 center, float2 halfSize, int zoomStepsOut)
        {
            _viewCenter = center;
            _viewHalf = math.max(halfSize, new float2(0.5f, 0.5f));
            _zoomVolume = math.max(0.2f, 1f - _bank.zoomFalloff * math.max(0, zoomStepsOut));
        }

        float Bus(SoundBus bus) => _master * bus switch
        {
            SoundBus.Ui => _ui,
            SoundBus.Ambience => _ambience01,
            SoundBus.Music => _music01,
            _ => _sfx,
        };

        /// <summary>Plays a cue that is not placed in the world (interface, announcements).</summary>
        public void Play(SoundCue cue, float volume = 1f)
        {
            if (_entries[(int)cue] == null) return;
            Start((int)cue, volume, 0f);
        }

        /// <summary>Plays a cue at a world position (only if it is in view).</summary>
        public void PlayAt(SoundCue cue, float2 worldPos, float volume = 1f)
        {
            var e = _entries[(int)cue];
            if (e == null) return;
            if (!e.positional)
            {
                Start((int)cue, volume, 0f);
                return;
            }
            if (!Place(worldPos, out float place, out float pan)) return;
            Start((int)cue, volume * place, pan);
        }

        bool Place(float2 worldPos, out float volume, out float pan)
        {
            float2 d = (worldPos - _viewCenter) / _viewHalf;
            volume = pan = 0f;
            if (math.any(math.abs(d) > 1.1f)) return false;
            volume = math.lerp(1f, _bank.edgeVolume, math.saturate(math.length(d) / 1.41f)) * _zoomVolume;
            pan = math.clamp(d.x, -1f, 1f) * _bank.stereoWidth;
            return true;
        }

        void Start(int cue, float volume, float pan)
        {
            var e = _entries[cue];
            var clips = _clips[cue];
            if (e == null || clips == null || clips.Length == 0) return;
            float v = e.volume * volume * Bus(e.bus);
            if (v <= 0.001f) return;
            var clip = clips[_rng.NextInt(clips.Length)];
            float pitch = _rng.NextFloat(math.min(e.pitch.x, e.pitch.y), math.max(e.pitch.x, e.pitch.y) + 1e-4f);
            if (!_limiter.TryStart(cue, Time.unscaledTimeAsDouble, clip.length / math.max(0.1f, pitch))) return;

            // Take a free voice, else the oldest one in turn.
            AudioSource source = null;
            for (int i = 0; i < _voices.Length && source == null; i++)
            {
                var s = _voices[(_nextVoice + i) % _voices.Length];
                if (!s.isPlaying) source = s;
            }
            source ??= _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            source.clip = clip;
            source.volume = math.min(1f, v);
            source.pitch = pitch;
            source.panStereo = pan;
            source.Play();
        }

        // ---------------------------------------------------------------- simulation events

        void Queue(SoundCue cue, int2 cell) => Queue(cue, WorldGrid.CellCenterToWorld(cell));

        void Queue(SoundCue cue, float2 worldPos)
        {
            int c = (int)cue;
            var e = _entries[c];
            if (e == null) return;
            float dist = e.positional ? math.lengthsq((worldPos - _viewCenter) / _viewHalf) : 0f;
            if (e.positional && dist > 1.21f * 2f) return;
            if (_frameCount[c] == 0 || dist < _frameDist[c])
            {
                _framePos[c] = worldPos;
                _frameDist[c] = dist;
            }
            _frameCount[c]++;
        }

        void QueueUi(SoundCue cue)
        {
            if (_entries[(int)cue] == null) return;
            _frameCount[(int)cue]++;
            _frameDist[(int)cue] = 0f;
        }

        /// <summary>Turns this frame's events into sounds.</summary>
        public void Consume(EventBus events)
        {
            bool playerDug = false;
            foreach (var e in events.Read<DigResolvedEvent>())
            {
                var pos = e.CellPos * WorldConstants.CellSize;
                if (e.CellsBroken > 0) Queue(SoundCue.DigBreak, pos);
                else if (e.CellsDamaged > 0) Queue(SoundCue.DigHit, pos);
                if (e.CaveCellsDiscovered > 0) Queue(SoundCue.CaveFound, pos);
                playerDug = true;
            }
            if (!playerDug)
                foreach (var e in events.Read<CellDamagedEvent>())
                    if (e.Broken) Queue(SoundCue.AntDig, e.Cell);
            foreach (var e in events.Read<CaveInEvent>()) Queue(SoundCue.CaveIn, e.Cell);
            foreach (var e in events.Read<FluidReactionEvent>()) Queue(SoundCue.Sizzle, e.Cell);
            foreach (var e in events.Read<WallBuiltEvent>()) Queue(SoundCue.WallBuilt, e.Cell);

            foreach (var e in events.Read<ItemDeliveredEvent>()) Queue(SoundCue.Deliver, e.Cell);
            foreach (var e in events.Read<EggLaidEvent>()) Queue(SoundCue.EggLaid, e.Cell);
            foreach (var e in events.Read<AntHatchedEvent>()) Queue(SoundCue.Hatch, e.Cell);
            foreach (var e in events.Read<AntDiedEvent>()) Queue(SoundCue.AntDied, e.Cell);
            foreach (var e in events.Read<PossessionEvent>())
            {
                if (e.Phase == PossessionPhase.Started) QueueUi(SoundCue.Possess);
                else if (e.Phase == PossessionPhase.Missed) QueueUi(SoundCue.Refused);
                else QueueUi(SoundCue.Release);
            }

            foreach (var e in events.Read<BuildingPlacedEvent>()) Queue(SoundCue.Place, e.Origin);
            foreach (var e in events.Read<BuildingCompletedEvent>()) Queue(SoundCue.BuildDone, e.Origin);
            foreach (var e in events.Read<BuildingRemovedEvent>()) Queue(SoundCue.Remove, e.Origin);
            if (events.Read<PlaceFailedEvent>().Length > 0) QueueUi(SoundCue.Refused);
            foreach (var e in events.Read<ItemCraftedEvent>()) Queue(e.Knowledge > 0f && e.Item == ContentDB.NoItem ? SoundCue.Study : SoundCue.Craft, e.Origin);
            foreach (var e in events.Read<PrincessRaisedEvent>()) Queue(SoundCue.PrincessRaised, e.Cell);

            foreach (var e in events.Read<EnemyKilledEvent>()) Queue(SoundCue.EnemyKilled, e.Cell);
            foreach (var e in events.Read<CreaturesFoundEvent>()) QueueUi(SoundCue.CreaturesFound);
            foreach (var e in events.Read<RaidEvent>())
            {
                switch (e.Phase)
                {
                    case RaidPhase.Warning: QueueUi(SoundCue.RaidWarning); break;
                    case RaidPhase.Started: QueueUi(SoundCue.RaidStart); break;
                    case RaidPhase.Repelled: QueueUi(SoundCue.RaidRepelled); break;
                }
            }
            foreach (var e in events.Read<TrapTriggeredEvent>()) Queue(SoundCue.TrapSnap, e.Cell);
            foreach (var e in events.Read<GateBrokenEvent>()) Queue(SoundCue.GateBroken, e.Cell);

            if (events.Read<ResearchCompletedEvent>().Length > 0) QueueUi(SoundCue.Research);
            if (events.Read<ResearchFailedEvent>().Length > 0 || events.Read<AbilityFailedEvent>().Length > 0
                || events.Read<GeneticFailedEvent>().Length > 0) QueueUi(SoundCue.Refused);
            if (events.Read<ContractClaimedEvent>().Length > 0) QueueUi(SoundCue.ContractClaim);
            if (events.Read<GeneticBoughtEvent>().Length > 0) QueueUi(SoundCue.GeneticBought);
            foreach (var e in events.Read<AbilityUsedEvent>())
            {
                var kind = e.Ability >= 0 && e.Ability < _content.Abilities.Length ? _content.Abilities[e.Ability].kind : AbilityKind.Frenzy;
                QueueUi(kind switch
                {
                    AbilityKind.SeismicSense => SoundCue.Seismic,
                    AbilityKind.RoyalDecree => SoundCue.Decree,
                    AbilityKind.PheromoneBurst => SoundCue.PheromoneBurst,
                    _ => SoundCue.Frenzy,
                });
            }
            foreach (var e in events.Read<RandomEventEvent>())
            {
                bool meteor = e.Event >= 0 && e.Event < _content.Events.Length && _content.Events[e.Event].Def.kind == RandomEventKind.Meteorite;
                QueueUi(e.Phase switch
                {
                    RandomEventPhase.Started => meteor ? SoundCue.Meteorite : SoundCue.EventStart,
                    RandomEventPhase.Won => SoundCue.EventWon,
                    _ => SoundCue.EventLost,
                });
            }
            Flush();
        }

        void Flush()
        {
            for (int c = 0; c < CueCount; c++)
            {
                int count = _frameCount[c];
                if (count == 0) continue;
                _frameCount[c] = 0;
                float boost = math.min(1.35f, 1f + 0.12f * math.log2(count));
                var e = _entries[c];
                if (e.positional) PlayAt((SoundCue)c, _framePos[c], boost);
                else Start(c, boost, 0f);
            }
        }

        // ---------------------------------------------------------------- ambience and music

        /// <param name="biome">Biome index under the camera, -1 above the surface.</param>
        /// <param name="muted">Silence everything (the game is in the background and the player asked for that).</param>
        public void Update(float dt, int biome, bool muted)
        {
            AudioListener.volume = muted ? 0f : 1f;
            FinishRendering();
            if (_rendering != null) return;
            int key = math.clamp(biome + 1, 0, _ambienceClips.Length - 1);
            if (_ambienceClips[key] == null) key = 0;
            if (key != _ambienceKey)
            {
                _ambienceKey = key;
                _ambienceSlot = 1 - _ambienceSlot;
                var next = _ambience[_ambienceSlot];
                next.clip = _ambienceClips[key];
                next.volume = 0f;
                if (next.clip != null)
                {
                    next.time = _rng.NextFloat(0f, next.clip.length * 0.9f);
                    next.Play();
                }
                _fade = 0f;
            }
            _fade = math.min(1f, _fade + dt / math.max(0.1f, _bank.ambienceFade));
            float bed = Bus(SoundBus.Ambience);
            _ambience[_ambienceSlot].volume = bed * _fade;
            var old = _ambience[1 - _ambienceSlot];
            old.volume = bed * (1f - _fade);
            if (_fade >= 1f && old.isPlaying) old.Stop();

            _music?.Update(dt, Bus(SoundBus.Music), math.max(0, biome));
        }

        public void Dispose()
        {
            try
            {
                _rendering?.Wait();
            }
            catch (AggregateException)
            {
                // reported (or irrelevant) on shutdown
            }
            if (_root != null) Object.Destroy(_root);
            foreach (var clip in _owned)
                if (clip != null) Object.Destroy(clip);
            _owned.Clear();
        }
    }
}
