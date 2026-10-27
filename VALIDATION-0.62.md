# PortForge 0.62 / Engine 1.45 validation

Tested on Windows 11 with an AMD Radeon RX 9070 XT and a Ryzen 7 9800X3D on 2026-10-27, using a copy of a converted Pokémon game. The player's own game folder and save were not touched.

## The problem

A player's Pokémon game stayed at 30 FPS at FPS limit 120 with AMD FSR 3 frame generation on, while the in-game menu showed AMD FSR 3 On and "120 (60 on screen)". Its log read `Display FPS 30, target 60`. On Engine 1.42 the Doctor had answered a GPU reset by setting `paced_display=0` for that game, which shows only the game's own frames. Nothing ever turned it back on, and Engine 1.44 still counted frame generation as running.

## Changes

- **Honest in-game menu (engine).** With `paced_display=0`, AMD FSR 3 reads "Off (GPU safe mode)", and the FPS limit row, the Display FPS target and the log count only the game's own frames.
- **Menu switch lifts the safe mode (engine).** Turning AMD FSR 3 On in the Home/Esc menu sets `paced_display=1` in the game's .ini, so frame generation and the limit apply straight away.
- **New engine lifts it automatically (app).** When the app installs a new engine into a game (on Play or from the Doctor), it sets `paced_display=0` back to 1 and keeps a backup of the .ini. If the GPU resets again on the new engine, the Doctor sets it again.

## Real-game results (Pokémon copy, limit 120, VSync, AMD FSR 3)

- On Engine 1.44 with the safe mode removed, a 260 s run had no GPU reset. So did six more runs of 45 to 120 s on Engine 1.45 builds.
- Engine 1.45 in safe mode: `Display FPS 20-21, target 30 (... FSR 3 off)`. The target and the FSR 3 state now match what is shown.
- Engine 1.45 with the safe mode lifted: `Display FPS 40-42, target 60 (... FSR 3 on)`, which is twice the game's own frames.

## Limits

- In the scene the scripted test reaches, this game produces 20 to 21 frames a second of its own, with or without frame generation (a control run with frame generation off showed `Display FPS 20-21`). Frame generation doubles that to about 42. It does not make the game itself render faster. The game's frames wait about 60 ms per frame on GPU results while the GPU is about 41% busy. That is a separate engine performance problem and isn't fixed here.

## Automated checks

- Engine CTest suites `runtime_present`, `frame_policy`, `render_scale`, `overlay`, `pipeline_file` and `nx_jit_region` passed.
- The app test suites passed, including a new check that a new engine lifts `paced_display=0` once and keeps the player's FPS limit. Native integration tests need the ScarletNX backend and `original-test.nro` fixture, which are not on this PC, so they were not run.
