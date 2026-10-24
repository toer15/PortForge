# PortForge 0.42 / Engine 1.25

## Root cause

The FPS number on screen was not the limit the user set, and it was not 2× that limit with PortForge FSR 3.

1. **The HUD counted guest frames whenever frame generation was off.** `vk_fps_presents()` returned `g_vk_present`, which advances for every game frame, including frames `present_show_due` keeps off the swapchain. A cap of 30 still read as 60. With frame generation on it counted `g_vk_display`, but only pictures that actually acquired a swapchain image.
2. **The extra frame generation picture was dropped.** Engine 1.24 preferred mailbox present mode. Mailbox still waits for the monitor's vblank, and the acquire used a zero timeout, so the midpoint or the real frame got `VK_TIMEOUT` and was not presented. Successful presents stayed at the game's ~60. The steady-pace window also turned frame generation off above ~125 game FPS, so a 144 or 240 cap could not insert a midpoint.
3. **A numeric cap above the game's pace did nothing to the display when frame generation was on.** Speed lock kept a 60 Hz title at 60 game frames for a limit of 120, and the dropped second present left the counter at 60 instead of ~240.
4. **Esc paused frame generation**, so a live sample taken while the menu was open collapsed to the game's own pace. The frozen sample was often that 60.

## Fix

- Frame generation presents with **immediate** mode (mailbox only if the device has no immediate mode), asks for enough swapchain images for a midpoint and a real frame in flight, and retries a short acquire instead of dropping the generated picture.
- With frame generation on, a numeric `fps_limit` is the game-side rate. The display target is about **2× that cap** (60 → ~120, 120 → ~240). A lower cap still does not slow game logic; it drops pictures.
- Without frame generation, speed lock still refuses to run a swap-interval game faster than the pace it asked for. Display FPS shows that real rate. A lower cap drops display pictures, and the counter follows them (30 does not stay 60).
- HUD, Esc **Display FPS**, and the frame-time graph title all use the display-present count. Frame generation keeps running while Esc is open, so the readout keeps updating instead of falling back to 60.

## What you should see

- Limit **60** and PortForge FSR 3 on: about **120** on screen, and Display FPS / the HUD / the graph around 120.
- Limit **120** and PortForge FSR 3 on: about **240** on screen, and the same counters around 240.
- Limit **30** and PortForge FSR 3 off: about **30**, not 60.
- Limit **120** and PortForge FSR 3 off, on a title that asks for 60: the game stays at its own pace and Display FPS shows that real rate (about 60), not a fake 120.

Restart the game after changing Settings. Existing games need **Doctor → Engine** (or a reinstall of the engine) so they pick up Engine 1.25.
