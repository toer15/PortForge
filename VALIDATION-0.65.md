# PortForge 0.65 / Engine 1.47 validation

Tested on Windows 11 with an AMD Radeon RX 9070 XT and a Ryzen 7 9800X3D on 2026-10-27, at 2560x1440 on a 280 Hz panel.

## The problem

**Unlimited was not unlimited.** Engine 1.46 made a typed limit above twice the game's pace reach the number typed, but `present_display_target` still treated Unlimited (`fps_limit=0`, no number at all) as Game speed (`SNX_FPS_GAME`): with FSR 3 frame generation both returned twice the game's frames, so a 60 FPS game showed 120 at Unlimited while the engine could make 240 - the ceiling the 240 preset had just learned to reach. Esc said `Unlimited` and a rate the setting did not mean, and with FSR 3 off Unlimited was the game's own frames, which is right, but nothing else about the row was (the two modes were indistinguishable in the code).

In gaming, "Unlimited" means no cap: as many frames as the machine can make. Here the game itself keeps its own pace (the speed lock, and it stays that way: a limiter must not change the game's speed), so the pictures that can be made are the game's own frames, and - with AMD FSR 3 frame generation - up to three generated pictures a real frame.

## Changes

- **Unlimited is no cap (engine, one condition).** `present_display_target` now gives `fps_limit == 0` the same ceiling a typed limit above twice the game's pace gets: `side * (PRESENT_FG_MAX_MIDS + 1)`, four times the game's own frames, which is 240 in a 60 FPS game and 120 in a 30 FPS game. `SNX_FPS_GAME` is untouched: Game speed stays the game's own pace, doubled by FSR 3 (120 in a 60 FPS game). With FSR 3 off, Unlimited is still the game's own frames, because a picture the game never drew cannot be shown.
- **The panel and VSync still rule it.** Under VSync the target is capped at the display's refresh (144 on a 144 Hz panel, 60 on a 60 Hz one); with VSync off nothing is (240 in a 60 FPS game on this 280 Hz panel, i.e. free FPS, with tearing the game's own).
- **The app predicts the same number (app).** The Settings tab's "on screen" note had one prediction for Game speed and Unlimited together; it now computes Unlimited as the ceiling (240 in a 60 FPS game, 120 in a 30 FPS game, with FSR 3) and Game speed as the doubled game pace (120 and 60), and the FPS and upscaling notes say what Unlimited means. `InterfaceTests` pins all three predictions by reading the panel's own note.
- **Tests updated, not loosened.** The engine's own `runtime_present` rows now hold Unlimited to 240 (it held it to 120), add a 144 Hz panel row and a no-VSync row for it, and the 30 FPS table's Unlimited row is 120 with FSR 3, 30 without. `FgCadenceTests` checks the new condition text and those rows, and the pacing harness now asserts that Unlimited's target pays four pictures a frame.

## What was measured

The pacing code was extracted from `engine/portforge-engine.patch` itself (`verification/limiter_harness.py`; the harness is not part of the repository), compiled with clang 23.1.3 (MSYS2 CLANG64) and driven with the engine's own table plus the rates above.

Pictures a second on a 280 Hz panel, by limit, a 60 FPS game:

| limit | FSR 3 off | FSR 3 on |
|---|---|---|
| Game speed | 60 | 120 |
| 30 / 60 | 30 / 60 | 30 / 60 |
| 120 | 60 | 120 |
| 144 | 60 | 144 |
| 240 | 60 | 240 |
| **Unlimited** | **60** | **240** |

The same Unlimited at other panels and games:

| case | result |
|---|---|
| Unlimited, FSR 3 on, 144 Hz panel | 144 (the panel, not 240) |
| Unlimited, FSR 3 on, 60 Hz panel | 60 |
| Unlimited, FSR 3 on, no panel known / VSync off | 240 (uncapped) |
| Unlimited, FSR 3 on, 30 FPS game | 120 (four times its own frames) |
| Unlimited, FSR 3 off, any panel | the game's own frames (60 / 30) |
| Game speed, FSR 3 on, 60 FPS game | 120 (unchanged) |

Pictures a real frame: Unlimited's target pays four in a 60 FPS game (three generated plus the real frame), the same as the 240 preset - 2400 pictures over 600 frames, every frame four - checked against the cadence the presenter actually runs, and a hitch (`steady` false) is still never paid with generated pictures.

`InterfaceTests` (46 checks, 3 new) passes against the built frontend on this PC, `FgCadenceTests` passes, and `engine/portforge-engine.patch` applies with `git apply --check` to ScarletNX at `engine/BASE` (0de0a87).

## Limits

- **No real game was run for this release**, so the rates and the picture counts above are host-side and the warp quality of Unlimited's extra pictures is not measured here. Unlimited with FSR 3 uses the same generation the 240 preset uses: a midpoint plus two warped quarter phases, which are less exact than the doubled picture. Unlimited is a rate choice, like 240; use 120 or Game speed where quality matters.
- **Unlimited on a 30 FPS game is 120, not 240**, for the same reason the 240 preset is: one real frame pays at most three generated pictures. More than that is the 1.31/1.32 behaviour (3x to 10x generation) that was rolled back for visible artifacts.
- **It is not unlimited simulation speed.** The speed lock still keeps the game at its own swap interval, so "free FPS" here means pictures on the display, never the game's logic or physics running faster.
- CI builds this release's engine by applying the patch to ScarletNX at `engine/BASE` and runs the engine's presenter suites (`runtime_present`, `frame_policy`, `render_scale`, `pipeline_file`, `nx_jit_region`) on it. Two app suites could not be run here: `EngineTests` fails identically on the unmodified sources (its own fixture, before this change), and the native integration test needs the ScarletNX backend and the `original-test.nro` fixture, which are not on this PC.
