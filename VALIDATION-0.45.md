# PortForge 0.45 / Engine 1.28

## What changed

Speed lock is unchanged. A game that asked for a swap interval still waits that interval, with or without PortForge FSR 3. Frame generation does not call `present_pace`. Characters are not stepped on the display clock.

Engine 1.26 left the on-screen rate stuck near 60. The guest frame's bookkeeping present advanced the two-slot frame ring before the generated picture, and that picture acquired a swapchain image with a zero timeout. The acquire failed, so the midpoint was not queued. The HUD, Escape **Display FPS** and the frame-time graph only saw guest presents.

Engine 1.28:

- The bookkeeping present no longer consumes that frame slot. The midpoint submits the same command buffer.
- A generated picture waits up to 8 ms, then once more, for a swapchain image. That wait is not the game's pace.
- `present_show_due` still runs once per guest frame. It is not called on the generated picture, so a cap at or above the game cannot drop the midpoint. A lower cap still drops both pictures and does not slow the game.
- Every successful `vkQueuePresentKHR` increments one counter. The HUD, Escape Display FPS and the frame-time graph read that counter. Changing the limit restarts the half-second window.
- Settings includes 30 again, next to 60, 120, 144, 240 and Unlimited. Esc/Home session FPS and the global ini both reach `g_opt_fps`.
- PortForge FSR 3 quality from 1.27 is unchanged. Selective color render-target scaling stays off.

## What Display FPS should read (60 Hz game)

| Limit | FSR 3 off | FSR 3 on |
| --- | --- | --- |
| 30 | ~30 | ~60 |
| 60 | ~60 | ~120 |
| 120, 144, 240, Unlimited | ~60 | ~120 |

120 without frame generation cannot invent pictures, so the counter shows the game's own rate. The Escape FPS row says that (`120 (~60 on screen)`). With frame generation the same row says `120 (~120 on screen)`.

## Measured

`runtime_present` checks the guest wait (still one swap interval under the speed lock) and the display targets in the table above.

Restart the game after changing Settings. Existing games need **Doctor → Engine** (or a reinstall of the engine) so they pick up Engine 1.28.
