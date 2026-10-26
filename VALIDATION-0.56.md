# PortForge 0.56 / Engine 1.39

## What's new

- **The first launch has the game's own loading screen (app).** On a game's very first Play, the screen PortForge shows while it plays the opening once, hidden, to record its shaders is now full screen: the game's key art blurred behind, its poster, its name, and along the foot a progress bar with the game's character running at its front (bobbing with each stride, leaning into it, kicking up dust).
- **The engine's preparing screen has the same runner (Engine 1.39).** At later launches, while the shaders recorded before are built, the game's character runs at the front of the bar, dust behind each footfall.
- **Neither screen can be skipped.** The game starts once its shaders are built. The app's screen stays until the warm-up ends (Alt+F4 included). The engine waits as long as building makes progress; only a minute without progress (a stuck driver) lets the game go on without the rest.

## How the character is found

A game gives a PC no separate character art, only its key art. PortForge looks for the square of the key art, 40% of its side, with the most skin-toned texels (a warm hue of 5 to 32 degrees at a middle lightness, which keeps out a logo's gold at about 40 degrees), counted twice in the square's middle (a face, not a sunset's rim). Key art without a face runs the whole poster instead. The app and the engine use the same rule.

## Verification on the developer PC

- Super Mario Odyssey's key art: Mario's face and body; Zelda BotW's: Link, with part of the logo's "Z" behind him.
- Engine screen in Super Mario Odyssey with an empty driver cache: 539 shaders and pipelines built in 3.4 s behind the screen, the character running at the bar's front at 60% (screenshot checked), then the game went on.
- App screen rendered for both games at 1920x1080 through its paint code.
- Engine tests: the menu, controller and presenting tests pass, the suite as in 0.55. App tests as in 0.55.
