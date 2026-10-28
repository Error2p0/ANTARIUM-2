using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    /// <summary>
    /// Fills the sound bank from recorded files in <c>Assets/_Antarium/Audio</c>, by name (see docs/SUNO_PROMPTS.md):
    /// <c>music_&lt;biome id&gt;_*</c> (or <c>music_surface_*</c>), <c>music_title_*</c>, <c>music_raid_*</c>,
    /// <c>music_genetics_*</c>, <c>music_flight*</c>, <c>music_discovery*</c>, <c>amb_&lt;biome id | surface&gt;*</c> and
    /// <c>sting_&lt;SoundCue&gt;*</c>. Re-running replaces the assigned clips with what the folder holds now; synth
    /// patches stay, so removing a file falls back to the synthesized sound.
    /// </summary>
    public static class AudioAssign
    {
        public const string AudioFolder = "Assets/_Antarium/Audio";

        [MenuItem("Antarium/Audio/Assign Audio Files")]
        public static void Assign()
        {
            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(ContentSetup.SoundBankPath);
            var db = AssetDatabase.LoadAssetAtPath<ContentDatabase>(ContentSetup.DatabasePath);
            if (bank == null || db == null)
            {
                Debug.LogError("No sound bank or content database: run Antarium/Setup/Create Default Content.");
                return;
            }
            ContentSetup.EnsureFolder(AudioFolder + "/Music");
            ContentSetup.EnsureFolder(AudioFolder + "/Ambience");
            ContentSetup.EnsureFolder(AudioFolder + "/Stingers");
            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => (Name: Path.GetFileNameWithoutExtension(p).ToLowerInvariant(), Clip: AssetDatabase.LoadAssetAtPath<AudioClip>(p)))
                .Where(c => c.Clip != null)
                .ToList();
            var biomes = db.worldGen.biomes.Where(b => b != null).ToList();
            var report = new List<string>();
            var music = bank.music;

            // Music: per biome (longest id first, so "stone_strata" never matches a shorter id), then the looping sets.
            music.biomeTracks.Clear();
            foreach (var biome in biomes.OrderByDescending(b => b.Id.Length))
                AddSet(music.biomeTracks, biome, clips.Where(c => c.Name.StartsWith($"music_{biome.Id}")).Select(c => c.Clip), report);
            AddSet(music.biomeTracks, null, clips.Where(c => c.Name.StartsWith("music_surface")).Select(c => c.Clip), report);
            music.titleTracks = Collect(clips, "music_title", report);
            music.raidTracks = Collect(clips, "music_raid", report);
            music.geneticsTracks = Collect(clips, "music_genetics", report);
            music.flightCue = Collect(clips, "music_flight", report).FirstOrDefault();
            music.discoveryCue = Collect(clips, "music_discovery", report).FirstOrDefault();

            // Ambience beds.
            foreach (var bed in bank.ambience)
            {
                if (bed == null) continue;
                string id = bed.biome != null ? bed.biome.Id : "surface";
                bed.clip = clips.FirstOrDefault(c => c.Name.StartsWith($"amb_{id}")).Clip;
                if (bed.clip != null) report.Add($"ambience {id}: {bed.clip.name}");
            }

            // Stingers / sound effects by cue name.
            foreach (var entry in bank.sounds)
            {
                if (entry == null) continue;
                string prefix = $"sting_{entry.cue}".ToLowerInvariant();
                entry.clip = clips.FirstOrDefault(c => c.Name.StartsWith(prefix)).Clip;
                if (entry.clip != null) report.Add($"sound {entry.cue}: {entry.clip.name}");
            }

            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Debug.Log(report.Count == 0
                ? $"No recorded audio found in {AudioFolder} (Music / Ambience / Stingers). Everything stays synthesized."
                : $"Sound bank: {report.Count} recorded assignments.\n" + string.Join("\n", report));
        }

        static void AddSet(List<BiomeTracks> sets, BiomeDef biome, IEnumerable<AudioClip> clips, List<string> report)
        {
            var list = clips.Where(c => !sets.Any(s => s.tracks.Contains(c))).ToList();
            if (list.Count == 0) return;
            sets.Add(new BiomeTracks { biome = biome, tracks = list });
            report.Add($"music {(biome != null ? biome.Id : "surface")}: {list.Count} track(s)");
        }

        static List<AudioClip> Collect(List<(string Name, AudioClip Clip)> clips, string prefix, List<string> report)
        {
            var list = clips.Where(c => c.Name.StartsWith(prefix)).Select(c => c.Clip).ToList();
            if (list.Count > 0) report.Add($"{prefix.Replace('_', ' ')}: {list.Count} clip(s)");
            return list;
        }
    }

    /// <summary>Import settings for recorded audio: music streams, ambience stays compressed in memory, stingers are decompressed.</summary>
    sealed class AudioImportRules : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioAssign.AudioFolder + "/", StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            string name = Path.GetFileName(assetPath).ToLowerInvariant();
            bool stinger = name.StartsWith("sting_");
            bool music = name.StartsWith("music_");
            settings.loadType = stinger ? AudioClipLoadType.DecompressOnLoad : music ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = stinger ? AudioCompressionFormat.ADPCM : AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.preloadAudioData = !music;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = music;
        }
    }
}
