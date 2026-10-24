# PortForge 0.51 / Engine 1.33

## What was wrong in 0.50 / Engine 1.32

- **With PortForge FSR 3 off the screen never showed the limit.** A 30 FPS game such as Zelda BotW showed 30 at every limit, because 1.32 never made pictures without frame generation. The Esc menu and every FPS counter said 30 at Limit 60, 120, 144 and 240.
- **With PortForge FSR 3 on, 144 and 240 did nothing.** Frame generation stopped at 4 pictures per real frame, and only whole numbers of them. A 30 FPS game showed 120 at Limit 60, 120, 144 and 240 alike, and a 60 FPS game showed 240 at 120, 144 and 240.
- **The motion search lost fast camera turns.** Its 5x5 matching window found lookalike matches in flat areas on the coarse pyramid levels, and the wrong motion went down the pyramid. On a fast camera turn more than half of the motion vectors were wrong, so the generated pictures were barely better than blending two frames (see below). This is where most of the smearing and tearing in motion came from.
- **A real frame that arrived a little early showed every queued generated picture at once.** With more pictures per frame that becomes a burst of presents, a visible stutter.

## What Engine 1.33 does

| Limit | FSR 3 off | FSR 3 on |
|---|---|---|
| 30 | 30 | 60 |
| 60 | 60 | 120 |
| 120 | 120 | 240 |
| 144 | 144 | 288 |
| 240 | 240 | 480 |
| Unlimited / Game | the game's own pace | twice the game's pace |

Everything is capped by the monitor's refresh: on a 280 Hz monitor, 288 and 480 show as 280.

- The game always runs at its own speed (speed lock). A 30 FPS game stays at 30 real frames a second at every limit.
- When the screen should show more than the game's real frames, the pictures in between are generated, with FSR 3 off too. A 30 FPS game at Limit 120 with FSR 3 off shows 4 pictures per real frame. A limit at or below the game's pace with FSR 3 off generates nothing.
- Uneven ratios work: 144 from a 30 FPS game is 4.8 pictures per real frame, made as four frames of 5 and one of 4. Up to 10 pictures per real frame.
- Every picture is a real present, so the PortForge counter, Esc, the frame-time graph and outside overlays (AMD, Steam) all count it.
- Esc shows `Engine 1.33 | Display FPS: 144 (game 30 x4.8) | Limit: 144`.
- Motion search: the coarse pyramid levels match over a 9x9 window (every other texel, the same 25 taps), and each level's vectors are propagated twice: each texel re-tests its neighbours' vectors by its own match.
- Generated pictures left over when the next real frame arrives are dropped instead of being shown all at once.

## Verification

**Pacing.** `runtime_present` has 144 checks (79 before), all passing. They cover every limit in the app with FSR 3 off and on, for a 60 FPS game and a 30 FPS game, the 144, 280 and 60 Hz display caps, and the pictures each real frame makes over two seconds of frames: the total matches the Esc number at every limit, and no two real frames differ by more than one picture. `frame_policy` also passes. Both were built with the engine's test flags (`-Wall -Wextra -Werror`).

**Frame generation quality.** The four frame-generation shaders were ported to Python (`tools/fg-eval/`) and run on synthetic 960x540 scenes whose in-between frames are known exactly: a textured world panning with a textured object moving the other way (occlusion at its edges), a static HUD and a see-through HUD panel. Three speeds: a walk with the camera turning (18 px of world motion per real frame), a fast camera turn (40 px) and a slow pan (5 px). Each pair is scored at phases 0.25, 0.5 and 0.75. Moving pixels off by more than 60 (summed RGB) count as visibly wrong.

| | PSNR | Visibly wrong moving pixels | Fast turn, wrong |
|---|---|---|---|
| Engine 1.32 | 22.82 dB | 13.7% | 20 to 23% |
| Engine 1.33 | 26.86 dB | 5.5% | 4.4 to 4.7% |
| Blending the two real frames | 20.0 dB | 37% | 45 to 51% |
| Perfect motion vectors (the ceiling of this interpolation) | 27.3 dB | | |

Motion vectors more than one texel wrong: fast turn 58% → 15%, walk 23% → 16%, slow pan 34% → 13%. HUD pixels stay exact (0% wrong) in both versions.

All five shader variants compile with glslang 15.4. The engine sources compile with `-Werror=missing-prototypes`, and the patch rebuilds the exact tree on ScarletNX `0de0a87`.

## Limits

- This was not run in Zelda on a GPU. The session had no Windows PC or GPU. The shader port follows the GLSL line by line, but the quality numbers come from synthetic scenes, not game frames. Use `fg_dump` and `snx-test-vulkan -H --fg-eval` for a real-game check.
- Generated pictures still make artifacts. Things seen in only one frame, motion beyond the search and the world seen through gaps in a dense HUD (about 38% wrong between HUD strokes in the test) remain the weak spots. FSR 3 off now also generates pictures when the limit is above the game, so those artifacts can appear there too.
- At 8x to 10x most of what you see is generated. Each real frame costs up to nine warp passes at the output size.
- A monitor can't show more pictures than it refreshes. 288 and 480 show as the refresh rate.
- This is PortForge's own frame generation, not the AMD FidelityFX SDK. Switch games give no motion vectors or depth to the host.
- The Engine and app were built by the new GitHub workflow (`.github/workflows/ship.yml`), not on the developer's PC.
