# PortForge 0.59 / Engine 1.42 validation

Tested on Windows 11 with an AMD Radeon RX 9070 XT and a Ryzen 7 9800X3D on 2026-10-27, using copies of two converted games (the player's own folders and saves were not touched).

## Changes

- True resolution is now exact. Engine 1.41 assumed every game draws 1080 lines in TV mode, so a game that draws 900 lines rendered "1440p" at 2133x1200 and "4K" at 3200x1800. Engine 1.42 counts the heights of the frames the game shows, records the game's own output height in `<game>.output`, and scales from it at the next start. The warm-up run after an engine install records it, so the first real session already gets the exact size.
- New per-game setting `paced_display`. When it is 0, only the game's own frames reach the display: no generated pictures and no repeats up to the FPS limit. Global settings do not change it.
- The Doctor's new `safe_pacing` repair sets `paced_display=0` for a game whose log shows a GPU device loss while frame generation or an FPS limit above the game's pace was adding pictures between its frames. Other games keep frame generation and their FPS limit.

## Real-game results

- Mario game (draws 900 lines in TV mode), 1440p, true resolution, FSR 1 and frame generation on. The first run recorded `tv 900`. The second run rendered screen-sized targets at 1.60x, which is 2560x1440. Before the change they rendered at 1.33x, which is 2133x1200. Frame pacing was unchanged across both 60-second runs: p99 about 17.5 ms and 1% low about 57 FPS in steady scenes. A screenshot showed no rendering errors.
- Pokémon game, 190-second scripted runs:
  - The GPU device was lost in 2 of 3 runs that added pictures between frames: once with frame generation at FPS limit 120 (37 s), and once with only the FPS-limit repeats (177 s).
  - The run at the game's own pace had no device loss.
  - With `paced_display=0` and the player's global settings applied (FPS limit 120, frame generation on), 2 of 2 runs had no device loss.

## Known problem, not fixed

The Pokémon game crashes with an invalid memory access when it opens the movie `d020_movie_a.bk2`. This happened in all 3 runs that reached it, with or without frame generation or an FPS limit.

The game's movie player computes the movie's audio sample rate as 0. It then requests a zero-size audio memory pool, which the SDK refuses (`nn::audio::AcquireMemoryPool` returns false). The game does not check the result and calls `MemoryPoolInfo::Attach` on a null pointer.

The emulated value that makes the rate 0 is not identified yet. No setting avoids this crash. The Doctor's existing memory_gb=6 repair does not fix it either.

## Automated checks

- Built Engine 1.42 with MSYS2 CLANG64. These engine CTest suites passed: `runtime_present`, `frame_policy`, `render_scale` (with new checks for recorded output heights and the frame-height counter), `pipeline_file` and `nx_jit_region`.
- Regenerated `engine/portforge-engine.patch` against `engine/BASE` (`0de0a87`) and confirmed it applies to a clean worktree at that base.
- `build-native.ps1 -Test` passed with the packaged engine as backend: native, interface, cloud-save, optimization (117), download, diagnostic (40, with new `safe_pacing` checks), update, doctor (38), engine (22) and frame-generation cadence suites.
