# PortForge 0.27 / Engine 1.10 validation

## Problem and change

Selecting FSR does not itself reduce the game's GPU workload. Since 0.26 protects internal images at their native sizes, Performance's old 75% request was overridden and only filtering changed. A real FSR dispatch log was therefore insufficient evidence of a performance improvement.

On protected engines, Performance now requests the game's handheld mode with FSR 1, keeping internal scale at 100%. The game chooses its own rendering layout. Balanced and Quality restore TV mode. Entry-level PC matching also starts with handheld + FSR. Both manual and automatic tuning compare the game's TV/handheld modes and skip only genuinely duplicate configurations. TV wins within the existing FPS, freeze and p99 measurement tolerances. Legacy engine behavior is retained.

Engine 1.10 records actual FSR dispatch or bypass transitions, source and destination dimensions, and the game's mode. Unsupported input, equal/smaller output and GPU setup failures have distinct messages. The graphics UI shows the latest recorded status without interrupting editing or claiming it is a live measurement. GPU-ready setup alone is not shown as active upscaling.

## Actual game comparison

An isolated Mario copy ran the same original save and 65-second input script sequentially, in a maximized window on the user's AMD RX 9070 XT. The player's original game and save were not used for these samples. The initial comparison used Engine 1.9, whose FSR algorithm is unchanged in 1.10.

| Mode | Source image | Display scaling | Frames / 65 s | Last 600-frame p99 |
| --- | --- | --- | --- | --- |
| TV, linear | 1600x900 | Linear | 3747 | 17.702 ms |
| TV, FSR 1 | 1600x900 | FSR to 2433x1369 | 3743 | 17.621 ms |
| Handheld, FSR 1 | 1280x720 | FSR to 2433x1369 | 3621 | 17.711 ms |

The handheld screenshot showed the world, character, tower, terrain and UI correctly. Source pixel count fell by 36%; observed render-target cache allocation was approximately 414 MB versus 767 MB in TV mode. These are workload/memory observations, not a measured 36% FPS gain. Handheld's first run compiled additional shaders and had more startup stalls; driver and engine caches were not cleared or normalized across all modes. The settled scene was paced near 60 FPS in every mode. These results do not establish an FPS, loading-stall or 1% low improvement. They specifically do not establish doubled FPS or universally stable lows.

Ordinary engine screenshots capture the guest source image, not the FSR presentation output. The windowed runtime logs confirm actual display dispatch dimensions. Headless Auto tune excludes display/FSR cost and says so in its report.

## Source and tests

The bundled AMD headers are byte-for-byte Git blob matches to official upstream commit `a21ffb8f6c13233ba336352bdff293894c706575`: `ffx_a.h` is `d04bff55cbe5f0c77af7a39d6a3452f2744dc6f4`, and `ffx_fsr1.h` is `4e0b3d548553e61eedd384021409f24e3eb74de3`. The EASU/RCAS algorithms are unchanged in this release; license and source pin remain in the engine package and installed games.

Six selected engine suites passed: render_scale, vulkan_headless, frame_times, runtime_present, frame_policy, pipeline_file. The real GPU test now also reconstructs a diagonal edge and verifies that FSR differs from ordinary linear scaling, while preserving neutral colors and nonconstant content. Both UNORM and sRGB, cropped inputs, odd output sizes, repeated resize, constant-color invariants and no additional host wait are covered. RCAS deliberately sharpens beyond the input edge endpoints. The full upstream engine suite was not run.

All 356 application checks and all 10 installer checks passed. New cases cover the protected Performance mode, restoring TV quality, actual TV/handheld fixture runs with save/settings restoration, choosing measured handheld p99 improvement, choosing TV on a tie, and interpreting dispatch versus GPU-ready/bypass records. The changed upscaler C file passes an additional `-Wall -Wextra -fsyntax-only` check and the engine's code-quality candidate check. Source patch reverse application checks cleanly against the current tree.

A final 65-second windowed run using Engine 1.10 completed with 3,750 frames, logged active 1280x720 to 2433x1369 FSR dispatches in handheld mode, and finished with a last-block p99 of 17.625 ms. The captured source frame again showed the world and character correctly. This warmed follow-up is a rendering/startup check, not proof of a performance gain over the earlier runs.

The installed app and bundled engine were verified as 0.27 / 1.10. Mario was upgraded through the backed-up install path and Performance saved with `docked=0`, `upscaler=1`, `resolution=1`, and graphics protection enabled. The player's save tree hash was unchanged across installation and configuration. The cumulative engine source patch retains original line endings so its vendored AMD headers are preserved in source exports.

## Limits

Some games use identical rendering layouts in both modes or lack a useful handheld mode. Lower resolution does not fix CPU/game-loading stalls, invalid shaders, unsupported operations or insufficient hardware. FSR 1 adds GPU work and can be slower than a linear blit. It reconstructs one frame, including guest UI; it cannot create temporal detail, motion vectors or new frames. FSR 4, DLSS and frame generation are not implemented. Other games and physical GPUs remain unverified. The runtime remains AOT/JIT.
