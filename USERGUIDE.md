# PortForge 0.37

**PortForge FSR 3 (Engine 1.21):** Settings Upscaling ? PortForge FSR 3, or Esc/Home ? PortForge FSR 3 On/Off. Built from FSR 3/AFMF ideas without AMD SDK (no Switch motion vectors). 60 FPS ? ~120 on screen. See `VALIDATION-0.37.md`.

**Settings for all games:** open the main **Settings** tab, choose your preferences, then **Save for all games**. Updated games read these on their next launch, including when started directly from the desktop with PortForge closed. Turn off **Apply these settings to every game** and save to return to individual game settings. The global layer takes priority over Game tools. Engine 1.17 or newer is required; older converted games need the runtime update while closed.

**Cursor and saving:** the pointer hides during focused fullscreen play and returns for Escape menus or Windows focus. **Saving...** appears at the bottom right when the game writes save data, including autosaves; errors are reported. A short write during a loading pause is kept pending until a frame can show it. This does not capture unsaved progress. Saving anywhere and an Escape Save Game action are not implemented.

**Compute caching:** compute programs are now saved and prepared on subsequent launches. First-time compilation and other loading stalls remain possible. This update may rebuild the shader cache once.

**Frame generation:** choose **PortForge 2x - experimental** in Settings and save for all games. The engine estimates a picture between game frames using GPU motion search. It is separate from AMD FSR 1 upscaling and is not AMD FSR 3 or the Radeon driver feature. It starts off; you can also turn it on or off for the current session in the Escape menu. A fast display and GPU headroom are required. Extra latency is still possible. Simulation and input stay at the game's real rate.

**Frame-time trace:** F7 or **Escape > Frame-time graph** shows the graph, with hitches in red. Turn on **Record frame-time trace** in Settings before launching to write `frametime-trace.csv` beside the game's log. After play, **View frame-time trace** shows the slowest frames and measured work. This measures real game frames; generated display frames do not inflate it. Cause labels identify the largest measured component; overlapping counters are not a complete causal profile. The CSV is replaced on each recorded launch.

**Resolution and FSR:** choose a 720p, 1080p, 1440p or 4K display target. With true resolution on, screen-shaped render targets render at that 16:9 size. Engine 1.42 records each game's own output height during its first run (or the warm-up after an engine install) and scales from it from the next start. Data-shaped targets stay native. Dynamic-resolution games can differ, so exact internal pixels are not guaranteed for every title. Scaling up costs GPU; scaling down loses detail but can reduce GPU work. Turn it off if artifacts appear. FSR 1 separately upscales the finished image. No zero-lag or universal-compatibility promise is made.

# PortForge 0.33

**Smoother first plays:** if a game dropped to a few frames per second right after its first loading screens and never recovered, Engine 1.16 fixes the cause. Expect a few short loading pauses the first time a game builds its shaders, then normal speed. Existing games get Engine 1.16 from the Doctor when they are closed.

**Game menu:** press Esc or right-click inside the game. Esc/right-click goes back through menu pages and returns to the game; it no longer quits. On a controller use Home/Guide, or hold Back/Share/Minus and press Start/Options/Plus. Use the D-pad or stick and the controller's mapped confirm/back buttons; keyboard arrows and Enter work too, and you can click items with the mouse. Select Exit to close the game. Existing games need Engine 1.15 or later, installed automatically by the Doctor when the game is closed. The menu captures game controls while open; it does not freeze the game's simulation.

**Menu look and remembered choices (Engine 1.35):** the menu shows the game's key art as a poster beside the items and blurred behind the whole menu. The FPS limit, PortForge FSR 3 and the frame-time graph you choose in the menu (or with F7) are remembered for the game's next launch. When you save other global settings in PortForge, those apply again instead. A game converted with PortForge 0.53 gets its key art automatically. An older game shows the menu without a poster.

**Co-op (Engine 1.35):** **Esc > Co-op** lists players 1 to 4 and the controller driving each. Connect another controller, USB or Bluetooth, and it joins as the next player: a card pops in at the top right with the player number, the controller's name and how it is connected, and another when a controller leaves. Players 2 to 4 can be switched on or off on that page. The game decides how many players it uses: a game made for one player still has one character.

