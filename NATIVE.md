# PortForge 0.6: Switch native conversion

The requirement is actual conversion of Switch game code into Windows PC code. The older 0.1 emulator packager did not meet it. This frontend invokes ScarletNX's AOT compiler, verifies generated x64 PE binaries, and publishes the generated game executable, game-code DLL, assets and runtime libraries.

## Current evidence and limits

- A synthetic Switch-format AArch64 NRO was compiled by the downloaded Windows backend. Its exported executable printed the test marker, exited successfully, and reported **zero JIT-compiled instructions** for that test.
- The user's local **Super Mario Odyssey 1.3.0** input was extracted and compiled successfully with 0.4. The completed output contains 4,972 RomFS asset files, including the previously reported `EffectDataBase.szs`. A bounded launch rendered 774 frames, reached the mode-selection screen, and exited with code 0. The runtime reported 12 JIT batches compiling 67 instructions during that test.
- Startup is verified for that input on this PC. The user subsequently reported working gameplay, with lag and a 60 FPS ceiling. Our bounded tests do not establish complete gameplay, audio, controller, save or other-game compatibility.
- ScarletNX includes JIT fallback for code its static analysis missed. PortForge does not certify fully static execution. The output runtime still supplies Switch system services and translates graphics operations.
- Output is a Windows executable **and accompanying files**, as with many PC games. It is not a single asset-free executable. The backend may use the build PC's CPU features, limiting portability to other CPUs.
- No PS5 conversion backend is implemented.

## On this PC

Use **Downloads** for a direct game-file or ZIP URL. The completed file can be selected and converted automatically using the existing conversion settings. This changes acquisition/recovery behavior; it does not extend the native compiler's game compatibility.

1. Extract the entire ZIP and open `PortForge.exe`.
2. The verified local ScarletNX backend should be selected automatically through `backend-location.txt`.
3. Choose a game file, or use **Folder** for an extracted game containing `exefs` and `romfs`. Input headers are checked, then the backend validates the contents. Use **Extra settings** for the conversion engine and locally supplied key files. Encrypted NSP/XCI inputs need your keys; no keys are included or downloaded.
4. Choose a game name and save folder, then **Start conversion**.
5. The process extracts containers, compiles the game code ahead of time, separates runtime DLLs from any backend hard links, and moves the complete game into its final folder. This avoids an extra full copy of large game assets. The compiled game can be launched from its own executable; PortForge is not needed for subsequent launches.
6. The status card shows **Done** when files are ready. **Open game folder** shows them. Keep the whole folder together. A complete build is not a gameplay compatibility verdict.

The **Try a test file** button in Extra settings selects an original synthetic NRO. It is a CPU test which prints to the backend log and exits, not a playable game or graphics demo. The test's log is beside its output executable.

Every input has its own Clear button. The status card remains visible beside the form while advanced settings scroll. It reports actual conversion stages and persistent completion, error or cancellation states. The technical details have Open full log, Copy details and Clear details buttons. Clearing the details does not remove the main result. Missing prod.keys/title.keys diagnostics explain the next step and offer a direct file picker. The default main screen shows only game file, game name and save folder.

Build logs are preserved in the chosen output folder. Existing game output folders are not overwritten. Cancellation stops the compiler process tree; temporary files are cleaned when possible. If compilation completed but publication failed, the compiler output is preserved and the error card offers **Open saved work**. Cleanup problems are warnings and never replace the primary error. When automatic tuning is enabled, the frontend runs bounded headless tests of the generated game before publication.

## Graphics and measured tuning in 0.5

The frontend queries `--settings` on the selected game's executable and checks the required controls before saving. INI edits use invariant numbers, replace duplicate target keys, preserve unrelated settings and comments, and use an atomic replacement with a dated backup. A running game blocks edits. The executable and game-code DLL are not modified by graphics tuning.

The tuner uses an eight-second startup warm-up followed by four 20-second headless startup trials. It records `frames=N` and available renderer slow-frame summaries. Samples include loading; mean FPS is rendered frames divided by the requested sample duration, not a 1% low or steady gameplay measurement. Runs that fail, report no frames or exit early are rejected. Profiles within 5% of the target, or 3% of the fastest measured profile if the target is unreachable, are ranked by resolution then texture filtering. CPU worker auto-sizing remains the backend's default. The tuner does not turn off correctness features, bypass compute work, raise process priority or invent unsupported runtime options.

Timed CLI overrides temporarily disable VSync and use the requested test target. Chosen persistent FPS and VSync values are preserved. The exact original INI bytes are restored after success, cancellation or failure; a recovery snapshot remains if restoration fails. Cache warming and normal runtime logging remain, as do any normal save changes the game itself makes during startup. Existing-game tuning returns a recommendation for the user to save; conversion tuning applies its recommendation automatically and treats unsupported or failed tuning as a warning.

