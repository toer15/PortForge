# PortForge Engine

The PortForge Engine is PortForge's own build of the [ScarletNX](https://github.com/scarletnx/ScarletNX) runtime (GPL-3.0-or-later), changed to reduce some compilation stalls in games converted from the Switch. PortForge ships it in its `Engine` folder and installs it under a converted game from the **Engine** tab, keeping the original engine so it can be put back.

## What it changes

**Speed lock** (`speed_lock`, on by default since Engine 1.5). A game that asks for a swap interval counts its frames, so presenting sooner than that interval ran the game itself faster, and later ran it in slow motion. With the lock on, `present_pace` always paces such a game at its interval, whatever the FPS limit (a number, 0 for none, or F4). Since Engine 1.6 a lower limit keeps some of the game's frames off the display (`present_show_due`, still drawn and submitted) instead of slowing the game. A game asking for interval 0 is paced only by a set limit. `speed_lock=0` restores the old behavior.

**Frame guard** (`frame_guard`, on by default since Engine 1.4). All skippable pipeline binds in a presented frame share one 1.5 ms optional wait budget. Earlier versions allowed 1.5 ms at every bind, which could add up during a burst of new pipelines. Time spent waiting for required pipelines also consumes the optional budget; required draws themselves still complete. The recording queue carries an ordered frame marker so the budget belongs to the frame being recorded, even if the GPU producer runs ahead. OS scheduling may overshoot a timed wait; subsequent optional waits then stop for that frame.

Cached background pipeline jobs can occupy at most one fewer than the available shader workers when there are two or more. Demand jobs can use every worker. Automatic worker counts use the runtime's allowed CPUs on each launch: one on machines with four or fewer threads, then two to six. Explicit thread counts remain available. New conversions made by a frame-guard engine use automatic shader and JIT worker settings so copying the game does not pin those counts to its build PC.

FPS pacing drops the old scheduling deadline when the limit or native game interval changes. This prevents an obsolete future deadline from causing an avoidable pause after a mode change. The requested game speed and FPS limit are preserved.

**Smart async shaders** (`async_shaders`, on by default). The original engine stops the game whenever a draw needs a GPU pipeline that isn't built yet. The PortForge Engine waits at most 1.5 ms, then skips that draw for one frame, but only when every image the draw writes was drawn in each of the last three frames (the game's 3D world). Draws into anything else, such as menus, loading screens and images drawn once, still wait, so nothing goes missing for good.

Historical Engine 1.0 measurement on one game's opening with an empty shader cache (45 seconds of scripted play): 20 frames over 50 ms with the worst at 4,234 ms and 7,660 ms spent waiting became 4 frames over 50 ms (all loading) with the worst at 349 ms and 1 ms waiting. This is not a benchmark of Engine 1.3 or a prediction for other games. See [0.17 validation](../VALIDATION-0.17.md) for current evidence and limitations.

## Source and build

**Background shader translation** (`async_translation`, experimental, off by default). First-use shaders can be translated by the existing worker pool from copied code and constant buffers. The GPU thread validates recorded constant-buffer reads before reusing the finished shader. Snapshot jobs are bounded to 16 and completed jobs are collected once per submission, with synchronous fallback if the queue is full. Query-counting and transform-feedback draws wait. Graphics pipelines that write storage buffers or images also wait. Repeatedly drawn targets are a heuristic, not a guarantee that dropping a draw is harmless, so this option requires per-game testing.

This follows the general background-compilation approach documented by [DXVK](https://github.com/doitsujin/dxvk/wiki/Developer-guidelines) and explored by [dxvk-async](https://github.com/Sporif/dxvk-async). Their Direct3D DLLs are not compatible replacements for this engine's Vulkan renderer; no code or DLLs from those projects were copied. [Vulkan graphics pipeline libraries](https://docs.vulkan.org/features/latest/features/proposals/VK_EXT_graphics_pipeline_library.html) are a separate possible future change, not implemented in this release. A presentation filter cannot remove CPU loading work, and synthesizing pictures would not remove input latency.

- Base: ScarletNX commit `0de0a87` (the nightly of 2026-10-21).
- Changes: [`portforge-engine.patch`](portforge-engine.patch).
- Build on Windows: install MSYS2, run `msys-setup.sh` in it, then `build-engine.sh` and `package-engine.sh` (MSYS2 CLANG64 shell). The package lands in `engine/package`, which `build.ps1` copies into the app's `Engine` folder.
- Run the frame-policy, runtime-pacing and cache-file regression tests with `bash test-frame-guard.sh` in CLANG64 after configuring the engine. Tests and measurements for this release are documented in `VALIDATION-0.21.md` at the repository root.

## AI Engine Lab

With the engine source and MSYS2 on the PC, the Engine tab's **Let AI improve the engine** has Codex write a change for a game's recorded problems. PortForge builds it, measures it against the current engine on the same scripted play with an empty shader cache, and keeps it only when it reduces stutter by at least 20% without crashes or a lower frame rate. Anything else is reverted.
