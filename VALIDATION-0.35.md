# PortForge 0.35 / Engine 1.19

## What changed

- **FSR + frame generation artifacting.** Engine 1.18 could shimmer or corrupt edges when AMD FSR 1 and PortForge frame generation ran together. Two present submits per game frame shared one FSR scratch image across frames in flight, and FSR also ran on interpolated midpoints. Engine 1.19 keeps FSR scratch per `VK_FRAMES` slot and, when FSR would enlarge, upscales each real history frame once then interpolates in that output size so `show()` does not FSR a generated picture.
- **FPS limit with frame generation.** A numeric `fps_limit` is doubled for display-side cadence (capped at 1000) while frame generation is on, so a setting of 60 targets about 60 pictures on screen with 2x generation. The speed lock still keeps the game's own pace.
- **FPS readout.** With frame generation on, the on-screen counter uses display submissions (including generated frames) instead of only real game presents.

## Build and tests

- Engine built from base `0de0a87` with the updated PortForge patch; `ENGINE_BUILD_OK`, packaged as Engine 1.19.
- `runtime_present` checks include frame-generation doubling of a numeric limit under the speed lock.
- App assembly version is 0.35.0.0 for in-app update detection against GitHub `v0.35.0`.

## Limits

Frame generation remains experimental. It can still add latency, miss difficult motion, and needs GPU/display headroom. It is not AMD FSR 3. Universal save states, native resolution overrides and FSR 2/3 remain unsupported. Results may vary by GPU and driver; the ring-buffer and FSR-before-FG path are meant to make the FSR+FG combination behave the same on any supported PC rather than depend on one machine's timing.
