# PortForge 0.33 / Engine 1.16: first-play lag and shader workers

PortForge 0.33 also includes 0.32's game menu (VALIDATION-0.32.md), which was not published separately.

## The lag

Test PC: Windows 11 (build 26300), AMD Ryzen 7 9800X3D (8 cores, SMT off), Radeon RX 9070 XT (driver 32.0.32015.7029), 32 GB DDR5-6000, 2560x1440 at 280 Hz, Kingston NV3 NVMe.

A player's two first runs of a converted open-world game ran its opening at 3 to 5 FPS once control was given to the player, and never recovered. The frames alternated between about 210 and 310 ms. The engine's per-frame breakdown showed no shader, pipeline, upload, file or GPU wait time in those frames, and the 1 ms CPU sampler found every thread waiting.

Mechanism, from the engine's log and thread dumps:

1. On a cold driver pipeline cache the GPU thread fell several seconds behind the game. Required pipelines queued behind hundreds of new ones on two shader workers.
2. The game waits for each frame's fence with `EVENT_WAIT` and a 100 ms timeout. Its wait timed out, it treated the submission as finished and wrote its next commands into the same command memory.
3. The engine reads the GPFIFO entries after a SYNC entry when the GPU thread reaches them, as the hardware does. It then read the new frame's words as the old submission's methods. The log shows skipped pushbuffer headers, inline-to-memory launches of gigabytes, render target format 0x3a400040 and `ended a submission at syncpoint value 4300, short of the 4301 it promised`.
4. Later fences were never reached. Each frame waited out two or three 100 ms timeouts.

A diagnostic build hashed every late entry at submission and again at fetch: 2421 entries matched, then entries 7 to 132 of one submission changed 7.4 s after submission, right after the game's wait for that submission's fence (syncpoint 20, threshold 356) timed out.

## The fix

- nvdrv records the latest fence value a game's own wait ioctl (`EVENT_WAIT`, `SYNCPT_WAIT`, `SYNCPT_WAITEX`) gave up on. A poll with timeout 0 doesn't count.
- A submission now also copies the entries after its first SYNC entry, within the existing 64 MiB bound. They are still read when the GPU thread reaches them. The copy replaces the read words only when they differ and the game gave up on a fence value in the submission's range (from the previous submission's fence on that syncpoint) or later. A warning names the channel and the number of entries.
- Automatic shader workers: Windows now reports CPU threads and physical cores (`GetActiveProcessorCount`, `GetLogicalProcessorInformationEx`). With 6 or more cores, half of them (at most 6). Before this, `plat_cpus` returned 0 on Windows and every PC got 2. The log line says `4 shader workers (8 CPU threads, 8 cores)` on the test PC.

## Measurements

Zelda-type opening, isolated copy, headless, `--vsync=0`, A pressed every 2 s, the game's own 30 FPS pace:

| Engine | Cache | Runs | Corrupted | First-run loading freezes (sum of frames over 50 ms) | 1% low after loading |
| --- | --- | --- | --- | --- | --- |
| 1.14 (installed) | cold | 2 of the player's, 2 here | 4 of 4 | permanent | 3.2 FPS |
| 1.16, 2 workers forced | cold | 2 | 0 | 7.1 s, 7.6 s | not reached (75 s runs) |
| 1.16 | cold | 2 | 0 | 5.9 s, 5.8 s | 27.3 to 28.0 FPS |
| 1.16 | warm | 2 | 0 | 0.41 s, 0.37 s (worst frame 112 ms) | 25.4 to 28.1 FPS |
| 1.16, pipelines slowed to 150 ms each | none | 2 | 0 | (forced) | 26.5 to 27.7 FPS |

"Cold" removed the engine's shader cache and the AMD Vulkan driver cache files before the run. The pipeline delay was a temporary diagnostic environment variable, removed from the release build. With it, the unfixed engine corrupted the stream in every run. An earlier version of the fix that only matched the submission's final fence missed one case where the game waited for a value partway through a submission (3 of 6 cold runs corrupted later in the run). The released range check had no misses.

Mario-type game, isolated copy, 40 s startup, uncapped: 2258 and 2277 frames with Engine 1.14, 2277 and 2275 with 1.16. The p99 frame times (17.2 to 20.5 ms) were within run-to-run variation. No corruption in any run.

## Tests

- `gpu_park` gains a case where the game gives up on a submission and overwrites its entries after SYNC: they run as submitted, with one warning. The existing case where the CPU writes those entries before execution without a give-up still sees the new words. The test now also builds with MinGW (SRW locks), and passes on Windows.
- `frame_policy` covers the core-based worker counts.
- Full engine suite on Windows: 225 of 230 passed. `ldn_lan` and `ldn_service` need LAN broadcast sockets this PC's firewall doesn't allow. `pipeline_file` failed only under `-j6` and passed alone. `astc_vulkan` and `astc_vulkan_pieces` don't link on Windows because `plat_win32.c` calls the profiler, which that test doesn't build (unchanged since Engine 1.9).
- Two test build breaks from earlier releases were fixed: the 3DS runtime's calls to `fs_tree_share` and `tex_upload_rows`' missing `plat_ns`.

## Not changed

Game code and its ABI are unchanged, so existing conversions only need the new runtime, which the Doctor installs when the game is closed. The first run of a game still pauses while the driver builds pipelines; this release only stops those pauses from breaking the game and shortens them on CPUs with 6 or more cores. Other games, other GPUs and other CPUs were not measured.
