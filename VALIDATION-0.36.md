# PortForge 0.36 / Engine 1.20

## What changed

- **True resolution presets.** Settings: Auto / 720p / 1080p / 1440p / 4K. The engine sizes the window/swapchain to that 16:9 height and may raise presentable color render targets toward it. Depth, storage and writeback targets stay native (`render_scale_safety`).
- **PortForge FSR 3-style.** One Upscaling entry sets `upscaler=1` + `frame_generation=1` (FSR 1 on real frames, then PortForge 2x FG). Not the AMD FSR 3 driver plugin. FG confidence falloff tightened further for residual shimmer.
- **60 → ~120.** With FSR 3-style / FG on, a numeric `fps_limit` is doubled for display cadence (already in 1.19). Settings labels that path; PC match applies FG when global Settings ask for 60 + FSR 3-style.
- **Esc/Home FPS.** Pause menu FPS limit cycles Game / 30 / 60 / 120 / 144 / 240 / Unlimited for the session (does not rewrite global.ini).
- **UI.** Graphics and FPS header button removed; Settings tab is the place for resolution, upscaling and FPS.

## Build / ship

- Engine `1.20` from base `0de0a87` with updated PortForge patch; app assembly `0.36.0.0`.
- Limits unchanged: no AMD FSR 3 SDK, no save-anywhere, not every title scales every buffer to 4K without FSR fallback.
