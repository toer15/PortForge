# Native conversion roadmap

## User requirement

Select a Switch game input, convert its actual program code to native PC code, and play the resulting Windows game executable. Bundling an emulator does not satisfy this requirement.

## Verified so far

- The native frontend invokes ScarletNX's extraction and ahead-of-time compilation stages.
- The official Windows backend was downloaded and its GitHub release digest checked.
- A self-authored synthetic NRO was compiled into an x64 game-code DLL and executable, with inspectable LLVM output.
- The result ran after moving the folder and deleting the original test input, reporting zero JIT-compiled instructions for that test.
- Native tests cover actual backend execution, relocation, failed compilation, overwrite protection and cancellation cleanup.
- Version 0.4 fixes long paths, preserves the primary error when cleanup fails, separates runtime DLL hard links, and publishes the game without an extra full asset copy. 24 native checks and 12 interface checks pass.
- The user's local Super Mario Odyssey 1.3.0 input was converted successfully. Its 4,972 RomFS files were published, and a short executable launch reached mode selection with 774 rendered frames and exit code 0. That run used JIT fallback.
- The user subsequently reported working gameplay, lag and a 60 FPS ceiling. Version 0.5 exposes per-game FPS and graphics controls and compares measured startup profiles. A 12-second startup run with a 120 FPS cap rendered 1,239 frames (103.25 FPS including loading), establishing rendering above 60 in that sample. This does not verify correct gameplay speed at that cap. Automatic selection stays at or below the chosen render scale and is not an AI model or whole-game optimization pass.

## Outstanding acceptance test

Version 0.6 adds direct-link acquisition, safe resume and parallel transfers, bounded recovery, ZIP preparation and conversion handoff. Local HTTP fixtures exercise these behaviors. Real source throughput varies; universal download-speed and automatic compatibility-repair claims remain unsupported.

The selected target is Super Mario Odyssey 1.3.0. Conversion and startup are verified on this PC; representative gameplay, audio, input and saves remain to be tested. Inspect AOT/JIT statistics; a build is not certified as fully static merely because it contains an .exe.

ScarletNX supports JIT fallback. A strict no-runtime-CPU-translation requirement may require a game-specific backend or changes to the backend, with verification over gameplay. Source inspection also shows generated code can depend on the build CPU's features, so cross-PC portability needs testing.

XCI extraction has been verified with the local selected input. Other containers, game versions and games remain unverified. No game, firmware or keys were downloaded. PS5 and other console conversion backends are not implemented.

The next milestone is one verified playable game, with precise version, build evidence and observed limitations. Do not add claims for arbitrary games or other consoles before that result exists.
