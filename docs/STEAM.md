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
- **The demo** is the same code built with `ANTARIUM_DEMO` as its own product and Steam app: see [The demo](#the-demo).
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
- **Installation → Redistributables**: tick **none**. The build is IL2CPP with the C/C++ runtime linked in (no `vcruntime`/`msvcp` imports in `GameAssembly.dll`, `UnityPlayer.dll`, `baselib.dll`, the Burst and Steam DLLs), it renders with D3D11/D3D12 from Windows, and XInput comes as `xinput1_4.dll` with Windows 10/11, so DirectX June 2010, Visual C++, .NET, XNA, OpenAL and PhysX are all unused. Check again with `grep -aoiE '(vcruntime|msvcp)[0-9_]*\.dll' Builds/Windows/Release/*.dll` if the scripting backend or a native plugin changes.
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

## The demo

The Steam Next Fest demo (GDD 2.16) is the same project built with `ANTARIUM_DEMO`: the first colony down to the Ancient Seal at the bottom of the stone strata, no achievements, Wishlist buttons. On Steam it is **its own app** (a demo app linked to Antarium 2): **App ID 5676510**, depot 5676511 (App ID + 1; check it under SteamPipe → Depots).

**What the code does.** *Antarium → Build → Windows Demo* builds `Builds/Windows/Demo/Antarium2Demo.exe` with the define, product name *ANTARIUM 2 Demo* and bundle id `com.elevatestudios.antarium2.demo` (both only for the build; Player Settings go back to the full game's afterwards), so the demo's saves, settings and profile live in `LocalLow/Elevate Studios/ANTARIUM 2 Demo/`. It starts Steam with `DemoConfig.demoAppId` (`Assets/_Antarium/Content/DemoConfig.asset`); while that is **0** the demo runs without Steam and *Wishlist on Steam* opens the store page in the browser. With Steam it opens the full game's page (`fullGameAppId`, 4453940) in the overlay. No achievements, stats or rich presence are sent from the demo. *Windows Demo (Development)* adds `steam_appid.txt` with the demo's App ID. The full game's title screen offers *Continue your demo colony* when it has no saves and finds one in the demo's folder (same PC only: Steam Cloud is per app).

**One-time setup (you):**
1. ✅ (App ID 5676510) Steamworks → Antarium 2 → **Create demo** (on the app's landing page, under *All Associated Packages, DLC, Demos and Tools* or the *Demo* section; Valve's wording changes). It gets its own App ID; the depot is usually that ID + 1.
2. The demo's store page is short: it inherits most of the main page. Fill in what Steamworks asks for (capsules can reuse the main ones with a *Demo* tag). Check *Steam Next Fest* rules for the demo (a playable build, no time-limited trials that hide the game, available during the fest).
3. **Installation → General**: launch option `Antarium2Demo.exe`, *Launch (Default)*, Windows. **Redistributables**: none (as the main app). Steam Cloud is not needed for the demo.
4. The App ID is in `DemoConfig.asset` → *Demo App Id* (5676510) and in `steam/upload.ps1` (`$DemoAppId` 5676510, `$DemoDepotId` 5676511: change the depot there if Steamworks shows another). Rebuild (*Antarium → Build → Windows Demo*).
5. Upload: `./steam/upload.ps1 -User <account> -Demo` (`-Preview` for a dry run; as for the main app the first upload may need the `default` build set live by hand before a `beta` branch can be made). Install the demo from Steam, start it, check `LocalLow/Elevate Studios/ANTARIUM 2 Demo/Player.log`, the Wishlist button (overlay) and that nothing unlocks achievements.
6. Publish the demo (Valve reviews it like any build; allow several business days, more over the holidays), then post a Steam event on the main page.

## Store page media and text

Screenshots, GIFs and the trailer are rendered by the game itself, so they can be redone after any change.

**Text** (`steam/store/`): `short_description.txt` (258 of 300 characters, opens with the Old Colony story) goes in *Store Page Admin → Description → Short description*; `about_this_game.txt` (Steam BBCode: the story in italics, then 9 sections with a GIF each) goes in *About This Game*. Its GIF slots read `[img]{STEAM_APP_IMAGE}/extras/<name>[/img]`, **without** `.gif`: Steam stores uploaded images under their name without the extension. Upload the GIFs first with the editor's image button, check the path it inserts matches the slot (otherwise put that path in place of the slot), and preview before publishing.

**Localized store text** (`steam/store/<language>/`, the same two files per language; folder names are Steam's language codes): `schinese`, `tchinese`, `russian`, `spanish` (Spain), `latam` (Latin America), `brazilian`, `german`, `french`, `japanese`, `koreana`, `polish`, `turkish`. On *Store Page Admin → Description*, switch the language selector at the top of the page to each language and paste its two files; the GIFs are shared by all languages, so the slots work as is. Leave *Supported languages* at **English only**: the store text is translated, the game is not, and each *About* ends with a line saying so (players who buy expecting their language refund and review badly). After changing the English text, redo the translations; Steam shows English to languages without their own text.

**Capturing** (editor):
1. *Antarium → Capture → Rebuild Showcase and Run All Store Shots* (or *Run All Store Shots* to reuse the last showcase colony; *Run Every Shot* also writes the review stills of each milestone). Play mode starts, runs every store shot and stops by itself, about 10 minutes (plus ~2 minutes to build the showcase). **Keep the Unity window in front** meanwhile: play mode only runs while the editor has focus.
2. Output in `Captures/` (git-ignored): `stills/NN_*.png` (29 screenshots, 1920×1080 with the HUD, numbered in the suggested upload order: the first five show on the store page; `intro_*.png` are checks of the opening intro, not screenshots), `clips/<name>/NNNNN.png` (1080p, 60 fps), `capture_log.txt`.
3. GIFs: `python tools/media/make_gifs.py` → `Captures/gifs/*.gif` (9 GIFs, 640×360, 15 fps, at most 3 MB each, about 12 MB together): colony, dig, factory, sky (starfall and storm), depths, ruins (glow ants), raid, possess, flight.
4. Trailer: `python tools/media/make_trailer.py` → `Captures/trailer/antarium2_trailer.mp4` (~106 s, 1920×1080, 60 fps, H.264 + AAC, music `music_title.wav`). It opens with the Old Colony's story on cards in the game's body font (lines from the lore fragments) over the dark ruins, then the meadow, the queen and the beats *Dig deep*, *Build a living machine*, *Day, night and storm*, *Descend*, *Defend the nest*, the Old Colony's last message and the Engine waking, *Raise a dynasty*. The end card shows the game logo (`Assets/_Antarium/Icon/shortcut.png`, `--logo` for another) over "Wishlist now on Steam" and the Elevate Studios logo (`elevate_white_logo.png`, `--studio`); `--music <wav>` picks another track. This is the **general** cut: on Steamworks set its category to *General / Cinematic*.
   **Gameplay trailer**: `python tools/media/make_trailer.py --cut gameplay` → `Captures/trailer/antarium2_gameplay_trailer.mp4` (~67 s). It opens on the player digging and stays in the game until the end card: no story or title cards, short captions over the footage instead (*Dig your nest*, *Grow a colony*, *Build a living machine*, *Day, night and storm*, *Descend*, *Defend the nest*, *Take control of any ant*, *Wake the Ancient Engine*, *Raise a dynasty*). Set its category to *Gameplay* and **drag it first** in the trailer list: Valve recommends a gameplay trailer first, the first two trailers play before the screenshots and the store's 6-second microtrailer is cut from the first one. Both edits (clips, cards, captions, lengths) are `CUTS` at the top of the script.
5. Trailer thumbnails: `python tools/media/make_thumbnail.py [--cut gameplay|general]` → `Captures/trailer/antarium2_gameplay_trailer_thumbnail.png` (the colony in cross-section, `g_colony/00090`) or `antarium2_trailer_thumbnail.png` (the storm over the hills and the nest, `s03_storm/00120`). Steam only takes **a frame from the video itself** (1920×1080, no text advised), so the script writes the captured frame unchanged and refuses one the cut does not show; `--frame clip/NNNNN` picks another.
6. The scripts need Python 3 with `pip install pillow imageio-ffmpeg` (imageio-ffmpeg brings its own ffmpeg); `make_shorts.py` also needs `numpy`.

### Short-form videos (TikTok, YouTube Shorts, Instagram Reels)

Vertical videos to drive wishlists, cut from footage the game renders natively in 9:16.

1. *Antarium → Capture → Run Short-Form Video Shots* (capture set `shorts`; about 7 minutes, Unity in front). It writes `Captures/clips/v_*` (1080×1920, 60 fps, HUD off): the shots are in `GameBootstrap.Shots.Shorts.cs`.
2. `python tools/media/make_shorts.py` → `Captures/shorts/NN_name.mp4` (25 shorts, 7–24 s, H.264 1080×1920 60 fps, AAC 48 kHz, about -14 LUFS: fits TikTok, Shorts and Reels as is), plus `posting_guide.md` and `posting.csv` with a YouTube title, a caption and hashtags for each. `make_shorts.py 02 storm` makes only those, `--list` prints the slate, `--preview` writes a contact sheet per short instead of a video. The slate (clips, hook texts, music, sound balance, posting text) is `SHORTS` at the top of the script.
3. **Sound**: the game's own effects in sync with the frames (the capture logs every sound per frame to `clips/<clip>/sounds.tsv` and writes the synthesized samples to `clips/_sounds/`), under a track from `Assets/_Antarium/Audio/Music`, balanced per short in dB against the music.
4. **Posting**: put the Steam link in each profile's bio (TikTok and Instagram captions don't link; on YouTube link the Steam page in the description). Post the same file on all three. Text stays inside the area the apps leave clear (below the top tabs, above the caption, left of the button column).

**How it works:** `ShowcaseColony` (Sim) builds a fixed-seed colony with the instant showcase edits in `Simulation.Showcase.cs`: nest with light wells, storage hall, nurseries, a factory hall (ore room → belt → root lift → smelter → archive, gardens, princess chamber), a shaft to the ruins with an outpost in each deep biome, every caste and research; then it runs 90 s so the colony is busy, and is saved to `Captures/_showcase/`. The shots (`GameBootstrap.Shots.cs`; the Milestone 10 / 11 and story shots *surface*, *story*, *living* and *intro* in `GameBootstrap.Shots.Store.cs`) load it, move the camera, act like a player (digging with a drawn cursor, possessing an ant, abilities, a raid, the Nuptial Flight, the research and genetics screens) and record through `CaptureRig`, which renders world and UI into render textures at a fixed size and frame rate (independent of the window), so every clip is smooth however long a frame takes. A capture run never touches the player's save, settings, profile or Steam, and unlocks no achievements. *Antarium → Capture → Write Showcase Map* writes the whole showcase world as one image for planning shots.

A development build runs the same capture from the command line: `Antarium2.exe -nosteam -capture stills,raid -captureOut D:/media [-rebuildShowcase]` (`-capture store` for every store shot).

**Steam limits** (checked November 2026): screenshots at least 1920×1080 (at least 5); trailer up to 1920×1080, 30 or 60 fps, 5000+ kbps, `.mp4`/`.mov`/`.wmv`; GIFs in the description have no hard limit, but Valve may remove very heavy ones (around 15 MB per page or more), so they are kept small.

## Logs and crashes

- Player log: `%USERPROFILE%/AppData/LocalLow/Elevate Studios/ANTARIUM 2/Player.log` (`Player-prev.log` for the previous run).
- Crash dumps: `%TEMP%/Elevate Studios/ANTARIUM 2/Crashes/`.
- Matching debug symbols for a release: `Builds/Symbols/<version>/`.
