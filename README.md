# PortForge 0.25 / Engine 1.8

Play opens your game or returns to the existing window; background settings probes no longer count as gameplay. The Graphics and performance window now includes **AMD FSR 1 spatial upscaling**, with a Linear off switch and per-game settings. It applies automatically on Engine 1.8 when the rendered SDR image is smaller than the game window. Try 75% render scale to reduce GPU load. New conversions and the existing backed-up engine upgrade path include it.

344 application checks, 10 installer checks and five selected engine suites cover this release. Mario launched on this PC with FSR actually active; short samples did not establish a game-wide speedup. **FSR 4, DLSS and frame generation are not implemented.** There is no promise of doubled FPS, fixed 1% lows, zero lag, or fully static native execution; the engine retains AOT/JIT translation. See VALIDATION-0.25.md for the test scope.

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
