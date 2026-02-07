# TopToolbar Agent Notes

## Logs
- Log root: `%LOCALAPPDATA%\TopToolbar\Logs` (from `AppPaths.Logs`).
- Logs are written into versioned subfolders under the root (see `Logging/AppLogger.cs`).
- After finishing coding tasks, **clear the log folder contents** so the next test run starts clean.

## Workspace configuration
- Toolbar config (groups/buttons): `%LOCALAPPDATA%\TopToolbar\toolbar.config.json` (`AppPaths.ConfigFile`).
- Workspace definitions: `%LOCALAPPDATA%\TopToolbar\config\workspaces.json` (`WorkspaceStoragePaths.GetWorkspaceDefinitionsPath`).
- Workspace provider config/buttons: `%LOCALAPPDATA%\TopToolbar\Providers\WorkspaceProvider.json` (`WorkspaceStoragePaths.GetProviderConfigPath`).
- Legacy (PowerToys) workspaces path (migration source): `%LOCALAPPDATA%\Microsoft\PowerToys\Workspaces\workspaces.json`.

## Workspace workflow (snapshot/launch)
- Snapshot:
  - UI triggers `WorkspaceProvider.SnapshotAsync` → `WorkspacesRuntimeService` → `WorkspaceSnapshotter`.
  - Captures current window/monitor state, filters excluded windows, resolves `ApplicationFrameHost` paths, then writes `workspaces.json`.
  - Binds current window handles to app IDs in `ManagedWindowRegistry` for reuse on launch.
- Launch:
  - `WorkspaceProvider` invokes `WorkspacesRuntimeService.LaunchWorkspaceAsync` → `WorkspaceLauncher`.
  - Phase 1: reuse existing windows, then launch missing apps.
  - Phase 2: resize/arrange windows.
  - Phase 3: minimize extraneous windows.
  - Matching uses AUMID/package/PWA/process/title; excludes windows on other virtual desktops during launch assignment.

## Build
- Build using **arm64**. Prefer:
  - `dotnet build .\TopToolbar.slnx -c Debug -r win-arm64 -p:Platform=arm64`
- Before building, kill `TopToolbar.exe` if it is running.

## Design/behavior considerations
- Always consider **virtual desktops** and **multi-monitor** behavior when making changes.
