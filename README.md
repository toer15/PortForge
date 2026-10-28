# PortForge 0.69 / Engine 1.50

**New in 0.69:** the top right corner tells you how old the game's save is, in every game. Engine 1.50 reads the save files the game writes (`save/system`, `save/device`, `save/user*` in its data folder), stamps the moment of the newest write, and shows the age in the picture's corner and in the Escape menu's head band: **Saved just now**, **Saved 4 min ago**, **Saved 3 h ago**, **Saved 3 days ago**. It is right from the first second of a session - the engine indexes the save files at startup and counts from the newest one - and it keeps reading while you play, so a save the game makes at a checkpoint, an autosave, or on its own save screen updates the line within a frame: **Saving...** while a write is in flight, **Save failed** in red if one fails. That is the game's own save data, not a save the engine invented: a menu button cannot make a game write a save it isn't at a save point for, and true save-anywhere needs save states of the whole emulated machine, which the engine does not have (`VALIDATION-0.53.md`). What the line gives you is a live, honest answer to "when did this game last save?". See [VALIDATION-0.69.md](VALIDATION-0.69.md).

**0.68:** no FPS limit changes a game's speed, in any game, automatically. Engine 1.49 gives every game its own pace whatever the limit is set to - Game speed, any number from 10 to 1000, Maximum - so a game made for 30 FPS still runs its world at 30 while the screen receives up to 240 pictures a second: its own frames, and with AMD FSR 3 up to the limit typed (240 shows 240 in a 60 FPS game, 144 stays 144), capped by the limit and, under VSync, the panel's refresh. The engine no longer lets a leftover `speed_lock=0` reach the pacing (the app now writes `1` for every limit), and a game that times itself is paced only by a number, Maximum leaving it to its own timers. Pacing also moved to the microsecond clock: the last 0.8 ms of every frame's wait is decided on `plat_ns`. Measured at 240 Hz over five one-second runs on this PC, a timer-only wait landed a mean 293 to 331 us late (p99 616 to 1413 us, worst 1662 us); the new wait a mean 0.1 to 8.3 us (p99 0.1 to 79 us), so a 4.17 ms frame is 4.17 ms apart instead of 4.17 ms with a third of a millisecond of judder on it. When Windows deschedules the thread mid-wait no wait can help, and that frame is late (490 to 970 us in one run that overlapped a build). See [VALIDATION-0.68.md](VALIDATION-0.68.md).

**0.67:** every game gets the best-performance profile **without being asked**. When there is no settings file, or one still holding the old Game speed default, the app writes **240 FPS** - the game itself running at up to 240, as a PC game at a 240 cap, with AMD FSR 3 filling the pictures it cannot make and the in-game frame-time graph on - and every updated runtime reads that file at its own launch, so a game played straight from its own folder gets it too, with no per-game setup. A game's own Esc menu still changes its limit for that session, and anything you saved (another limit, FSR 1, the whole layer switched off) is never overwritten. Engine 1.48's rules are unchanged: a number is the game's own frame rate, FSR 3 doubles the pictures up to it and the monitor's refresh under VSync. See [VALIDATION-0.67.md](VALIDATION-0.67.md).

**0.66:** the FPS limit now drives the game itself, as a PC game's FPS cap does, instead of only choosing which of its pictures the display shows. **Maximum** (the top of the list, `fps_limit=0`) waits for nothing at all: the game runs as fast as your PC can render it, so a game made for 30 runs far above 30 until the machine or the panel stops it, and you can then pick a number to cap that rate. A typed number is the game's own frame rate (a 30 FPS game at 120 runs four times its console speed, its physics, timers and music following its frames, exactly as PC games do above their design rate). **Game speed** keeps the console's 30 or 60 with the speed lock and lets AMD FSR 3 frame generation smooth it instead. Frame generation off means the screen shows the game's own frames; on, it doubles them, VSync caps them at your display's refresh, and with VSync off nothing does (your "free FPS"). The Doctor no longer turns the speed lock back on (it is a choice now: the app writes it for Game speed, and never for a number or Maximum), and Esc reads `Maximum` with the live rate the game reaches. See [VALIDATION-0.66.md](VALIDATION-0.66.md).

