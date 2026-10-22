# PortForge Engine

The PortForge Engine is PortForge's own build of the [ScarletNX](https://github.com/scarletnx/ScarletNX) runtime (GPL-3.0-or-later), changed to remove stutter in games converted from the Switch. PortForge ships it in its `Engine` folder and installs it under a converted game from the **Engine** tab, keeping the original engine so it can be put back.

## What it changes

**Smart async shaders** (`async_shaders`, on by default). The original engine stops the game whenever a draw needs a GPU pipeline that isn't built yet. The PortForge Engine waits at most 1.5 ms, then skips that draw for one frame, but only when every image the draw writes was drawn in each of the last three frames (the game's 3D world). Draws into anything else, such as menus, loading screens and images drawn once, still wait, so nothing goes missing for good.

Measured on one game's opening with an empty shader cache (45 seconds of scripted play): 20 frames over 50 ms with the worst at 4,234 ms and 7,660 ms spent waiting became 4 frames over 50 ms (all loading) with the worst at 349 ms and 1 ms waiting. Title and gameplay screenshots match the original engine's.

## Source and build

- Base: ScarletNX commit `0de0a87` (the nightly of 2026-10-21).
- Changes: [`portforge-engine.patch`](portforge-engine.patch).
- Build on Windows: install MSYS2, run `msys-setup.sh` in it, then `build-engine.sh` and `package-engine.sh` (MSYS2 CLANG64 shell). The package lands in `engine/package`, which `build.ps1` copies into the app's `Engine` folder.

## AI Engine Lab

With the engine source and MSYS2 on the PC, the Engine tab's **Let AI improve the engine** has Codex write a change for a game's recorded problems. PortForge builds it, measures it against the current engine on the same scripted play with an empty shader cache, and keeps it only when it reduces stutter by at least 20% without crashes or a lower frame rate. Anything else is reverted.
