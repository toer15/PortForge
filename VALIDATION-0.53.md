# PortForge 0.53 / Engine 1.35

## What's new

- **A new Esc menu look.** The game's key art is a poster beside the menu, and the same art blurred fills the screen behind it, darker toward the bottom. The head band is red and reads `PORTFORGE / <GAME>`. The focused item is a red bar. Games converted with 0.53 get their key art automatically: the extraction writes `icon.jpg` and the conversion copies it into `<game>_data`. A game converted earlier shows the menu without a poster until it is converted again, or until its `icon.jpg` is fetched with `scarletnx -x <dump> --control-only` and put in its data folder.
- **The menu remembers your choices.** The FPS limit, PortForge FSR 3 and the frame-time graph chosen in the menu (or with F7) are saved in `<game>.ini.menu` and come back at the game's next launch. When you save other global settings in PortForge, those apply again, because the file records which global settings it was made under.
- **Co-op.** `Esc > Co-op` lists players 1 to 4 and the controller driving each, and turns players 2 to 4 on or off. A controller connecting or leaving, USB or Bluetooth, pops a card in at the top right with its player number, its name and its connection, with or without the menu open.
- **Code patches.** `.pchtxt` files in `%LOCALAPPDATA%\PortForge\patches` are applied when a game is converted, when their build ID matches the game's code. The patched modules go to `<game>_data\mods\PortForge code patches\exefs`, and the game is compiled from them. The build log lists what was applied.
- **Fewer shader stalls.** Draws into images the game redraws every frame no longer wait for a pipeline that is still being built inside occlusion queries either. The log counts the waits that remain by cause.

## Verification on the developer PC (Ryzen 7 9800X3D, RX 9070 XT, 2560x1440 at 280 Hz)

- Engine test suite: 227 of 232 pass. `ldn_lan` and `ldn_service` need a LAN broadcast answer this PC doesn't get. `nx_jit_region` and `astc_vulkan` don't link on Windows, unchanged by this release. The menu, controller and presenting tests (overlay, gamepad, frame_trace, vpad, runtime_present, frame_policy, vulkan_headless) all pass, including new checks of the Co-op page, the player switches written to the .ini, and the join card's text, timing and name fitting.
- App tests: the code-patch parser, build-ID matching, NSO rewrite and mods folder checks pass.
- Super Mario Odyssey (isolated copy): the main menu and the Co-op page show the key art and blurred backdrop. Run 1 turned the graph on and raised the limit from 120 to 144 by mouse. Run 2 started with both, and the log said `the menu's 2 choices from the last run kept`.
- A staged conversion (`-r --no-build`) of a folder with `icon.jpg` copied it into the data folder.
- Zelda BotW 1.0.0 with a community 60 FPS `.pchtxt` (isolated copy): the patch applied to `main`, the game converted and ran at 56 to 60 real frames a second. Whether the game also runs at the right speed with the patch was not measured. The patch's first edit writes bytes that decode to a floating-point instruction instead of the NOP the patch's comment describes, so it may be written for a different byte order.

## Not done

- **Save anywhere.** A Switch game saves when its own code decides to. A menu button can't make it write a save it isn't at a save point for. True save-anywhere needs save states of the whole emulated machine, which the engine doesn't have. The **Saving...** indicator still shows every save the game writes.
- **Co-op in one-player games.** The Co-op page and join cards work in every game, and a game made for several players sees each connected controller as its next player. A game made for one player has no second character for the engine to give a controller to.
- **Screen tearing** wasn't seen in testing: Mario presents with vsync (FIFO). Where and when it shows on the player's PC is still needed to find its cause.
- **Hitches** at first play come from the driver building pipelines (100 to 250 ms each on this AMD driver). The remaining large ones happen at level loads. A second play of the same area is nearly free of them, because the driver's cache keeps the pipelines. Linking pipelines from libraries (`fast_pipelines`) would remove most of the wait but is off: on this driver it made frame generation's compute shaders give wrong pictures.