**0.65:** Unlimited means as many pictures as the engine can make, instead of the game's frames doubled. Unlimited used to be treated as Game speed (120 in a 60 FPS game with AMD FSR 3 frame generation); it now asks for no number at all and gets the same ceiling a typed limit above twice the game's pace gets - three generated pictures a real frame, four times the game's own, so 240 in a 60 FPS game and 120 in a 30 FPS game - and with FSR 3 off it is the game's own frames, since a picture the game never drew cannot be shown. VSync still caps it at the panel's refresh; with VSync off nothing does (240 in a 60 FPS game on a 280 Hz panel, which is what free FPS means here). Game speed is unchanged: the game's own pace, doubled by FSR 3. The game still keeps its own speed, and no frame is repeated. See [VALIDATION-0.65.md](VALIDATION-0.65.md).

**0.64:** the FPS limit reaches 240. A limit typed above twice the game's pace used to stop at twice its frames (240 in a 60 FPS game showed 120); the engine now pays it with up to three generated pictures a real frame, so 240 in a 60 FPS game is 240 distinct pictures on a 240 Hz or faster monitor with VSync (and 144, 165 and 200 are honored the same way). A 30 FPS game reaches 120, four times its own frames. The frame's share is carried as a fraction, no frame is shown twice, the game keeps its own speed, and Unlimited and Game speed still show twice the game's frames (0.65 gives Unlimited the same ceiling a typed limit gets). The pictures above twice the game's pace are PortForge's phased warps, since AMD's FidelityFX interpolation makes the midpoint only: expect the extra quarter-phase pictures to be less exact than 2x. See [VALIDATION-0.64.md](VALIDATION-0.64.md).

**0.63:** the app installs new versions by itself, at start and every hour while it is open, and reopens. Before, it only showed "Update available" on the Check for updates button, so a new engine and its fixes never reached games played from their own folders. Not during a conversion.

**0.62:** the FPS limit is followed again in games the Doctor had put in GPU safe mode after a GPU reset. That mode showed only the game's own frames while the in-game menu still read AMD FSR 3 On, so a 30 FPS game stayed at 30 at limit 120. The app now lifts it when a game gets a new engine (the Doctor sets it again if the GPU resets), turning AMD FSR 3 On in the Home/Esc menu lifts it too, and while it is on the menu says Off (GPU safe mode) and counts only real frames. See [VALIDATION-0.62.md](VALIDATION-0.62.md).

**0.61:** type any FPS limit from 10 to 1000 in the global settings, or change it in the in-game Home/Esc menu: left and right by 1, L/R by 10, A for presets. With AMD FSR 3 frame generation the screen locks to that exact number, for example 90 on a 60 FPS game is 60 real and 30 generated frames, with no repeated frames. See [VALIDATION-0.61.md](VALIDATION-0.61.md).

**0.60:** frame generation in the global settings is AMD FSR 3: the optical flow and frame interpolation AMD publishes in the FidelityFX SDK 1.1.4. The FPS limit is now a real limiter. It caps distinct pictures and never repeats a frame to reach a number, and the AMD-generated picture is timed on a high-resolution timer. A new crash guard, on for every game, catches null-pointer accesses that used to close the game. The Pokémon game now gets past the movie that closed it every time. See [VALIDATION-0.60.md](VALIDATION-0.60.md).

**0.59:** true resolution is exact. The engine records each game's own output height (many games draw 900 lines in TV mode, not 1080) and scales from it, so 1440p renders at 2560x1440 and 4K at 3840x2160 from the next start. The warm-up after an engine install records it. In one Mario game, 1440p went from 2133x1200 to 2560x1440 with unchanged frame pacing. The Doctor now repairs a game whose GPU was reset while frame generation or an FPS limit above its pace added pictures. It sets the per-game `paced_display=0`, and the Pokémon game ran without a reset afterward. A movie crash in that game is traced but not fixed yet. See [VALIDATION-0.59.md](VALIDATION-0.59.md).

**0.58:** experimental True resolution scales screen-shaped render targets toward 720p, 1080p, 1440p or 4K while leaving data-shaped targets native; dynamic-resolution games may differ, and it can be disabled if a game shows artifacts. Low latency requests fewer swapchain images with VSync and raises Windows game scheduling priority where supported; drivers and the display compositor still affect actual latency. Neither setting guarantees higher FPS or zero stutter. See [VALIDATION-0.58.md](VALIDATION-0.58.md) for the evidence and limits.