**Preparing screen (0.55, Engine 1.37; character 0.56, Engine 1.39):** at launch a game waits behind its own screen (its key art, name, and its character running along a progress bar) while the shaders it used in earlier runs are built, so it plays without stopping for them. It shows only when there is something to build, as after a graphics driver update. On a game's very first Play, PortForge shows a full-screen loading screen of the game's own while it plays the opening once, hidden (about 45 seconds, your save is backed up and put back), so those shaders are recorded before you play. Neither screen can be skipped. The character is found in the game's key art; key art without a face shows the whole poster running instead.

**AMD FSR 3 frame generation (0.55, Engine 1.38):** with PortForge FSR 3 on, the extra pictures come from AMD's own FidelityFX optical flow and frame interpolation. Switch games don't give a PC motion vectors, so the motion comes from AMD's optical flow; fast close-up movement can still smear a little.

**Code patches (0.53):** put `.pchtxt` code patches (the format emulators use, such as a game's 60 FPS patch) in `%LOCALAPPDATA%\PortForge\patches` before converting a game. PortForge applies each patch whose build ID matches the game's code, builds the game from the patched code, and lists the applied patches in the build log. Patches for other games or versions are skipped. A patch can change how a game behaves, game speed included.

**Startup fixes:** Engine 1.14 selects the game's requested memory layout automatically and avoids opening every game asset before play. New conversions also receive the corrected atomic-instruction translation. An existing affected game-code DLL needs rebuilding once to receive a translator fix; installing a new runtime alone does not rewrite compiled game code.

**Controllers:** open Controllers to choose Xbox, DualSense, DualShock 4, Switch Pro, Joy-Cons, another controller, or Automatic. The controller picture stays still while buttons light up, triggers fill and sticks move with live input. Save for all my games applies the button layout to known closed games and future conversions. Custom game button maps are preserved. The chart translates Switch prompts to your controller's buttons; it does not replace graphics drawn inside games.

**Pixel smoothing:** choose On to remove jagged, staircase edges. The engine follows each edge in the game's image and blends along it before FSR enlarges the picture, and FSR sharpens less so the steps do not return. Text and fine detail soften a little, and it cannot add detail the game did not render. Save and restart the game to apply. Engine 1.12 or newer gives the full effect (the AI Doctor installs it under watched games).

**Performance + FSR:** on a protected engine, Performance selects handheld mode and AMD FSR 1. The game chooses its own rendering layout; FSR then reconstructs and sharpens it to a larger window. This can reduce rendering work in games that use a lighter handheld layout. Some games use identical layouts in both modes. FSR itself adds GPU work; it cannot double FPS or remove loading stalls. Balanced and Quality restore TV mode. The graphics window shows the latest recorded FSR status, not a promise that a selected option is active. Maximize the game window or use fullscreen for upscaling to the display.

**Auto tune** now compares protected native rendering in TV and handheld modes. TV quality wins when handheld brings no measured benefit. It uses an isolated save and restores settings after each sample. Its offscreen measurements exclude FSR and presentation cost.

**Play game** opens the game or returns to its existing window. Background engine-settings probes do not count as gameplay. Play starts from the local save immediately; to fetch another PC's progress first, use **Cloud saves** before playing. Automatic cloud saving resumes after the game closes. If a game exits during startup, the status shows the exit and available log details.

**Graphics protection:** Engine 1.9 keeps the legacy render-scale setting at **100%**, including when an old Performance preset saved 75%. Engine 1.41's separate experimental resolution matching only scales screen-shaped targets together; square, tiny, array and unusual data targets stay native. Some titles may still show artifacts, so turn the option off for those games. Reduced or increased legacy render-scale presets remain protected.

**Upscaler:** choose **AMD FSR 1 (automatic)** to reconstruct and sharpen the finished image when the game window is larger. This works at the game's native internal resolution. Linear is the off switch. Install Engine 1.9 from the Engine tab for graphics protection; new conversions and downloaded inputs receive it automatically. Your existing game needs an engine upgrade and a restart; a running game keeps its old engine.

FSR 1 is spatial upscaling, without frame generation. FSR 4 and NVIDIA DLSS are not included. It does not change game speed or guarantee doubled FPS, zero stutter or fixed 1% lows. Auto tune renders offscreen and excludes display upscaling cost; compare actual gameplay as well.

# PortForge 0.24

## Play any time

**Play game** always starts the game, even while PortForge is busy. It stops the app's own background work for that game first: Auto tune, Doctor checks, shader warm-up and engine checks. Each of these puts your save, settings and engine back exactly as they were. Nothing new starts for that game for two minutes, and nothing at all while the game is running.

Cloud saves never block Play. PortForge fetches your latest cloud progress when it can. If the cloud is busy, offline or has different progress from another PC, the game starts with this PC's save. Sync runs again after you close the game, and both copies are kept when they differ.

If PortForge was closed or crashed during a test, a `PortForge-test-backup-...` folder could be left in the game folder. Earlier versions then kept cloud saves and Play waiting forever. PortForge now resolves it by itself, and it never deletes a save that differs from yours:
- A backup identical to your save is removed.
- If your save changed only during the test, your save from before the test is put back.
- If you played after the test, your current save stays.

Any copy that differs is moved to `PortForge-save-backups` in the game folder.

## Game speed lock

PortForge Engine 1.6 has a **Speed lock**, on by default. Most games count their frames, so on earlier engines an FPS cap above the game's own pace (for example 120 on a 60 FPS game), Unlimited or F4 made the game itself run faster, and a cap below it (for example 30 on a 60 FPS game) made it run in slow motion. With the speed lock on, the engine always runs the game at the pace it asks for. A 30 FPS game stays at its speed, and so does a 60 FPS game, whatever the cap. A lower cap shows fewer of the game's frames, the way a console game drops frames, instead of slowing it down. (Engine 1.5 in PortForge 0.21 covered only higher caps.) A game that times itself (swap interval 0) is still limited only by the cap you set.

Since Engine 1.34 the screen shows the FPS limit with PortForge FSR 3 off and twice the game's own frames with it on, never more than the monitor refreshes with VSync. When the game makes fewer real frames than that (Zelda BotW makes 30), the engine puts generated pictures between them (FSR 3 on) or shows the real frames alone (FSR 3 off). A 30 FPS game at Limit 120 shows its own 30 frames with FSR 3 off and 120 with it on, and still runs at its own speed.

Since Engine 1.46 a limit above twice the game's pace is reached instead of stopping at twice its frames. **Limit 240** in a 60 FPS game shows 240 on a monitor that refreshes 240 times a second or faster with VSync on (144, 165 and 200 work the same way); a 30 FPS game reaches 120, four times its own frames, because one real frame pays at most three generated pictures. The frames above twice the game's pace come from PortForge's own motion warps, at a quarter and three quarters of the frame, with AMD's interpolation in the middle: they are less exact than the doubled pictures, so use 120 (or 2x) where you prefer quality and 240 where you prefer the rate. The share is carried as a fraction (144 in a 60 FPS game is 2.4 pictures a frame), and no picture is ever one already shown.

**Unlimited** (Engine 1.47) asks for no number at all, so it means as many pictures as the engine can make: with AMD FSR 3 frame generation it gets the same ceiling a limit above twice the game's pace gets — three generated pictures a real frame, four times the game's own, 240 in a 60 FPS game and 120 in a 30 FPS game — and with FSR 3 off it is the game's own frames, because a limiter can only show pictures the game drew. With VSync it still stops at your monitor's refresh; with VSync off nothing caps it (240 in a 60 FPS game on a 280 Hz monitor, which is what free FPS means here). **Game speed** is the game's own pace, doubled by FSR 3 (120 in a 60 FPS game). Esc shows the live Display FPS, its target and the game's real frames.

Your PC's power goes into picture quality instead of speed. PC matching and automatic tuning pick the highest render scale and texture filtering that hold the game's pace on your hardware. New conversions, watched games that receive Engine 1.6, PC matching and **Auto fix everything** all keep the lock on. If it is ever switched off, the Doctor reports it and its automatic repair turns it back on. Advanced users can set `speed_lock=0` in the game's .ini to go back to the old behavior.

## Automatic engine performance protection

The bundled PortForge Engine 1.6 has **Frame guard** on by default. New conversions use it automatically, including conversions handed over from Downloads. The setting works inside the generated game's runtime when the game is launched directly.

Frame guard shares one short optional shader-wait allowance across a frame, instead of waiting again for each new pipeline. It also keeps cached background compilation from taking every shader worker. Draws needed to preserve menus or game data still complete. In **Graphics and FPS > Engine**, **Automatic frame guard** can be turned off for a compatibility comparison; it applies at the next launch. Smart async shaders and threaded command recording must remain enabled for optional draw waits to be avoided.

New games matched by this engine use automatic shader/JIT worker counts on every launch. Counts follow the CPUs available on the PC running the game. Explicit manual thread counts remain available. The engine also resets its pacing deadline when an FPS limit or native game interval changes, avoiding a pause from the previous pace. It preserves the selected game speed, FPS cap and VSync setting.

The Doctor installs a newer bundled engine under watched converted games when they are closed, with the existing backup and startup check. **Auto fix everything** also selects the new engine's automatic shader workers. Engine 1.7 reuses compatible shader caches across routine engine updates and reports p99 frame time/1% low and GPU-wait details for diagnosis. A game restored manually to its original engine remains an exception until you choose to install again.

These changes address specific engine stalls. Game loading, new shader translation, driver waits, CPU/GPU limits and compatibility problems can still produce slow frames. See `Source/VALIDATION-0.21.md` in the source ZIP for measured results and limits.

## Cloud saves

1. Open **Cloud saves** in the main window. You do not need to select a game first.
2. Choose **OneDrive** or **WebDAV** and enter your account's connection details below.
3. Select **Connect all games** once. Existing games the app knows about join automatically, including games found by background discovery. New conversions and games loaded later join using the same account.
4. Keep **Automatic cloud saves for all games** on. Close the game and keep PortForge open to synchronize; no per-game setup is needed.
5. On another PC, connect the same cloud account once. A game's title ID is used as its cloud save name automatically. When a title ID is unavailable, check that game names match on both PCs.

Automatic mode is selected by default, but it uploads nothing until a cloud account is configured. Each player supplies and connects their own account on each PC. If you already enabled cloud saving in 0.19 with one connection, that account is reused automatically. If several accounts were used, select the shared one with Connect all games. Existing individually configured games on the same connection are adopted without changing their save names. Games using a different individual cloud connection retain that connection.

To exclude a game, browse to its `.exe` in this window and turn **Cloud saves on for this game** off. Discovery respects that choice. Turning the global **Automatic cloud saves for all games** switch off pauses games using the shared connection; turning it on again resumes them without losing sync history. Individually configured connections remain separate. To change the account used automatically, enter the new connection and select **Connect all games** again. **Sync now** saves changes for the selected game and checks immediately.

**OneDrive:** sign in to OneDrive in Windows first, then select its account root folder (for example `C:\Users\You\OneDrive`). PortForge puts versioned saves under `PortForgeCloudSaves`. Windows OneDrive uploads and downloads those files. A **QUEUED** status confirms the local OneDrive folder, not a completed internet upload. Before using another PC, confirm that OneDrive reports the files as synced on both PCs. If OneDrive is paused, offline or signed out, saves stay queued. See [Microsoft's OneDrive status guide](https://support.microsoft.com/en-us/onedrive/what-do-the-onedrive-icons-mean).

**WebDAV / Nextcloud:** enter the HTTPS folder URL supplied by your cloud provider, the username and an app password. A typical Nextcloud URL is `https://your-server/remote.php/dav/files/your-user/`. The account needs folder creation, listing, download and upload permissions. PortForge creates a `PortForgeCloudSaves` subfolder and reads each uploaded archive back to verify its checksum. App passwords are encrypted for the current Windows user and must be entered separately on another PC. Never enter your cloud credentials in a game download link. See [Nextcloud's WebDAV instructions](https://docs.nextcloud.com/server/stable/user_manual/en/files/access_webdav.html).

The app checks enabled games every 15 seconds while open and synchronizes before launching through **Play game**. It waits while the game runs or a protected gameplay test uses its save. If cloud access fails, local progress stays on the PC and syncing retries later. Launching a game's `.exe` directly bypasses the pre-launch check: open PortForge and let it sync first on a second PC.

If both PCs changed progress, **CONFLICT** stops automatic replacement. **Keep this PC** publishes this PC's save as a new version. **Use cloud copy** restores the selected version; select a version first when multiple PCs uploaded simultaneously. A restore keeps the old local save in `PortForge-cloud-backups` beside the game. To recover it manually, close the game, turn cloud saves off, and copy the chosen backup's contents to `<game>_data\save` after backing up the current folder.

Turning a game's switch **off** stops further synchronization for that game and keeps both local and cloud copies. New games continue joining the automatic connection until the global switch is turned off. Close PortForge to stop its background checks. OneDrive may finish already queued Windows uploads independently.

Only `<game>_data\save` is synchronized. Game files, ROMs, keys, logs and hardware information are excluded. Sync requires a supported converted game, a real local save folder, and your own cloud account. PortForge does not provide cloud hosting. The save limit is 256 MB and 10,000 entries; linked files are rejected. Up to 512 revisions are retained before manual archive maintenance is required. Existing versions are kept to avoid automatic loss of older progress. Settings are stored under `%LOCALAPPDATA%\PortForge\CloudSaves` and survive app updates.

## Download a game file and continue to conversion

1. Select **Downloads** at the top of PortForge.
2. Paste a **Direct download link** to an `.nro`, `.nsp`, `.xci` or `.zip` file from your chosen source.
3. Choose **Save as**. If the link ends in a supported filename, the initial filename is filled in for you.
4. If the source supplies a **SHA-256**, paste it in the optional checksum box. This lets PortForge check that the downloaded bytes match the source's expected file.
5. Select **Download / Resume**.

The status panel shows progress, completion, pauses and errors. The speed display shows transfer speed and downloaded size. Every text input has a **Clear** button. Clearing fields does not delete files.

The download manager automatically:

- Measures each server for a few seconds and uses the number of connections it serves fastest (one to eight; remembered for twelve hours), when the source supports byte ranges. Falls back to a full single transfer if ranges are refused, and resumes by itself up to three times after a dropped connection.
- Retries temporary connection failures and busy-server responses with delays, including the server's Retry-After instruction up to two minutes.
- Keeps partial download data when paused. A later download with the same destination resumes when the server supplies byte ranges and a matching strong ETag. Otherwise it restarts safely. If the source file changes, old and new bytes are never mixed.
- Checks file headers and ZIP structure. A supplied checksum mismatch or broken file/ZIP structure triggers one fresh download. If that also fails, the app explains the problem and keeps the failed file out of the final destination.
- Reuses a completed file after checking its local checksum and the source's validator. A file matching the supplied source checksum can be reused without a network transfer.
- Unpacks ZIP files into a new folder beside the download, preserving the original ZIP. Unsafe archive paths are rejected. If the ZIP contains several supported games, select the one you want.
- Hands a ready game back to conversion. **Start conversion when the game is ready** is enabled by default. Turn it off to inspect the result and choose **Use selected game** yourself. The main form's engine, local key files, output folder and automatic graphics-tuning choice are used.

Use **Pause** to stop a transfer while keeping its temporary parts. **Open folder** opens the location of a saved download. Temporary data uses a folder ending in `.portforge-download`; completed files have a small `.portforge-receipt.xml` next to them for verification and reuse. The recovery metadata stores a hash of the URL, not its query tokens. Existing destination files are never overwritten by the downloader.

This screen needs a direct file link. Browser sign-in pages, expired links, RAR/7z archives, corrupt source files, missing keys and unsupported games can require your action. It does not search game-sharing sites or download key files. Without a source checksum, header/structure checks do not prove every byte is correct. No downloader can guarantee a speed increase on every server or repair every source or game-engine problem.

Temporary Windows locks while replacing runtime DLLs or settings files are retried briefly. Persistent publication failures keep the completed compiler output for recovery.

## Graphics, FPS and automatic tuning

Select **Graphics and FPS** at the top of PortForge. Browse to a generated game's `.exe`, or use **Load** if its path is already shown. You can change an existing game's settings without converting it again.

- **FPS limit:** choose Game speed, a Custom limit from 10 to 1,000 FPS, or Unlimited. Custom enables the number box beside the choice. Game speed is the game's own pace, doubled by AMD FSR 3 frame generation. Unlimited asks for as many pictures as the engine can make: the game's own frames with FSR 3 off, and up to four times them with it on (240 in a 60 FPS game, 120 in a 30 FPS game), capped by your monitor's refresh with VSync and uncapped without it. The limit is a maximum, not a promise of that frame rate. Higher FPS can speed up a game's logic or affect physics. Some games need a game-specific timing patch to work correctly above their original limit.
- **VSync:** reduces tearing, but can cap presentation at your display's refresh rate. Turn it off when deliberately testing higher FPS.
- **Render scale:** 100% keeps the game's original 3D resolution. 75% reduces GPU work; 150% improves sharpness with more GPU work. Supported range: 25% to 400%.
- **Texture detail at angles:** higher filtering values can sharpen floors and surfaces. **Draw shadows**, **TV mode** and **Start fullscreen** offer further control.
- **Performance**, **Balanced** and **Quality** preserve FPS, VSync and fullscreen. Protected Performance selects handheld + FSR; Balanced and Quality select TV mode. **Clear** beside a control resets it to its default. **Reset defaults** resets the graphics controls together.

Select **Save settings**, then launch the game. Settings live beside its executable, so launching that executable directly also uses them. A dated backup of the previous INI is kept beside it. Close the game before saving settings or testing; PortForge does not close your running game.

**Auto tune** warms the game, then compares the current settings and supported presets using scripted opening scenes from a backed-up save. Duplicate profiles are skipped. Protected engines compare TV and handheld rendering with internal scaling kept at 100%; older engines also test experimental internal scales. It considers average FPS and p99 frame time. Your selected FPS and VSync remain unchanged. Select **Save settings** to apply a recommendation, and **Open test report** to review its measurements and limits. A script may remain in a menu in some games; later areas can be heavier.

The tests include loading and menus. They do not measure every level, verify game speed, or guarantee smooth gameplay. Shader caches are retained for later runs, but new scenes may still stutter while new shaders or missing game code compile. The tuner uses local measurements and rules; it does not contain an AI model, rewrite game logic, add an FPS patch, or certify every ROM.

**Automatically tune each new game** is enabled on the conversion screen. It applies the measured recommendation before saving each completed game. If the engine cannot complete a usable test, conversion still saves the game, shows **Done / Tuning skipped**, and records the reason in the build log. Disable the checkbox to skip testing. **Cancel test** stops the test and restores the original settings. Saves and caches are not deleted.

## Create your Windows game files

1. Extract the whole ZIP and open **PortForge.exe**.
2. Use **Browse** beside **Game file** to select your Switch game. Use **Folder** if the game is already extracted.
3. Enter the **Game name** you want to use.
4. Choose a **Save folder**.
5. Select **Start conversion**.

The panel on the right tells you what is happening:

- **Ready:** choose your files and start when you are ready.
- **Working:** the game is being read, converted or saved.
- **Done:** your Windows files have been created. Use **Open game folder** to find the executable.
- **Error:** the conversion could not finish. Read the message in the panel for the next step.
- **Cancelled:** the operation stopped without publishing a completed game folder.

Use **Cancel** to stop a running conversion. Wait for the Cancelled message before starting again.

Keep all the app's files together, including `PortForge.exe.config`. This update fixes the long-folder-path error seen while saving large games. It requires Windows with .NET Framework 4.8.

## Clear or change a choice

Every input has a **Clear** button beside it. It clears just that box and leaves your other choices alone. Browse buttons let you replace a choice directly.

## Extra settings

Open **Extra settings** if you need to choose the conversion engine or key files.

- **Conversion engine:** select `scarletnx.exe` from your extracted ScarletNX installation. On this PC, the verified installation should be selected automatically.
- **prod.keys file:** encrypted game files need this locally supplied key file. A **Key file needed** message means the engine could not find it. Choose it using the message's button or the Browse button in Extra settings, then start again.
- **title.keys file:** select it if the engine says it is needed.
- **Try a test file:** selects a small built-in test of the conversion process. It is not a playable game.

## Details and results

The right-hand technical details are available when you need more information. **Open full log** opens the saved build log; **Copy details** copies the displayed text; **Clear details** clears that text while keeping the main result visible. Clearing inputs or details never deletes your files or saved logs.

If the game code finished compiling but the final save failed, **Open saved work** opens the preserved compiler output. Temporary-file cleanup warnings are recorded in the log and do not turn a completed conversion into an error.

Run the generated game executable from its output folder and keep all its accompanying files together.

Conversion is experimental: some games may not convert or run. A Done message means files were created, not that gameplay has been verified. See DEVELOPING.md for the backend's current evidence and limitations.

## Updating PortForge

Select **Check for updates** at the top of the window. PortForge also checks quietly when it opens and changes the button to "Update available" when a newer version exists.

1. Select **Install update**.
2. PortForge downloads the new version, verifies it, closes, replaces its program files and opens again.
3. Your converted games, settings, downloads and saves are not touched. The previous program is kept in the `update-backup` folder, and `update.log` records what happened.

Updates cannot be installed while a conversion is running. If something goes wrong, PortForge keeps the version you have and explains why.

## Smoother gameplay: what the app can and cannot fix

**Auto tune** plays the first minute of the game with scripted button presses (A) and measures real gameplay and loading, instead of only the title screen. Before it starts, your save folder, log and settings are backed up, and they are restored when it ends, even if you cancel.

**Automatic fixes** reads the game's log and tells two kinds of stutter apart:

- **Shader/pipeline compile stalls** (new shaders built while you play). The repair gives the engine more pipeline threads and pre-builds the shader cache by playing the opening of the game once. It helps the scenes the test reaches. Areas you have not visited can still compile once.
- **Loading freezes** (the game creates many textures or render targets at a scene change). These are not a setting. The app reports them, with the size and worst frame, instead of pretending to fix them. Keep the game on an SSD and exclude its folder from antivirus scanning.

On the test PC (Ryzen 7 9800X3D, RX 9070 XT), Super Mario Odyssey compiled shaders in a few milliseconds even with an empty cache, and gameplay at 150% render scale ran as fast as at 75%. Its only slow frames were four short loading freezes at scene changes. Settings did not change them.

## AI Doctor

The bar at the bottom of the main window is the **AI Doctor**. It watches every game you converted or opened in Graphics and FPS, as long as PortForge is open.

- **While the game runs**, it reads the game's log and tells you (bottom bar and a Windows notification) about shader stutter, loading freezes, graphics memory running out, crashes, and slowdowns with no game work. For those slowdowns it also checks which other programs were using your CPU, and whether the game was the active window.
- **It never changes a running game.** Changing the engine while it runs could break the game or your save.
- **When the game closes**, with **Fix automatically** on, it applies the supported repairs: more pipeline threads and a shader-cache warm-up for stutter, and the settings fixes. Your save, log and settings are backed up first. With **Use AI (Codex)** on, it then asks the Codex AI installed on this PC to review a short summary of the session. Codex can only choose from the supported repairs, and every choice is checked again before it is applied.
- Select **AI Doctor** to see everything it noticed and did. It also writes `PortForge-doctor.log` in the game's folder.

Some problems need a change inside the game engine (for example, loading freezes). The doctor reports these instead of pretending to fix them.

## Engine tab: the PortForge Engine

Converted games run on an engine made for emulation. When a game needs a new shader, that engine freezes the game until the shader is built. On a first visit to an area, that can mean freezes of several seconds.

The **PortForge Engine** is PortForge's own version of that engine, built to remove that stutter:

1. Open **Graphics and FPS**, load your game, and open the **Engine** tab.
2. Select **Install PortForge Engine**. The original engine is kept, and PortForge checks that the game still starts. If it doesn't, the original goes back automatically.
3. **Smart async shaders** is on. Instead of freezing, the game skips a not-yet-ready object for one frame in the 3D world. Menus and loading screens still wait, so nothing goes missing for good. Turn it off if something looks wrong.
4. **Measure the stutter fix** plays the same scene twice from an empty shader cache, once each way, and shows the difference. Your own cache, save and settings are put back.
5. **Restore original engine** puts the original back at any time.

**AI Engine Lab** (needs the engine source, MSYS2 and Codex on the PC): the AI writes a change to the engine's code for your game's recorded problems. PortForge builds it, measures it on your game, and keeps it only if stutter measurably drops with no new errors. Otherwise it is thrown away.

Loading freezes and game-specific engine problems can remain. The log records stage timings to help identify the cause.

**Background shader translation** in the Engine tab is experimental and off by default. It moves first-time shader translation to workers, using a copy of the shader and its constant buffers. Some draws can appear late or incorrectly; turn it off for that game if this happens. It requires Smart async shaders. This option cannot remove all stutter or make every game compatible.

Automatic checkups run on background workers. The settings window refreshes its findings only when they change, without locking inputs, replacing unsaved selections or changing the main status card. The main AI Doctor still performs supported automatic repairs after a game closes. Manual scans and tests temporarily lock inputs while keeping the text readable.

## Installing PortForge

Run **PortForge-Setup.exe**. It installs PortForge in `%LocalAppData%\Programs\PortForge` (or a folder you pick), adds a Start menu shortcut and, if you tick it, a desktop shortcut with the PortForge logo, and lists PortForge under Windows **Settings > Apps > Installed apps**. It needs no administrator rights. Games you convert go to `Documents\PortForge Games` and downloads to `Downloads\PortForge`, outside the program folder.

To remove it, use **Installed apps > PortForge > Uninstall**. It removes the program, the shortcuts and the Windows entry, and keeps your converted games and downloads unless you ask for them to be deleted too. **Check for updates** inside the app updates an installed copy in place.

Silent install for IT use: `PortForge-Setup.exe /S /D=C:\Tools\PortForge /NODESKTOP /NOLAUNCH`.

## Automatic settings for your PC

The main window shows **Your PC** when PortForge opens: the CPU, available CPU threads, Windows graphics adapters, dedicated graphics memory and RAM. Detection runs in the background. Finding a Vulkan loader is not proof that a GPU can run a particular game; the runtime checks that when it starts.

**Match new games to this PC** is on by default. After conversion, it saves supported graphics settings and shader/JIT worker counts for the computer doing the conversion, and verifies what was saved. Automatic matching starts at no more than 100% scale (75% for entry hardware). It keeps FPS limits, VSync, controllers and experimental shader choices. It works with supported local inputs and with games handed over from Downloads. No AI account or hardware upload is required.

**Measure game settings** is also on by default. The app warms the cache, then compares the baseline with lower resolutions on this PC. It prefers measured FPS and fewer/shorter freezes before image quality, while tolerating small timing differences. Every sample starts from the same protected save. Scripted button presses may remain in a menu in some games; later areas can behave differently.

The completed game folder contains **PortForge-PC-settings.txt**, identifying the hardware, applied settings and whether measurement succeeded. If a benchmark cannot finish, the hardware baseline is kept and the completion status explains the limitation. Switching measurement off still applies the baseline; switching matching off leaves the old manual tuning path available. Converted games still use the ScarletNX AOT/JIT runtime. Hardware matching cannot guarantee zero lag, compatibility with every game or native-only execution.

## Auto fix everything

On the **Engine** tab, **Auto fix everything** works through one game in four steps: it puts the PortForge Engine under the game, applies every repair the app knows (each with a backup), matches the engine's shader workers to this PC, and plays the game for 20 seconds to check it works. If the engine makes the game fail that check, the original engine is put back. The report lists what was fixed, what was already fine, what the check measured, and what no setting can change. Close the game first.
