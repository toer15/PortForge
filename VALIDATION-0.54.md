# PortForge 0.54 / Engine 1.36

## What was wrong

The first time a game shows a new area, it needs hundreds of new pipelines (the driver's compiled form of the game's shaders with their draw state). On the developer PC each takes the AMD driver 20 to 250 ms to build. Engine 1.35 built them on 4 worker threads, and the game's frame waited when it reached one that wasn't ready.

A trace of every build and wait in Super Mario Odyssey's first run showed the longest freeze (3.5 s) waiting for a pipeline the driver built in 7 ms. Each new pipeline went to the **front** of the workers' queue. The thread that reads the game's GPU commands runs ahead of the thread that records them, so 290 pipelines asked for after the needed one (13.9 s of work for 4 workers) were all built first.

## What Engine 1.36 does

- The workers build pipelines in the order the game asks for them.
- When the frame reaches a pipeline no worker has started, it builds that one itself, so it waits for its own build only.
- From 6 physical cores on, every core but one builds pipelines (at most 12; 1.35 used half). The workers run below normal priority, so the game's own threads keep theirs.

## Verification on the developer PC (Ryzen 7 9800X3D with 8 cores, RX 9070 XT)

Each run started from empty engine and driver caches, with the same scripted input (title screen, then walking and turning).

| | Engine 1.35 | Engine 1.36 |
|---|---:|---:|
| Mario, 150 s: worst freeze | 3.8 s | 2.3 s |
| Mario: time in freezes over 50 ms | 8.4 s | 5.9 s |
| Mario: pipeline waiting in them | 5.2 s | 3.2 s |
| Mario: frames outside freezes | 17.5 ms avg | 17.3 ms avg |
| Zelda BotW (60 FPS patch), 180 s: time in freezes | 7.7 s | 6.6 / 6.7 s |
| Zelda: pipeline waiting in them | 2.8 s | 1.7 / 1.8 s |
| Zelda after loading | 60 FPS | 60 FPS |

A second play of the same areas is nearly free of these freezes either way, because the driver keeps what it built.

Engine tests: 227 of 232 pass, as in 0.53 (the LAN tests and two that don't link on Windows fail the same way there).

## Still open

- **Compute programs.** Most of Zelda's remaining first-run freeze time (4.4 of 6.7 s) is 5 of its compute programs. The driver takes 0.5 to 1.8 s to compile each one, because each becomes 190 to 450 KB of SPIR-V. An optimizer removes only about 20%, so shrinking them means changing how the translator writes them.
- **Fewer pipelines.** About 30% of Mario's pipelines differ from another only in their vertex layout. A driver feature that sets the vertex layout per draw (VK_EXT_vertex_input_dynamic_state) would let one pipeline serve them all.