Automatic selection never raises render scale above the player's current choice based on startup performance. No AI model, game-specific timing patch or binary optimization pass is included. Raising a presentation limiter can alter game speed or leave an intrinsic game cap intact. Menu performance does not certify gameplay. See [USERGUIDE.md](USERGUIDE.md) for the controls and limitations.

## Downloads and recovery in 0.6 / 0.48

HTTP transfers use the framework's HTTP stack with normal certificate validation. Metadata is probed using a one-byte range with a normal-request fallback. Parallel transfers require a known size, byte ranges and a strong ETag. Each range must have the exact requested Content-Range and validator. Interrupted parts resume using If-Range; changed validators discard only the fixed temporary part files and restart. These rules follow [HTTP range and validator semantics](https://www.rfc-editor.org/rfc/rfc9110.html#name-range-requests). From 0.48, automatic connection choice starts at eight connections with ~1 s samples (~2 s total) and remembers the winner for twelve hours; transfer and assemble use multi-megabyte sequential buffers.

The app stores fixed part filenames plus XML metadata in a destination-specific work folder, protected with an exclusive file lock. URLs are represented by hashes in metadata so signed query strings are not persisted there. Request failures have bounded retries and cancellation aborts active requests. Servers rejecting ranges fall back to a single complete transfer; unsafe resume without a validator is avoided. Final files are assembled separately, hashed, checked for supported headers/ZIP directory structure and moved into place without overwriting existing files. Local checksum receipts enable verified reuse. Source checksum or structural failures get at most one fresh download per attempt. Without a source checksum, content authentication is limited to headers/structure and transport behavior.

ZIP extraction preflights total uncompressed size and every target path. Absolute paths, traversal, alternate-stream names, duplicate targets and symlinks are rejected. The original ZIP is preserved and a unique folder receives the unpacked files. No archive scripts or included installers are executed. RAR/7z extraction, authenticated browser sessions, source discovery and arbitrary source/engine repair are not implemented.

Publication and atomic settings/metadata replacement retry selected Windows sharing/replacement errors for a bounded period. Cancellation interrupts retry waits. Disk exhaustion and permanent errors still get actionable failures; completed compiler output remains recoverable. Download and interface tests use a local controlled HTTP fixture, with no commercial ROM downloads.

## Conversion speed in 0.48

Native conversion asks ScarletNX for `HardwareProfile.CompilerJobsFor` workers (logical CPUs minus one, two to thirty-two) instead of a fixed `--jobs 2`, and no longer passes `--keep-ir`. The finished game folder and correctness checks are unchanged; build logs record the job count.

## Path handling in 0.4

The reported `EffectDataBase.szs` error was a secondary cleanup failure. The original build log recorded a 260-character path limit while copying the already-compiled output. The 0.3 cleanup then attempted to remove those long paths and replaced the primary exception with a missing-path error.

The frontend and tests now target .NET Framework 4.8, opt into modern path handling and long paths in runtime configuration, and carry a longPathAware Windows manifest. No machine-wide settings were changed. The test PC already has Windows long paths enabled. See [Microsoft's path compatibility documentation](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/retargeting/4.6.x#long-path-support).

Regression checks publish an original synthetic `EffectDataBase.szs` asset at a path over 260 characters, preserve its content and empty directories, separate a real hard-linked DLL, and remove a deep staging tree. A locked temporary file also verifies that cleanup cannot hide the real compiler error.

## Backend setup on another PC

Download the Windows ZIP from the official [ScarletNX nightly releases](https://github.com/scarletnx/nightly/releases), extract it in its own directory, then select `bin/scarletnx.exe` in PortForge. The complete backend directory must stay together. No system-wide toolchain installation was needed for the tested release. Runtime display support requires a compatible Vulkan driver.

Tested release: `nightly-2026-10-21-0de0a87`.

Windows ZIP SHA-256: `b5fc8c4db44184609ea78baaee9fd3a38677b0eb9b82790b58e49aa8efc20256`.

The backend is third-party GPLv3 software, downloaded from its official release. It is not included in this frontend ZIP. Its source, license and build instructions remain available in the [ScarletNX repository](https://github.com/scarletnx/ScarletNX). For distributing compiled outputs, review the licenses of their included libraries and rights to their game assets.

## Build this frontend

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-native.ps1
```

Requires .NET Framework 4.8 and uses its installed C# compiler. Keep `PortForge.exe.config` beside the executable. The synthetic NRO is generated from `tests/Create-NativeFixture.ps1`. `-Test` runs native integration, interface and optimization tests with the local backend; supply `-BackendPath C:\Tools\ScarletNX\bin\scarletnx.exe` for another location. The source includes `src/NativeApp.cs`, `src/NativeConversion.cs`, `src/ConversionFiles.cs`, `src/ConversionFeedback.cs`, `src/GameSettingsWindow.cs`, `src/GameOptimization.cs`, and path/argument helpers in `src/Core.cs`.

The next acceptance test is representative Super Mario Odyssey gameplay, including audio, input and saves. The conversion and startup checks above do not establish fully static execution or complete game compatibility.
