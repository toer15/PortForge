# PortForge 0.17 validation

The settings window's periodic checks now only scan on a worker and publish changed findings. They do not call Busy, reserve its foreground cancellation slot, reload the graphics profile, or replace its main status card. Automatic repairs and optional AI review remain on the main LiveDoctor worker. Doctor events are collected for the UI every 500 ms; notifications are limited to one per 30 seconds while all reports remain in the history.

Manual work locks individual inputs without disabling container panels. Disabled button labels are explicitly painted with light text. Composited window painting reduces intermediate redraws. UI checks cover keyboard/mouse edit blocking, restored input states, actual disabled-button pixels, unsaved settings during a background scan, unchanged results, and closing while a scan is pending. The working-state window was also rendered and inspected visually.

Application checks: 30 native conversion, 39 interface, 30 optimization, 35 download, 29 diagnostics, 33 update, 37 doctor, and 21 engine-management checks (254 total). Native conversion checks use the bundled engine. Selected upstream engine tests: shader_brx, shader_decode, shader_program, pipeline_file and settingsgen.

The packaged installer passed 10 installation/uninstallation checks in a redirected temporary location, including shortcuts, the Windows apps entry, engine files, the icon and preservation of converted games on uninstall. The running user conversion was not interrupted or updated in place.

Engine 1.3 adds experimental background shader translation, off by default. Snapshot jobs are bounded, completed jobs are collected, and constant-buffer answers are revalidated. Draws contributing captured data or query counts wait; pipelines writing storage data also wait. This is a heuristic optimization with remaining correctness risks, not a universal no-stutter mode.

Two runs of an independent copy of Super Mario Odyssey reached the first level, with translation off/on and separate empty shader caches and copied saves. Both rendered frames and completed without a reported crash. End-of-run screenshots were inspected; they show the same scene with animation differences. This does not establish visual correctness throughout the game.

These runs recorded 22/19 frames over 50 ms and worst frames of 3601/4321 ms respectively. The CPU was also running a user conversion, and JIT/driver cache effects were not controlled. No speedup conclusion is justified. Earlier 0.10 benchmark numbers describe earlier engine code and are not a measurement of Engine 1.3. Other games and other PCs remain unverified.

No DXVK binaries or source were copied. The implementation uses the same general background-compilation idea, adapted to the existing Vulkan renderer. Sources and distinctions are documented in engine/README.md.
