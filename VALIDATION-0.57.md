# PortForge 0.57 / Engine 1.40 validation

Tested on Windows on 2026-10-26. This is a small performance/reliability fix batch, not a new interpolation algorithm or a whole-game compatibility certification.

## Changes

- In `engine/src/src/nx/gpu/vk/fg.c`, enlarged AMD interpolation no longer computes the legacy motion field before trying AMD. The field is computed only when legacy warps are needed, including the filter/blit-failure fallback. The native-size branch is unchanged. This removes redundant GPU commands on the successful enlarged AMD path; no universal FPS uplift is claimed.
- In `engine/src/src/nx/runtime/present.c`, shader preparation exits after 60 seconds without an increase in completed work. An unknown total, or merely discovering a total, no longer renews the deadline. Genuine progress renews it, even at the old deadline. Preparation can take longer than one minute while progressing. This polling watchdog cannot interrupt a prepare callback that itself never returns.
- In `src/GameOptimization.cs`, running-game detection uses Windows limited-query image paths instead of .NET module enumeration. This avoids a reproduced `ProcessModule.FileName` null-reference crash. Other-directory copies are not mistaken for the selected game, registered settings probes are excluded, and access/query uncertainty fails closed rather than allowing a save operation.
- Candidate version identifiers are 0.57.0.0 (app) and 1.40 (engine). `engine/portforge-engine.patch` is regenerated from the actual engine working tree against `engine/BASE` (0de0a87).

## Engine build and automated regressions

Native MSYS2 CLANG64 tools were used on Windows; no toolchain was installed and no PowerShell execution policy was changed.

Built `scarletnx`, `snx-test-runtime-present`, `snx-test-frame-policy`, and `snx-test-pipeline-file` with the existing `build/default` configuration.

Passed all four selected CTest suites:

- `runtime_present`, including eight deterministic fake-clock preparation cases: unknown-total and known-total stalls, progress with unknown total, discovered total without work, progress past one minute, normal completion, progress exactly at the deadline, and already-complete preparation. Each checks cleanup and reentry as well as timing.
- `frame_policy`
- `pipeline_file`
- `frame_times`

Ran `python tests/run_fg_motion_deferral_tests.py --cc <installed-clang> --check-sensitivity`:

- 23 compiled mocked-production-function cases passed.
- Five deliberately broken control-flow variants were rejected (eager motion search, missing enlarged fallback search, missing native fallback search, and two stale-command-buffer variants).
- The harness extracts the current production function. It tests control flow, not Vulkan internals or generated-image quality.

## Real-game smoke tests

An independent local copy of an existing Super Mario Odyssey conversion was used, including copied game assets, save data and configuration. The player's normal game folder was not launched or altered for these tests. Test game files, saves, screenshots and raw logs are not included in the release.

The runtime reported Ryzen 7 9800X3D, Radeon RX 9070 XT and 31 GB RAM. It saw eight CPU threads in the test environment. Tests were windowed, with global profile overrides disabled, speed lock enabled, TV mode, 1440p output, FSR 1 and frame generation on, Limit 60 and VSync on. Network and local wireless were disabled for the test process. The scripted inputs pressed A periodically; this is not a representative traversal of the whole game.

| Runtime | Timed duration | Real game frames reported | Exit | Verified path |
| --- | ---: | ---: | ---: | --- |
| Original Engine 1.39 | 45 s | 2,440 | 0 | AMD interpolation, FSR 1600x900 to 2560x1440 |
| Candidate Engine 1.40 | 45 s | 2,535 | 0 | AMD interpolation, same FSR dimensions |
| Candidate Engine 1.40, AMD explicitly disabled | 25 s | 1,344 | 0 | Legacy one-midpoint fallback, same FSR dimensions |

Both original and candidate reached rendered in-world gameplay. Screenshots and per-frame traces were collected. These are short sequential smoke tests, not randomized or cache-controlled benchmarks: startup/loading and cache warming differ. Do not turn the frame-count difference into a percentage performance claim. Display-submission counters include generated/repeated pictures and do not measure monitor scanout. These runs do not establish a reduction in interpolation artifacts or certify every scene/game.

Engine SHA256 values for the runs:

- Original 1.39: `474d6dddc23c08259412737ae7df84d396cf1e4ecea8ff41458f98bd59f47601`
- Candidate 1.40: `1bf03e7ac2c7fdcb43d526cbfe4ed57d517cb01b2c6fd12106d6ab6d59b5177c`

## Frontend and process regressions

The app and all ten frontend test executables compiled with the existing native .NET Framework compiler. The rebuilt app reports 0.57.0.0 and carries the tested Engine 1.40 package. Completed suites on the rebuilt frontend: Interface 43, CloudSave 60, Optimization 114, Download 35, Diagnostic 37, and Doctor 38 checks. The engine-install suite passed its first 14 checks (install/backup/rollback and the first AutoFix/save-protection checks) but then exceeded a four-minute run limit before producing a complete result. It is not reported as passed; investigation remains open before stable promotion. Frame-cadence assertions pass against both the distributable patch and actual engine source.

`python tests/run_process_lookup_tests.py` passed 18 live-process checks and nine injected failure/race checks. Coverage includes selected live processes, same-named copies in other directories, Unicode/spaces, exited processes, registered probes, repeated lookup and handle cleanup, and fail-closed error paths. Restoring the original production lookup reproduced the exact null-reference crash; basename-only matching and swallowed-access-error variants were rejected. The rebuilt frontend then passed all 60 existing cloud-save integration checks.

The native conversion suite compiled and ran its synthetic AArch64 input and passed its conversion/error/cancellation checks before stopping at the fixture-creation assertion for an actual hard link. A separate short-path probe returned EPERM for hard-link creation in this execution environment. The test was not weakened or reported as fully passed. The updater suite was compiled but not run: its installer test invokes a PowerShell execution-policy bypass, which was not used for this work. No toolchain or security-policy changes were made.

## Distribution and remaining limits

The engine patch passes clean-base application and working-tree reverse-application checks. Export preserved raw Git output bytes, including existing non-UTF8 source comments. The packaging helper selects app/runtime files explicitly rather than copying development test output, includes engine and regression sources, verifies ZIP CRC/manifests and copied-file hashes, and generates an artifact SHA256 file. The published release checksum asset identifies the resulting artifacts; this source document alone is not proof of publication. No proprietary game files, saves, keys, screenshots, raw gameplay logs, or local backend pointers are selected.

AMD interpolation's fixed frame delta and explicit-reset forwarding are unchanged in this batch. The existing `blit_to_pic` helper's command-buffer lifetime during internal resizing also remains a separate review item. Neither the game's physics rate, interpolation quality settings nor the display-target rule was changed.
