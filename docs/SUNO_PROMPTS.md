# Suno prompts for Antarium 2

Prompts for generating the game's music, ambience beds and short stingers with Suno. They replace the synthesized placeholders from Milestone 8 (see GDD 2.14). Each track has a **Style** (paste into *Style of Music*), a **Structure** (paste into the *Lyrics* box with *Instrumental* on), and the shared **Exclude** list.

---

## Before you start

- **Rights:** check Suno's current terms before generating anything for the release. When this was written, only songs made on a **paid plan** could be used commercially, and the plan had to be active **when the song was generated**. Keep the song links / IDs as a record.
- **Steam disclosure:** Steam asks you to disclose AI-generated content in the content survey, and shows it on the store page. Add this to the Milestone 9 store checklist if you ship Suno tracks.
- **Instrumental:** always switch *Instrumental* on. Structure tags in the Lyrics box (`[Intro]`, `[Outro]`, `[End]`) are hints; Suno follows them loosely.
- **No artist names:** Suno refuses them. Describe instruments, mood and tempo instead.
- **Keys and tempos:** the keys below follow the in-game music: it starts in A near the surface, drops a semitone per biome and turns minor from the crystal caverns down. Suno doesn't always hit the key, but asking for it helps the tracks sit together. Generate several takes and keep the ones that fit.
- **Style length:** each Style is kept under ~200 characters so it works on any Suno version. Add the **shared anchor** below to the front if your version allows longer prompts.

### Shared anchor (the game's sound)

```
cozy underground pixel-art game soundtrack, instrumental, warm analog synth pads, kalimba, soft marimba, gentle, spacious, lo-fi tape warmth
```

### Exclude (every track)

```
vocals, lyrics, singing, rap, spoken word, EDM drop, dubstep, trap hi-hats, distorted electric guitar, epic trailer orchestra, loud brass
```

---

## 1. Title theme

The title screen, over the slowly drifting colony. Should loop or end cleanly.

- **Style:** `instrumental cozy adventure game title theme, warm analog synth pads, kalimba melody, soft marimba, gentle string swell, hopeful, curious, 76 bpm, A major`
- **Structure:**
  ```
  [Intro]
  [Instrumental]
  [Build]
  [Instrumental]
  [Outro]
  [End]
  ```

---

## 2. Background music per biome

These play in waves while you play (a few minutes of music, then quiet), so they should be calm and not tiring on repeat. Use 2–4 takes per biome.

| Biome (id) | Style |
|---|---|
| Surface and Topsoil (`topsoil`) | `instrumental ambient game music, earthy and warm, kalimba and nylon guitar plucks, soft synth pad, sparse pentatonic melody, calm, curious, 72 bpm, A major, no drums` |
| Clay and Groundwater (`clay`) | `instrumental ambient game music, underground water caves, marimba and soft mallets, dripping textures, warm pad, sparse pentatonic melody, calm, 70 bpm, G# major, no drums` |
| Stone Strata (`stone_strata`) | `instrumental ambient game music, deep stone caverns, low cello drone, felt piano, soft analog pad, slow and patient, warm mystery, 66 bpm, G major, minimal percussion` |
| Crystal Caverns (`crystal_caverns`) | `instrumental ambient game music, glittering crystal cave, glass bells, celesta, shimmering reverb pad, minor pentatonic, wonder and mystery, 64 bpm, F# minor, no drums` |
| Fungal Depths (`fungal_depths`) | `instrumental ambient game music, glowing fungus forest underground, wobbly warm synths, soft bubbling textures, dreamy, slightly eerie, 62 bpm, F minor, no drums` |
| Magma Zone (`magma_zone`) | `instrumental ambient game music, volcanic depths, deep rumbling bass drone, slow soft taiko pulses, dark synth pad, tense but restrained, 60 bpm, E minor` |
| Ancient Ruins (`ancient_ruins`) | `instrumental ambient game music, lost ancient civilization, hammered dulcimer, wordless choir pad, distant bells, solemn, mysterious, awe, 58 bpm, D# minor` |

**Structure (all biomes):**
```
[Intro]
[Instrumental]
[Instrumental]
[Break]
[Instrumental]
[Outro]
[Fade Out]
[End]
```

---

## 3. Situational music

| Use | Style | Notes |
|---|---|---|
| **Raid** (termites attacking) | `instrumental game battle music, tribal hand drums, driving low strings, urgent synth ostinato, tense, energetic but not epic, 120 bpm, A minor` | Should loop; it plays until the raid ends. |
| **Nuptial Flight** (princesses take off) | `instrumental emotional game cue, soaring strings, kalimba arpeggios, warm wordless choir pad, triumphant and bittersweet, rising, 80 bpm, A major` | The flight lasts ~4.5 s, then the genetic tree opens: keep the first 15–30 s and let it ring out under the tree. |
| **Genetic Memory tree** | `instrumental contemplative game menu music, music box, soft synth pad, gentle harp arpeggios, nostalgic, timeless, 68 bpm, D major` | Loops quietly while the panel is open. |
| **Deep discovery** (first time in a new biome) | `instrumental short game discovery cue, swelling pad, glass bells, single low gong, sense of awe, 70 bpm, minor` | Trim to 6–10 s. |

**Structure (raid and Genetic Memory, loopable):**
```
[Intro]
[Instrumental]
[Instrumental]
[End]
```

**Structure (one-shot cues):**
```
[Intro]
[Short Instrumental]
[End]
```

---

## 4. Ambience beds (no music)

