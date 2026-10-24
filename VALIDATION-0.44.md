# PortForge 0.44 / Engine 1.27

## What changed

PortForge FSR 3 is still the from-scratch path: FSR 1 (EASU + RCAS) on real frames, then optical-flow frame generation. It is not the AMD FidelityFX SDK. Switch games still do not give the engine motion vectors or depth.

Engine 1.26 copied the current pixel unless the warp error was tiny, then blended with a weight that faded out quickly. On the headless moving-block fixture that was no better than averaging the two frames where they sit (midpoint error 81156, plain blend 80592). The test asks for better than three quarters of that blend.

Engine 1.27 keeps the midpoint when the two warped colors agree. A static pixel, including HUD, is still the current texel (`e0` under 0.018). A large color error, a failed bidirectional check, or a vector longer than 28 pixels still copies the current pixel. In between, the blend weight falls off instead of being squared away. The coarsest motion search stays at radius 4. Fine levels stay at radius 1 with a 5×5 window. A 3×3 window on the fine level missed the block match, so it is not used.

Scale-aware RCAS from 1.26 is unchanged: stronger when pixel smoothing is off (down to 0.16 stops on a large enlarge), about 1.37 stops when pixel smoothing is on. Pixel smoothing stays off unless the user turns it on. Selective color-only render-target scaling stays off. Game pace is unchanged from 1.26: frame generation does not speed the simulation up.

PortForge FSR 3 still turns on for every game from the existing global Settings toggle. There is no new per-game opt-in.

## Measured on this PC

Headless Vulkan on an RX 9070 XT.

- Frame generation, RGBA8: midpoint error 48036, unwarped blend 80592.
- The same fixture, sRGB: 54503 vs 82020.
- The stationary HUD band stays exact.
- Pixel smoothing staircase: 0.45 rows alone, 0.25 rows before FSR.
- `render_scale`, `overlay` and `runtime_present` passed.
- Graphics protection still forces internal scale to 1.0.

## Limits

This cannot see the game's motion vectors, so fast cuts, translucency and particles can still fall back to the current frame or show a short error. It does not add detail the game did not render. It does not guarantee a higher frame rate. Only the spatial FSR 1 shaders are AMD's. The frame generation shaders are PortForge's.

Restart the game after changing Settings. Existing games need **Doctor → Engine** (or a reinstall of the engine) so they pick up Engine 1.27.
