# PortForge 0.67 / Engine 1.48 validation

Tested on Windows 11 with an AMD Radeon RX 9070 XT and a Ryzen 7 9800X3D on 2026-10-28, at 2560x1440 on a 280 Hz panel.

## The problem

The limiter worked; nothing used it. Engine 1.48 makes a typed FPS limit the game's own frame rate and Maximum the game running as fast as the PC can render it, but the app still left every game at **Game speed** - the console's own 30 or 60 - so a game only ran fast if you opened Settings and chose a number or Maximum yourself, and a game played straight from its own folder kept whatever its `.ini` last held. The ask: every game, in any state, should run at the fastest rate this PC can render, up to the limiter's own maximum of **240 FPS**, with frame generation filling the pictures it cannot make, and with the rate visible in the graphs, with no per-game setup.

## Changes

- **The automatic profile is the default: 240 FPS, AMD FSR 3, the in-game frame-time graph.** A number is the game's own frame rate (Engine 1.48), so 240 runs the game at up to 240 FPS - its own real frames, its physics, timers and music following them - and AMD FSR 3 (FSR 1 plus FidelityFX frame interpolation) doubles the pictures up to the limit where the game cannot reach it. `GraphicsProfile`'s default limit, `GlobalSettings`' defaults (limit, upscale mode, graph) and the Settings tab's own list changed; the in-game frame-time graph shows the live Display FPS in every game.
- **Applied to every game once, at app start.** `GlobalSettings.EnsureAutomatic()` runs before the window shows. With no settings file it writes the whole profile. With a file still holding the old defaults (`fps_limit=game`, `upscale_mode=0`, graph off) it raises exactly those three and keeps every other saved choice (tested with a saved 4K display resolution). A file with another frame limit, FSR 1, or the whole settings layer switched off is never touched.
- **It reaches games nobody opens in PortForge.** Every updated runtime reads `%LOCALAPPDATA%\PortForge\global-settings.ini` at its own launch, so a game started from Explorer or its own folder gets 240 as well. The game's own Esc/Home menu still changes its limit for that session, and a per-game choice in Game tools wins for that game.
- **The app says what it did.** The Settings tab opens on the profile, its status line reports that the 240 profile was applied, the frame-limit note for 240 says PortForge applies it to every game automatically, and Game tools starts on Custom limit 240 with Clear restoring it.

## What was measured

**No game was run for this release.** The rates this profile produces are Engine 1.48's own rules, measured in [VALIDATION-0.66.md](VALIDATION-0.66.md): a number is the game's own frame rate (240 = up to 240 real frames), AMD FSR 3 frame generation doubles the game's frames up to the limit and the monitor's refresh, and VSync caps the pictures at the refresh. What was run on this PC:

| suite | result |
|---|---|
| `InterfaceTests` against the built frontend | **51 checks pass**, 3 new: app start writes the 240 profile for every game; the Settings tab opens on 240 FPS + AMD FSR 3 + the frame-time graph and says so; Game tools starts on Custom 240 and Clear restores it |
| `OptimizationTests` | **128 checks pass**, 5 new: a fresh file is the profile (240, `speed_lock=0`, `frame_generation=1`, `frametime_graph=1`, `portforge_fsr3=1`); it is written when missing; the old defaults are raised once with a saved display resolution kept; a saved 120/FSR 1 file is left alone; a switched-off layer is left alone |
| `CloudSaveTests` / `DownloadTests` / `UpdateTests` / `DiagnosticTests` / `DoctorTests` | 60 / 35 / 36 / 40 / 38 checks pass |
| `FgCadenceTests` | passes (the engine patch is unchanged, Engine 1.48) |

`EngineTests` fails in this environment on its own launcher fixture - the check compares an unquoted path while `EngineManager.TimedRun` hands the fixture the quoted `--game=` argument. Compiling and running it from a clean `git archive HEAD` tree fails identically, so it predates this change; CI does not run it, and this release does not touch that path.

## Limits

- **The profile is Engine 1.48's behaviour, not a new engine measurement.** Nothing in the engine changed; this release makes every game use what 1.48 already does.
- **240 pictures a second need either 240 real frames (FSR 3 off) or 120 real frames with AMD FSR 3** (a PC game's frame generation doubles at most). A game that cannot render 120 stays below 240 even with generation on, and a game that cannot reach 240 keeps running at whatever it can - the limit is a cap, never a promise. VSync caps the pictures at the monitor's refresh (280 Hz on this PC, so 240 passes).
- **A high limit runs the game's own world faster.** A 30 FPS game at 240 steps eight times its console pace, its physics, timers and music following its frames, as a PC game does above its design rate. **Game speed** is the console-pacing choice and is still one click away, per game or in Settings.
- **A game in the Doctor's GPU safe mode (`paced_display=0`, set after a GPU reset) shows only its own frames** whatever the limit reads, until AMD FSR 3 is turned On in its own Esc menu. The app does not lift that repair by itself, deliberately: it was set because the GPU reset while extra pictures were shown.
- **Historic per-game settings are not overwritten, only the old untouched defaults.** A game whose own `.ini` holds a number you typed, and a settings file you saved with your own choices, stay exactly as they are.
