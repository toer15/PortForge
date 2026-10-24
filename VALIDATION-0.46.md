# PortForge 0.46 / Engine 1.29

## Root cause (why Esc still showed 60)

Three separate failures stacked:

1. **Games keep their own `scarletnx.exe`.** App updates replace `PortForge\Engine\`, but each converted game runs the copy beside its `.exe`. Doctor only reinstalled that copy after a session ended (or on first watch). Updating the app while a game was open, or never closing while Doctor watched, left Engine 1.22–1.28 in the game folder. Esc had no engine version, so every report looked like the same FPS bug.

2. **Esc “Display FPS” was a live sample that stayed at 60.** Changing the FPS limit resets the half-second counter to “not measured yet”; the overlay ignored that and kept printing the previous 60. The Limit row could change while the only number people looked at did not. With FSR 3 off, a Limit of 120/144/240 correctly still *displays* ~60 (speed lock) — without a Limit readout that looked “stuck.”

3. **Generated pictures could still miss the swapchain** on some GPUs when the 8 ms acquire timed out twice, so with FSR 3 on the counter stayed near the guest 60.

Settings → `%LOCALAPPDATA%\PortForge\global-settings.ini` → `global_load` → `g_opt_fps`, and Esc `options_fps_live` / session layer above `ini_poll`, were already correct on Engine 1.23+.

## What 0.46 / 1.29 does

- Esc status line: `Engine X.Y | Display FPS: N | Limit: L` (Limit updates on left/right immediately; Display FPS is held while Esc is open).
- Longer generated-picture acquire (16 ms × 3) and a log when those acquires fail.
- `present_limit_changed` clears the display-drop schedule when the limit changes.
- Doctor reinstalls the app Engine into closed games when the Engine folder version/size changes, not only after a play session.
- Install copies `ENGINE.txt` beside the game. App in-app update still replaces the whole `Engine\` folder.

Speed lock and guest pace are unchanged. Selective color RT scale stays off.

## Verification

1. Install **0.46**. Confirm `PortForge\Engine\ENGINE.txt` says `version=1.29`.
2. Close every game. Open PortForge so Doctor runs, or open **Doctor → Engine → Install** on the game.
3. Launch the game. Esc must show **Engine 1.29**. If it shows an older Engine, the game exe was not updated — Install again with the game closed.
4. With FSR 3 Off: Limit 30 → after closing Esc, Display FPS ~30. Limit 120 → Display FPS ~60 and Limit reads `120 (~60)`.
5. With FSR 3 On: Limit 60 → Display FPS ~120. Limit 30 → ~60.

## Engine vs app

| Path | Role |
| --- | --- |
| `PortForge\Engine\` | What the app ships and in-app update replaces |
| Game folder `scarletnx.exe` | What actually runs; Doctor / Engine tab copies from the app Engine folder |
| Esc `Engine X.Y` | Version baked into that running binary |
