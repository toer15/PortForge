# PortForge 0.34 / Engine 1.18

## Implemented

- The main application has a Settings tab. Save for all games writes `%LOCALAPPDATA%/PortForge/global-settings.ini` atomically, retaining a backup. Updated runtimes read it at launch even when the application is closed. No individual executable selection is needed. Graphics and FPS opens this tab; Game tools retains individual diagnostics and tuning.
- The global layer overrides per-game graphics while preserving the original per-game file values. Disabling it restores per-game choices on next launch. Explicit command-line overrides win. Automated timed measurements bypass the layer unless `--global_settings=1` is explicitly passed. Game source paths, saves, mods and controller mappings cannot be overridden by the global file.
- Fullscreen play hides the mouse pointer while the game has keyboard focus. Escape menus, control editing, loss of focus, the system key and fatal-error panels restore it. This is SDL cursor visibility, without pointer confinement or changing the Windows desktop cursor setting.
- A bottom-right `Saving...` overlay follows actual nonempty writes to account, device and system save files, including automatic saves. It stays visible during writes and briefly afterwards. A failed write displays `Save write failed`. This reports filesystem activity, not proof that the game has stored its current location or committed a complete checkpoint. SD-card files and temporary/cache save storage do not trigger it.
- Compute programs are recorded alongside graphics shaders and prepared by a background worker on subsequent launches. A required queued program is claimed by the game thread immediately; a program already building is waited for. The cache reader now accepts compute-stage records (the unfinished implementation wrote these but rejected them when reading). Compute creation/wait time appears in slow-frame diagnostics. Cache compatibility includes compute implementation changes, GPU and driver properties. The existing native graphics safety policy and game-speed lock remain enabled.
- A real-game frame-time graph (F7 or Escape) and optional CSV recording include shader translation, pipeline waits, compute builds, render-target creation, texture uploads, file reads, GPU waits/finishes, JIT waits and presentation. The app's trace viewer plots frames and lists hitches and measured components. Generated frames do not inflate these game-frame measurements. Overlapping counters and attribution thresholds mean the largest component is evidence, not a complete proof of causation.
- Optional **PortForge 2x - experimental** frame generation runs motion search and midpoint interpolation on the GPU, then schedules the current real frame. The app persists this choice in global settings, not Radeon driver settings; it also has a session-only Escape toggle. It is off by default. This is PortForge code, not AMD FSR 3. Menus bypass interpolation; unsupported formats, invalid crops and allocation/descriptor failures fall back. Resolution/format changes clear history and reacquire the command buffer after resource replacement. The presentation timer rechecks its deadline after taking the Vulkan lock, avoiding stale-wakeup races. The final motion field uses quarter/eighth/sixteenth-resolution luma, filtered vectors, exact preservation of unchanged pixels, and confidence-based fallback to limit smearing. Artifacts remain possible.

## Verification

- Runtime precedence checked in an isolated Mario copy: global FPS/mode/cursor/indicator applied, command-line FPS won, disabled global settings and timed measurement bypass worked, original per-game values stayed intact. A source path deliberately inserted into the global file was ignored. Output: `verification/global-1.17-results.txt`.
- Engine regression tests cover mixed graphics/compute cache records, filesystem save classification, save indicator duration/failure, the complete cursor-policy truth table, and existing Escape menu behavior. Output: `verification/engine-1.17-tests.txt`.
- Compute reuse: the isolated Zelda opening recorded four compute programs; on the next launch all four were prepared before demand. Warm scene p99 was about 36.9 ms in that earlier test. See `verification/compute-1.17-results.txt`. First use of an unseen compute shader is still synchronous.
- App checks: 381 checks across native conversion, interface, cloud saves, optimization/settings/trace parsing, downloads, diagnostics, updates, Doctor and engine integration. The initial full run had 379; the final optimization suite adds two trace tests (86 total in that suite). Final interface and installer checks are recorded with the packaged build.
- Five focused engine regression suites pass: filesystem service, overlay, save activity, frame trace, and mixed graphics/compute pipeline records. The complete headless Vulkan integration test also passes on the RX 9070 XT. Its new interpolation checks use actual GPU shaders and known synthetic motion: stationary colour/HUD preservation, a moving midpoint closer to ground truth than simple blending, odd-sized resize, sRGB/BGRA, invalid crops and history reset. Khronos validation layers are not installed; a validation-request run therefore is **not** a validation-layer pass.
- Global settings precedence rechecked in a launched isolated Mario copy, now including frame generation and trace flags. The global profile applies; explicit `--frame_generation=0` wins; disabled and automated timed-test bypasses work; original per-game values and source stay intact. See `verification/global-1.18-results.txt`.

