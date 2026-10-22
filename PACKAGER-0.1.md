# PortForge 0.1 (legacy packager; does not meet the native-conversion requirement)

A working Windows prototype for turning an already compatible console-game setup into a one-click desktop app.

**This release packages an existing emulator/runtime. It does not recompile a console game into native PC game code.** Switch and PS5 are custom runtime profiles, not compatibility guarantees. Your game must already run in the runtime you supply.

## Try it

1. Extract the entire ZIP into a writable folder on Windows 10 or 11.
2. Open **PortForge.exe**. No installer or admin access is needed. Uses Windows .NET Framework 4.x; it was built with the compiler installed on the development PC.
3. Click **Load packaging demo**, then **Build Windows app**.
4. Click **Open export** and run **Orbit Playground.exe**. Move with WASD or arrow keys; Esc exits.

The included original demo tests packaging and launching. It is a small Windows program, not an emulator or a console game.

## Package your own setup

1. Choose a game name and runtime profile.
2. Select your configured emulator/runtime `.exe`. For a copied export, put it in its own folder with its required DLLs and other dependencies first.
3. Choose the game file or its complete directory. For games with companion assets, choose the directory and adapt the launch arguments to point to its entry file, for example `{game}\boot.elf`.
4. Enter launch arguments as **one argument per line**. Do not add quotes around paths; PortForge adds the correct Windows escaping. Consult that runtime's documentation. The Dolphin preset supplies `-b`, `-e`, and `{game}` on separate lines.
5. Choose an export folder outside the runtime and game folders. **Check package** validates files and reports the copy size; it does not launch the game or verify gameplay compatibility.
6. Build the app. Keep the resulting `.exe`, `portforge.xml`, and copied subfolders together.

Available placeholders: `{game}` is the selected file or folder, `{gameDir}` its directory (or itself for folder input), `{runtime}` the runtime executable's directory, and `{package}` the export directory. Placeholders may appear within an argument. Other literal arguments are supported.

With **Copy runtime folder + game files** checked, the entire selected runtime directory and selected game input are copied into the package. Paths in the manifest are relative so the package can move. If unchecked, PortForge creates a smaller linked launcher that depends on the original paths on that PC.

Copied files alone do not guarantee a self-contained game: runtime settings stored elsewhere, firmware, keys, installed redistributables, graphics drivers and saves are not collected automatically. Configure the runtime's own portable mode if required. Input folders containing links or junctions are rejected. Existing exports are never overwritten. Cancellation removes the unfinished temporary export.

Game launches use a direct Windows process, not a shell. The launcher sets the runtime's working directory, waits for it, returns its exit code and records logs under `%LOCALAPPDATA%\PortForge\Logs`. The emulator controls its own saves, settings, controller support, display mode and performance. New game-specific icons, shortcuts and installers are future work.

No commercial games, emulators, console firmware or keys are included or downloaded. Use files you have permission to use; check each runtime's license before sharing a package.

## Build and test from source

Run from Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-packager.ps1 -Test
```

Uses the installed Windows .NET Framework C# compiler and built-in assemblies, with no NuGet packages. Output: `build\PortForge\PortForge.exe`. The builder embeds the prebuilt game launcher, so end users do not need a compiler.

The integration tests build and run an instrumented local runtime through real exported executables, verify process arguments and exit codes, relocate a package, and exercise validation, cancellation and XML parsing. No console-game compatibility has been tested for this prototype.

## Research behind the next stage

Native recompilation is a separate backend. [N64Recomp](https://github.com/N64Recomp/N64Recomp) translates N64 binaries to C and needs metadata plus a runtime. [XenonRecomp](https://github.com/hedge-dev/XenonRecomp) translates Xbox 360 code to C++ and explicitly requires a runtime implementation. These are useful building blocks, not universal conversion engines. The [KytyPS5 project](https://github.com/KytyPS5/KytyPS5) documents a PS5 translation layer whose compatibility depends on the game and runtime build.

See ROADMAP.md for the proposed path to native conversion.
