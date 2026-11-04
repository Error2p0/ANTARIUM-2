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
- **SteamPipe → Builds**: create a branch named `beta` (a password is optional; testers type it under Properties → Betas). **Steamworks only allowed this once a build had been set live on `default`**, so this step comes after the first upload: see [First upload](#first-upload).
- Publish.

### 5. Upload account and steamcmd
- Upload with your own Steamworks account, or (Valve's advice) a separate Steam account added under **Users & Permissions** with only the *Edit App Metadata* and *Publish App Changes To Steam* rights for this app.
- Download steamcmd for Windows (`https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip`) and unzip it to `tools/steamcmd/` in the project (git-ignored). Run `tools/steamcmd/steamcmd.exe` once so it updates itself, type `quit`.

## Building

- **Antarium → Build → Apply Release Player Settings**: company / product name, bundle id, window defaults, single instance, no Unity splash. *Build* runs it when the names don't match.
- **Antarium → Build → Windows Release**: `Builds/Windows/Release/Antarium2.exe` (IL2CPP when the module is installed, else Mono; no debug keys, no debug readout). Burst / IL2CPP debug folders go to `Builds/Symbols/<version>/` and are never uploaded. Unity's symbol uploader (`usymtool`, log in `%LOCALAPPDATA%/Unity/Editor/symbol_upload.log`) reads the IL2CPP folder for about 20 seconds after the build, so the build keeps retrying the move for up to 2 minutes; if it still fails it logs a warning and leaves the folder in place, where `upload.ps1` excludes it. Rebuilding the same version replaces that version's symbols, which is another reason to raise the version before every upload.
- **Antarium → Build → Windows Development**: `Builds/Windows/Development/`, with `steam_appid.txt` so it runs without being started by Steam (debug keys and readout on).
- The **version** is Player Settings → *Version* (shown on the title screen and in the upload description). Raise it before every upload.
- IL2CPP needs the Unity module *Windows Build Support (IL2CPP)* (Unity Hub → Installs → 6000.6.5f1 → Add modules) and Visual Studio with the *Desktop development with C++* workload.

## Uploading a build

Run these in a normal PowerShell window, **not at the `Steam>` prompt**. The script starts steamcmd itself, so close any open steamcmd first (`quit`).

```powershell
cd "E:\Unity Projects\ANTARIUM 2"
Set-ExecutionPolicy -Scope Process Bypass            # once per window; Windows blocks scripts by default

./steam/upload.ps1 -User <steam account> -Preview    # dry run: uploads nothing
./steam/upload.ps1 -User <steam account>             # uploads Builds/Windows/Release and sets it live on 'beta'
./steam/upload.ps1 -User <steam account> -Branch ""  # uploads without setting it live on a branch
```

The first login asks for the password and Steam Guard code in the terminal; steamcmd remembers the login afterwards. Then on Steam: Library → Antarium 2 → Properties → Betas → `beta`, install, play. The `default` branch is set live by hand under **SteamPipe → Builds** (the script refuses to do it).

**Dry run check:** open `steam/output/log/4453941_preview.manifest.txt` and make sure no `*DontShip*` / `*DoNotShip*` files, `.pdb` files or `steam_appid.txt` are listed. For reference, 0.9.0 was 35 entries, 142 MB.

### First upload

A new app has no `beta` branch, and Steamworks did not offer to create one until a build was live on `default`. The order that worked for 0.9.0 on 2026-11-04:

1. Upload. With no `beta` branch the upload itself succeeded but the commit failed with exit code 6 (see below).
2. **SteamPipe → Builds**: set a build live on `default`.
3. **SteamPipe → Builds**: create the `beta` branch.
4. Upload again with the normal command. The content was already on Steam, so this took seconds: BuildID **26441965**, v0.9.0 (`106b24a`), live on `beta`.

Using `-Branch ""` for step 1 should avoid the failed commit, but this has not been tried.

This only had to be done once. **BuildID 26441965 has a startup bug and must be replaced:** every launch hit a `NullReferenceException` in `GameBootstrap.ShowTitle` because the UI was built before the `UIDocument` had a root. This only shows in player builds, never in the editor, so always start a build outside the editor before uploading it and check `Player.log`. Fixed in the code; a build from the fixed code has not been uploaded yet. Still to do: raise the version, build Windows Release, upload, set it live on `default` by hand, install it from `beta` and test it (`docs/TODO.md`).

### Upload problems

| What you see | Cause | Fix |
|---|---|---|
| `command not found` after typing `./steam/upload.ps1` | Typed at the `Steam>` prompt, which only takes steamcmd commands | `quit`, then run it in PowerShell |
| `running scripts is disabled on this system` | Windows' default script policy | Run the `Set-ExecutionPolicy` line above |
| `steamcmd failed with exit code 6`, and `Failed to commit build for AppID 4453940 : Failure` in the log | The content uploaded, but the commit failed. Here the branch in `SetLive` did not exist; the log does not name the cause | Create the branch and upload again. If it still fails, try `-Branch ""`; if that fails too, check the account's *Publish App Changes To Steam* right and that the Steamworks changes are published |

Logs: `steam/output/log/app_build_4453940.log` (the commit result), `steam/output/log/depot_build_4453941.log` (files and chunks), `tools/steamcmd/logs/console_log.txt` (the whole steamcmd session). The first two are overwritten by each run.

## Logs and crashes

- Player log: `%USERPROFILE%/AppData/LocalLow/Elevate Studios/ANTARIUM 2/Player.log` (`Player-prev.log` for the previous run).
- Crash dumps: `%TEMP%/Elevate Studios/ANTARIUM 2/Crashes/`.
- Matching debug symbols for a release: `Builds/Symbols/<version>/`.
