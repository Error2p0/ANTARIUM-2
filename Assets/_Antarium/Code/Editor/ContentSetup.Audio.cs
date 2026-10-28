using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        public const string SoundBankPath = Root + "/Audio/SoundBank.asset";

        /// <summary>
        /// Milestone 8: the sound bank with a synth patch for every cue, an ambience bed per biome and the music.
        /// Missing cues and biomes are added to an existing bank; tuned entries are left alone.
        /// </summary>
        [MenuItem("Antarium/Setup/Create Sound Bank")]
        public static SoundBank CreateSoundBank()
        {
            EnsureFolder(Root + "/Audio");
            var bank = LoadOrCreate<SoundBank>(SoundBankPath, b =>
            {
                b.music.pluck = Patch(SynthWave.Pluck, 1.6f, 220f, decay: 6f, harmonics: 0.2f, gain: 0.9f);
                b.music.pad = Patch(SynthWave.Triangle, 14f, 110f, attack: 2.5f, lowpass: 900f, gain: 0.7f);
            });
            int added = 0;

            void Sfx(SoundCue cue, SynthPatch patch, float volume, float cooldown = 0.05f, int voices = 3, bool positional = true,
                SoundBus bus = SoundBus.Sfx, float pitchLow = 0.94f, float pitchHigh = 1.06f, int variants = 2)
            {
                if (bank.Find(cue) != null) return;
                bank.sounds.Add(new SoundEntry
                {
                    cue = cue, synth = patch, volume = volume, cooldown = cooldown, maxVoices = voices, positional = positional, bus = bus,
                    pitch = new Vector2(pitchLow, pitchHigh), variants = variants,
                });
                added++;
            }

            void Ui(SoundCue cue, SynthPatch patch, float volume, float cooldown = 0.05f, int voices = 2) =>
                Sfx(cue, patch, volume, cooldown, voices, positional: false, bus: SoundBus.Ui, pitchLow: 0.98f, pitchHigh: 1.02f, variants: 1);

            void Note(SoundCue cue, SynthPatch patch, float volume, float cooldown = 0.2f) =>
                Sfx(cue, patch, volume, cooldown, 2, positional: false, pitchLow: 0.99f, pitchHigh: 1.01f, variants: 1);

            // digging and the world
            Sfx(SoundCue.DigHit, Patch(SynthWave.Noise, 0.09f, 2200f, 800f, decay: 3f, lowpass: 3000f, gain: 0.9f), 0.5f, 0.045f, pitchLow: 0.85f, pitchHigh: 1.15f, variants: 3);
            Sfx(SoundCue.DigBreak, Patch(SynthWave.Noise, 0.22f, 1600f, 260f, decay: 2.5f, lowpass: 2200f), 0.65f, 0.06f, pitchLow: 0.85f, pitchHigh: 1.1f, variants: 3);
            Sfx(SoundCue.AntDig, Patch(SynthWave.Noise, 0.07f, 3000f, 1500f, decay: 4f, lowpass: 4000f, highpass: 400f), 0.16f, 0.12f, 2, pitchLow: 0.8f, pitchHigh: 1.2f, variants: 3);
            Note(SoundCue.CaveFound, Patch(SynthWave.Sine, 0.7f, 220f, 660f, attack: 0.05f, decay: 1.5f, noise: 0.15f, lowpass: 3000f, harmonics: 0.3f), 0.45f, 1f);
            Sfx(SoundCue.CaveIn, Patch(SynthWave.Noise, 0.9f, 300f, 60f, decay: 1.6f, lowpass: 700f, gain: 1.2f), 0.7f, 0.4f, 2);
            Sfx(SoundCue.Sizzle, Patch(SynthWave.Noise, 0.5f, 0f, attack: 0.02f, decay: 1.2f, lowpass: 9000f, highpass: 3000f), 0.3f, 0.25f, 2);
            Sfx(SoundCue.WallBuilt, Patch(SynthWave.Sine, 0.18f, 140f, 70f, decay: 3f, noise: 0.3f, lowpass: 1200f), 0.5f, 0.1f);

            // colony
            Sfx(SoundCue.Deliver, Patch(SynthWave.Pluck, 0.18f, 880f, decay: 4f), 0.2f, 0.07f, pitchLow: 0.9f, pitchHigh: 1.2f, variants: 3);
            Sfx(SoundCue.EggLaid, Patch(SynthWave.Sine, 0.12f, 600f, 900f, decay: 2f, harmonics: 0.2f), 0.22f, 0.3f);
            Sfx(SoundCue.Hatch, Patch(SynthWave.Triangle, 0.16f, 500f, 1000f, decay: 2f, notes: 2, noteStep: 5f), 0.28f, 0.2f);
            Sfx(SoundCue.AntDied, Patch(SynthWave.Square, 0.22f, 400f, 120f, decay: 2f, lowpass: 2500f, crush: 6000f), 0.32f, 0.15f);
            Note(SoundCue.Possess, Patch(SynthWave.Sine, 0.35f, 300f, 900f, attack: 0.02f, decay: 1.5f, harmonics: 0.4f, vibratoHz: 6f, vibratoDepth: 0.3f), 0.45f, 0.1f);
            Note(SoundCue.Release, Patch(SynthWave.Sine, 0.3f, 900f, 300f, attack: 0.02f, decay: 1.5f, harmonics: 0.4f), 0.4f, 0.1f);

            // buildings
            Sfx(SoundCue.Place, Patch(SynthWave.Triangle, 0.07f, 700f, decay: 3f), 0.3f, 0.035f, 4);
            Sfx(SoundCue.BuildDone, Patch(SynthWave.Pluck, 0.4f, 523f, decay: 3f, harmonics: 0.3f, notes: 2, noteStep: 7f), 0.42f, 0.1f);
            Sfx(SoundCue.Remove, Patch(SynthWave.Triangle, 0.12f, 500f, 250f, decay: 2f), 0.28f, 0.04f);
            Sfx(SoundCue.Craft, Patch(SynthWave.Pluck, 0.25f, 660f, decay: 4f, notes: 2, noteStep: 4f), 0.26f, 0.15f);
            Sfx(SoundCue.Study, Patch(SynthWave.Sine, 0.35f, 1046f, attack: 0.005f, decay: 2.5f, harmonics: 0.4f, notes: 3, noteStep: 4f), 0.24f, 0.2f);
            Note(SoundCue.PrincessRaised, Patch(SynthWave.Sine, 0.9f, 523f, attack: 0.01f, decay: 2f, harmonics: 0.5f, vibratoHz: 5f, vibratoDepth: 0.15f, notes: 4, noteStep: 4f), 0.5f);

            // threats
            Sfx(SoundCue.EnemyKilled, Patch(SynthWave.Noise, 0.25f, 900f, 200f, decay: 2f, lowpass: 2500f), 0.45f, 0.08f);
            Note(SoundCue.CreaturesFound, Patch(SynthWave.Saw, 0.9f, 110f, 90f, attack: 0.1f, decay: 1.2f, lowpass: 600f, vibratoHz: 5f, vibratoDepth: 0.5f), 0.5f, 1f);
            Note(SoundCue.RaidWarning, Patch(SynthWave.Saw, 1.2f, 196f, attack: 0.06f, decay: 0.9f, lowpass: 1000f, harmonics: 0.3f, notes: 2), 0.6f, 1f);
            Note(SoundCue.RaidStart, Patch(SynthWave.Square, 0.8f, 110f, decay: 4f, noise: 0.4f, lowpass: 500f, notes: 4), 0.6f, 1f);
            Note(SoundCue.RaidRepelled, Patch(SynthWave.Triangle, 0.7f, 523f, decay: 2f, harmonics: 0.5f, notes: 3, noteStep: 7f), 0.55f, 1f);
            Sfx(SoundCue.TrapSnap, Patch(SynthWave.Noise, 0.08f, 4000f, 1000f, decay: 5f, highpass: 800f), 0.45f, 0.06f);
            Sfx(SoundCue.GateBroken, Patch(SynthWave.Noise, 0.6f, 500f, 80f, decay: 1.8f, lowpass: 1500f, crush: 4000f), 0.65f, 0.3f);

            // progression
            Ui(SoundCue.Research, Patch(SynthWave.Sine, 0.6f, 659f, decay: 2f, harmonics: 0.5f, notes: 3, noteStep: 5f), 0.5f, 0.2f);
            Ui(SoundCue.ContractClaim, Patch(SynthWave.Square, 0.28f, 988f, decay: 2f, lowpass: 5000f, notes: 2, noteStep: 5f), 0.35f, 0.15f);
            Note(SoundCue.Frenzy, Patch(SynthWave.Saw, 0.6f, 200f, 800f, decay: 1.5f, lowpass: 3000f, vibratoHz: 12f, vibratoDepth: 0.8f), 0.45f);
            Note(SoundCue.Seismic, Patch(SynthWave.Noise, 1.2f, 120f, 40f, decay: 1.2f, lowpass: 400f, gain: 1.4f), 0.75f);
            Note(SoundCue.Decree, Patch(SynthWave.Sine, 1f, 392f, decay: 2f, harmonics: 0.6f, vibratoHz: 5f, vibratoDepth: 0.2f, notes: 4, noteStep: 5f), 0.5f);
            Note(SoundCue.PheromoneBurst, Patch(SynthWave.Sine, 0.5f, 1200f, 300f, decay: 1.6f, noise: 0.4f, lowpass: 4000f), 0.45f);
            Ui(SoundCue.EventStart, Patch(SynthWave.Triangle, 0.35f, 880f, decay: 2f, harmonics: 0.3f, notes: 2, noteStep: 5f), 0.4f, 0.5f);
            Ui(SoundCue.EventWon, Patch(SynthWave.Triangle, 0.5f, 659f, decay: 2f, harmonics: 0.4f, notes: 3, noteStep: 4f), 0.45f, 0.5f);
            Ui(SoundCue.EventLost, Patch(SynthWave.Triangle, 0.6f, 440f, decay: 2f, notes: 3, noteStep: -3f), 0.4f, 0.5f);
            Note(SoundCue.Meteorite, Patch(SynthWave.Noise, 1.6f, 600f, 40f, attack: 0.4f, decay: 1.4f, lowpass: 900f, gain: 1.5f), 0.8f, 1f);
            Ui(SoundCue.GeneticBought, Patch(SynthWave.Sine, 0.6f, 523f, decay: 2f, harmonics: 0.6f, vibratoHz: 4f, vibratoDepth: 0.15f, notes: 3, noteStep: 7f), 0.5f, 0.15f);
            Note(SoundCue.Flight, Patch(SynthWave.Triangle, 2.5f, 300f, 1200f, attack: 0.3f, decay: 1.2f, noise: 0.3f, lowpass: 3000f, vibratoHz: 18f, vibratoDepth: 1f), 0.5f, 2f);
            Ui(SoundCue.Achievement, Patch(SynthWave.Triangle, 0.9f, 523f, decay: 2f, harmonics: 0.5f, notes: 4, noteStep: 5f), 0.6f, 0.5f);

            // interface
            Ui(SoundCue.UiClick, Patch(SynthWave.Square, 0.035f, 1200f, decay: 4f, lowpass: 6000f), 0.22f, 0.02f, 3);
            Ui(SoundCue.UiHover, Patch(SynthWave.Sine, 0.02f, 1800f, decay: 4f), 0.07f, 0.04f, 1);
            Ui(SoundCue.UiOpen, Patch(SynthWave.Triangle, 0.09f, 500f, 900f, decay: 2f), 0.24f);
            Ui(SoundCue.UiClose, Patch(SynthWave.Triangle, 0.09f, 900f, 500f, decay: 2f), 0.2f);
            Ui(SoundCue.Refused, Patch(SynthWave.Square, 0.15f, 220f, 180f, decay: 2f, lowpass: 1800f, notes: 2), 0.28f, 0.2f);
            Ui(SoundCue.Toast, Patch(SynthWave.Sine, 0.06f, 1320f, decay: 3f), 0.1f, 0.5f, 1);

            void Bed(string biomeId, AmbiencePatch patch)
            {
                var biome = biomeId == null ? null : AssetDatabase.LoadAssetAtPath<BiomeDef>($"{BiomesFolder}/{biomeId}.asset");
                if (biomeId != null && biome == null) return;
                foreach (var a in bank.ambience)
                    if (a != null && a.biome == biome) return;
                bank.ambience.Add(new AmbienceEntry { biome = biome, synth = patch });
                added++;
            }

            Bed(null, Amb(noise: 0.5f, lowpass: 600f, swell: 0.08f, twinkles: 0.4f, gain: 0.8f));
            Bed("topsoil", Amb(noise: 0.35f, lowpass: 300f, swell: 0.05f, drips: 0.15f, dripHz: 1200f, gain: 0.7f));
            Bed("clay", Amb(noise: 0.3f, lowpass: 250f, swell: 0.05f, drips: 0.6f, dripHz: 1000f, gain: 0.7f));
            Bed("stone_strata", Amb(noise: 0.25f, lowpass: 180f, drone: 0.3f, droneHz: 55f, drips: 0.2f, dripHz: 1600f, gain: 0.7f));
            Bed("crystal_caverns", Amb(noise: 0.15f, lowpass: 400f, swell: 0.1f, drone: 0.5f, droneHz: 110f, twinkles: 0.8f, gain: 0.7f));
            Bed("fungal_depths", Amb(noise: 0.3f, lowpass: 500f, swell: 0.15f, drone: 0.2f, droneHz: 73f, bubbles: 0.4f, twinkles: 0.2f, gain: 0.7f));
            Bed("magma_zone", Amb(noise: 0.45f, lowpass: 150f, swell: 0.2f, drone: 0.35f, droneHz: 41f, bubbles: 1.2f, gain: 0.8f));
            Bed("ancient_ruins", Amb(noise: 0.2f, lowpass: 220f, drone: 0.45f, droneHz: 65f, drips: 0.25f, dripHz: 900f, twinkles: 0.1f, gain: 0.7f));

            if (added > 0)
            {
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssets();
                Debug.Log($"Sound bank: added {added} sounds and ambience beds.");
            }
            return bank;
        }

        /// <summary>Writes every synthesized cue, ambience bed and the music voices as WAV files to Builds/AudioPreview (git-ignored).</summary>
        [MenuItem("Antarium/Audio/Export Sounds to WAV")]
        public static void ExportSounds()
        {
            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(SoundBankPath);
            if (bank == null)
            {
                Debug.LogError("No sound bank: run Antarium/Setup/Create Sound Bank.");
                return;
            }
            string dir = System.IO.Path.GetFullPath("Builds/AudioPreview");
            System.IO.Directory.CreateDirectory(dir);
            int rate = bank.sampleRate;
            foreach (var s in bank.sounds)
                if (s != null) WriteWav($"{dir}/{s.cue}.wav", Antarium.Presentation.Synth.Render(s.synth, rate, 1u), rate);
            foreach (var a in bank.ambience)
                if (a != null) WriteWav($"{dir}/ambience_{(a.biome != null ? a.biome.Id : "surface")}.wav",
                    Antarium.Presentation.Synth.RenderAmbience(a.synth, rate, bank.ambienceSeconds, 5u), rate);
            WriteWav($"{dir}/music_pluck.wav", Antarium.Presentation.Synth.Render(bank.music.pluck, rate, 7u), rate);
            WriteWav($"{dir}/music_pad.wav", Antarium.Presentation.Synth.RenderPad(bank.music.pad, rate, 11u), rate);
            Debug.Log($"Exported the sounds to {dir}.");
            EditorUtility.RevealInFinder(dir);
        }

        /// <summary>16-bit mono PCM WAV.</summary>
        static void WriteWav(string path, float[] samples, int rate)
        {
            using var w = new System.IO.BinaryWriter(System.IO.File.Create(path));
            int bytes = samples.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + bytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(bytes);
            foreach (float v in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
        }

        static SynthPatch Patch(SynthWave wave, float duration, float frequency, float frequencyEnd = 0f, float attack = 0f, float decay = 1f,
            float noise = 0f, float lowpass = 0f, float highpass = 0f, float harmonics = 0f, float vibratoHz = 0f, float vibratoDepth = 0f,
            int notes = 1, float noteStep = 0f, float crush = 0f, float gain = 1f) => new()
        {
            wave = wave, duration = duration, frequency = frequency, frequencyEnd = frequencyEnd, attack = attack, decayPower = decay,
            noise = noise, lowpass = lowpass, highpass = highpass, harmonics = harmonics, vibratoHz = vibratoHz, vibratoDepth = vibratoDepth,
            notes = notes, noteStep = noteStep, crush = crush, gain = gain,
        };

        static AmbiencePatch Amb(float noise = 0f, float lowpass = 300f, float swell = 0f, float drone = 0f, float droneHz = 0f,
            float drips = 0f, float dripHz = 0f, float bubbles = 0f, float twinkles = 0f, float gain = 1f) => new()
        {
            noise = noise, noiseLowpass = lowpass, swellHz = swell, drone = drone, droneHz = droneHz, drips = drips, dripHz = dripHz,
            bubbles = bubbles, twinkles = twinkles, gain = gain,
        };
    }
}
