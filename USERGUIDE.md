# PortForge 0.22

## Game speed lock

PortForge Engine 1.6 has a **Speed lock**, on by default. Most games count their frames, so on earlier engines an FPS cap above the game's own pace (for example 120 on a 60 FPS game), Unlimited or F4 made the game itself run faster, and a cap below it (for example 30 on a 60 FPS game) made it run in slow motion. With the speed lock on, the engine always runs the game at the pace it asks for. A 30 FPS game stays at its speed, and so does a 60 FPS game, whatever the cap. A lower cap shows fewer of the game's frames, the way a console game drops frames, instead of slowing it down. (Engine 1.5 in PortForge 0.21 covered only higher caps.) A game that times itself (swap interval 0) is still limited only by the cap you set.

Your PC's power goes into picture quality instead of speed. PC matching and automatic tuning pick the highest render scale and texture filtering that hold the game's pace on your hardware. New conversions, watched games that receive Engine 1.6, PC matching and **Auto fix everything** all keep the lock on. If it is ever switched off, the Doctor reports it and its automatic repair turns it back on. Advanced users can set `speed_lock=0` in the game's .ini to go back to the old behavior.

## Automatic engine performance protection

The bundled PortForge Engine 1.6 has **Frame guard** on by default. New conversions use it automatically, including conversions handed over from Downloads. The setting works inside the generated game's runtime when the game is launched directly.

Frame guard shares one short optional shader-wait allowance across a frame, instead of waiting again for each new pipeline. It also keeps cached background compilation from taking every shader worker. Draws needed to preserve menus or game data still complete. In **Graphics and FPS > Engine**, **Automatic frame guard** can be turned off for a compatibility comparison; it applies at the next launch. Smart async shaders and threaded command recording must remain enabled for optional draw waits to be avoided.

New games matched by this engine use automatic shader/JIT worker counts on every launch. Counts follow the CPUs available on the PC running the game. Explicit manual thread counts remain available. The engine also resets its pacing deadline when an FPS limit or native game interval changes, avoiding a pause from the previous pace. It preserves the selected game speed, FPS cap and VSync setting.

The Doctor installs a newer bundled engine under watched converted games when they are closed, with the existing backup and startup check. **Auto fix everything** also selects the new engine's automatic shader workers. A game restored manually to its original engine remains an exception until you choose to install again.

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
