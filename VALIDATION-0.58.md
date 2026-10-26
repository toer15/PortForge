# PortForge 0.58 / Engine 1.41 validation

Tested on Windows on 2026-10-26. True resolution is experimental; these checks do not certify every game's rendering or establish a universal performance gain.

## Changes

- Engine 1.41 scales screen-shaped render targets toward the selected 720p, 1080p, 1440p or 4K display height, relative to typical 1080p TV and 720p handheld output. Small, square, layered and unusual-shaped targets remain native. Dynamic-resolution games may differ.
- Low latency requests fewer swapchain images with VSync and, on Windows, attempts above-normal process priority and opts out of process power throttling. Drivers and the display compositor still control actual latency; this cannot guarantee zero delay or prevent GPU-bound stutter.
- App and release packaging install the Windows runtime at the Engine folder root and include the AMD FidelityFX SDK license and upstream notices.

## Build and automated checks

- Regenerated `engine/portforge-engine.patch` from the engine working tree against `engine/BASE` (`0de0a87`) and verified it applies to a clean worktree at that base.
- Built the engine with the existing MSYS2 CLANG64 toolchain. CMake installed `scarletnx.exe` at `engine/package/scarletnx.exe`; `--shader-abi` returned `016c3197680beb35`, and `engine/package-engine.sh` generated version 1.41 package metadata.
- Passed all five targeted engine CTest suites: `runtime_present`, `frame_policy`, `render_scale`, `pipeline_file` and `nx_jit_region`.
- Built the native app frontend. All 117 optimization checks passed, including global settings persistence and the 720p/4K scale-note assertions. Frame-generation cadence checks passed after changing their version assertion to compare against `engine/VERSION`.
- Converted the synthetic AArch64 NRO with the packaged engine and ran its generated x64 program headlessly; it printed the expected fixture marker and exited with code 0. This verifies the compiler/runtime path, not game rendering.

## Limits

No real game was run for this candidate. There are no new FPS measurements, visual-artifact comparisons, or per-game compatibility claims. The synthetic headless fixture does not exercise the scaled-render-target drawing path. True resolution can be disabled per game if artifacts appear; FSR and frame generation remain experimental.

The full `build-native.ps1 -Test` suite was not run for this candidate. Only the focused optimization and frame-generation cadence suites, plus the synthetic backend conversion/runtime check above, are reported as passed.
