# PortForge

**Turn a Switch game file into a Windows game folder you can play from an `.exe`.**

PortForge is a Windows app (.NET Framework 4.8, no install needed) built around the open-source [ScarletNX](https://github.com/scarletnx/ScarletNX) ahead-of-time compiler. You pick a game file you own, PortForge runs the compiler, and saves the generated Windows executable, game-code DLL and assets in a folder.

> **Experimental.** A test program compiled and ran with zero JIT-compiled instructions, and Super Mario Odyssey 1.3.0 converted and played. Many games will not work, some use JIT fallback when static compilation misses code, and performance varies. PortForge does not contain any game, keys or a game catalog: you provide your own legally obtained game files and `prod.keys`.

## Features

- **Simple conversion screen** – game file, game name, save folder. Clear buttons beside every input, and a status card for progress, done, errors and cancelled.
- **Downloads** – paste a direct link: up to 8 connections, retries, pause/resume, checksum checks, ZIP unpacking, hand-off to conversion.
- **Graphics and FPS** – per-game FPS limit (10–1,000 or unlimited), render scale, texture filtering, shadows, VSync, plus an automatic benchmark tuner (local measurements, no AI model).
- **Automatic fixes** – reads a game's log, reports known problems, applies supported repairs with backups and undo, and can ask a locally installed Codex for an AI diagnosis. Engine-level glitches are reported, not fixed.
- **AI Doctor** – watches converted games while they run, explains every slowdown live (shader stutter, loading freezes, or other programs using the CPU), and when the game closes applies the supported repairs and asks Codex AI about new problems. It never changes a running game, and saves and settings are backed up first.
- **PortForge Engine** (Engine tab) – our own build of the ScarletNX runtime with **smart async shaders**: a new shader no longer freezes the game. On one game's opening with an empty shader cache, the worst freeze went from 4.2 seconds to 349 ms. Installs under any converted game with one click and keeps the original. See [engine/README.md](engine/README.md).
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
- [BENCHMARKS.md](BENCHMARKS.md), [ROADMAP.md](ROADMAP.md)
- `PACKAGER-0.1.md` / `src/App.cs` – the legacy emulator-packaging prototype, kept for reference only

## Legal

PortForge is a tool. You are responsible for only using game files and keys you are legally entitled to use. This project is not affiliated with Nintendo or the ScarletNX authors.