**0.57:** enlarged AMD frame generation no longer runs the older motion search unless fallback is needed. Shader preparation also recovers after a minute without completed work when its total is still unknown. Windows game-process detection also avoids a .NET module-lookup crash while retaining exact-path matching and refusing unsafe save operations when a live process cannot be identified. The engine build, focused regressions, and short Mario runs with AMD and legacy interpolation passed; these runs do not establish a universal FPS gain. See [VALIDATION-0.57.md](VALIDATION-0.57.md) for the evidence and limits.

**0.56:** every game's first launch now shows a full-screen loading screen of its own while its shaders are built: its key art, its name, and its character (found in the key art automatically) running along the progress bar. The engine's preparing screen at later launches has the same running character. Neither can be skipped, so a game starts only once its shaders are ready. See [VALIDATION-0.56.md](VALIDATION-0.56.md).

**0.55:** PortForge FSR 3 now generates its pictures with AMD's own FidelityFX frame interpolation and optical flow (from AMD's open-source SDK 1.1.4), with about a fifth fewer visible artifacts on moving parts of Zelda BotW than before. Each game also gets its own preparing screen at launch (its key art, name and a progress bar) while the shaders it used before are built, so it plays without stopping for them; on a game's very first Play, PortForge first plays its opening once, hidden, to record them. Recorded shaders now survive engine updates. Play now updates a game's engine instead of cancelling the start. See [VALIDATION-0.55.md](VALIDATION-0.55.md).

**0.54:** shorter freezes the first time a game shows a new area. A frame no longer waits behind hundreds of shaders it doesn't need yet, and PortForge uses every CPU core but one to prepare them, at low priority so the game keeps its speed. Super Mario Odyssey's worst first-run freeze went from 3.8 s to 2.3 s. See [VALIDATION-0.54.md](VALIDATION-0.54.md).

**0.53:** a new Esc menu with the game's key art as a poster and, blurred, behind the whole menu. The FPS limit, PortForge FSR 3 and frame-time graph you choose in it are remembered for the next launch. **Esc > Co-op** lists players 1 to 4 and their controllers, and a card pops in when a controller joins or leaves, USB or Bluetooth. `.pchtxt` code patches in `%LOCALAPPDATA%\PortForge\patches` (such as a 60 FPS patch) are applied when a game is converted. Fewer shader stalls inside occlusion queries. See [VALIDATION-0.53.md](VALIDATION-0.53.md).

**0.52:** tested in Zelda BotW at every FPS limit: Display FPS is the limit with PortForge FSR 3 off and twice the limit with it on, as in 0.51, but with FSR 3 off the extra pictures are the game's real frames shown again instead of generated ones, so FSR 3 off has no frame-generation artifacts at all. With FSR 3 on, one generated picture sits between each two real frames (no longer up to 9), and the rest are repeats. 0.51's better motion search stays. Frame generation also no longer warps from a stale frame after a hitch, no longer smooths pictures twice (it blurred them with pixel smoothing on), and keeps characters the camera follows. PC matching keeps Game speed and Unlimited exactly and turns FSR 3 off when you turn it off. Esc shows `Engine 1.34 | Display FPS: 120 of 120 (game 30) | Limit: 120`. See [VALIDATION-0.52.md](VALIDATION-0.52.md).

**0.51:** the FPS limit shows on screen at every setting. With PortForge FSR 3 off the screen shows the limit (Zelda BotW at Limit 120 shows 120), and with it on twice the limit (Limit 60 shows 120, Limit 120 shows 240), only capped by your monitor's refresh. The game still runs at its own speed: where it makes fewer real frames, the pictures in between are generated, up to 10 per real frame, and uneven ratios such as 144 from 30 FPS work. Frame generation finds motion much better on fast camera turns: in synthetic tests, visibly wrong moving pixels fell from 13.7% to 5.5% (from about 21% to 4.5% on a fast turn). Esc shows `Engine 1.33 | Display FPS: 144 (game 30 x4.8) | Limit: 144`. See [VALIDATION-0.51.md](VALIDATION-0.51.md).

**0.50:** the FPS limit works at every setting with PortForge FSR 3 off and on. Off, games always keep their own speed (0.49 ran a 30 FPS game four times too fast with a 120 limit). On, the screen shows twice the limit, using up to 4 pictures per real frame when the game makes fewer (Zelda BotW: Limit 30 → 60, Limit 60 or more → 120). Frame generation artifacts are reduced: every generated picture has its own point in time, edges no longer tear into the newer frame, and vsync no longer tears generated pictures. Esc shows `Engine 1.32 | Display FPS: 120 (game 30 x4) | Limit: 60`. Also includes 0.48's faster conversion and downloads. See [VALIDATION-0.50.md](VALIDATION-0.50.md).

