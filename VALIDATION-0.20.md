# PortForge 0.20 validation

Automatic cloud saving uses a shared connection, configured once per PC with Connect all games. Existing known/discovered converted games, completed conversions and games loaded later join automatically. A selected game is not required to configure the connection. No upload occurs before the player supplies a cloud account. The preference for automatic mode is on by default, pending that setup.

Each game retains a separate cloud key, sync baseline, conflict state and save history. A per-game off switch creates an exclusion that discovery respects. The global switch pauses and resumes automatic records while preserving baselines and copies. Connection changes update automatic records together and reset their baselines so an existing save at the new destination requires comparison. Existing enabled per-game records on the same connection are adopted with their cloud names preserved. Individual settings on another connection remain separate.

The monitor runs on a worker timer, enrolls known games and checks enabled saves. Conversion completion and game-settings loading also register games. The main app's game discovery supplies the monitor with known converted games. Network work stays off the UI thread, and settings controls retain their normal colors while busy. Status text changes only when necessary. The automatic connection and game records use one atomic settings write protected by the existing cross-process settings mutex. Credentials remain encrypted with Windows DPAPI.

## Checks

319 application checks passed: 269 existing conversion, interface, optimization, download, diagnostics, update, doctor and engine-management checks, plus 50 cloud-save checks. The full suite passed with 46 cloud checks; after the final changes preserving automatic membership for custom save names and reusing an existing unambiguous cloud connection, the app was rebuilt and all 50 cloud checks passed against that build. The cloud window was rendered and checked visually.

Tests cover connection setup without a selected game; automatic enrollment of current and future games; persistence across service restart; actual background upload without a per-game click; automatic restore to another copy of the same game; global pause/resume; explicit exclusions surviving repeated discovery; migration of existing cloud names; custom aliases; rejection of unsupported executables; and conflicts discovered in the background. Existing corruption, missing-archive, path traversal, authentication, cancellation, per-game switching, protected-test and process-running checks still pass. Transfers use authenticated loopback WebDAV servers; the OneDrive transport uses isolated local folders.

All 10 installer checks passed in a redirected temporary installation: app and engine files, icon, shortcuts, Windows registration, removal and preservation of converted games. Engine 1.3 is unchanged. No real user game was enabled or uploaded during testing.

## Limits

Players need their own cloud account and must connect it once on each PC. No live OneDrive or Nextcloud account was connected for validation, and no hosted PortForge storage is included. OneDrive uploads are queued locally and confirmed by Windows OneDrive; WebDAV round trips are verified directly. The user still needs to wait for OneDrive to finish syncing before switching PCs.

Automatic enrollment covers converted games the app knows about, new conversions, loaded games and games found in its normal discovery folders. Arbitrary game folders can be loaded manually once to make the app aware of them. It does not synchronize unrelated PC games or unknown emulator save formats. The application must remain open; synchronization waits for the game or protected test to close. Direct executable launches bypass the pre-launch sync check. Errors retain offline progress, and divergent progress requires the existing explicit conflict choice. Save limits remain 256 MiB, 10,000 entries and 512 retained revisions.
