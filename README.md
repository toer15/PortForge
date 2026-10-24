# PortForge 0.35 / Engine 1.19

**New in 0.35:** Engine 1.19 fixes edge shimmer and corruption when AMD FSR 1 and PortForge frame generation are both on. FSR scratch images are ring-buffered per frame in flight, and real frames are upscaled before interpolation so FSR never runs on a generated picture. A numeric FPS limit is doubled for display cadence while frame generation is on, and the on-screen FPS counter counts display submissions (about 2x the game rate). New conversions ship Engine 1.19; existing games get it from the Doctor when closed. See [validation and limits](VALIDATION-0.35.md).

**0.34:** a global Settings tab applies preferences to updated games launched directly, focused fullscreen play hides the cursor, actual save writes show a Saving... indicator, and compute shaders are cached and prepared on later launches. F7/Escape shows a frame-time graph; optional CSV recording and an app viewer help investigate hitches. Experimental PortForge frame generation can be selected globally or toggled in Escape. It is not AMD FSR 3. Universal save-anywhere, native 720p-through-4K overrides and AMD FSR 2/3 remain unsupported.

**Lag fix for first plays:** a game whose GPU work fell seconds behind on a cold driver cache (the first run after a conversion) could get stuck at 3 to 5 FPS for good. The game gave up waiting for its frame and reused GPU command memory the engine had not read yet, so the engine ran garbage commands and later frames never completed. Engine 1.16 keeps the commands as the game submitted them and runs those when this happens. The automatic number of shader workers now also follows the PC's CPU cores on Windows, where every PC previously got two: an 8-core CPU gets four, for shorter loading freezes on a first run. On a Ryzen 7 9800X3D with a Radeon RX 9070 XT, one game's opening went from a permanent 3 to 5 FPS to its full 30 FPS after about 6 s of first-run loading freezes. Existing games receive Engine 1.16 from the Doctor when they are closed. Game code doesn't need rebuilding.

**Game menu:** Esc opens the engine's menu instead of quitting. Right-click opens the same menu, and either shortcut goes back through its pages and returns to the game. Controllers can use Home/Guide, or hold Back/Share/Minus and press Start/Options/Plus on the same pad. Use the arrows/D-pad/stick and Enter/controller confirm, or click an item. Select Exit to close the game. Keyboard repeats and the controls held while opening the menu cannot select an item accidentally. This is part of the bundled engine for new conversions and existing games updated by the Doctor, including games launched directly.

Startup compatibility: the engine reads each game's NPDM memory layout before allocating guest memory, including the 36-bit layout required by older games. Startup file mappings have a fixed budget, with read-on-demand fallback. The translator also supports the narrowing exclusive-store sequence used by older Tegra game code. Existing native modules containing the old translation must be rebuilt for that translator correction to take effect.

The Controllers window includes automatic detection, controller family selection, live buttons/triggers/sticks on a fixed picture, and a saved layout for converted games. It shows a Switch-to-controller button chart; game artwork and embedded button prompts are unchanged.

**Pixel smoothing** (per game, off by default) now really antialiases. Engine 1.12 replaces the weak neighbour blend of 1.11 with FXAA-style edge antialiasing on the game's own image before FSR, and FSR sharpens less while it is on, so staircase edges turn into smooth lines. In a GPU test the largest step of a jagged edge fell from a whole pixel to 0.44 (alone) and from 0.66 to 0.25 (with FSR, as smooth as that slope can be). In Mario, edge harshness on the tower and roller coaster fell by 22 to 25 percent with the sky unchanged. It cannot add detail the game did not render, and text softens a little.

**Performance** now selects the game's handheld rendering mode with AMD FSR 1, while keeping intermediate images at their protected native scale. This lets the game choose a lower-resolution layout without blindly shrinking images used as game data. Balanced and Quality restore TV mode. Auto tune compares the two modes and prefers TV quality when handheld provides no measured benefit. Entry-level PC matching starts in handheld mode. New conversions and downloaded inputs use the bundled engine; watched games use the backed-up upgrade path.

The graphics window shows the latest recorded FSR dispatch or bypass status, including actual source/output sizes. Merely preparing the GPU shaders does not count as active upscaling. FSR adds presentation work; potential performance gains come from the game's lighter rendering mode. Mario rendered correctly at 1280x720 in handheld mode versus 1600x900 in TV mode on the tested scene, but the comparison did not establish an FPS or 1% low improvement. Some games render identically in both modes. See VALIDATION-0.27.md. The runtime remains AOT/JIT; FSR 4, DLSS and frame generation are not implemented.

# PortForge

**Turn a Switch game file into a Windows game folder you can play from an `.exe`.**