**0.49:** PortForge FSR 3 can raise **Display FPS** toward your Limit — not only classic 2×. A 30 FPS title with FSR 3 On and Limit 120 shows ~**120** (multi-mid FG) without speeding the guest. Esc: `Engine 1.31 | Display FPS: N (4x game 30) | Limit: 120`. Play / Doctor still install the app Engine. See [VALIDATION-0.49.md](VALIDATION-0.49.md).

**0.48:** ROM → native conversion and Downloads are faster: AOT uses almost every CPU core instead of two jobs, skips writing unused IR, and Downloads use larger buffers and a ~2 s connection probe. See [VALIDATION-0.48.md](VALIDATION-0.48.md).

**0.47:** Stops PortForge FSR 3 / present crashes from a bookkeeping path that left GPU commands unsubmitted. **Play** updates the game's Engine from `PortForge\Engine\` before launch when it is older. See [VALIDATION-0.47.md](VALIDATION-0.47.md).

**0.46:** Esc shows Engine version, held Display FPS and live Limit. See [VALIDATION-0.46.md](VALIDATION-0.46.md).

**0.45:** The FPS limit changes the pictures on screen again. Speed lock still keeps the game at its own pace. See [VALIDATION-0.45.md](VALIDATION-0.45.md).

**0.44:** PortForge FSR 3 keeps the in-between picture when the optical-flow match agrees, instead of copying the current frame and flattening detail. A static HUD stays the current pixel. A bad warp still copies it, so the old multi-ghost guard remains. On a headless RX 9070 XT fixture the midpoint error fell from 81156 (no better than a plain blend) to 48036. Sharpening after the enlarge stays stronger when pixel smoothing is off. This is not the AMD SDK, and the engine still has no game motion vectors. Update the app and **Doctor → Engine** on existing games. See [VALIDATION-0.44.md](VALIDATION-0.44.md).

**0.43:** Every game stays at its own speed. The FPS limit and PortForge FSR 3 only change how many pictures are shown. A 30 FPS game stays at 30 and a 60 FPS game stays at 60, even if you pick 120 or turn frame generation on. Frame generation can still show about twice that pace (30 → ~60 on screen, 60 → ~120). A lower limit shows fewer frames and does not slow the game down. Characters that were falling through the ground were stepping on the display clock. The same release sharpens PortForge FSR 3 when the window is larger than the game, without turning on pixel smoothing, and keeps more scene detail in generated frames when the optical-flow match is reliable. Update the app and **Doctor → Engine** on existing games. See [VALIDATION-0.43.md](VALIDATION-0.43.md).

**0.42:** The FPS readout follows pictures on screen. See [VALIDATION-0.42.md](VALIDATION-0.42.md).

**0.41:** PortForge FSR 3 present mode and Esc Display FPS. See [VALIDATION-0.41.md](VALIDATION-0.41.md).

**0.40:** In-app updates no longer fail when a release ZIP contains `Source/.../.git`. See [VALIDATION-0.40.md](VALIDATION-0.40.md).

**0.39:** FPS limit session override / FG frame dropping. See [VALIDATION-0.39.md](VALIDATION-0.39.md).

**0.38:** Fixes BOTW-class ghosting (no selective RT scale under protect; hardened FG; safe-area reject) and on-screen FPS shows ~2x with PortForge FSR 3. See [VALIDATION-0.38.md](VALIDATION-0.38.md).

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

