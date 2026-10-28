# PortForge 0.69 / Engine 1.50 validation

Tested on Windows 11 with an AMD Radeon RX 9070 XT and a Ryzen 7 9800X3D on 2026-10-28.

## The problem

**"Saved … ago" had nowhere to come from.** The engine's save indicator was a notice: `Saving...` appeared for 1.2 s after a write and then vanished, so at any other moment the picture said nothing about the game's save. The Escape menu said nothing at all. A player looking at a game's own pause menu (most Switch titles show `Saved` with a time in the corner) had no equivalent, and no way to see whether the game in front of them had saved since they picked it up.

**A save button cannot invent a save.** What the same request usually means - "let me save whenever I want" - is save-anywhere. A Switch game writes its save when its own code reaches a save point; no menu in the emulator can make it write one it is not at, and true save-anywhere needs save states of the whole emulated machine (guest RAM, GPU state, open host handles), which this engine does not have. `VALIDATION-0.53.md` says the same thing about the earlier request, and the app's Global Settings panel now states it next to the switch. Shipping a button that claimed to save, and then did not, is exactly the failure this release avoids: the corner tells the truth about the save the game made, and nothing pretends to make one.

## Changes

- `save_activity.[ch]`: `pf_save_end` stores the wall-clock second of every successful write (`last_unix`). `pf_save_seeded` takes the newest write found on disk at startup. `pf_save_status` formats the line - `No save yet`, `Saved just now` (under a minute), `Saved N min ago`, `Saved N h ago` (up to two days), `Saved N days ago`, with `Saving...` and `Save failed` taking precedence. A save stamped in the future (a file copied from another clock) reads `Saved just now`, never a negative age. The old `pf_save_message` expiry rules are untouched for the frame-refresh path.
- `fs.c` (`save_seed`, called under `tree.lock` at the end of `fs_init`): walks `save/system`, `save/device` and `save/user*` and takes the newest `fs_tree_mtime`. A fresh session therefore opens reading the save already on disk rather than `No save yet`, and a game whose save is four days old says so before the title screen.
- `src/nx/gpu/vk/ui.c`: the HUD line moved from the bottom right to the picture's top right corner and is now always shown while `save_indicator` is on, on a dark plate (a red one for `Save failed`), at 1x, 2x or 3x depending on height.
- `src/nx/runtime/platform/overlay.c`: the menu's head band carries the same line at its right, before the console user's chip, which moves left by the line's width (and drops to its picture, or out, when the panel is too narrow for both).
- `settings.txt`: `save_indicator`'s help now describes the corner line and says it does not create a save point; the app's Global Settings label and notes match.

## Checks run

- **`tests/save_activity.c`**, extended with the new line: `No save yet` before any save; `Saved just now`, `Saved 1 min ago`, `Saved 5 min ago`, `Saved 3 h ago`, `Saved 3 days ago` from seeds relative to the wall clock; a future stamp reading `Saved just now`; `Saving...` while a write is in flight; `Save failed` during and after the 6 s error window, then back to the saved line; the original expiry, presented-notice, failure-stickiness and cursor-policy checks unchanged. Built with `clang -std=c11 -Wall -Wextra -Werror -UNDEBUG` and run: **passed**.
- **`overlay.c`** and **`fs.c`**: compiled with `clang -std=c11 -Wall -Wextra -Werror -fsyntax-only` against the generated `snx_settings.h`, the theme header and the swipc dispatchers (`settingsgen`, `swipc`) - clean. `fs.c`'s include of `save_activity.h` had to move to the file's include block: the first compile failed with `call to undeclared function 'pf_save_seeded'`, which is exactly what this check is for.
- **`tests/CMakeLists.txt`**: the overlay test target compiles `save_activity.c` too, since `overlay.c` now calls `pf_save_status`.
- **Patch integrity**: applied to a clean ScarletNX `0de0a87`, `git apply --check` clean, and the tree's `pf_engine_version.h` reads `1.50`.

## Not tested

- **No game session on Engine 1.50.** The line is exercised by its own unit test and compiled into the renderer and the menu, but no title has been played with it on screen. The first session on a game with a known save is the check that matters (the age should match the game's own save screen).
- **A device that saves only to `save/user.<id>`**: the walk covers every `user*` folder, but only one console user's folders have been seen in a dump.
- **`src/nx/gpu/vk/ui.c` was not compiled here** (no Vulkan headers in this checkout); its block only calls `pf_save_status`, `ui_fill` and `ui_text` with the same arguments the replaced notice used, and CI builds it. Treat the corner's placement as unproven until a build has drawn it.
- **The HUD in a paused game**: while a game presents nothing, the corner text updates on the menu's frames and the next presented frame, not between them; the age is wall-clock so it is right the moment the next picture is drawn.
