# PortForge 0.52 / Engine 1.34

## What was wrong in 0.51 / Engine 1.33 and 0.50 / Engine 1.32

- **0.51 (Engine 1.33) generated the pictures above the game's real frames even with PortForge FSR 3 off,** up to 9 per real frame. FSR 3 off therefore still showed frame-generation artifacts, and at 120 to 240 they were on screen nearly all the time. It was never run in a game. 0.52 shows the real frames again instead, as chosen by the player.
- **With FSR 3 on, 1.33 generated up to 9 pictures per real frame** (1.32: 3). 0.52 makes one per real frame and repeats it.
- **0.50 (Engine 1.32) never followed the limit with FSR 3 off**: Zelda stayed at 30 at every limit. With FSR 3 on it stopped at 4 pictures per real frame.
- **A stale upscaled frame could be warped after a skipped pair** (the capture didn't invalidate its FSR'd copy), giving a flash of an old picture after a hitch.
- **Frame generation warped across hitches and catch-up bursts.**
- **FSR'd and generated pictures were smoothed a second time** when pixel smoothing was on (it is on in the tested PC's global settings), which blurred them.
- **The bad-match fallback could never trigger.** Its threshold was 9..10 on a 0..3 scale.
- **Esc showed PortForge FSR 3 "Off" while frame generation ran** whenever FSR 1 was off.
- **PC matching (app)** turned Game speed into 60 with FSR 3, skipped Unlimited, and left frame generation on after FSR 3 was turned off.

## What Engine 1.34 does

`present_display_target`: the limit with FSR 3 off, twice the limit with it on. Game speed and Unlimited are the game's own frames (doubled with FSR 3). With vsync, it never goes above the display's refresh. The game always steps at its own swap interval (speed lock).

- FSR 3 off, limit above the game's pace: the frame is filtered once (FSR 1 / smoothing) into a held copy. That copy is shown at even steps until the next real frame (`vk_fg_hold`, `fg_schedule`). A fractional share is carried over to the next frame, so 144 on a 30 FPS game alternates 4 and 5 pictures.
- FSR 3 on: one generated picture between each two real frames. The generated and the real picture each fill half of the frame's time, repeated up to the target.
- The swapchain always has spare images (5), so a limit or FSR 3 change in Esc doesn't remake it.
- 1.33's motion search stays (9x9 window on coarse levels, vectors propagated twice a level). Staying put costs 0.8 of its match (`FG_ZERO_BIAS`) as a guard against erasing a character the camera follows.
- Esc: `Engine 1.34 | Display FPS: N of T (game G) | Limit: L`. The log writes the same every 10 s.

## Verification on the developer PC (Ryzen 7 9800X3D, RX 9070 XT, 2560x1440 at 280 Hz, vsync on)

Isolated full copy of Zelda BotW (data and save copied, the player's folder untouched), FSR 1 + pixel smoothing as in the player's global settings. The limit and FSR 3 were changed in the running game's .ini. Readings are the engine's own `Display FPS` log line (presents per half second, sampled every 10 s):

| Limit | FSR 3 off | FSR 3 on |
|---|---:|---:|
| Game speed | 30 | 60 |
| Unlimited | 30 | 60 |
| 30 | 30 | 60 |
| 60 | 60 | 120 |
| 120 | 120 | 240 |
| 144 | 144 | 278, 279 (display max 280) |
| 240 | 241, 239 | 280, 279 (display max) |

An earlier local build with the same pacing and 1.32's motion search gave the same numbers on this PC.

The game reported 30 real frames a second throughout. No generated or repeated picture was dropped for want of a swapchain image (0 log lines).

Frame-generation quality on the 20 dumped Zelda triplets (`snx-test-vulkan -H --fg-eval`, picture generated between frames 0 and 2 against real frame 1):

| | PSNR, 20 sets | Visibly wrong moving pixels |
|---|---:|---:|
| Engine 1.32 (0.50) | 22.61 dB | 20.58% |
| Engine 1.33 (0.51) | 23.89 dB | 16.29% |
| Engine 1.34 (0.52) | 23.94 dB | 16.31% |
| Repeating the newer frame | 19.57 dB | 45.76% |

0.51's motion search is the big gain on these real frames, and 0.52 keeps it. On screen, the bigger change is how often generated pictures appear. With FSR 3 off there are none. With FSR 3 on there is one per real frame, where 1.33 had up to 9 and 1.32 up to 3. In set 16 (fast turn while running) Link vanished in 1.32 and is whole in 1.33 and 1.34. Fast camera turns still smear walls at the edge of the screen.

Engine tests: `runtime_present` (94 checks: every limit with FSR 3 off and on, 30 and 60 FPS games, 60/144/280 Hz, vsync off), `overlay`, `frame_trace`, `frame_policy` and the headless Vulkan suite (frame generation included) pass. App: `FgCadenceTests` and `OptimizationTests` (107 checks, including every global limit × upscaling mode) pass.

## Limits

- Repeats raise Display FPS, not smoothness: with FSR 3 off motion is still the game's 30. With FSR 3 on, motion is twice the game's frames however high the limit goes.
- Only Zelda BotW (30 FPS) was run on screen. 60 FPS games follow from the same code and its unit tests.
- This is PortForge's own frame generation, not the AMD FidelityFX SDK. Switch games give no motion vectors or depth.
