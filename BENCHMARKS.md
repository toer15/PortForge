# PortForge 0.5 verification

Test PC: Windows 11, Ryzen 7 9800X3D, Radeon RX 9070 XT, 32 GB RAM. Backend: ScarletNX Windows nightly-2026-10-21-0de0a87.

The locally converted Super Mario Odyssey 1.3.0 executable was tested with an eight-second cache warm-up and four separate 20-second startup samples. Rendered frames include loading and the mode-selection menu. No game input was scripted.

| Tested graphics | Render scale | Average frames/second, including startup |
| --- | --- | --- |
| Existing settings | 100% | about 55 |
| Performance | 75% | about 55 |
| Balanced | 100% | about 55 |
| Quality | 150% | about 55 |

These samples used a temporary 60 FPS cap with VSync disabled. Lower render scale brought no measurable improvement in this scene. This does not establish a CPU bottleneck, whole-game performance, or gameplay quality at any of these settings. Automatic selection respects the player's maximum chosen render scale, even if the higher-scale startup sample is fast.

A separate 12-second startup sample with a temporary 120 FPS cap, VSync disabled and 100% render scale rendered **1,239 frames**, averaging **103.25 FPS including loading**, and exited successfully. This establishes that the limiter can permit rendering above 60 FPS for this input. The samples have different durations and are not a controlled whole-game speed comparison. Correct game speed and physics at higher FPS remain unverified.

The original INI was restored byte-for-byte after testing. The tests keep shader and JIT caches. The user previously reported working gameplay, with lag. Demanding gameplay, new shaders, other game versions and arbitrary ROMs need separate testing. No AI model or executable-rewriting optimization pass is present.

The release test suite covers actual AOT conversion and relocation, conversion with unsupported tuning, publication and cleanup errors, long paths, the interface controls, configuration preservation and backups, rejected settings, measured-profile selection and cancellation of a real test process. Build and run it using `build.ps1 -Test` with the installed backend.
