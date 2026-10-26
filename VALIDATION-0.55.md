# PortForge 0.55 / Engine 1.37 and 1.38

## What's new

- **AMD's own FSR 3 frame generation (Engine 1.38).** With PortForge FSR 3 on, the generated pictures come from AMD FidelityFX SDK 1.1.4's optical flow and frame interpolation (AMD's open-source, MIT-licensed shaders, compiled into the engine, with the SDK's host side ported to the engine's Vulkan code). A Switch game gives a PC no motion vectors or depth, so they are zero and flat: AMD's interpolation then takes the motion from its optical flow, and what stays in place (a HUD) stays where it is. `amd_frame_generation=0` goes back to the earlier search.
- **The game's own preparing screen (Engine 1.37).** At launch a game waits behind a screen with its key art, name and a progress bar while the shaders, pipelines and compute programs it used before are built. It shows only when there is something to build (a first play after the warm-up, a graphics driver update). Esc, a right-click or a controller's A, B or Home starts the game at once.
- **First-launch warm-up (app).** On a game's very first Play, PortForge plays its opening once, hidden, behind a screen of the game's own (about 45 s; the save is backed up and put back), so its shaders are recorded before the player plays. Esc skips it.
- **Recorded shaders survive engine updates (Engine 1.37).** The record's key hashed whole source files that nearly every update changed, so 1.35 and 1.36 threw away every player's recorded shaders. It now hashes only the code that gives a record its meaning.
- **Play no longer cancels itself when it updates a game's engine (app).** Play put a hold on the game against background work, and its own engine update then met that hold and the start ended as cancelled. Play's own work is now exempt.

## Verification on the developer PC (Ryzen 7 9800X3D, RX 9070 XT, 2560x1440)

- AMD frame generation, offline on 20 sets of Zelda BotW's real frames (the picture generated between two real frames, scored against the real frame between them: twice the motion of 60 to 120 play, a hard case): **23.94 dB, 16.4% of moving pixels visibly wrong**, where the earlier search scored 22.78 dB and 20.8%, and repeating a frame 19.57 dB and 45.8%. On evenly spaced moving sets: 22.40 dB, 16.3% (earlier 21.25 dB, 20.8%). Fast close-up movement still smears in the hardest sets.
- AMD frame generation in Zelda (60 FPS test copy, FSR 1 1920x1080 to 2560x1440, limit 60): Display FPS 120 of 120 through a 100 s scripted run, thousands of generated pictures, no errors.
- Preparing screen in Super Mario Odyssey with an empty driver cache: 506 shaders and 540 pipelines loaded, 547 built in 3.5 s behind the screen, then the game went on.
- First-launch warm-up on Super Mario Odyssey: 528 pipelines recorded in 47 s behind the app's themed screen.
- Engine tests: the menu, controller and presenting tests pass; the whole suite as in 0.54. App test suites pass as in 0.54.

## Not done

- **Game depth and motion vectors.** The interpolation would separate a moving character from the background better with the game's own depth buffer. Finding it in every game's draws is a separate piece of work.
- **Latency.** A generated picture sits between two real frames, so the newest real frame is shown about half a game frame later (about 8 ms at 60 game frames a second). That is the same for every frame interpolation, AMD's included. With FSR 3 off the pictures are the game's real frames, with no added delay.