- **No FPS limit changes a game's speed** - Engine 1.49 keeps every game at its own console pace whatever the limit asks: **Game speed**, a **number** (10 to 1,000) or **Maximum** decide how many pictures a second the screen gets - the game's own frames, and with AMD FSR 3 up to the limit (four times the game's frames at most, so 240 from 60 and 120 from 30), capped by the panel under VSync. **Maximum** asks for the most the pipeline can make, and a limit below the game's rate shows fewer of its frames without slowing it down. Pacing waits resolve on the microsecond clock, so a 240 picture a second stream is evenly 4.17 ms apart instead of taking the timer grid's judder, and a leftover `speed_lock=0` from an older release cannot speed a game up.
- **Automatic engine frame guard** - Engine 1.4 shares optional shader waits across each frame, reserves compilation capacity for current draws, chooses worker counts on each launch, and resets stale pacing deadlines when FPS modes change. On by default for converted games, including Downloads handoff. Required game work and hardware limits can still stall.
- **Automatic cloud saves** - connect OneDrive or HTTPS WebDAV once per PC. Existing known games, new conversions and discovered converted games join automatically. A global switch pauses automatic saving, and each game can be excluded. Syncs while the app is open and the game is closed, verifies archives, keeps restore backups and asks which copy to keep when PCs have different progress. Nothing uploads before an account is configured; no PortForge-hosted account is included.
- **Simple conversion screen** ? game file, game name, save folder. Clear buttons beside every input, and a status card for progress, done, errors and cancelled.
- **Downloads** ? paste a direct link: automatic connection count, retries, pause/resume, checksum checks, ZIP unpacking, hand-off to conversion.
- **Automatic PC matching** ? shows CPU, graphics adapters, dedicated graphics memory and usable RAM on startup. New conversions get supported settings for that PC, followed by optional measurements that favor FPS and fewer freezes. On by default, works for local and downloaded inputs, and needs no AI account. Each game gets a report separating hardware estimates from measured results.
- **Graphics and FPS** ? per-game FPS limit (10?1,000 or unlimited), render scale, texture filtering, shadows, VSync, plus an automatic benchmark tuner that compares average FPS, freezes, worst frames and measured p99 frame time/1% low (local measurements, no AI model). Engine 1.7 keeps compatible shader caches across routine engine updates and reports per-frame GPU waits.
- **Automatic fixes** ? reads a game's log, reports known problems, applies supported repairs with backups and undo, and can ask a locally installed Codex for an AI diagnosis. Engine-level glitches are reported, not fixed.
- **AI Doctor** ? watches converted games while they run, explains every slowdown live (shader stutter, loading freezes, or other programs using the CPU), and when the game closes applies the supported repairs and asks Codex AI about new problems. It never changes a running game, and saves and settings are backed up first.
- **PortForge Engine** (Engine tab) ? our own build of the ScarletNX runtime with background pipeline compilation to reduce some shader stalls, plus experimental background translation that is off by default. Game loading and compatibility problems can remain. Keeps the original engine for restore. See [engine/README.md](engine/README.md).
- **AI Engine Lab** ? Codex writes engine changes for a game's recorded problems; PortForge builds and measures each one and keeps it only when stutter measurably drops.
- **Check for updates** ? the header button reads this repository's latest release, shows what is new, and installs it (your games, settings and saves are kept; the old program is backed up).

## Download

Get **PortForge-?-Windows-and-Source.zip** from the [Releases](../../releases) page, extract it, and open `PortForge.exe`. Read `README.md` inside for the user guide.

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

1. Raise the version in `src/AssemblyInfo.cs` (and the window title in `src/NativeApp.cs`), build with `-Test`.
2. Run `build-release.ps1 -Version <version>` (or `-SkipBuild` after a local build). It stages the Windows+Source ZIP, source-only ZIP, Setup.exe and SHA256 sums under `build\release-<version>`, and **excludes** `.git`, `.cache` and `build` from Source (required so older in-app updaters can install the package).
3. Create a GitHub release tagged `v<version>` and attach those assets. The in-app **Check for updates** button picks it up.

## More documentation

- [USERGUIDE.md](USERGUIDE.md) ? how to use the app
- [NATIVE.md](NATIVE.md) ? setup, evidence, limitations
- [VALIDATION-0.24.md](VALIDATION-0.24.md) ? Engine 1.7 and p99 tuning checks and limitations
- [BENCHMARKS.md](BENCHMARKS.md), [ROADMAP.md](ROADMAP.md)
- `PACKAGER-0.1.md` / `src/App.cs` ? the legacy emulator-packaging prototype, kept for reference only

## Legal

PortForge is a tool. You are responsible for only using game files and keys you are legally entitled to use. This project is not affiliated with Nintendo or the ScarletNX authors.

## Install

Download **PortForge-Setup-<version>.exe** from the latest release and run it. It adds Start menu and desktop shortcuts with the PortForge logo and an uninstaller, and needs no administrator rights. The ZIP (`PortForge-<version>-Windows-and-Source.zip`) holds the same app as loose files, with the source.
