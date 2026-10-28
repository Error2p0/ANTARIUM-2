using System;
using System.IO;
using UnityEngine;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Player preferences (audio, display, gameplay, key bindings). Stored as JSON in <c>settings.json</c>, apart from
    /// the colony save, so they survive new colonies and erased progress. Unknown or missing fields keep their
    /// defaults; <see cref="Sanitize"/> clamps everything read from disk.
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const int CurrentVersion = 1;
        public const int MaxUiScale = 4;

        public int version = CurrentVersion;

        [Header("Audio (0..1)")]
        public float masterVolume = 0.8f;
        public float musicVolume = 0.4f;
        public float sfxVolume = 0.8f;
        public float ambienceVolume = 0.6f;
        public float uiVolume = 0.7f;
        public bool muteInBackground;

        [Header("Display")]
        public FullScreenMode windowMode = FullScreenMode.FullScreenWindow;
        /// <summary>0 = the display's native resolution.</summary>
        public int width, height;
        public bool vsync = true;
        /// <summary>Frames per second without vsync; 0 = unlimited.</summary>
        public int frameCap = 144;
        /// <summary>UI pixel scale; 0 = pick from the screen height.</summary>
        public int uiScale;

        [Header("Gameplay")]
        /// <summary>Camera shake strength, 0..1.</summary>
        public float screenShake = 1f;
        /// <summary>Pan the camera when the pointer touches the screen edge.</summary>
        public bool edgePan;
        public bool scientificNumbers;
        /// <summary>0 = only save when quitting.</summary>
        public int autosaveMinutes = 2;
        /// <summary>Show the number pops over deliveries and crafts.</summary>
        public bool numberPops = true;

        [Header("Controls")]
        /// <summary>Input System binding overrides (<c>SaveBindingOverridesAsJson</c>).</summary>
        public string bindingOverrides = "";

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        public void Sanitize()
        {
            masterVolume = Mathf.Clamp01(masterVolume);
            musicVolume = Mathf.Clamp01(musicVolume);
            sfxVolume = Mathf.Clamp01(sfxVolume);
            ambienceVolume = Mathf.Clamp01(ambienceVolume);
            uiVolume = Mathf.Clamp01(uiVolume);
            if (!Enum.IsDefined(typeof(FullScreenMode), windowMode)) windowMode = FullScreenMode.FullScreenWindow;
            if (width < 0 || height < 0 || width > 16384 || height > 16384) width = height = 0;
            frameCap = frameCap <= 0 ? 0 : Mathf.Clamp(frameCap, 30, 1000);
            uiScale = Mathf.Clamp(uiScale, 0, MaxUiScale);
            screenShake = Mathf.Clamp01(screenShake);
            autosaveMinutes = Mathf.Clamp(autosaveMinutes, 0, 60);
            bindingOverrides ??= "";
            version = CurrentVersion;
        }

        /// <summary>The UI scale to use on a screen <paramref name="screenHeight"/> pixels high (whole numbers only, pixel art).</summary>
        public int EffectiveUiScale(int screenHeight) => uiScale > 0 ? uiScale : AutoUiScale(screenHeight);

        /// <summary>1 below 1440p, 2 from 1440p (and 4K), 3 from 3240p.</summary>
        public static int AutoUiScale(int screenHeight) => screenHeight >= 3240 ? 3 : screenHeight >= 1440 ? 2 : 1;
    }

    /// <summary>Reads and writes <see cref="GameSettings"/>.</summary>
    public sealed class SettingsStore
    {
        public string Path { get; }

        public SettingsStore(string directory) => Path = System.IO.Path.Combine(directory, "settings.json");

        public GameSettings Load()
        {
            GameSettings settings = null;
            try
            {
                if (File.Exists(Path)) settings = JsonUtility.FromJson<GameSettings>(File.ReadAllText(Path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not read the settings ({e.Message}); using defaults.");
            }
            settings ??= new GameSettings();
            settings.Sanitize();
            return settings;
        }

        public void Save(GameSettings settings)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, JsonUtility.ToJson(settings, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not write the settings: {e.Message}");
            }
        }
    }
}
