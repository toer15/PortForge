# PortForge 0.18 validation

## Result

The main window detects and displays CPU model, CPU threads available to the app, Windows graphics adapters, dedicated graphics memory and usable RAM without locking the window. Hardware matching and measurement are enabled by default. Local supported files and the Downloads handoff use the same conversion path.

After compilation, the app writes and verifies supported graphics settings and pipeline/JIT worker counts. Automatic matching uses conservative scales (at most 100%, or 75% for entry hardware) and preserves FPS, VSync, display/controller choices and experimental shader preferences. It does not raise quality on the strength of a GPU model name alone.

The optional measurement warms the cache and compares the baseline with lower scales. Selection first requires competitive FPS, then filters significant differences in slow-frame count and worst freeze before choosing higher detail. Each run starts from the same protected save. Configuration and session logs are restored, including removal of a test log when no log existed originally. The final selected graphics settings are saved and verified. Failure to measure keeps the hardware baseline and is reported prominently.

Every matched conversion gets PortForge-PC-settings.txt with hardware, baseline settings, measurement status and a link by filename to the measurement report. This is local logic, not an AI-model service; it needs no account and uploads no hardware information.

## Checks

269 application checks passed: 31 real-backend conversion, 40 interface, 43 optimization, 35 download, 29 diagnostics, 33 update, 37 doctor and 21 engine-management checks. New checks cover the startup hardware panel/default options, real configuration writes, supported keys, low/high hardware profiles, FPS-preserving selection, lower-stutter decisions, per-sample save restoration, crash rejection, cancellation and reports. The real backend also verifies that a converted game is still published with a report when its graphical benchmark is unavailable. The working startup screen was rendered and inspected visually.

The end-to-end smoother-profile test launches a synthetic runtime that reports known FPS and stalls for different scales. It validates the selection and application mechanism, not commercial-game speed gains. No commercial-game speedup or performance on another physical PC was established in this release. The bundled Engine 1.3 code is unchanged from 0.17.

The 0.18 installer passed 10 checks in a redirected temporary installation: program/engine files, shortcuts, Windows registration, icon, removal and preservation of converted games. The user's running app/conversion was not restarted to install this release.

## Limits

Hardware classes and worker counts are heuristics. Windows adapter enumeration does not prove which Vulkan GPU a game actually uses; the runtime selects that device. Finding vulkan-1.dll does not verify GPU compatibility. Headless scripted input may remain in menus, and a few opening scenes do not represent all later gameplay, controller latency, or background workloads. No fixed FPS prediction or zero-stutter guarantee is made. Games still use ScarletNX AOT/JIT execution.