## Final Zelda measurements on this PC

Ryzen 7 9800X3D, RX 9070 XT, 1440p 280 Hz display; visible fullscreen, VSync on, warm cache, the same copied user save and scripted outdoor walking/camera movement. Two 105-second tests; compare game frames from 60 seconds onward. The test copy's saves and settings are restored. Original game saves are read only for copying into the isolated test. No system-wide driver cache was removed in this continuation.

| Engine 1.18 | Frame generation off | Frame generation on |
|---|---:|---:|
| Game frames in the selected window | 1,299 | 1,298 |
| Median real frame | 33.57 ms | 33.47 ms |
| p99 real frame | 43.52 ms | 41.79 ms |
| Worst real frame | 52.45 ms | 49.85 ms |
| Real frames over 100 ms | 0 | 0 |
| Visible patch changes in a moving scene | 300 / 10.003 s (30.0/s) | 600 / 10.002 s (60.0/s) |

The external GDI sampler hashes a 64x64 patch of the actual desktop without storing images; it sampled about 2,700 times per ten-second measurement. Static scenes, overlays and unrelated foreground windows would invalidate this approach. The engine separately recorded 3,000 generated plus 2,999 real display submissions over about 100 seconds (60.0/s). Submission counts alone are not scanout measurements. The game remains at its 30 FPS simulation/input rate; these p99 differences are within ordinary variation and are not evidence of faster simulation. This warm scene did not reproduce the earlier multi-second first-use stalls. It does not prove they are eliminated elsewhere. Gameplay images were inspected in private before/midpoint/after triplets; they are excluded from releases.

Evidence: `verification/trace-1.18/{fg-final,fg-off-final}.csv`, `final-summary.json`, `display-final.txt`, `display-off-final.txt`, `engine-1.18-tests.txt`, `vulkan-1.18-tests.txt`, and `optimization-0.34-final-tests.txt` (local verification outputs).

## Limits and unfinished requests

The requested feature set is **not fully implemented**. Universal save states, a Save Game action in Escape, true native 720p/1080p/1440p/2160p overrides, and AMD FSR 2/3 remain unsupported. The UI states these limitations. The experimental PortForge interpolator does not satisfy the request for AMD FSR 3 and must not be represented as it.

Saving anywhere requires consistent serialization and restoration of guest CPU/memory, HLE threads/handles, GPU state and host resources. Flushing or copying existing save files cannot capture progress that the game has not written.

The renderer handles some guest render targets as game data, with sampling, copies, storage operations and CPU readback. Scaling them without propagating the correct coordinate and data semantics can corrupt rendering or gameplay; `render_scale_safety` therefore remains on. TV/handheld modes let the game choose its resolution. FSR 1 enlarges that image and is not native-resolution rendering.

[AMD's FSR 3 integration documentation](https://gpuopen.com/fidelityfx-super-resolution-3/) requires render-resolution depth and motion vectors, UI composition and frame-generation swapchain integration. This runtime does not reliably identify those semantic resources for arbitrary guest games. FSR 2 is upscaling; FSR 3 frame generation adds display frames, not simulation or input updates. No universal 2x FPS or latency improvement is claimed.

Compute caching helps programs encountered before. First use of unseen programs, a new driver/cache mismatch, file loading, allocation or other engine bottlenecks can still stall. The native pipeline cache is device/driver-specific. Existing shader caches may rebuild once after this engine update because compute support changes cache compatibility. No system-wide AMD cache was removed for these tests. No claim of eliminating all freezes or improving every game's FPS is made.

The runtime still uses its existing AOT plus JIT fallback; this release does not turn it into fully static native conversion. Only updated game runtimes consume global settings or show the new HUD. New conversions use the bundled engine; existing games need the runtime update while closed.
