# PortForge 0.50 / Engine 1.32

## What was wrong in 0.49 / Engine 1.31

- **The game sped up with PortForge FSR 3 off.** `present_period` paced the game at the FPS limit whenever frame generation was off. The player's own Zelda log showed the game stepping 120 frames a second with Limit 120: a 30 FPS game ran four times too fast. The speed lock's own tests (`runtime_present`) already said this must not happen.
- **Extra generated frames were all the same picture.** The interpolation shader ignored the phase it was given, so 3x/4x showed the midpoint three times, then the real frame: motion stopped and jumped (the "ghosting and lag" that made 1.31b go back to 2x).
- **Imperfect matches copied the newer real frame.** Wherever the match wasn't confident, the generated picture took the newer frame's pixel in place, so moving edges tore into it. Scored against real frames, the generated picture was barely better than repeating a frame (19.7 dB against 19.6).
- **Generated pictures used immediate presents,** which can tear mid-scanout.
- **The limit was checked after the frame was captured,** so with a limit below the game's pace the generated pictures sat between frames that were never shown.
- **Esc held the Display FPS reading from before the menu opened.**
- **FSR 1 logged its state twice every frame** with frame generation on (3,268 lines in a 4-minute session).

## What Engine 1.32 does

| Game's own pace | Limit | FSR 3 off | FSR 3 on |
|---|---|---|---|
| 30 (Zelda BotW) | Game / Unlimited | 30 | 60 (2x) |
| 30 | 30 | 30 | 60 (2x) |
| 30 | 60 / 120 / 144 / 240 | 30 | 120 (4x) |
| 60 | 30 | 30 | 60 (2x) |
| 60 | 60 | 60 | 120 (2x) |
| 60 | 120 / 144 / 240 | 60 | 240 (4x) |

The game always steps at its own swap interval (speed lock). Frame generation targets twice the limit with 2 to 4 pictures per shown real frame. It never goes above the display's refresh, and it is off when the display can't show twice the real frames. Each generated picture is warped to its own point in time. Imperfect matches blend the best patch-matched motion candidates instead of copying the newer frame. Motion is searched on a five-level pyramid from half resolution to 1/32. Vsync uses FIFO presents. Esc shows `Engine 1.32 | Display FPS: N (game G xK) | Limit: L`, measured live.

## Verification

All on the developer's PC: Ryzen 7 9800X3D, RX 9070 XT, 1440p 280 Hz, VSync on. Isolated Zelda BotW copy with a copy of the player's save, walking with the camera turning. The limit was changed while the game was running, through the game's .ini. "Screen" counts changes in a 64x64 patch of the real desktop. It samples about 250 times a second, so at 120 it misses some changes. "Engine" counts the presents the engine submitted.

| Run | Limits | Screen pictures/s | Game frames/s (trace) |
|---|---|---|---|
| FSR 3 off, 1440p | Unlimited, Game, 30, 60, 120, 144, 240 | 29.9, 30, 30, 30, 30, 30, 30 | 30.0 throughout |
| FSR 3 on, 1440p (FSR 1920x1080 → 2560x1440) | Unlimited, 30 | 60, 60 | 30.0 |
| FSR 3 on, 1440p | 60, 120, 240 | 108.6, 107.7, 107.7 (engine: 120) | 30.0 throughout, no frame over 47 ms |

Frame generation quality: 20 triplets of three consecutive real frames were captured (`fg_dump`). The picture generated between frames 0 and 2 is scored against the real frame 1. That is twice the motion of real use, so it is a hard case. Moving pixels off by more than 60 (summed RGB) count as visibly wrong.

| | PSNR, all 20 | Visibly wrong moving pixels | PSNR, 14 evenly spaced moving sets |
|---|---|---|---|
| Engine 1.31 | 19.70 dB | 43.4% | not measured |
| Engine 1.32 | 22.61 dB | 20.6% | 21.09 dB |
| Repeating the newer frame | 19.57 dB | 45.8% | 18.04 dB |
| Unwarped 50/50 blend | 21.46 dB | 44.1% | |

The synthetic moving-plane test's midpoint error fell from 48,036 to 186 (an unwarped blend is 80,592).

Tests: `runtime_present` 79 checks, overlay, vpad (link fixed), frame_trace, save_activity and the full headless Vulkan suite (frame generation included) pass. 226 of 232 engine tests pass. Of the other 6: `ldn_lan` and `ldn_service` need LAN broadcast, which this PC's firewall blocks. `astc_vulkan`, `astc_vulkan_pieces` and `nx_jit_region` don't link on Windows (missing `plat_prof_add_thread` / `plat_ns` in their test builds). None of these changed in this update.

## Limits

- Only Zelda BotW (30 FPS) was run. The 60 FPS rows follow from the same code and its unit tests, but no 60 FPS game was measured on screen.
- Fast motion and things seen in only one frame still show artifacts. Interpolating twice the real motion still gets about a fifth of moving pixels visibly wrong. 4x shows generated pictures three quarters of the time, so artifacts are more visible than at 2x.
- Zelda's real frames are not always evenly spaced in time. 6 of the 20 triplets had most of the motion between one pair. Frame generation can't fix that.
- This is PortForge's own frame generation, not the AMD FidelityFX SDK. Switch games give no motion vectors or depth to the host.
