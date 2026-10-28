using System;
using System.IO;
using Antarium.Content;
using Antarium.Core;
using Antarium.Gameplay;
using Antarium.Presentation;
using Antarium.Sim;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Antarium.Tests
{
    /// <summary>Milestone 8: sounds, settings, platform and achievements, balance runs.</summary>
    public class PolishTests : SimulationFixture
    {
        string _dir;

        [SetUp]
        public void MakeDir()
        {
            _dir = Path.Combine(Path.GetTempPath(), "antarium-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void RemoveDir()
        {
            NumberFormat.PreferScientific = false;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        // ---------------------------------------------------------------- audio

        static SynthPatch Patch(SynthWave wave, float duration = 0.2f, float frequency = 440f) => new()
        {
            wave = wave, duration = duration, frequency = frequency, decayPower = 2f, gain = 3f, notes = 1,
        };

        [Test]
        public void Synth_RendersEveryWaveAudibleLimitedAndDeterministic()
        {
            foreach (SynthWave wave in Enum.GetValues(typeof(SynthWave)))
            {
                var a = Synth.Render(Patch(wave), 22050, 7u);
                var b = Synth.Render(Patch(wave), 22050, 7u);
                Assert.AreEqual((int)(0.2f * 22050), a.Length, wave.ToString());
                CollectionAssert.AreEqual(a, b, $"{wave}: same seed, same samples");
                float peak = 0f;
                foreach (float v in a) peak = Mathf.Max(peak, Mathf.Abs(v));
                Assert.That(peak, Is.GreaterThan(0.05f).And.LessThanOrEqualTo(0.951f), $"{wave}: audible and limited");
                Assert.That(Mathf.Abs(a[^1]), Is.LessThan(0.01f), $"{wave}: fades out without a click");
            }
            var other = Synth.Render(Patch(SynthWave.Noise), 22050, 8u);
            CollectionAssert.AreNotEqual(Synth.Render(Patch(SynthWave.Noise), 22050, 7u), other, "other seed, other noise");
        }

        [Test]
        public void Synth_AmbienceLoopsAndPadSustains()
        {
            var amb = new AmbiencePatch { noise = 0.5f, noiseLowpass = 300f, swellHz = 0.1f, drone = 0.4f, droneHz = 60f, drips = 1f, gain = 1f };
            var loop = Synth.RenderAmbience(amb, 8000, 4f, 3u);
            Assert.AreEqual(4 * 8000, loop.Length);
            Assert.That(Mathf.Abs(loop[^1] - loop[0]), Is.LessThan(0.1f), "the end runs into the start");
            var pad = Synth.RenderPad(new SynthPatch { duration = 3f, frequency = 110f, attack = 0.5f, gain = 1f }, 8000, 1u);
            Assert.AreEqual(3 * 8000, pad.Length);
            Assert.That(Mathf.Abs(pad[pad.Length / 2]) + Mathf.Abs(pad[pad.Length / 2 + 7]), Is.GreaterThan(0f), "the pad sounds in the middle");
        }

        [Test]
        public void VoiceLimiter_HonoursCooldownAndVoices()
        {
            var limiter = new VoiceLimiter(new[] { 0.1f, 0f }, new[] { 4, 2 });
            Assert.IsTrue(limiter.TryStart(0, 0.0, 1f));
            Assert.IsFalse(limiter.TryStart(0, 0.05, 1f), "within the cooldown");
            Assert.IsTrue(limiter.TryStart(0, 0.2, 1f));
            Assert.IsTrue(limiter.TryStart(1, 0.0, 1f));
            Assert.IsTrue(limiter.TryStart(1, 0.0, 1f));
            Assert.IsFalse(limiter.TryStart(1, 0.5, 1f), "both voices still sound");
            Assert.IsTrue(limiter.TryStart(1, 1.0, 1f), "a voice ended");
        }

        [Test]
        public void Music_PicksTheBiomeSetOrTheNearestAbove()
        {
            var clips = new AudioClip[3];
            for (int i = 0; i < clips.Length; i++) clips[i] = AudioClip.Create($"t{i}", 100, 1, 8000, false);
            var deep = ScriptableObject.CreateInstance<BiomeDef>();
            deep.EditorSetIdentity("deep", "Deep");
            try
            {
                var settings = new MusicSettings();
                settings.biomeTracks.Add(new BiomeTracks { biome = null, tracks = new() { clips[0], null } });
                settings.biomeTracks.Add(new BiomeTracks { biome = deep, tracks = new() { clips[1], clips[2] } });
                var ids = new[] { "topsoil", "clay", "deep", "magma" };
                var sets = MusicPlayer.BiomeSets(settings, ids);
                Assert.AreEqual(5, sets.Length, "surface + one per biome");
                CollectionAssert.AreEqual(new[] { clips[0] }, sets[0], "empty clip slots are skipped");
                Assert.AreEqual(2, sets[3].Count);
                Assert.AreEqual(0, MusicPlayer.PickSet(sets, 0), "surface");
                Assert.AreEqual(0, MusicPlayer.PickSet(sets, 2), "clay has none: the nearest set above");
                Assert.AreEqual(3, MusicPlayer.PickSet(sets, 3), "its own set");
                Assert.AreEqual(3, MusicPlayer.PickSet(sets, 4), "magma falls back to deep");

                var onlyDeep = MusicPlayer.BiomeSets(new MusicSettings { biomeTracks = { new BiomeTracks { biome = deep, tracks = new() { clips[1] } } } }, ids);
                Assert.AreEqual(3, MusicPlayer.PickSet(onlyDeep, 1), "nothing above: the nearest set below");
                Assert.AreEqual(-1, MusicPlayer.PickSet(MusicPlayer.BiomeSets(new MusicSettings(), ids), 2), "no tracks at all: generative music");
            }
            finally
            {
                foreach (var c in clips) Object.DestroyImmediate(c);
                Object.DestroyImmediate(deep);
            }
        }

        [Test]
        public void SoundBank_HasEveryCueAndBiome()
        {
            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>("Assets/_Antarium/Content/Audio/SoundBank.asset");
            Assert.IsNotNull(bank, "run Antarium/Setup/Create Sound Bank");
            foreach (SoundCue cue in Enum.GetValues(typeof(SoundCue)))
                Assert.IsNotNull(bank.Find(cue), $"no sound for {cue}");
            var db = AssetDatabase.LoadAssetAtPath<ContentDatabase>("Assets/_Antarium/Content/ContentDatabase.asset");
            foreach (var biome in db.worldGen.biomes)
                Assert.IsTrue(bank.ambience.Exists(a => a.biome == biome), $"no ambience for {biome.Id}");
            Assert.IsTrue(bank.ambience.Exists(a => a.biome == null), "no surface ambience");
        }

        // ---------------------------------------------------------------- settings

        [Test]
        public void Settings_SanitizeAndRoundTrip()
        {
            var s = new GameSettings { masterVolume = 3f, uiScale = 9, frameCap = 5, autosaveMinutes = -2, width = -4, screenShake = -1f };
            s.Sanitize();
            Assert.AreEqual(1f, s.masterVolume);
            Assert.AreEqual(GameSettings.MaxUiScale, s.uiScale);
            Assert.AreEqual(30, s.frameCap);
            Assert.AreEqual(0, s.autosaveMinutes);
            Assert.AreEqual(0, s.width);
            Assert.AreEqual(0f, s.screenShake);

            var store = new SettingsStore(_dir);
            Assert.AreEqual(new GameSettings().musicVolume, store.Load().musicVolume, "defaults when there is no file");
            s.musicVolume = 0.3f;
            s.edgePan = true;
            s.bindingOverrides = "{\"bindings\":[]}";
            store.Save(s);
            var back = store.Load();
            Assert.AreEqual(0.3f, back.musicVolume);
            Assert.IsTrue(back.edgePan);
            Assert.AreEqual(s.bindingOverrides, back.bindingOverrides);
            File.WriteAllText(store.Path, "not json {");
            Assert.AreEqual(new GameSettings().masterVolume, store.Load().masterVolume, "a broken file gives defaults");
        }

        [Test]
        public void Settings_UiScaleIsWholeAndFollowsTheScreen()
        {
            Assert.AreEqual(1, GameSettings.AutoUiScale(720));
            Assert.AreEqual(1, GameSettings.AutoUiScale(1080));
            Assert.AreEqual(2, GameSettings.AutoUiScale(1440));
            Assert.AreEqual(2, GameSettings.AutoUiScale(2160));
            Assert.AreEqual(3, new GameSettings { uiScale = 3 }.EffectiveUiScale(720), "a chosen scale wins");
        }

        [Test]
        public void NumberFormat_ScientificPreference()
        {
            Assert.AreEqual("1.23K", NumberFormat.Format(1234));
            NumberFormat.PreferScientific = true;
            Assert.AreEqual("1.23e3", NumberFormat.Format(1234));
            Assert.AreEqual("999", NumberFormat.Format(999), "small numbers stay plain");
        }

        [Test]
        public void Input_BindingOverridesRoundTrip()
        {
            var asset = Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Antarium/Input/AntariumInput.inputactions"));
            try
            {
                using var input = new GameInput(asset);
                Assert.That(input.Rebindables.Length, Is.GreaterThan(20));
                Assert.IsTrue(Array.Exists(input.Rebindables, r => r.Action == input.Help), "F1 help is listed");
                int version = input.BindingsVersion;
                input.Possess.ApplyBindingOverride(0, "<Keyboard>/g");
                string json = input.SaveOverrides();
                StringAssert.Contains("<Keyboard>/g", json);
                input.ResetOverrides();
                Assert.AreEqual("<Keyboard>/f", input.Possess.bindings[0].effectivePath);
                input.LoadOverrides(json);
                Assert.AreEqual("<Keyboard>/g", input.Possess.bindings[0].effectivePath);
                Assert.That(input.BindingsVersion, Is.GreaterThan(version), "labels know to refresh");
                input.LoadOverrides("garbage");
                Assert.AreEqual("<Keyboard>/f", input.Possess.bindings[0].effectivePath, "bad overrides fall back to defaults");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        // ---------------------------------------------------------------- platform and achievements

        [Test]
        public void LocalPlatform_KeepsAchievementsAndHighestStats()
        {
            using (var p = new LocalPlatform(_dir))
            {
                Assert.AreEqual(Path.Combine(_dir, "Saves"), p.SaveDirectory);
                p.UnlockAchievement("ACH_A");
                p.SetStat("STAT", 5);
                p.SetStat("STAT", 3);
            }
            var again = new LocalPlatform(_dir);
            Assert.IsTrue(again.IsAchievementUnlocked("ACH_A"), "kept in profile.json");
            Assert.IsFalse(again.IsAchievementUnlocked("ACH_B"));
            Assert.AreEqual(5, again.GetStat("STAT"), "stats keep their highest value");
            again.Reset();
            Assert.IsFalse(new LocalPlatform(_dir).IsAchievementUnlocked("ACH_A"), "erased");
        }

        [Test]
        public void Achievements_BakeInOrderAndUnlockOnce()
        {
            Assert.AreEqual(2, C.DB.Achievements.Length);
            Assert.AreEqual(C.AchDig, C.DB.Achievements[0].Def, "ordered by order");

            var platform = new LocalPlatform(_dir);
            var tracker = new AchievementTracker(C.DB, platform);
            int unlocked = 0;
            tracker.Unlocked += _ => unlocked++;
            tracker.Update(0f, Sim, force: true);
            Assert.AreEqual(0, unlocked);

            Sim.Colony.Stats.CellsDug = 5;
            tracker.Update(0.1f, Sim);
            Assert.AreEqual(0, unlocked, "checked about once a second");
            tracker.Update(1f, Sim);
            Assert.AreEqual(1, unlocked);
            Assert.IsTrue(platform.IsAchievementUnlocked("ACH_DIG"));
            Assert.AreEqual(1, tracker.UnlockedCount);

            Sim.Legacy.Flights = 1;
            tracker.Update(0f, Sim, force: true);
            tracker.Update(0f, Sim, force: true);
            Assert.AreEqual(2, unlocked, "each achievement unlocks once");
            Assert.AreEqual(1, platform.GetStat("STAT_FLIGHTS"), "progress is pushed to the stat");

            var later = new AchievementTracker(C.DB, new LocalPlatform(_dir));
            Assert.AreEqual(2, later.UnlockedCount, "unlocks belong to the player, not the colony");
        }

        TerrainMaterialDef[] Materials => new[] { C.Dirt, C.Air, C.Grass, C.Stone, C.Crystal, C.Bedrock, C.Meteor, C.Sand, C.Obsidian, C.Packed, C.OreRock };
        ItemDef[] Items => new[] { C.Ore, C.Seed, C.Bar, C.Chitin };

        [Test]
        public void Achievements_RefuseBadContent()
        {
            var a = ScriptableObject.CreateInstance<AchievementDef>();
            var b = ScriptableObject.CreateInstance<AchievementDef>();
            try
            {
                a.EditorSetIdentity("a", "A");
                b.EditorSetIdentity("b", "B");
                a.apiName = b.apiName = "SAME";
                var progression = new ProgressionContent { Achievements = new[] { a, b } };
                Assert.Throws<ArgumentException>(() => ContentDB.Build(Materials, C.WorldGen, Items, progression: progression).Dispose(), "duplicate API names");
                b.apiName = "OTHER";
                b.kind = AchievementKind.Deliver;
                Assert.Throws<ArgumentException>(() => ContentDB.Build(Materials, C.WorldGen, Items, progression: progression).Dispose(), "deliver needs an item");
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        // ---------------------------------------------------------------- balance

        [Test]
        public void BalanceRun_PlaysAndSamplesTheColony()
        {
            var samples = BalanceRun.Run(Sim, 1f, 20f);
            Assert.AreEqual(4, samples.Count, "the start plus one sample every 20 s");
            Assert.AreEqual(1f, samples[^1].Minutes, 0.01f);
            Assert.That(samples[^1].CellsDug, Is.GreaterThan(0), "the autopilot's mining orders get dug");
            Assert.That(samples[^1].Depth, Is.GreaterThan(0));
            string csv = BalanceRun.ToCsv(samples);
            StringAssert.StartsWith("minutes,ants,food", csv);
            Assert.AreEqual(5, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        }
    }
}
