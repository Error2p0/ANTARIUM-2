using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    /// <summary>Every sound the game plays. The audio director maps simulation events and UI actions to these.</summary>
    public enum SoundCue : byte
    {
        // digging and the world
        DigHit, DigBreak, AntDig, CaveFound, CaveIn, Sizzle, WallBuilt,
        // colony
        Deliver, EggLaid, Hatch, AntDied, Possess, Release,
        // buildings
        Place, BuildDone, Remove, Craft, Study, PrincessRaised,
        // threats
        EnemyKilled, CreaturesFound, RaidWarning, RaidStart, RaidRepelled, TrapSnap, GateBroken,
        // progression
        Research, ContractClaim, Frenzy, Seismic, Decree, PheromoneBurst, EventStart, EventWon, EventLost, Meteorite,
        GeneticBought, Flight, Achievement,
        // interface
        UiClick, UiHover, UiOpen, UiClose, Refused, Toast,
    }

    public enum SoundBus : byte { Sfx, Ui, Ambience, Music }

    public enum SynthWave : byte { Sine, Triangle, Square, Saw, Noise, Pluck }

    /// <summary>
    /// A procedural sound in the spirit of sfxr: one oscillator (or plucked string) with a pitch sweep, noise,
    /// filters, an attack / decay envelope and optional arpeggio. Rendered once at startup.
    /// </summary>
    [Serializable]
    public struct SynthPatch
    {
        public SynthWave wave;
        [Min(0.01f)] public float duration;
        [Tooltip("Hz at the start. Noise: sample-and-hold rate (0 = white).")]
        public float frequency;
        [Tooltip("Hz at the end (exponential sweep); 0 = no sweep.")]
        public float frequencyEnd;
        [Min(0f)] public float attack;
        [Tooltip("Decay curve after the attack: 1 = linear, higher = shorter, punchier.")]
        [Min(0.1f)] public float decayPower;
        [Range(0f, 1f)] public float noise;
        [Tooltip("Hz, 0 = off.")] [Min(0f)] public float lowpass;
        [Tooltip("Hz, 0 = off.")] [Min(0f)] public float highpass;
        [Range(0f, 1f)] public float harmonics;
        [Min(0f)] public float vibratoHz;
        [Tooltip("Semitones.")] [Min(0f)] public float vibratoDepth;
        [Tooltip("> 1: an arpeggio of this many notes.")]
        [Min(1)] public int notes;
        [Tooltip("Semitones between arpeggio notes.")]
        public float noteStep;
        [Tooltip("Retro sample-and-hold rate in Hz, 0 = off.")]
        [Min(0f)] public float crush;
        [Range(0f, 2f)] public float gain;
    }

    /// <summary>A looping ambience bed: filtered noise, a soft drone, and random drips / bubbles / twinkles.</summary>
    [Serializable]
    public struct AmbiencePatch
    {
        [Range(0f, 1f)] public float noise;
        [Tooltip("Hz of the noise bed's low-pass (wind brightness).")]
        [Min(20f)] public float noiseLowpass;
        [Tooltip("How fast the noise swells (Hz).")]
        [Min(0f)] public float swellHz;
        [Range(0f, 1f)] public float drone;
        [Min(0f)] public float droneHz;
        [Tooltip("Drips / pings per second.")]
        [Min(0f)] public float drips;
        [Min(0f)] public float dripHz;
        [Tooltip("Low bubbles / thumps per second.")]
        [Min(0f)] public float bubbles;
        [Tooltip("High twinkles per second.")]
        [Min(0f)] public float twinkles;
        [Range(0f, 2f)] public float gain;
    }

    [Serializable]
    public sealed class SoundEntry
    {
        public SoundCue cue;
        [Tooltip("Optional recorded clip; empty = use the synth patch.")]
        public AudioClip clip;
        public SynthPatch synth;
        [Tooltip("Synth variants rendered with different noise seeds (picked at random).")]
        [Range(1, 6)] public int variants = 2;
        public SoundBus bus;
        [Range(0f, 1f)] public float volume = 0.6f;
        public Vector2 pitch = new(0.95f, 1.05f);
        [Tooltip("Seconds before the cue may play again.")]
        [Min(0f)] public float cooldown = 0.05f;
        [Min(1)] public int maxVoices = 3;
        [Tooltip("Placed in the world: panned, quieter at the edge of the view, silent outside it.")]
        public bool positional = true;
    }

    [Serializable]
    public sealed class AmbienceEntry
    {
        [Tooltip("Empty = the surface (sky) ambience.")]
        public BiomeDef biome;
        public AudioClip clip;
        public AmbiencePatch synth;
    }

    /// <summary>Generative background music: a soft pad and sparse plucked notes on a pentatonic scale, in waves with rests between.</summary>
    [Serializable]
    public sealed class MusicSettings
    {
        [Min(20f)] public float bpm = 72f;
        [Tooltip("MIDI note of the key's root near the surface; the key drops with depth.")]
        [Range(36, 72)] public int rootNote = 57;
        [Tooltip("Semitones the key drops per biome below the first.")]
        [Range(0, 5)] public int dropPerBiome = 1;
        [Tooltip("Biomes from here down use the minor pentatonic scale.")]
        [Min(0)] public int minorFromBiome = 3;
        [Range(0f, 1f)] public float noteChance = 0.45f;
        [Range(0f, 1f)] public float padVolume = 0.35f;
        [Range(0f, 1f)] public float pluckVolume = 0.5f;
        public Vector2 playSeconds = new(150f, 240f);
        public Vector2 restSeconds = new(60f, 120f);
        [Min(0f)] public float firstDelay = 8f;
        public SynthPatch pluck;
        public SynthPatch pad;
    }

    /// <summary>All sounds, ambience beds and the music settings. Created by "Antarium/Setup/Create Default Content".</summary>
    [CreateAssetMenu(menuName = "Antarium/Sound Bank", fileName = "SoundBank")]
    public sealed class SoundBank : ScriptableObject
    {
        [Min(8000)] public int sampleRate = 44100;
        [Tooltip("Seconds of every ambience loop.")]
        [Min(2f)] public float ambienceSeconds = 12f;
        [Tooltip("Seconds to crossfade between ambience beds.")]
        [Min(0.1f)] public float ambienceFade = 2.5f;
        [Tooltip("Positional sounds: volume at the edge of the view (1 at the centre).")]
        [Range(0f, 1f)] public float edgeVolume = 0.45f;
        [Tooltip("Positional sounds: how far left / right they pan at the edge of the view.")]
        [Range(0f, 1f)] public float stereoWidth = 0.6f;
        [Tooltip("Positional sounds are this much quieter per zoom step out from the closest zoom.")]
        [Range(0f, 0.5f)] public float zoomFalloff = 0.08f;
        [Min(4)] public int voices = 32;
        public List<SoundEntry> sounds = new();
        public List<AmbienceEntry> ambience = new();
        public MusicSettings music = new();

        public SoundEntry Find(SoundCue cue)
        {
            foreach (var s in sounds)
                if (s != null && s.cue == cue) return s;
            return null;
        }
    }
}
