# PortForge 0.61 / Engine 1.44 validation

Tested on Windows 11 with an AMD Radeon RX 9070 XT and a Ryzen 7 9800X3D on 2026-10-27, using a copy of a converted Mario game.

## Changes

- **Any FPS limit.** Global settings: the FPS limit box takes any whole number from 10 to 1000, and the presets stay in its list. In-game Home/Esc menu: left and right change the limit by 1 (hold to repeat), L/R or ZL/ZR change it by 10, and A walks the presets (Game speed, 30, 60, 120, 144, 240, Unlimited). Mouse taps on the arrows still step through the presets.
- **Locked to the typed limit.** With AMD FSR 3 frame generation, a limit between the game's pace and twice it now gets exactly that many distinct pictures a second. Engine 1.43 fell back to the game's own frames in that range. A generated picture follows as many real frames as the limit pays for, every real frame keeps the same half-frame delay so the real frames stay evenly spaced, and no frame is repeated.

## Real-game result

- Mario game at a typed limit of 90 with VSync and AMD FSR 3 frame generation. Display FPS held at 89 to 91. Over 41 s there were 2,400 real and 1,200 generated pictures, a 2:1 ratio: 60 real plus 30 generated each second. Steady-scene frame-time p99 was about 17.2 to 17.6 ms, the same as at limit 120.

## Limits

- The game itself still runs at its own pace (speed lock). Pictures above that pace are AMD-generated. Real game frames above the game's pace would also run a frame-locked game faster.

## Automated checks

- Engine CTest suites `runtime_present` (with new typed-limit checks), `frame_policy`, `render_scale`, `overlay` (with new controller-stepping checks), `pipeline_file` and `nx_jit_region` passed.
- `build-native.ps1 -Test` passed with the packaged engine, including the frame-generation cadence checks.