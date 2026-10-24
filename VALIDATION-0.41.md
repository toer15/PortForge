# PortForge 0.41 / Engine 1.24

## Root causes (why 0.39/0.40 still looked broken)

1. **PortForge FSR 3 did not double on-screen FPS when VSync was on.** Swapchain present mode stayed **FIFO** whenever `vsync=1`. Mid + real presents then raced a non-blocking `vkAcquireNextImageKHR`; every other picture was dropped. Esc still labeled `120 (~240 on screen)` from the numeric limit alone.
2. **That Esc label ignored speed lock.** A 60 Hz game with `fps_limit=120` still runs at 60 game-side; with FG the honest target is **~120** on screen, not 240.
3. **Live display FPS was easy to miss.** The frame-time graph (default Off) only showed FPS while the graph was On and Esc was closed. Opening Esc also pauses FG, so any live counter would collapse unless frozen.
4. **Esc left/right did not change FPS limit** on the main page (`overlay_side` skipped `PAGE_MAIN`); only select/tap stepped forward. FSR 3 toggles were not session-protected against `ini_poll` the way FPS already was.

Settings → `global-settings.ini` → launch apply for `fps_limit` / `portforge_fsr3` was already working; the failure was display path + UI honesty.

## Fixes (Engine 1.24)

- **FG present mode:** with frame generation on, prefer **mailbox** then **immediate** even if VSync is checked, so mid+real can both reach the display. Remake the swapchain when FG toggles. Log if only FIFO is available (then refresh caps the rate).
- **Honest Esc FPS label:** expected on-screen rate is `~2 × present_game_side_fps` (speed lock aware).
- **Display FPS: N** always on the Esc main page (frozen while the menu is open). Same `vk_fps_presents` counter on the frame-time graph title when the graph is On.
- **Esc left/right** adjusts FPS limit; FSR 3 On/Off uses `options_fsr3_live` (session above global re-apply).

## App 0.41

Docs and Settings copy updated for Engine 1.24. Existing games need **Doctor → Engine** (or reinstall Engine) after updating the app.

## How to verify in Esc

1. Settings: PortForge FSR 3 On, FPS 60 or 120, save; **restart the game** (or Doctor Engine first if still on 1.23).
2. Play a few seconds, open Esc:
   - **Display FPS: N** near the top (should be about **2× game pace** with FSR 3 On — e.g. ~120 for a 60 Hz title, not 240 unless the game itself runs at 120).
   - FPS limit row expected `~N on screen` should match that order of magnitude.
3. Left/right on FPS limit; after closing Esc, Display FPS should follow (30 → lower, higher cap up to the game’s pace ×2 with FSR 3).
4. Optional: Frame-time graph On — title shows the same live display FPS.
