# PortForge 0.48 — faster ROM → native conversion / download

App-only speedups. Engine stays **1.30** (no Esc FPS string change in this release).

## What was slow

1. **AOT conversion used `--jobs 2` on every PC**, so an 8–16 thread machine left most cores idle during the longest step (ScarletNX `-r`).
2. **`--keep-ir` wrote intermediate representation for every function**, multiplying disk I/O that the finished game folder never needs.
3. **Downloads** used 256 KiB sockets, re-stat'd every part file for the progress bar, and spent ~8 s measuring 1→4→8 connections before the first game byte on an uncached host.
4. **Hash / assemble / DLL publish / ZIP unpack** used 1 MiB buffers without sequential-scan hints.

## What 0.48 does

| Area | Change |
| --- | --- |
| Conversion | `HardwareProfile.CompilerJobsFor`: 2…32 jobs ≈ logical CPUs − 1; logged and shown in the status card |
| Conversion | Drop `--keep-ir` from the ScarletNX invoke |
| Publish | 4 MiB sequential copies when breaking hard-linked top-level DLLs |
| Download | 1 MiB transfer buffers; progress from an atomic byte counter (no per-tick `FileInfo` scans) |
| Download | Connection trial starts at 8, ~1.1 s samples, ~2 s total before transfer; 12-hour host cache unchanged |
| Hash / assemble / ZIP | 4 MiB sequential I/O |

Correctness paths unchanged: range/`If-Range` resume, checksum / ZIP structure re-download, non-overwrite of existing destinations, publication move, cancellation.

## Engine 1.31 gap (not in this release)

Esc on Engine **1.30** still formats Limit as `120 (~60)` under speed lock when the expected display rate differs from the chosen limit (see VALIDATION-0.46 / 0.47). The intended **1.31** wording — `Display FPS: 60 (2x game 30) | Limit: 120` with Limit free of the `~` annotation — needs an Engine rebuild on Windows and is **not** included here. 0.47 crash + Play `EnsureForPlay` work is already on `main` (Engine 1.30).

## Verification

1. Build with `build.ps1 -Test` and a ScarletNX backend.
2. Convert a known game; the build log must show `Compiler jobs: N` with N ≫ 2 on a multi-core PC, and no `--keep-ir` in the backend command line.
3. Download a multi-GB direct link with automatic connections: first host should start transferring within ~2–3 s; progress MB/s should move without stalling the UI.
4. Pause/resume and checksum mismatch still behave as in VALIDATION download coverage.
