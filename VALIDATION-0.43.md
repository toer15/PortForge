# PortForge 0.43 / Engine 1.26

## What changed

PortForge FSR 3 is still the from-scratch path: FSR 1 (EASU + RCAS) on real frames, then optical-flow frame generation. It is not the AMD FidelityFX SDK. Switch games still do not give the engine motion vectors or depth.

### Clarity

RCAS after EASU is scale-aware. A small enlarge stays near 0.38 stops. A 2× enlarge (720p to 1440p, or 1080p to 4K) is about 0.22 stops. It never goes to 0, which rings UI. Pixel smoothing is off by default and is not turned on by PortForge FSR 3. When it is on, RCAS stays about a stop softer (around 1.3 to 1.55) so the antialias is not sharpened back into jaggies, and it is no longer the old 2.0 stops that left the enlarged picture soft.

### Detail

The interpolator used to copy the current frame for any match past a very small error, then square the remaining blend weight. High-frequency areas failed that test because bilinear sampling of a correct warp is not a perfect pixel match, so the generated frame was mostly the current frame and detail looked flat.

- A static pixel (HUD lock) is still the current texel.
- A textured match is blended at the midpoint when the two warped colors agree.
- A wrong warp, a disocclusion, or a whip-pan still copies the current pixel. That is the guard that removed the Breath of the Wild multi-ghost.
- Motion vectors are kept only when they beat stillness. A tiny win is treated as noise.

### Performance

Fine pyramid levels search radius 1 with the 5×5 window. A 3×3 window there missed block matches. The widest level searches radius 4 instead of 6. No extra full-frame pass was added. If FSR cannot enlarge, or the upscale image is unavailable, that skip is logged once and the motion field is not searched a second time. Frame generation still runs while Esc is open, so Display FPS does not collapse to the game's own rate.

### Game pace

Speed lock keeps a game that asked for a swap interval at that interval, with or without frame generation. Frame generation inserts a picture between frames that are shown. It does not shorten the game's wait. A 60 FPS game stays at 60 game frames and can show about 120. A limit of 120 or 240 does not make that game run at 240 or 480 on screen. A limit below the game's pace drops pictures and does not slow the game. A game that times itself still follows a numeric cap.

### Resolution

`display_resolution` sizes the window and swapchain. Internal render targets stay native under graphics protection. Selective color-only render-target scaling stays off.

PortForge FSR 3 still turns on for every game from the existing global Settings toggle (`portforge_fsr3` forces FSR 1 and frame generation together). There is no new per-game opt-in.

## Limits

This cannot see the game's motion vectors, so fast camera cuts, translucency and particle effects can still fall back to the current frame or show a short error. It does not add detail the game did not render. It does not guarantee a higher frame rate: the upscaler and the search are extra GPU work, and the cheaper finest level is the only cost cut. Only the spatial FSR 1 shaders are AMD's. The frame generation shaders are PortForge's.

Restart the game after changing Settings. Existing games need **Doctor → Engine** (or a reinstall of the engine) so they pick up Engine 1.26.
