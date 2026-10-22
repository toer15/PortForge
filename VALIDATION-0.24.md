# PortForge 0.24 / Engine 1.7 validation

## Verified

- The Engine 1.7 Windows runtime built in MSYS2 CLANG64 and packaged with its runtime libraries.
- Engine regressions `frame_times`, `runtime_present`, `frame_policy` and `pipeline_file` passed.
- All 337 PortForge application checks passed, including p99 parsing/profile selection, diagnostics and engine-install tests.
- The engine reports p99 frame time and 1% low per 600-frame sample. Slow-frame logs include GPU wait and allocation information. Compatible shader/pipeline caches are keyed by shader ABI, GPU and driver instead of the engine build number.

## Limits

The new telemetry and cache behavior have not been measured in a real game on multiple GPUs or PCs. PortForge's auto-tuner still measures only the scripted sample it runs, and later areas can behave differently. Cache reuse is limited to compatible shader ABI, GPU and driver combinations.

This release does not implement FSR 4 or DLSS. Render scale uses the Vulkan renderer's standard linear blit. No setting or hardware profile can promise zero lag, native performance, or consistent 1% lows across every game and PC.

The full upstream CTest list was not validated: the configured build did not build several unrelated test executables and dependencies. Only the four named engine regression tests are reported as passing here.
