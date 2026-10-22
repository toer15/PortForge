# PortForge 0.25 / Engine 1.8 validation

## What passed

- All 343 app checks passed with Engine 1.8 as the backend. After adding license propagation, the affected 31 native-conversion checks and 22 engine-management checks passed; the added engine check brings the app total to 344.
- Five targeted engine CTest suites passed: `vulkan_headless`, `frame_times`, `runtime_present`, `frame_policy`, `pipeline_file`. The headless integration test actually dispatches both FSR shaders on the RX 9070 XT; it checks odd extents, cropped-edge clamps, resize, sRGB/linear color round trips, unchanged-size/downscale/invalid-crop fallbacks, and absence of a host finish during upscaling.
- The added C implementation compiled with extra warnings and a C99 syntax check. Only pre-existing warnings in included VMA/shader headers were seen. The engine code-quality check reported no candidates for the new implementation.
- The installed app's Play button launched the user's original Mario and its game window was observed. Separate copies then ran the new engine, keeping separate settings and saves. Original save hashes were identical before and after the isolated runs.
- The windowed FSR run logged 3000 frames actually upscaled, most recently from 1200x675 to 1280x720. A 60-second linear sample recorded p99 blocks of 17.203-17.546 ms; the FSR sample recorded 17.260-17.392 ms. These runs were capped at game speed, include the opening/menu and loading, and overlapped app regression work. They are functional checks, not a controlled speedup benchmark; they show no established FPS improvement. No visual equivalence or universal rendering-correctness claim is made.

## Behavior

Play returns to an existing game window without saving settings into its running session. Same-process engine metadata probes are excluded from gameplay detection. Settings are saved only once before launch; cloud sync is deferred until the game closes. The status waits for a window, reports early exit with available log details, and distinguishes a still-starting engine.

FSR 1 is an FP32 spatial EASU + RCAS presentation pass using AMD's official MIT-licensed headers, pinned at a21ffb8f6c13233ba336352bdff293894c706575. It is selected by default in Engine 1.8 and applies when the output dimensions exceed the rendered SDR image. Unsupported formats, insufficient scratch memory, unchanged size or downscaling retain linear scaling. The app preserves the off switch and omits the new setting for older engines. Engine and converted-game packages carry the AMD license.

## Limits

FSR 4, NVIDIA DLSS, ray tracing and frame generation are not implemented. The renderer lacks the motion vectors, jitter and temporal integration those features require. Neither zero stutter, fixed 1% lows nor doubled FPS is promised. Only one physical AMD PC was tested; NVIDIA/Intel hardware and other games remain unverified. Headless Auto tune excludes display upscaling and presentation cost, now stated in its report. The runtime still uses AOT/JIT translation.

The full upstream engine test list was not built or run; only the five named suites are reported. All 10 installer checks passed, including executable/engine installation, shortcuts, icon, registry registration and keeping converted games during uninstall. The new upscaler control and Clear button were also checked in a rendered settings-window image.
