# PortForge 0.66 / Engine 1.48 validation

Tested on Windows 11 with an AMD Radeon RX 9070 XT and a Ryzen 7 9800X3D on 2026-10-27, at 2560x1440 on a 280 Hz panel.

## The problem

**The FPS limit never drove the game.** Zelda BotW (a 30 FPS game) at **Limit 120, AMD FSR 3 off** showed Esc's `Display FPS: 30 of 30 (game 30) | Limit: 120`: the game itself stepped 30 times a second and the limiter only chose which of its 30 pictures reached the display. 120 was unreachable by design - the engine's own rule (the speed lock) kept every game at its console pace whatever the limit, so a limiter could only ever *drop* frames. In gaming terms a limit is the opposite: an FPS cap sets how fast the game runs. **Maximum** (0) was worse: it meant "the game's frames, as they are", not the machine's full speed.

What the user asked for: any game, through the app's engine, runs at the most FPS this PC can deliver, automatically - and the FPS limit menu then caps that to whatever the player picks.

## Changes

- **The limit is the game's own frame rate (engine).** With the speed lock off, `present_period` is `1/limit`: a 30 FPS game at 120 runs four times its console speed, a 60 FPS game at 60 twice - as a PC game runs above its design rate. `fps_limit=0` (**Maximum**) waits for nothing at all (`return 0`), so the game runs as fast as the PC can render it. Game speed (`SNX_FPS_GAME`) is the console's own pace in either case, and the speed lock (`speed_lock=1`) still keeps a game at that pace exactly as before.
- **The display side follows the same split (engine).** With the lock on, nothing changes: the screen gets the limit's pictures, twice them with FSR 3, up to four times the game's own for a limit above twice its pace and for Unlimited (Engine 1.46/1.47). With the lock off there is nothing for the pipeline to fill - the game is already running at the rate - so the display gets the game's own frames and FSR 3 doubles them at most. A stale measured rate can no longer pace the display: `present_game_fps_now` reports the measured rate only while the limit and the lock are the ones the last half-second window ran under (`present_game_side_fps` falls back to the limit until then), so switching from Maximum to 60 does not pace the display to the Maximum rate.
- **The menu says Maximum (engine).** The FPS limit row's top entry reads `Maximum` (`Unlimited` was the old meaning), the settings help text says what each choice does, and the speed-lock setting text says which world it enables.
- **The app writes the lock the choice implies (app).** `GlobalSettings.SpeedLockFor`: Game speed saves `speed_lock=1`, a number or Maximum saves `speed_lock=0` - in the global settings, in PC matching (`PcOptimization`), in per-game saves (`GraphicsProfile.Values`, removed for engines that have no such key) and wherever global preferences are copied into a game. The Doctor no longer reports `speed_lock=0` as a fault and no longer "repairs" it back on; `speed_lock` is gone from its repair list (`CodexDiagnosis` too). `0.65`'s PC-matching report text and the game tools' hints describe the new behaviour.
- **Settings tab and game tools (app).** The panel now explains the three choices (Maximum = as fast as this PC renders it and the game itself runs faster than real time; a number = the game's own frame rate, physics and timers following its frames; Game speed = the console's 30 or 60 with the lock, FSR 3 smoothing it instead), the game tools' FPS row reads `Maximum` and says a Custom number is a cap on the game itself, and the Doctor's "custom frame timing" note is kept for engines that have no speed lock.
- **Tests updated, not loosened.** The engine's `runtime_present` suite gained a lock-off world: `present_period(0, 2, 0) == 0` (Maximum holds nothing back), `present_period(240, 1, 0) == 1e9/240` (a number is the game's rate), `present_display_target(120/240, 0) == 120/240` and `present_display_target(0, 0) == 60` (Maximum before its first measurement is the pace the console stand-in gives). The app's `OptimizationTests` pin the saved `speed_lock` for 144/Game speed/Maximum and for PC matching, `DiagnosticTests` pin that a number with the lock off is no longer flagged (and that an engine without a lock still warns), `NativeInterfaceTests` read the panel's own note for Maximum, Game speed and a typed limit, and `FgCadenceTests` check the unlocked condition and rows in the patch itself.

## What was measured

The engine's own presenter test was built and run **on this PC** from the patch, not from a retyped copy: `git apply` of `engine/portforge-engine.patch` onto ScarletNX at `engine/BASE` (0de0a87), then `tests/runtime_present.c` compiled with clang 23.1.3 (MSYS2 CLANG64, `-std=c11 -Wall -Wextra -Werror`) and run: **147 checks passed**, including the eight new lock-off rows above and every pre-existing row (the locked world is unchanged).

The pacing harness (`verification/limiter_harness.py`) extracts `present_game_side_fps` and `present_display_target` from the patch itself and drives them with the engine's own table plus the unlocked world. With the speed lock off:

| setting | a 60 FPS game | pictures a second (FSR 3 off / on) |
|---|---|---|
| Maximum (0) | as fast as this PC renders it | the game's own frames, doubled at most |
| 30 / 60 | the game runs at 30 / 60 | 30 / 60 |
| 120 | the game runs at 120 | 120 / 120 |
| 240 | the game runs at 240 | 240 / 240 |
| a limit the PC cannot reach (120 with 90 measured) | the game runs at 90 | 90 |
| Maximum with 200 measured | 200 (the game's own rate) | 200 |

With the speed lock on, every 0.64/0.65 number is unchanged (Game speed 60/120, 240 → 60 without FSR 3 and 240 with it at a 280 Hz panel, Unlimited 240/120, the 30 FPS table as the engine's own suite has it), and no picture is generated where the game is already making the rate.

On the app side, the full local suite pass: `InterfaceTests` 48 checks, `CloudSaveTests` 60, `OptimizationTests` 122, `DownloadTests` 35, `UpdateTests` 36, `DiagnosticTests` 40, `DoctorTests` 38, `FgCadenceTests` itself - the Settings tab's note, the saved `speed_lock` values and the Doctor's changed rule are all pinned by them. CI builds this release's engine by applying the patch to ScarletNX at `engine/BASE` and runs its presenter suites (`runtime_present`, `frame_policy`, `render_scale`, `pipeline_file`, `nx_jit_region`) on it.

## Limits

- **No real game was launched for this release** (that needs a converted game and `prod.keys` on this PC), so the flat-out rate Maximum reaches in a given game, and the frame pacing of a game run above its console rate, are unmeasured here. Esc shows the live rate immediately, which is the check to run first: `Limit: Maximum ... Display FPS: N`.
- **A game run above its console rate runs its own physics faster.** That is what "the limit is the game's frame rate" means, and what the user asked for, but it is a real behaviour change: a 30 FPS game at 120 has its timers and physics at four times the console's rate (many PC games behave exactly like this above their design rate; a game that assumes 30 may break). **Game speed** is the setting that keeps the console's pace with FSR 3 smoothing, and the app no longer forces the lock, so the choice is the player's.
- **VSync still caps the pictures at the panel.** With VSync off nothing does, which is where "free FPS" and tearing both live. FSR 3 frame generation doubles the pictures at most when the game is already at the rate, since it has nothing to fill.
- **Maximum is a cap on the game, not a promise of a rate**: on a heavy scene the game runs as fast as the scene allows, which is the point, and a number below that caps it.
- Two checks are known and not caused by this change: `EngineTests` fails identically on the unmodified sources on this PC (`verification/EngineTests-pristine.log`, its own launcher fixture), and the native integration test needs the ScarletNX backend and the `original-test.nro` fixture, which are not on this PC.
