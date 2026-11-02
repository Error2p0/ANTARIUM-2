# Steam setup and release

Everything about getting Antarium 2 onto Steam: what is in the code, what has to be clicked on the Steamworks site, and how a build is uploaded. App ID **4453940**, developer / publisher **Elevate Studios**.

Steamworks changes its pages now and then: if a menu name below does not match, look for the same words nearby. Changes on the Steamworks site only take effect after **Publish** (Edit Steamworks Settings → Publish tab → *Prepare for Publishing* → *Publish to Steam*).

## What the code does

- **Steamworks.NET** (UPM git package `com.rlabrecque.steamworks.net`, pinned in `Packages/manifest.json`). The Gameplay assembly defines `ANTARIUM_STEAM` while the package is installed; removing the package builds a game without Steam.
- `PlatformServices.Create` picks the platform: **Steam** when the game runs under Steam, else the local profile. In a release build started outside Steam (no `steam_appid.txt` next to the exe) Steam relaunches it through the client. `-nosteam` on the command line skips Steam.
- **In the editor Steam is off** unless *Antarium → Steam → Use Steam in Editor* is checked (then play mode connects to the running Steam client, and achievements unlock on your real account). `steam_appid.txt` in the project root holds the App ID for this.
- `StorePlatform` keeps the local `profile.json` as the game's record and merges it with Steam both ways once Steam has loaded the user's stats: achievements unlocked on either side end up on both, stats keep the higher value. So achievements earned before the first Steam launch, in the editor or without Steam reach Steam on the next launch. *Erase all progress* only clears the local profile: Steam keeps its achievements and the next merge brings them back.
- **Stats** (`STAT_MAX_DEPTH`, `STAT_MAX_ANTS`, `STAT_FLIGHTS`) are sent after the merge, only ever upward.
- **Rich presence**: *In the main menu*, *Founding a colony, 120 cells deep*, *Generation 3, 420 cells deep*, *Watching a Nuptial Flight* (tokens in `steam/rich_presence_english.vdf`).
- **Overlay**: the game pauses while the Steam overlay is open.
- **Saves**: `%USERPROFILE%/AppData/LocalLow/Elevate Studios/ANTARIUM 2/` holds `Saves/colony.sav` (+ `colony.bak`), `profile.json` (local achievements and stats) and `settings.json`. The first start after the Company Name change copies the old `DefaultCompany` folder over once.

## One-time setup on Steamworks

### 1. Stats and achievements
1. In Unity: *Antarium → Steam → Export Achievements and Icons*. It writes `steam/achievements.md` (every stat and achievement with name, description, hidden flag and progress stat) and `steam/achievement_icons/` (64×64 PNGs, `<API name>.png` and `<API name>_locked.png`).
2. Steamworks → your app → **Edit Steamworks Settings → Stats & Achievements → Stats**: add the 3 stats from the table (*New Stat*: API name, type **INT**, *Set By* **Client**, **Increment Only** checked, default 0, the display name). Save each.
3. **Achievements** page: for each row of the table *New Achievement*: API name, display name, description, *Hidden* if the table says yes, the achieved icon and the unachieved (`_locked`) icon. For rows with a progress stat, set *Progress Stat* to that stat, minimum 0, maximum the amount. Save each.
4. **Publish**.

The API names must match exactly (they are in the content assets and never change after release).

### 2. Rich presence
**Edit Steamworks Settings → Stats & Achievements → Rich Presence**: upload `steam/rich_presence_english.vdf` (English). Publish.

### 3. Steam Cloud
**Edit Steamworks Settings → Application → Steam Cloud**:
- *Byte quota per user*: `104857600` (100 MB). *Number of files allowed per user*: `100`.
- *Enable cloud support for developers only*: checked while testing; uncheck it before release.
- **Root Overrides / Auto-Cloud** — add two paths:

  | Root | Subdirectory | Pattern | OS | Recursive |
  |---|---|---|---|---|
  | `WinAppDataLocalLow` | `Elevate Studios/ANTARIUM 2/Saves` | `*` | Windows | no |
  | `WinAppDataLocalLow` | `Elevate Studios/ANTARIUM 2` | `profile.json` | Windows | no |

  `settings.json` is **not** synced on purpose: resolution, window mode and UI scale belong to the machine (a desktop and a Steam Deck want different ones).
- Save, Publish.

### 4. Installation, depot and branch
- **Edit Steamworks Settings → Installation → General**: *Launch Options* → add one: executable `Antarium2.exe`, launch type *Launch (Default)*, OS *Windows*. Installation folder: `ANTARIUM 2`.
- **SteamPipe → Depots**: there is one depot (usually App ID + 1 = `4453941`). If its ID differs, change `$DepotId` in `steam/upload.ps1`. Its *Operating system* should be Windows, language *All languages*.
- **SteamPipe → Builds**: create a branch named `beta` (a password is optional; testers type it under Properties → Betas).
- Publish.

### 5. Upload account and steamcmd
- Upload with your own Steamworks account, or (Valve's advice) a separate Steam account added under **Users & Permissions** with only the *Edit App Metadata* and *Publish App Changes To Steam* rights for this app.
- Download steamcmd for Windows (`https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip`) and unzip it to `tools/steamcmd/` in the project (git-ignored). Run `tools/steamcmd/steamcmd.exe` once so it updates itself, type `quit`.

## Building

- **Antarium → Build → Apply Release Player Settings**: company / product name, bundle id, window defaults, single instance, no Unity splash. *Build* runs it when the names don't match.
- **Antarium → Build → Windows Release**: `Builds/Windows/Release/Antarium2.exe` (IL2CPP when the module is installed, else Mono; no debug keys, no debug readout). Burst / IL2CPP debug folders go to `Builds/Symbols/<version>/` and are never uploaded.
- **Antarium → Build → Windows Development**: `Builds/Windows/Development/`, with `steam_appid.txt` so it runs without being started by Steam (debug keys and readout on).
- The **version** is Player Settings → *Version* (shown on the title screen and in the upload description). Raise it before every upload.
- IL2CPP needs the Unity module *Windows Build Support (IL2CPP)* (Unity Hub → Installs → 6000.6.5f1 → Add modules) and Visual Studio with the *Desktop development with C++* workload.

## Uploading a build

```powershell
./steam/upload.ps1 -User <steam account>             # uploads Builds/Windows/Release and sets it live on 'beta'
./steam/upload.ps1 -User <steam account> -Preview    # dry run: what would be uploaded
```

The first login asks for the password and Steam Guard code in the terminal. Then on Steam: Library → Antarium 2 → Properties → Betas → `beta`, install, play. The `default` branch (what buyers get) is set live by hand under **SteamPipe → Builds**.

## Logs and crashes

- Player log: `%USERPROFILE%/AppData/LocalLow/Elevate Studios/ANTARIUM 2/Player.log` (`Player-prev.log` for the previous run).
- Crash dumps: `%TEMP%/Elevate Studios/ANTARIUM 2/Crashes/`.
- Matching debug symbols for a release: `Builds/Symbols/<version>/`.
