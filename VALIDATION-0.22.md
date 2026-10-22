# PortForge 0.22 / Engine 1.6 validation

## Lower FPS caps keep the game's speed

Engine 1.5 (PortForge 0.21) stopped caps above a game's pace from speeding it up, but a cap below the pace still slowed a frame-counting game down. One user's Super Mario Odyssey had `fps_limit=30` on a game that asks for 60 FPS (swap interval 1), so it ran in slow motion.

With `speed_lock=1`, `present_pace` now paces a game that asks for a swap interval at that interval for every cap. A lower cap is applied on the display side: `present_show_due` keeps some of the game's frames off the swapchain on the GPU thread. Those frames are still drawn and submitted with the per-present bookkeeping, through the renderer path already used for minimized windows. A frame is shown when it arrives within half a game period of its due time, which keeps the shown frames evenly spaced. A game asking for interval 0 still uses the cap for pacing, as before.

The runtime-pacing suite checks that 30, 45 and 20 FPS caps on a 60 FPS game show 300, 450 and 200 of 600 frames, that a 20 FPS cap on a 30 FPS game shows 400 of 600, and that 120 FPS, interval 0 and `speed_lock=0` show every frame.

Isolated Super Mario Odyssey copy (same copied save, scripted input):

| Run | Setting | Game frames |
| --- | --- | --- |
| Headless, 35 s | Game pace, lock on | 1,920 |
| Headless, 35 s | 30 FPS, lock on | 1,930 |
| Headless, 35 s | 30 FPS, lock off | 992 |
| Headless, 35 s | 120 FPS, lock on | 1,930 |
| Window, 25 s | 30 FPS, lock off | 691 |
| Window, 25 s | 30 FPS, lock on | 1,375 |

In the windowed run with the lock, the engine logged 601 frames kept off the display by 22.5 s, so about half the game's frames were shown, matching the 30 FPS cap while the game ran at full speed. Headless runs have no display, so they check game speed only.

Limits: one game was measured. A lower cap does not reduce rendering work for a frame-counting game, because the game still makes every frame. To lower GPU load, lower the render scale instead.

## App

The settings hint, Doctor text and PC report describe the full lock. App behavior from 0.21 is unchanged: PC matching sets `speed_lock=1`, and the Doctor repairs a disabled lock. All 324 application checks passed again against the bundled Engine 1.6.


## Speed lock for higher caps (Engine 1.5, from 0.21)

