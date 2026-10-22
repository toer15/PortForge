# PortForge 0.26 / Engine 1.9 validation

## Reproduced and fixed for the tested scene

The same isolated Mario save and scripted inputs were run with internal scale 100% and 75%, FSR off. At 100% the world, terrain, tower and character rendered; at 75% the HUD remained while most of the world became incorrect flat geometry. A sampling-copy experiment did not repair it and was reverted.

Engine 1.9's default graphics protection keeps the renderer's actual internal scale at 1.0, including with a legacy 75% request. The same saved scene then rendered normally again at 1600x900. The engine logged the requested 75% and protected 100% explicitly. The protection applies before creating render targets, so their sizes, fragment coordinates and guest texture/data access stay in the original relationship. Presentation upscaling is independent.

This is a conservative engine compatibility path. It does not complete arbitrary internal resolution scaling. The reduced-resolution performance tradeoff is removed in protected mode. The normal app enforces protection; advanced render_scale_safety=0 retains the experimental old path.

## Checks

All 348 application checks passed, including new checks for saving Performance safely, PC matching on an entry-level PC, tuning only actual native profiles, retaining the FPS cap, and readable protected controls. Existing save, cloud, conversion, installer-management, doctor and engine rollback checks passed.

Six selected engine suites passed: render_scale, vulkan_headless, frame_times, runtime_present, frame_policy, pipeline_file. The scalar test covers requested scales 25%, 50%, 75%, 100%, 150%, 200% and 400%, explicit opt-out and invalid inputs. The real Vulkan integration runs with a 75% request and protection on, asserts the actual native scale, then checks native-sized render operations and FSR shader output. The full upstream engine suite was not run.

## Scope and limits

The tested world corruption is prevented by keeping internal rendering native-sized. Other rendering bugs, games and physical GPUs remain unverified. Native-scale rendering may use more GPU work than the old reduced-resolution path. No FPS gain, zero-stutter or universal rendering-correctness claim is made. FSR 1 still scales the finished image to a larger window; FSR 4, DLSS and frame generation are not implemented. The runtime remains AOT/JIT.

All 10 installer checks passed, including preserving converted games through uninstall. The installed application version and bundled engine were verified as 0.26 / 1.9; the backed-up upgrade and save-integrity check for the user's Mario are recorded locally with the verification logs.
