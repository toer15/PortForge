# PortForge 0.47 / Engine 1.30

## Crash (why games died on FG / present)

Engine 1.28–1.29 FG bookkeeping used `g_show_keep_slot`: the guest frame's show did not submit or advance the frame ring, so the midpoint could reuse that command buffer. When the midpoint was null, failed acquire, or was skipped after capture/interpolate had already recorded into the buffer, those commands stayed unsubmitted and the next present corrupted the ring — hard crashes under PortForge FSR 3.

## What 0.47 / 1.30 does

- FG bookkeeping always retires the frame (submit + boundary + `frame_next`). Mailbox/immediate + waited acquire still lets the midpoint present afterward.
- Null / failed midpoint falls back to showing the filtered current (or guest) without a second guest boundary.
- Present errors other than success/suboptimal/out-of-date mark recreate (or lost) instead of only logging.
- **Play** calls `EngineManager.EnsureForPlay`: if the game's `scarletnx.exe` does not match `PortForge\Engine\`, it is updated before launch (fast copy when `ENGINE.txt` already exists).
- Doctor AutoEngine path unchanged for closed games.
- Speed lock and guest pace unchanged. Selective color RT scale stays off.

## Verification

1. Install **0.47**. Confirm `PortForge\Engine\ENGINE.txt` says `version=1.30`.
2. Press **Play** on a game that still had an older Engine — history should note the update; Esc must show **Engine 1.30**.
3. With FSR 3 On for several minutes: no crash; Display FPS ~2× guest (Limit 60 → ~120 on a 60 FPS title).
4. With FSR 3 Off: Limit 30 → Display FPS ~30; Limit 120 → ~60 and Limit reads `120 (~60)` under speed lock.
5. Characters must not fall through the floor at any Limit / FSR 3 combination.

## Engine vs app

| Path | Role |
| --- | --- |
| `PortForge\Engine\` | What the app ships and in-app update replaces |
| Game folder `scarletnx.exe` | What actually runs; **Play** / Doctor / Engine tab copy from the app Engine folder |
| Esc `Engine X.Y` | Version baked into that running binary |