A game that asks vi for a swap interval counts its frames. Presenting sooner than that interval (an FPS limit above the game's pace, no limit, or F4) therefore ran the game itself faster. `speed_lock` (default 1) makes `present_pace` hold the pace at the game's interval whenever the limit would be faster. A lower limit still lowers the pace. A game that asks for interval 0 is paced only by a set limit, as before. `speed_lock=0` restores the previous behavior.

The runtime-pacing suite now checks the locked period for 120, 1000 and unlimited on 60 and 30 FPS games, a lower 30 FPS cap, interval 0 with and without a cap, and the unlocked behavior. It also checks the real waits for locked 120 FPS and locked unlimited.

An isolated copy of Super Mario Odyssey (the same copied save, scripted input, 35-second headless runs, VSync off, warm driver cache) produced:

| Setting | Frames in 35 s |
| --- | --- |
| Game pace, lock on | 1,931 |
| 120 FPS, lock on | 1,929 |
| Unlimited, lock on | 1,932 |
| 120 FPS, lock off | 2,548 |

With the lock, the 120 FPS and unlimited runs match the game's own pace. Without it, the same cap ran the game about 32% faster. A first cold-cache 120 FPS run (1,574 frames, with multi-second shader waits) was excluded as not comparable.

The app now sets `speed_lock=1` during PC matching on engines that expose it. The Doctor reports a disabled lock as a repairable issue and its automatic repair turns it back on while keeping the player's FPS cap. A high cap under the lock is no longer reported as a game-speed risk. Older engines keep the previous timing warning. The real Engine 1.5 binary was checked to expose `speed_lock` to the app's capability reader.

Limits: only one game was measured. Games that time themselves with interval 0 but still count frames are not covered by the lock. Games that need a higher frame rate need a game-specific 60 FPS patch, which the lock leaves to the game's own pace.

## Changes

The engine now budgets optional pipeline waiting per presented frame, rather than allowing 1.5 ms for every pending pipeline bind. Time spent in required pipeline waits exhausts the optional allowance too, without cancelling required work. An ordered recording-ring marker resets the thread-local budget at the frame boundary. This avoids using the GPU producer's frame counter while the recorder may be behind. The 1.5 ms limit bounds requested optional waits; OS scheduling may overshoot a wait, after which later optional waits stop for that frame.

Background prewarming occupies at most N-1 workers when there are at least two; demand compilation can use all N. Queue notifications broadcast so a wake intended for an eligible worker cannot leave work stranded. Worker counts are selected from the host's allowed CPUs on each launch, with one worker on machines with up to four CPUs, otherwise two to six; explicit settings remain honored, up to 32. New-engine PC matching and supported Doctor/Auto fix repairs select automatic worker settings rather than a count tied to the build PC. JIT behavior is otherwise unchanged.

Frame pacing resets its schedule when FPS mode or native interval changes, including transitions through unlimited mode. The selected speed, FPS cap and VSync remain unchanged. Frame guard defaults on inside the runtime, independently of the app staying open. NativeConversion already uses the bundled engine for local files and Downloads handoff. Watched-game engine upgrades retain their existing backup, startup check and manual opt-out behavior. Experimental async shader translation remains off by default.

## Engine checks

The Windows CLANG64 runtime built successfully. Frame-policy, runtime-pacing, pipeline-cache-file and real offscreen Vulkan integration suites passed, including a final rebuild from engine commit 98e7770. Changed runtime files were checked with -Wall and -Wextra: no source warnings were reported (the syntax-only wrapper reported its unused -c flag). The exported patch was checked in reverse against the committed engine tree to verify it reproduces the source relative to base 0de0a87.

The frame-policy regression requests 1,000 pending pipelines and verifies that their optional requested wait totals 1.5 ms, then verifies reset, oversleep saturation, unknown/small/large CPU counts, explicit counts and background reservation. Runtime-pacing tests exercise changing caps, changing native intervals and returning from unlimited mode. The Vulkan test uses the actual renderer and recording path on this PC's GPU.

## Game comparison

Four sequential 45-second headless runs used an independent copy of Super Mario Odyssey, the same copied save, scripted input, automatic workers and experimental translation off. Its app shader cache was emptied before each run. The original game, save and cache were not used for execution or modified. Windows/GPU driver caches were not cleared.

| Run | Frame guard | Frames over 50 ms | Worst frame | Pipeline waiting |
| --- | --- | --- | --- | --- |
| First | Off | 18 | 3,715 ms | 6,743 ms |
| Second | On | 4 | 319 ms | 0 ms |
| Third | On | 4 | 360 ms | 1 ms |
| Fourth | Off | 4 | 358 ms | 1 ms |

All four reached gameplay without a crash and created 377 pipelines. Screenshots from guarded and unguarded runs were inspected: the level and HUD were present, with animation differences; this was not a pixel-identical comparison. The warmer unguarded repeat matched guarded runs, so this experiment does not establish a game-wide speedup caused by frame guard. The cold first run's large stalls cannot be credited as an improvement without controlling the driver's cache. Remaining guarded stalls of 144-360 ms occurred during loading. The controlled policy test establishes bounded cumulative optional waiting, not universal frame-time limits.

## App and installer validation

All 324 application checks passed against the newly bundled Engine 1.5: 31 conversion, 40 interface, 50 cloud-save, 46 optimization, 35 download, 31 diagnostics, 33 update, 37 doctor and 21 engine-management checks. One earlier full run hit a cloud-save timing check once; three direct reruns and the next full run passed. The added optimization check verifies that frame-guard engines select automatic worker counts without enabling experimental translation. The installer is validated in an isolated redirected installation before publication.

## Limits

Only this Windows PC and this game were measured for the new runtime behavior. CPU policies were tested with synthetic hardware counts, not measurements on other physical PCs. A native module may still depend on CPU instructions selected at compilation; automatic worker counts do not remove that requirement. The app remains Windows x64/.NET Framework 4.8 with a Vulkan 1.3 runtime. No Linux/macOS app port or fully static game conversion is added.

Required shader/pipeline work, game loading, shader translation, GPU memory pressure, driver scheduling and background processes can still cause slow frames. Smart async draw eligibility remains a heuristic and can cause brief visual omissions; it can be disabled per game. Frame guard requires the existing smart async and threaded recording paths to avoid optional waits. Games can still use JIT fallback. No zero-lag, zero-input-delay or all-games/all-hardware guarantee is established.
