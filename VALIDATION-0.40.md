# PortForge 0.40 / Engine 1.23

## Fix: in-app update failed on Source/.git

PortForge 0.38/0.39 users who used **Check for updates** against the 0.39 Windows-and-Source ZIP saw:

> Could not find a part of the path `...\Source\engine\src\.git\objects\info\`. Your current version was not changed.

### Cause

1. Release packaging for 0.36–0.39 copied `engine/src` into `Source/` and included a partial nested `.git` (empty `objects\info\`).
2. `ZipFile.CreateFromDirectory` wrote that empty folder as a ZIP directory entry ending in `\`.
3. `AppUpdates.Extract` only treated entries ending in `/` as directories, then called `ExtractToFile` on `...\info\`, which throws. The update aborted before swapping `PortForge.exe` / `Engine`.

`PortForge-Setup-*.exe` was not affected: the installer payload already excludes `Source/`.

### Changes

- **AppUpdates:** skip `.git`, `.svn`, `.hg`, `.cache`, `.claude`, and the whole `Source/` tree when unpacking an update. Treat both `/` and `\` as directory markers. Install still copies only the program files, `NativeTest`, and `Engine` (Source was never installed into the app folder).
- **build-release.ps1:** stages release ZIPs with robocopy `/XD .git .cache build ...`, then asserts the staged Source and the final ZIP contain no `.git` paths.
- **Engine** unchanged (still 1.23). Existing games do not need a Doctor engine reinstall for this app-only fix.

### Update path for 0.38 users

Install **0.40** (or newer) via in-app update or Setup. The 0.40 ZIP has no `.git` entries, so 0.38's extractor succeeds. Once on 0.40+, future packages cannot break updates the same way even if packaging regresses.
