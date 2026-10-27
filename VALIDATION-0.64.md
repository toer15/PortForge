# PortForge 0.64 / Engine 1.46 validation

Tested on Windows 11 with an AMD Radeon RX 9070 XT and a Ryzen 7 9800X3D on 2026-10-27, at 2560x1440 on a 280 Hz panel.

## The problem

The FPS limit offers 240, and the engine accepted any number from 10 to 1000, but nothing above twice the game's own pace was ever shown. `present_display_target` returned `side * 2` with frame generation on, and a 60 FPS game makes 60 frames a second, so Limit 240 showed 120 and Limit 1000 showed 120 as well. The presenter asked the interpolator for exactly one generated picture per real frame (`vk_fg_interpolate_mids(..., 1)`), so twice the game's frames was a hard ceiling: 240 was a number the menu offered and the display never reached. A 240 Hz or faster panel (this PC's is 280 Hz) could not be used at any limit above 120.

## Changes

- **The typed limit is the target, up to four pictures a real frame (engine).** `present_display_target` pays a limit above twice the game's pace with up to `PRESENT_FG_MAX_MIDS` (3) generated pictures a real frame, so 240 on a 60 FPS game is 240, 144 is 144 and 200 is 200, instead of all of them falling back to 120. Unlimited and Game speed still show twice the game's frames, since they ask for no number above them, and VSync still caps the target at the display's refresh.
- **The frame's share is carried as a fraction (engine).** `present_frame` accumulates the part of a picture a frame is still owed (`g_fg_owed`), so a 144 limit on a 60 FPS game is 2.4 pictures a frame: two, then three, then two, and never a repeat of a picture already shown. A frame whose share is not yet a whole picture shows the real frame alone at its half-frame delay, as before.
- **Phased pictures (engine).** `fg_schedule` takes the array the renderer made instead of one picture, and places them in phase order (0.25, 0.5, 0.75 of the frame) before the real frame. One generated picture a frame, which is what AMD's FidelityFX interpolation makes, is unchanged, and so is everything at or below twice the game's pace.
- **The two ceilings cannot drift (engine, build time).** `PRESENT_FG_MAX_MIDS` (runtime/options.h, where both present.c and the renderer can see it) is asserted equal to the renderer's `FG_MAX_MIDS` (gpu/vk/vk.h) with `_Static_assert`, so an engine whose numbers disagree does not build.
- **The app predicts the same number (app).** The global settings' "on screen" note showed 120 FPS for a 240 limit with FSR 3; it now shows 240 in a 60 FPS game and 120 in a 30 FPS game, matching the engine. The notes say where the panel's refresh caps it.

## What was measured

The pacing code was extracted from `engine/portforge-engine.patch` itself (`verification/limiter_harness.py`; the scratch files are not part of the repository), compiled with clang 23.1.3 (MSYS2 CLANG64) and driven with the engine's own `runtime_present` table plus the rates the new ceiling pays.

| limit | FSR 3 off, 60 FPS game | FSR 3 on | FSR 3 on, 144 Hz panel | FSR 3 on, 30 FPS game |
|---|---|---|---|---|
| Game speed | 60 | 120 | 120 | 60 |
| 30 | 30 | 30 | 30 | 30 |
| 60 | 60 | 60 | 60 | 60 |
| 90 | 60 | 90 | 90 | 60 |
| 120 | 60 | 120 | 120 | 120 |
| 144 | 60 | 144 | 144 | 120 |
| 165 | 60 | 165 | 144 | 120 |
| 200 | 60 | 200 | 144 | 120 |
| 240 | 60 | 240 | 144 | 120 |
| 1000 | 60 | 240 | 144 | 120 |
| Unlimited | 60 | 120 | 120 | 60 |

Pictures a real frame, over 600 frames of a 60 FPS game:

| limit | result |
|---|---|
| 240 | 2400 pictures, 600 frames of four, every frame |
| 160 | 1598 to 1602 pictures, two and three a frame |
| 144 | 1439 pictures (361 frames of two, 239 of three), within one of 1440 |
| 120 | 1200 pictures, two a frame |
| 60 | 600 pictures, the game's own frame |

The order the pictures reach the display was checked against the scheduled queue: a 240 frame is the first, second and third generated picture and then the real frame; a 120 frame is the generated midpoint and then the real frame; a fractional frame's real frame is queued at half the game's period. A hitch (`steady` false) is never paid with generated pictures.

## Limits

- **No real game was run for this release.** The rates, the fraction, the picture order and the tables above are checked on the host; the warp quality of the extra quarter-phase pictures is not measured here. Those above-twice pictures come from PortForge's own motion warps, because AMD's FidelityFX interpolation produces the midpoint only, and Engine 1.31/1.32's three-or-four-a-frame generation was rolled back for visible artifacts. A limit of 240 on a 60 FPS game therefore shows a midpoint plus two warped quarter phases: expect them to be less exact than the doubled pictures. **Use 120 (or any limit at or below twice the game's pace) where quality matters and 240 where the rate does.** Turn AMD FSR 3 off for the game's own frames only.
- **A 30 FPS game reaches 120, not 240.** One real frame pays at most three generated pictures, so four times the game's frames is the ceiling: 240 on a 30 FPS game shows 120.
- **The panel is the ceiling under VSync.** 240 needs a 240 Hz or faster display with VSync on; on a 144 Hz panel a 240 limit shows 144, and on a 60 Hz one, 60. With VSync off the target is the typed limit, and tearing is the game's own.
- The engine still never runs the game itself faster than its swap interval (speed lock): the extra pictures are generated, not game frames, so a frame-locked game's logic and physics are unaffected.
- Engine CTest suites `runtime_present`, `frame_policy`, `render_scale`, `overlay` and `pipeline_file` run in CI on this change; their expectations for 240, 144, 165 and 200 were updated here. The app suites were run locally. Native integration tests still need the ScarletNX backend and the `original-test.nro` fixture, which are not on this PC, so they were not run.