Background layers under the music, one per biome. They are not melodies: just texture. Suno is weaker here than at music, so expect to generate several and use only the cleanest 20–60 s.

| Biome | Style |
|---|---|
| Surface | `ambient field recording style soundscape, gentle wind through grass, distant birds, no music, no melody, no rhythm, calm` |
| Topsoil | `dark ambient soundscape, muffled underground air, soft earth crumbling, occasional drip, no music, no melody, no rhythm` |
| Clay | `dark ambient soundscape, wet cave, steady water drips, distant trickling stream, no music, no melody, no rhythm` |
| Stone Strata | `dark ambient drone, deep stone cave resonance, low hum, rare echoing drip, no melody, no rhythm` |
| Crystal Caverns | `ambient drone, resonant crystal cave, soft glassy shimmer, singing-glass overtones, no melody, no rhythm` |
| Fungal Depths | `dark ambient soundscape, damp living fungus cave, soft bubbling, spore hiss, organic creaks, no melody, no rhythm` |
| Magma Zone | `dark ambient soundscape, volcanic cave, deep rumble, lava bubbling and hissing, no melody, no rhythm` |
| Ancient Ruins | `dark ambient drone, vast empty ancient hall, hollow wind, distant stone echoes, no melody, no rhythm` |

**Structure:**
```
[Ambient]
[Ambient]
[End]
```

---

## 5. Stingers (short musical cues)

Suno makes songs, not sound effects, so generate a short cue and **cut out the best 1–4 seconds**. Gameplay sounds (digging, belts, bites) are better from a sound-effect tool or recordings.

| Cue (`SoundCue`) | Style |
|---|---|
| `RaidWarning` | `instrumental short warning cue, low war horn, deep drum hit, tense, A minor` |
| `RaidStart` | `instrumental short cue, tribal drum roll, rising tension, A minor` |
| `RaidRepelled` | `instrumental short victory fanfare, kalimba and marimba flourish, bright, A major` |
| `Achievement` | `instrumental short reward jingle, sparkling bells, warm synth chord, satisfying, A major` |
| `Research` | `instrumental short discovery jingle, celesta arpeggio, soft pad, curious, bright` |
| `ContractClaim` | `instrumental short coin reward jingle, marimba, cheerful, two notes up` |
| `GeneticBought` | `instrumental short magical jingle, harp glissando, glowing pad, mystical` |
| `PrincessRaised` | `instrumental short royal jingle, music box and strings, gentle and proud` |
| `CreaturesFound` | `instrumental short ominous cue, low cello swell, dissonant pad, unsettling` |
| `EventStart` | `instrumental short alert jingle, glass bells, friendly, attention` |
| `EventLost` | `instrumental short sad cue, descending felt piano notes, soft` |
| `Flight` | `instrumental short soaring cue, rising strings and wing-like flutter, hopeful` |

**Structure:**
```
[Intro]
[Short Instrumental]
[End]
```

---

## Getting the files into the game

**Preparing each file**
1. Download as WAV.
2. Trim silence and cut the part you want (Audacity is enough).
3. For **loops** (ambience beds, raid, genetic tree, title), make the end run into the start: crossfade the last 1–2 s into the beginning, then listen to the loop point. Biome tracks don't need to loop: they play once and a rest follows.
4. Match loudness so nothing jumps out:
   - Music around −18 LUFS.
   - Ambience around −26 LUFS.
   - Stingers peaking near −3 dBFS.

**Naming and folders.** Put the files in `Assets/_Antarium/Audio/` (any subfolder; *Music*, *Ambience* and *Stingers* keep it tidy) and name them by what they are. Anything after the prefix is free (`_01`, `_take3`, ...):

| File name starts with | Used as |
|---|---|
| `music_surface` | Biome music above ground (and for the topsoil if it has none of its own) |
| `music_<biome id>` | Biome music, e.g. `music_topsoil_01`, `music_crystal_caverns_02`. Ids: `topsoil`, `clay`, `stone_strata`, `crystal_caverns`, `fungal_depths`, `magma_zone`, `ancient_ruins` |
| `music_title` | Title screen (loops) |
| `music_raid` | While a raid is on (loops) |
| `music_genetics` | While the genetic tree is open (loops) |
| `music_flight` | One cue when the princesses take off |
| `music_discovery` | One cue the first time the colony digs into a deeper biome |
| `amb_<biome id>` / `amb_surface` | The ambience bed of that biome (loops) |
| `sting_<SoundCue>` | Replaces that sound, e.g. `sting_RaidWarning`, `sting_Achievement` (names as in the stinger table) |

**Then run *Antarium → Audio → Assign Audio Files*.** It fills `Assets/_Antarium/Content/Audio/SoundBank.asset` from the folder and logs what it assigned. Run it again whenever you add, rename or remove files. Import settings are set automatically: music streams, ambience is compressed in memory, stingers are decompressed on load.

**How the music behaves**
- **Biome music** plays in waves: a track picked by the biome under the camera, then a rest of 1–2 minutes (*Rest Seconds* in the bank's music settings), then another track (never the same one twice in a row if there are several).
- A biome without tracks uses the nearest biome above it that has some, so a single `music_surface` file already covers the whole game.
- **Title, raid and genetic-tree tracks** loop and crossfade in and out (3 s, *Crossfade Seconds*). Without them, the biome music continues in those moments.
- Wherever no recorded music exists at all, the synthesized generative music plays as before.
- **Volume:** the music volume setting controls tracks and cues. Use *Track Volume* and *Cue Volume* in the bank for the overall mix.
