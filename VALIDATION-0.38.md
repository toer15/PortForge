# PortForge 0.38 / Engine 1.22

## Fixes (BOTW ghosting / letterbox / FPS)

- **No selective color-only RT scale** under graphics protection. That path left depth native and corrupted titles (translucent Link, shattered geometry, multi-layer ghosts). `display_resolution` only sizes the window/swapchain; FSR / PortForge FSR 3 enlarge on present.
- **Hardened PortForge FSR 3 FG:** weak warps fall back to the latest real pixel (artifact-first).
- **Safe area:** ignore a reported safe rect under ~85% of the window so the game is not boxed into a corner with the map on black.
- **On-screen FPS:** with frame generation / PortForge FSR 3, the showcase FPS counter uses `vk_fps_presents()` (display submissions, including generated frames) so a 60 limit reads ~120.
