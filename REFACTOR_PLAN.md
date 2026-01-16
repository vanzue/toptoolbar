# Refactor Plan

Goals
- Reduce large files and separate responsibilities.
- Keep behavior the same while improving structure.

Sequence
- [x] Split `ViewModels/SettingsViewModel.cs` into partial files (core, groups, workspaces, icons, startup).
- [x] Split `TopToolbarXAML/SettingsWindow.xaml.cs` into partial files and reduce code-behind noise.
- [x] Split `TopToolbarXAML/ToolbarWindow.xaml.cs` into partial files by concern.
- [x] Break up `Services/Workspaces/WorkspacesRuntimeService*.cs` and `Services/Workspaces/NativeWindowHelper.cs` into smaller helpers.

Notes
- No feature changes during refactor.
- Keep file sizes small and cohesive.
