# Copilot Instructions for TopToolbar

## Project Overview
TopToolbar is a WinUI 3 desktop application that provides a customizable toolbar at the top of the screen.

## App Data Location
All application data is stored in `%LocalAppData%\TopToolbar\` (typically `C:\Users\<username>\AppData\Local\TopToolbar\`).

### Directory Structure
| Path | Description |
|------|-------------|
| `%LocalAppData%\TopToolbar\` | Root directory |
| `%LocalAppData%\TopToolbar\toolbar.config.json` | Main toolbar configuration file |
| `%LocalAppData%\TopToolbar\Logs\` | Application log files |
| `%LocalAppData%\TopToolbar\Profiles\` | User profiles (e.g., `profiles.json`) |
| `%LocalAppData%\TopToolbar\Providers\` | Provider configurations |
| `%LocalAppData%\TopToolbar\config\` | Additional config directory |
| `%LocalAppData%\TopToolbar\config\workspaces.json` | Workspace definitions (applications, positions, monitors) |
| `%LocalAppData%\TopToolbar\config\providers\` | Provider definition files |
| `%LocalAppData%\TopToolbar\icons\` | Custom icon assets |

### Key Configuration Files
- **toolbar.config.json**: Main configuration for toolbar groups and buttons
- **Providers/WorkspaceProvider.json**: Workspace button configurations (icon, name, enabled state)
- **config/workspaces.json**: Workspace definitions with application list, window positions, and monitor configurations

## Code Reference
See `AppPaths.cs` for all path definitions.
