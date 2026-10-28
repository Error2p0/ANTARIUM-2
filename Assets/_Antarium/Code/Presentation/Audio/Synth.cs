using System;
using Antarium.Content;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Renders the procedural sounds (<see cref="SynthPatch"/>, <see cref="AmbiencePatch"/>) into sample arrays.
    /// Deterministic for a seed, mono, peak-limited to 0.95. Plain C#, so tests can check it without audio.
    /// </summary>
    public static class Synth
    {
        const float Peak = 0.95f;
        const float TwoPi = math.PI * 2f;

        public static float[] Render(in SynthPatch p, int sampleRate, uint seed)
        {
            int n = math.max(1, (int)(math.max(0.01f, p.duration) * sampleRate));
            var samples = new float[n];
            var rng = new Unity.Mathematics.Random(seed | 1u);
            if (p.wave == SynthWave.Pluck) RenderPluck(p, sampleRate, ref rng, samples);
            else RenderOscillator(p, sampleRate, ref rng, samples);
            Filter(samples, sampleRate, p.lowpass, p.highpass);
            if (p.crush > 0f) Crush(samples, sampleRate, p.crush);
            float gain = p.gain > 0f ? p.gain : 1f;
            for (int i = 0; i < n; i++) samples[i] *= gain;
            FadeEdges(samples, sampleRate, fadeIn: p.attack <= 0f ? 0.001f : 0f, fadeOut: 0.004f);
            Limit(samples);
            return samples;
        }

        static void RenderOscillator(in SynthPatch p, int sampleRate, ref Unity.Mathematics.Random rng, float[] s)
        {
            int n = s.Length;
            int notes = math.max(1, p.notes);
            int noteLength = math.max(1, n / notes);
            float f0 = math.max(1f, p.frequency);
            float f1 = p.frequencyEnd > 0f ? p.frequencyEnd : f0;
            double phase = 0, phase2 = 0, phase3 = 0;
            float held = 0f, holdPhase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                int note = math.min(i / noteLength, notes - 1);
                float noteTime = (float)(i - note * noteLength) / sampleRate;
                float noteDuration = (float)noteLength / sampleRate;
                float freq = f0 * math.pow(f1 / f0, t) * math.pow(2f, (note * p.noteStep + Vibrato(p, i, sampleRate)) / 12f);
                phase += freq / sampleRate;
                phase2 += freq * 2f / sampleRate;
                phase3 += freq * 3f / sampleRate;
                float ph = (float)(phase - math.floor(phase));

                float osc;
                switch (p.wave)
                {
                    case SynthWave.Triangle: osc = 1f - 4f * math.abs(ph - 0.5f); break;
                    case SynthWave.Square: osc = ph < 0.5f ? 0.6f : -0.6f; break;
                    case SynthWave.Saw: osc = (2f * ph - 1f) * 0.7f; break;
                    case SynthWave.Noise:
                        if (p.frequency <= 0f) osc = rng.NextFloat(-1f, 1f);
                        else
                        {
                            holdPhase += freq / sampleRate;
                            if (holdPhase >= 1f || i == 0)
                            {
                                holdPhase -= math.floor(holdPhase);
                                held = rng.NextFloat(-1f, 1f);
                            }
                            osc = held;
                        }
                        break;
                    default: osc = math.sin(ph * TwoPi); break;
                }
                if (p.harmonics > 0f)
                    osc += p.harmonics * (0.5f * math.sin((float)(phase2 - math.floor(phase2)) * TwoPi)
                                          + 0.3f * math.sin((float)(phase3 - math.floor(phase3)) * TwoPi));
                if (p.noise > 0f) osc = math.lerp(osc, rng.NextFloat(-1f, 1f), p.noise);
                s[i] = osc * Envelope(noteTime, noteDuration, p.attack, p.decayPower);
            }
        }

        static float Vibrato(in SynthPatch p, int i, int sampleRate) =>
            p.vibratoHz > 0f && p.vibratoDepth > 0f ? p.vibratoDepth * math.sin(TwoPi * p.vibratoHz * i / sampleRate) : 0f;

        /// <summary>Linear attack, then (1 - x)^power down to silence at the end of the note.</summary>
        static float Envelope(float time, float duration, float attack, float power)
        {
            if (attack > 0f && time < attack) return time / attack;
            float rest = math.max(1e-4f, duration - attack);
            float x = math.saturate((time - attack) / rest);
            return math.pow(1f - x, math.max(0.1f, power));
        }

        /// <summary>Karplus–Strong plucked string (arpeggio notes restart the string).</summary>
        static void RenderPluck(in SynthPatch p, int sampleRate, ref Unity.Mathematics.Random rng, float[] s)
        {
            int n = s.Length;
            int notes = math.max(1, p.notes);
            int noteLength = math.max(1, n / notes);
            float damping = math.lerp(0.5f, 0.4985f, math.saturate(p.decayPower / 8f));
            for (int note = 0; note < notes; note++)
            {
                float freq = math.max(20f, p.frequency) * math.pow(2f, note * p.noteStep / 12f);
                int period = math.max(2, (int)(sampleRate / freq));
                var line = new float[period];
                for (int k = 0; k < period; k++) line[k] = rng.NextFloat(-1f, 1f) * (1f - p.noise * 0.5f);
                int start = note * noteLength, end = note == notes - 1 ? n : start + noteLength;
                int idx = 0;
                for (int i = start; i < end; i++)
                {
                    int next = (idx + 1) % period;
                    float v = line[idx];
                    line[idx] = (line[idx] + line[next]) * damping * 2f * 0.5f + (p.harmonics > 0f ? (line[next] - line[idx]) * 0.05f * p.harmonics : 0f);
                    s[i] += v * Envelope((float)(i - start) / sampleRate, (float)(end - start) / sampleRate, p.attack, 1f + p.decayPower * 0.25f);
                    idx = next;
                }
            }
        }

        static void Filter(float[] s, int sampleRate, float lowpass, float highpass)
        {
            if (lowpass > 0f)
            {
                float a = 1f - math.exp(-TwoPi * lowpass / sampleRate), y = 0f;
                for (int i = 0; i < s.Length; i++) s[i] = y += a * (s[i] - y);
            }
            if (highpass > 0f)
            {
                float a = 1f - math.exp(-TwoPi * highpass / sampleRate), low = 0f;
                for (int i = 0; i < s.Length; i++)
                {
                    low += a * (s[i] - low);
                    s[i] -= low;
                }
            }
        }

        static void Crush(float[] s, int sampleRate, float rate)
        {
            float step = sampleRate / math.max(100f, rate), acc = step;
            float held = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                acc += 1f;
                if (acc >= step)
                {
                    acc -= step;
                    held = math.round(s[i] * 16f) / 16f;
                }
                s[i] = held;
            }
        }

        static void FadeEdges(float[] s, int sampleRate, float fadeIn, float fadeOut)
        {
            int fin = math.min(s.Length, (int)(fadeIn * sampleRate));
            for (int i = 0; i < fin; i++) s[i] *= (float)i / fin;
            int fout = math.min(s.Length, (int)(fadeOut * sampleRate));
            for (int i = 0; i < fout; i++) s[s.Length - 1 - i] *= (float)i / fout;
        }

        static void Limit(float[] s)
        {
            float peak = 0f;
            foreach (float v in s) peak = math.max(peak, math.abs(v));
            if (peak <= Peak || peak <= 0f) return;
            float k = Peak / peak;
            for (int i = 0; i < s.Length; i++) s[i] *= k;
        }

        // ---------------------------------------------------------------- music

        /// <summary>
        /// A sustained open chord (root, fifth, octave, twelfth: no third, so it fits major and minor keys) with slow
        /// attack and release, for the music pad.
        /// </summary>
        public static float[] RenderPad(in SynthPatch p, int sampleRate, uint seed)
        {
            int n = math.max(1, (int)(math.max(0.5f, p.duration) * sampleRate));
            var s = new float[n];
            var rng = new Unity.Mathematics.Random(seed | 1u);
            float f = math.max(20f, p.frequency);
            float[] ratios = { 1f, 1.4983f, 2f, 2.9966f };
            float[] levels = { 0.5f, 0.32f, 0.22f, 0.12f };
            for (int v = 0; v < ratios.Length; v++)
            {
                float detune = 1f + rng.NextFloat(-0.003f, 0.003f);
                double phase = rng.NextFloat();
                for (int i = 0; i < n; i++)
                {
                    phase += f * ratios[v] * detune / sampleRate;
                    float ph = (float)(phase - math.floor(phase));
                    float tri = 1f - 4f * math.abs(ph - 0.5f);
                    s[i] += tri * levels[v];
                }
            }
            float attack = math.max(0.05f, p.attack), release = math.max(0.1f, p.duration * 0.4f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sampleRate, left = (float)(n - i) / sampleRate;
                s[i] *= math.min(1f, math.min(t / attack, left / release));
            }
            Filter(s, sampleRate, p.lowpass > 0f ? p.lowpass : 1200f, 0f);
            float gain = p.gain > 0f ? p.gain : 1f;
            for (int i = 0; i < n; i++) s[i] *= gain;
            Limit(s);
            return s;
        }

        // ---------------------------------------------------------------- ambience

        /// <summary>A seamless loop of <paramref name="seconds"/>: the last second is crossfaded into the start.</summary>
        public static float[] RenderAmbience(in AmbiencePatch p, int sampleRate, float seconds, uint seed)
        {
            int n = math.max(sampleRate, (int)(seconds * sampleRate));
            int fade = math.min(n / 4, sampleRate);
            var s = new float[n + fade];
            var rng = new Unity.Mathematics.Random(seed | 1u);
            float loopSeconds = (float)n / sampleRate;

            // Noise bed: brown-ish noise through a low-pass, swelling with a slow LFO that loops exactly.
            if (p.noise > 0f)
            {
                float a = 1f - math.exp(-TwoPi * p.noiseLowpass / sampleRate), y = 0f, brown = 0f;
                float swell = p.swellHz > 0f ? math.max(1f, math.round(p.swellHz * loopSeconds)) / loopSeconds : 0f;
                for (int i = 0; i < s.Length; i++)
                {
                    brown = math.clamp(brown + rng.NextFloat(-0.08f, 0.08f), -1f, 1f) * 0.998f;
                    y += a * (brown * 0.7f + rng.NextFloat(-0.3f, 0.3f) - y);
                    float lfo = swell > 0f ? 0.65f + 0.35f * math.sin(TwoPi * swell * i / sampleRate) : 1f;
                    s[i] += y * p.noise * 2.2f * lfo;
                }
            }
            // Drone: a few detuned sines whose beating also loops.
            if (p.drone > 0f && p.droneHz > 0f)
            {
                float[] ratios = { 1f, 1.5f, 2.003f };
                for (int v = 0; v < ratios.Length; v++)
                {
                    float freq = math.round(p.droneHz * ratios[v] * loopSeconds) / loopSeconds;
                    float wobble = math.max(1f, math.round(0.07f * (v + 1) * loopSeconds)) / loopSeconds;
                    for (int i = 0; i < s.Length; i++)
                    {
                        float t = (float)i / sampleRate;
                        s[i] += math.sin(TwoPi * freq * t) * p.drone * 0.18f / (v + 1) * (0.75f + 0.25f * math.sin(TwoPi * wobble * t + v));
                    }
                }
            }
            float dripHz = p.dripHz > 0f ? p.dripHz : 1400f;
            Events(s, sampleRate, n, p.drips, ref rng, (buf, at, r) => Ping(buf, at, sampleRate, dripHz * r.NextFloat(0.8f, 1.3f), 0.22f, 0.25f));
            Events(s, sampleRate, n, p.bubbles, ref rng, (buf, at, r) => Bubble(buf, at, sampleRate, r.NextFloat(60f, 140f), 0.3f));
            Events(s, sampleRate, n, p.twinkles, ref rng, (buf, at, r) => Ping(buf, at, sampleRate, r.NextFloat(2200f, 4200f), 0.08f, 0.6f));

            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                s[i] = s[i] * k + s[n + i] * (1f - k);
            }
            var loop = new float[n];
            Array.Copy(s, loop, n);
            float gain = p.gain > 0f ? p.gain : 1f;
            for (int i = 0; i < n; i++) loop[i] *= gain;
            Limit(loop);
            return loop;
        }

        delegate void EventWriter(float[] buffer, int at, Unity.Mathematics.Random rng);

        static void Events(float[] s, int sampleRate, int n, float perSecond, ref Unity.Mathematics.Random rng, EventWriter write)
        {
            if (perSecond <= 0f) return;
            int count = math.max(1, (int)math.round(perSecond * n / sampleRate));
            for (int e = 0; e < count; e++)
                write(s, rng.NextInt(0, n), new Unity.Mathematics.Random(rng.NextUInt() | 1u));
        }

        /// <summary>A short decaying sine with a downward chirp (water drip, crystal ping).</summary>
        static void Ping(float[] s, int at, int sampleRate, float freq, float level, float seconds)
        {
            int len = (int)(seconds * sampleRate);
            double phase = 0;
            for (int i = 0; i < len && at + i < s.Length; i++)
            {
                float t = (float)i / len;
                phase += freq * (1f + 0.6f * (1f - t) * (1f - t)) / sampleRate;
                s[at + i] += math.sin((float)(phase - math.floor(phase)) * TwoPi) * level * math.pow(1f - t, 3f);
            }
        }

        /// <summary>A low rising blip (lava bubble, distant thump).</summary>
        static void Bubble(float[] s, int at, int sampleRate, float freq, float level)
        {
            int len = (int)(0.18f * sampleRate);
            double phase = 0;
            for (int i = 0; i < len && at + i < s.Length; i++)
            {
                float t = (float)i / len;
                phase += freq * (1f + 1.5f * t) / sampleRate;
                s[at + i] += math.sin((float)(phase - math.floor(phase)) * TwoPi) * level * math.sin(t * math.PI);
            }
        }

        public static AudioClip ToClip(string name, float[] samples, int sampleRate)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
