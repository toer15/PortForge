# PortForge 0.6

## Download a game file and continue to conversion

1. Select **Downloads** at the top of PortForge.
2. Paste a **Direct download link** to an `.nro`, `.nsp`, `.xci` or `.zip` file from your chosen source.
3. Choose **Save as**. If the link ends in a supported filename, the initial filename is filled in for you.
4. If the source supplies a **SHA-256**, paste it in the optional checksum box. This lets PortForge check that the downloaded bytes match the source's expected file.
5. Select **Download / Resume**.

The status panel shows progress, completion, pauses and errors. The speed display shows transfer speed and downloaded size. Every text input has a **Clear** button. Clearing fields does not delete files.

The download manager automatically:

- Uses up to four simultaneous connections when the source supports safe byte ranges, and falls back to a full single transfer if ranges are refused.
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

- **FPS limit:** choose Game speed, a Custom limit from 10 to 1,000 FPS, or Unlimited. Custom enables the number box beside the choice. The limit is a maximum, not a promise of that frame rate. Higher FPS can speed up a game's logic or affect physics. Some games need a game-specific timing patch to work correctly above their original limit.
- **VSync:** reduces tearing, but can cap presentation at your display's refresh rate. Turn it off when deliberately testing higher FPS.
- **Render scale:** 100% keeps the game's original 3D resolution. 75% reduces GPU work; 150% improves sharpness with more GPU work. Supported range: 25% to 400%.
- **Texture detail at angles:** higher filtering values can sharpen floors and surfaces. **Draw shadows**, **TV mode** and **Start fullscreen** offer further control.
- **Performance**, **Balanced** and **Quality** are starting points. They preserve your chosen FPS, VSync and display mode. **Clear** beside a settings control resets that control to its default. **Reset defaults** resets the graphics controls together.

Select **Save settings**, then launch the game. Settings live beside its executable, so launching that executable directly also uses them. A dated backup of the previous INI is kept beside it. Close the game before saving settings or testing; PortForge does not close your running game.

**Auto tune** warms the startup scene for eight seconds, then runs four 20-second tests: your current graphics, Performance, Balanced and Quality. It uses temporary graphics and pacing overrides, records rendered frames, and recommends the highest tested detail close to the requested target or measured ceiling, at or below your chosen render scale. Set a higher render scale first if you want to test higher detail. Your selected FPS and VSync remain unchanged. For an existing game, select **Save settings** to apply the recommendation. **Open test report** shows the measurements and runtime diagnostics.

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
