# PortForge 0.70 / Engine 1.51 validation

Tested on Windows 11 with an AMD Radeon RX 9070 XT and a Ryzen 7 9800X3D on 2026-10-28.

## The problem

**The number was a ceiling, not the rate.** Engine 1.50 read the FPS limit as the most pictures the screen could get and fell back to the game's own frames above 2x them: a 30 FPS game at Limit 120 with AMD FSR 3 on showed **118 of 120**, and with FSR 3 off it showed **30**, not 120. The player asked for a number and got the game's rate instead.

**The save line stayed for the whole session.** 0.69's corner and Escape menu read `Saved 3 h ago` from launch to exit, so the picture carried a timestamp instead of returning to the game.

## Changes

- `present.c` (`present_display_target`): the FPS limit is the pictures a second the screen is asked for, and **AMD FSR 3 doubles it** - `120` typed is `240` with FSR 3 on and `120` with it off. Game speed is the console's pace, doubled by FSR 3; Maximum is the display's refresh, doubled by FSR 3 (or the game's own frames while the refresh is unknown); a limit under the game's own frames still shows only the frames it lets on. The limiter's 1,000 ceiling holds (a typed 1,000 with FSR 3 lands at 1,000) and VSync still caps everything at the panel. `present_period` is untouched: no limit moves the game off its console pace.
- `renderer.c`: the renderer already filled phases it had no generated picture for with the game's frame (`fg_schedule`), but `k` was only computed past 2x with FSR 3 on. With FSR 3 off it now counts too, so a typed limit above the game's frames is reached by repeating them - a 30 FPS game at Limit 120 shows 120.
- `save_activity.[ch]`, `ui.c`, `overlay.c`: the corner reads `Saving...` while a write is in flight, `Saved just now` for **five seconds** after one succeeds, and a red `Save failed` for six seconds after a failure, then shows nothing. The startup scan of the save folder and the age formats are gone with it.
- App wording (`GlobalSettingsPanel`, `GameSettingsWindow`, `GlobalSettings`, `GameOptimization`): the limit note, the Maximum entry and the automatic-profile notice now describe the doubled number; the top of the list reads **Maximum (your refresh)** instead of `Maximum (240 pictures)`.

## Checks run

- **`tests/runtime_present.c`**: 151 checks, **passed**. Every menu limit with FSR 3 off and on for a 30 FPS game on a 280 Hz panel (`Game speed` 30/60, `Maximum` 280/280, 20 20/40, 30 30/60, 60 60/120, 120 120/240, 144 144/280, 240 240/280), the doubled rule for typed numbers on a 60 FPS game (90 → 180, 121 → 242, 144 → 288, 200 → 400, 240 → 480), the 1,000 ceiling, VSync caps at 144, 60 and 280 Hz, vsync off uncapped, and the unchanged guest wait at every limit. Built with `clang -std=c11 -Wall -Wextra -Werror`.
- **`tests/save_activity.c`**: five-second notice, expiry, write failure, and the fullscreen/menu/focus/system-key cursor policy - **passed**.
- **`tests/frame_policy.c`**: **passed**.
- **`FgCadenceTests`** (reads the patch itself): 21 checks including the new rule, the doubled numbers, the ceiling, the repeats and Esc's 1.51 line - **passed**.
- **`NativeInterfaceTests`** (drives the built Settings tab): all checks, including the new Maximum entry and the limit notes - **passed**.
- **`OptimizationTests`**: 128 checks, **passed**.
- **Patch integrity**: regenerated from the edited engine tree, normalized by `verification/fix_patch.py`, applied to a clean ScarletNX `0de0a87` with `git apply --check` - clean, and the tree's `pf_engine_version.h` reads `1.51`.

## Not tested

- **No game session on Engine 1.51.** The rule is covered by the engine's own pacing tests and the patch tests, but no title has been played with it. The check that matters is the Escape menu on a game the player knows: `120 (240 on screen)` with FSR 3 on, and `Display FPS` near 240.
- **A 240 Hz panel under VSync** cannot show the 480 a typed 240 asks for with FSR 3; the panel caps it at 240. 480 needs a panel that refreshes that fast, or VSync off.
- **A 30 FPS game at 240 with FSR 3** is eight pictures a frame (one real, three generated, four repeats). The repeats are the game's frame, so motion above three pictures a frame is real and repeated, not interpolated; this is the engine's existing limit (`PRESENT_FG_MAX_MIDS`), not a new one.
- **`src/nx/gpu/vk/ui.c` was not compiled on this machine** (no Vulkan headers in this checkout). CI builds it; the corner's new wording only changes the string it draws.
