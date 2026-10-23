# PortForge 0.32 / Engine 1.15 game menu

Esc previously set the SDL backend's quit flag whenever the engine menu was closed. It now opens the shared engine menu; inside the menu it goes back one page or returns to play. Key repeats are consumed, including after the menu closes. Right-click uses the same behavior, with its release consumed. The operating system's window-close control remains available, and the menu's explicit Exit action still quits.

Home/Guide retains its menu shortcut. Controllers whose Home button is intercepted by the operating system can hold Back/Share/Minus and press Start/Options/Plus. The chord checks physical buttons on the same SDL device, independent of custom guest mappings. Only Start's button-down activates the chord, preventing two toggles when both presses arrive in one poll. Ordinary Start or Back input remains available to the game.

Before opening, the backend samples controller state into the closed overlay. The overlay blocks those held buttons and stick directions until release, including the shortcut's controls when custom mappings assign them to confirm/back. This prevents opening the menu from immediately selecting an item. Menu operation captures game inputs; it does not pause guest simulation.

Validation includes six selected engine suites: overlay, gamepad (SDL virtual Xbox, Switch and PlayStation devices), pad_screen, runtime_present, frame_policy and vulkan_headless. New controller assertions cover device isolation, physical mappings, ordinary buttons and remapped shortcuts. New overlay assertions cover confirm plus a held stick across opening and delayed repeats, followed by release and fresh navigation.

A windowed isolated Mario copy passed ten input checks: opening with Esc, held Esc, fresh Esc returning to play, repeats after closing, right-click opening, Enter navigation, Esc going back a page, right-click returning to play, F2 compatibility and explicit Exit closing with code 0. The actual game-window capture showed the readable menu over Mario. Runtime screenshots omit presentation overlays, so the window itself was inspected. The final runtime was used for the smoke check. Player saves were not used or modified by that check.

The app regression suites and installer checks are also run for this release. This is a runtime-only change: native game code and the game-code ABI are unchanged. New conversions receive the bundled runtime, and watched existing games use the existing backed-up upgrade path when closed. Physical controller shortcut operation on other PCs has not been tested.
