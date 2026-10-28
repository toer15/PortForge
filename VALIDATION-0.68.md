# PortForge 0.68 / Engine 1.49 validation

Tested on Windows 11 with an AMD Radeon RX 9070 XT and a Ryzen 7 9800X3D on 2026-10-28, at 2560x1440 on a 280 Hz panel.

## The problem

Three things were still wrong after 0.67, which had finally applied the 240 profile to every game automatically.

**A limit changed the game's speed.** Engine 1.48 read the FPS limit as the game's own frame rate - with the speed lock off, `present_period` returned `1/limit` - so the 240 profile ran a 30 FPS game eight times its console speed, and its physics, timers and music with it. That is not what a limiter does in a PC game, and it was applied to every game without being asked.

**A leftover lock could still decide it.** The app wrote `speed_lock=0` for a number and for Maximum, and the engine trusted it, so a game converted with an older engine could keep the 1.48 rule even after the app changed its mind.

**Pacing was on the kernel timer.** `present_pace` waited with `plat_sleep_ns` and checked the clock afterwards. A Windows timer wait comes back on a ~0.5 ms grid, which at 240 Hz is an eighth of a frame, so a "240" frame was 4.17 ms apart with 0.3 ms of judder on it - visible without VSync, and exactly what "no hitches" rules out.

## Changes

- **No limit paces the game (engine, `present_period`).** The game's own swap interval is returned at every limit and in every mode - Game speed, a number, Maximum - and the function ignores the lock (`(void)lock`), so a `speed_lock=0` written by 0.67 cannot speed anything up. A game that times itself (interval 0) is paced only by a numeric limit; Maximum leaves it to its own timers. `present_show_due` no longer reads the lock either: a lower limit keeps its frames off the display whatever the settings file says.
- **The limit is the pictures (engine, `present_display_target`).** Unchanged from 1.46/1.47 and now the only thing a limit does: with FSR 3 off the screen gets the game's own frames (a lower limit shows fewer); with FSR 3 on, twice them, and up to `PRESENT_FG_MAX_MIDS` generated pictures a real frame for a limit above twice the game's pace or Maximum - so 240 in a 60 FPS game is 240 distinct pictures, 144 stays 144, and the typed limit and the panel's refresh still cap it. An extra cap makes the typed limit final on 1.48's unlocked path too.
- **Microsecond pacing (engine, `plat_wait_until`).** On Windows and Linux, `present_pace` now sleeps to within `PLAT_WAIT_SPIN_NS` (0.8 ms) of the deadline on the high-resolution timer and decides the last stretch on `plat_ns`'s clock. Bounded spin, no yield (`SwitchToThread` can cost a millisecond). The same wait now carries frame generation's generated pictures.
- **The app writes the lock on, always (app).** `GlobalSettings.SpeedLockFor` returns `1` for every limit, so a game on an older engine keeps the console pace too; the Settings tab, the game tools' hint, the PC-matching report and the limiter rows now say pictures, never a faster game. The Doctor's custom-timing hint is left to engines without a speed lock, where a number really was the game's rate.
- **Tests updated, not loosened.** The engine's `runtime_present` suite gained rows for the 1.49 rule (a leftover `speed_lock=0` at 120, 240 and Maximum still returns the game's interval; a self-timed game keeps a number and Maximum waits) and its Esc/pacing rows were re-pinned. `FgCadenceTests` asserts the new patch text (`(void)lock`, no `if (lock && game)`, `plat_wait_until`, the spin constant), and the app suites assert `speed_lock=1` for 144, Maximum and the automatic profile.

## What was measured

The pacing code was extracted from `engine/portforge-engine.patch` itself (`verification/limiter_harness.py`, not part of the repository), compiled with clang (MSYS2 CLANG64) and driven with the engine's own tables: `HARNESS PASS` for the 60 FPS, 30 FPS and self-timed cases. Pictures a second on a 280 Hz panel, FSR 3 off / on:

| limit | 60 FPS game | 30 FPS game |
|---|---|---|
| Game speed | 60 / 120 | 30 / 60 |
| 30 | 30 / 30 | 30 / 30 |
| 60 | 60 / 60 | 30 / 60 |
| 120 | 60 / 120 | 30 / 120 |
| 144 | 60 / 144 | 30 / 120 |
| 240 | 60 / 240 | 30 / 120 |
| Maximum | 60 / 240 | 30 / 120 |

In every row the game itself runs at its own rate: 60 or 30, unchanged. The same table holds at 144 Hz and 60 Hz panels (VSync caps the pictures at the refresh) and with `speed_lock=0` in the file.

Pacing error against the schedule on this PC, five one-second runs per row:

| rate | wait | mean | p99 | worst |
|---|---|---|---|---|
| 240 Hz | timer (`plat_sleep_ns`) | 293-331 us | 616-1413 us | 1662 us |
| 240 Hz | timer + `plat_ns` spin | 0.1-8.3 us | 0.1-79 us | 490-1638 us |
| 120 Hz | timer | 283-365 us | 522-2808 us | 523-5030 us |
| 120 Hz | timer + spin | 0.3-5.4 us (3 quiet runs) | 12-114 us | 13-368 us |
| 60 Hz | timer | 291 us | 766 us | 777 us |
| 60 Hz | timer + spin | 54 us | 0.1 us | 3214 us |

The timer's mean is the grid, not the request: it is early or late by up to half its period, and one run that overlapped a build on this PC shows the spin's own limit - when Windows deschedules the thread mid-wait, the frame is late whatever the wait does (the 60 Hz `worst` row is that). No wait in user space can fix a thread that is not running.

The engine's own presenter suite was built and run from the shipped patch applied to a pristine ScarletNX at `engine/BASE` (`0de0a87`, `patch -p1` with no fuzz): `runtime present: 150 checks passed`, compiled with `-Wall -Wextra -Werror`.

On the app side: `InterfaceTests` 51 checks, `OptimizationTests` 128, `CloudSaveTests` 60, `DownloadTests` 35, `UpdateTests` 36, `DiagnosticTests` 40, `DoctorTests` 38 and `FgCadenceTests` all pass against the built frontend. `engine/portforge-engine.patch` passes `git apply --check` and its hunk headers are byte-exact (`verification/fix_patch_counts.py --check` rewrites nothing).

## Limits

- **No real game was played for this release.** The rates above are the engine's own functions and the pacing harness; only the presenter suite ran, not a game. The engine binary is built by CI from the patch, so a game session is the first real playback of 1.49.
- **FSR 3 quality is unchanged.** The generated pictures are still AMD FidelityFX 1.1.4's interpolation (Engine 1.38) with PortForge's phased warps above 2x (1.46). A 30 FPS game shows at most 120 pictures (four times its own frames), and the quarter-phase pictures above 2x are less exact than the doubled ones.
- **A 240 Hz panel gets 240 only if the game makes at least 60 real frames.** Below that the ceiling is four times what it really makes, and the FPS the game itself runs at is its console rate - nothing can make the game's own logic run at 240 without changing its speed, which 1.49 deliberately never does.
- **`EngineTests` fails here on its own fixture** ("A test engine loads the chosen executable even when the folder and game names differ"), identically on the unmodified sources; CI does not run it, and the launcher it tests is not part of this change.
- **The full engine build was not configured on this PC** (the CMake configure wants OpenSSL with Perl and GNU make on this shell's PATH), so the engine executable itself was not built here. CI builds it and runs `runtime_present`, `frame_policy`, `pipeline_file`, `nz_jit_region` and `render_scale` on it.