PortForge is a Windows app (.NET Framework 4.8, no install needed) built around the open-source [ScarletNX](https://github.com/scarletnx/ScarletNX) ahead-of-time compiler. You pick a game file you own, PortForge runs the compiler, and saves the generated Windows executable, game-code DLL and assets in a folder.

> **Experimental.** A test program compiled and ran with zero JIT-compiled instructions, and Super Mario Odyssey 1.3.0 converted and played. Many games will not work, some use JIT fallback when static compilation misses code, and performance varies. PortForge does not contain any game, keys or a game catalog: you provide your own legally obtained game files and `prod.keys`.

## Features

- **Game speed lock** - Engine 1.6 always runs a game at its own pace: a higher FPS cap, Unlimited or F4 can't make a 30 or 60 FPS game run faster, and a lower cap shows fewer frames instead of slow motion. The PC's power goes into render scale and filtering instead. On by default, and the Doctor turns it back on if it is switched off.
- **Automatic engine frame guard** - Engine 1.4 shares optional shader waits across each frame, reserves compilation capacity for current draws, chooses worker counts on each launch, and resets stale pacing deadlines when FPS modes change. On by default for converted games, including Downloads handoff. Required game work and hardware limits can still stall.
- **Automatic cloud saves** - connect OneDrive or HTTPS WebDAV once per PC. Existing known games, new conversions and discovered converted games join automatically. A global switch pauses automatic saving, and each game can be excluded. Syncs while the app is open and the game is closed, verifies archives, keeps restore backups and asks which copy to keep when PCs have different progress. Nothing uploads before an account is configured; no PortForge-hosted account is included.
- **Simple conversion screen** – game file, game name, save folder. Clear buttons beside every input, and a status card for progress, done, errors and cancelled.
- **Downloads** – paste a direct link: automatic connection count, retries, pause/resume, checksum checks, ZIP unpacking, hand-off to conversion.
- **Automatic PC matching** – shows CPU, graphics adapters, dedicated graphics memory and usable RAM on startup. New conversions get supported settings for that PC, followed by optional measurements that favor FPS and fewer freezes. On by default, works for local and downloaded inputs, and needs no AI account. Each game gets a report separating hardware estimates from measured results.
- **Graphics and FPS** – per-game FPS limit (10–1,000 or unlimited), render scale, texture filtering, shadows, VSync, plus an automatic benchmark tuner that compares average FPS, freezes, worst frames and measured p99 frame time/1% low (local measurements, no AI model). Engine 1.7 keeps compatible shader caches across routine engine updates and reports per-frame GPU waits.
- **Automatic fixes** – reads a game's log, reports known problems, applies supported repairs with backups and undo, and can ask a locally installed Codex for an AI diagnosis. Engine-level glitches are reported, not fixed.
- **AI Doctor** – watches converted games while they run, explains every slowdown live (shader stutter, loading freezes, or other programs using the CPU), and when the game closes applies the supported repairs and asks Codex AI about new problems. It never changes a running game, and saves and settings are backed up first.
- **PortForge Engine** (Engine tab) – our own build of the ScarletNX runtime with background pipeline compilation to reduce some shader stalls, plus experimental background translation that is off by default. Game loading and compatibility problems can remain. Keeps the original engine for restore. See [engine/README.md](engine/README.md).
- **AI Engine Lab** – Codex writes engine changes for a game's recorded problems; PortForge builds and measures each one and keeps it only when stutter measurably drops.
- **Check for updates** – the header button reads this repository's latest release, shows what is new, and installs it (your games, settings and saves are kept; the old program is backed up).

## Download

Get **PortForge-…-Windows-and-Source.zip** from the [Releases](../../releases) page, extract it, and open `PortForge.exe`. Read `README.md` inside for the user guide.

## Build from source

Requires Windows and the built-in .NET Framework compiler (no Visual Studio needed).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Output: `build\PortForgeNative\PortForge.exe`. To run the test suites (needs an extracted ScarletNX Windows build):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -BackendPath C:\Tools\ScarletNX\bin\scarletnx.exe
```

## Releasing a version

1. Raise the version in `src/AssemblyInfo.cs`, build with `-Test`.
2. Zip the release folder as `PortForge-<version>-Windows-and-Source.zip` (must contain `PortForge.exe`).
3. Create a GitHub release tagged `v<version>` and attach the ZIP. The in-app **Check for updates** button picks it up.

## More documentation

- [USERGUIDE.md](USERGUIDE.md) – how to use the app
- [NATIVE.md](NATIVE.md) – setup, evidence, limitations
- [VALIDATION-0.24.md](VALIDATION-0.24.md) – Engine 1.7 and p99 tuning checks and limitations
- [BENCHMARKS.md](BENCHMARKS.md), [ROADMAP.md](ROADMAP.md)
- `PACKAGER-0.1.md` / `src/App.cs` – the legacy emulator-packaging prototype, kept for reference only

## Legal

PortForge is a tool. You are responsible for only using game files and keys you are legally entitled to use. This project is not affiliated with Nintendo or the ScarletNX authors.

## Install

Download **PortForge-Setup-<version>.exe** from the latest release and run it. It adds Start menu and desktop shortcuts with the PortForge logo and an uninstaller, and needs no administrator rights. The ZIP (`PortForge-<version>-Windows-and-Source.zip`) holds the same app as loose files, with the source.
