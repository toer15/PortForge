# PortForge 0.39 / Engine 1.23

## Fixes (FPS limit / frame-time graph)

- **FPS limit did not stick or apply.** Three causes:
  1. **Global overwrite:** Esc/Home and F4 wrote `g_opt_fps` only. A later `ini_poll` / `apply_all` re-applied launch-time global `fps_limit` and wiped the live choice. Fixed with a session layer (`options_fps_live`) above global in `value_of`.
  2. **FG ignored the limiter:** the mid+real present path never called `present_show_due`, so every game frame still reached the display under PortForge FSR 3. FG now drops frames the same way the non-FG path does.
  3. **`present_limit_fps` doubling:** doubling the numeric limit under FG stopped lower caps from dropping frames (30 became 60 and matched the game rate) and raced self-timed games. `fps_limit` is the game-side target; FG alone provides ~2x on screen (60 → ~120).
- **Frame-time graph:** title row shows live **display FPS** from the same `vk_fps_presents` / showcase counter used for the on-screen HUD (includes generated frames when FG is on), updating in realtime with performance.

## Limits

- Speed lock (on by default from global settings) still keeps simulation at the game's own pace; Unlimited / a high cap cannot speed a frame-counting game.
- Display FPS on the graph is from swapchain submission counts over ~0.5 s, not a measured scanout rate.
