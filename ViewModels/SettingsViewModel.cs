// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.UI.Dispatching;
using TopToolbar.Logging;
using TopToolbar.Models;
using TopToolbar.Models.Providers;
using TopToolbar.Serialization;
using TopToolbar.Services;
using TopToolbar.Services.Providers;
using TopToolbar.Services.Workspaces;
using Windows.ApplicationModel;

namespace TopToolbar.ViewModels
{
    public class SettingsViewModel : ObservableObject, System.IDisposable
    {
        private readonly ToolbarConfigService _service;
        private readonly WorkspaceProviderConfigStore _workspaceStore = new();
        private readonly Timer _saveDebounce = new(300) { AutoReset = false };
        private Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
        private bool _suppressWorkspaceSave;

        public ObservableCollection<ButtonGroup> Groups { get; } = new();

        public ObservableCollection<WorkspaceButtonViewModel> WorkspaceButtons { get; } = new();

        private ButtonGroup _selectedGroup;

        public ButtonGroup SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                SetProperty(ref _selectedGroup, value);
                OnPropertyChanged(nameof(HasSelectedGroup));
                OnPropertyChanged(nameof(HasNoSelectedGroup));
                if (value != null)
                {
                    // Deselect General when a group is selected
                    _isGeneralSelected = false;
                    OnPropertyChanged(nameof(IsGeneralSelected));
                }
                // Must update IsGroupSelected after IsGeneralSelected is updated
                OnPropertyChanged(nameof(IsGroupSelected));
            }
        }

        private WorkspaceButtonViewModel _selectedWorkspace;

        public WorkspaceButtonViewModel SelectedWorkspace
        {
            get => _selectedWorkspace;
            set => SetProperty(ref _selectedWorkspace, value);
        }

        private ToolbarButton _selectedButton;

        public ToolbarButton SelectedButton
        {
            get => _selectedButton;
            set
            {
                SetProperty(ref _selectedButton, value);
                OnPropertyChanged(nameof(HasSelectedButton));
            }
        }

        public bool HasSelectedGroup => SelectedGroup != null;

        public bool HasNoSelectedGroup => SelectedGroup == null;

        public bool HasSelectedButton => SelectedButton != null;

        private bool _isGeneralSelected = true;

        public bool IsGeneralSelected
        {
            get => _isGeneralSelected;
            set
            {
                if (_isGeneralSelected != value)
                {
                    _isGeneralSelected = value;
                    OnPropertyChanged(nameof(IsGeneralSelected));
                    OnPropertyChanged(nameof(IsGroupSelected));
                    if (value)
                    {
                        // Deselect group when General is selected
                        SelectedGroup = null;
                    }
                }
            }
        }

        public bool IsGroupSelected => !IsGeneralSelected && SelectedGroup != null;

        private bool _isStartupEnabled;

        public bool IsStartupEnabled
        {
            get => _isStartupEnabled;
            set => SetProperty(ref _isStartupEnabled, value);
        }

        private bool _isStartupAvailable = true;

        public bool IsStartupAvailable
        {
            get => _isStartupAvailable;
            set => SetProperty(ref _isStartupAvailable, value);
        }

        private string _startupStatusText = string.Empty;

        public string StartupStatusText
        {
            get => _startupStatusText;
            set => SetProperty(ref _startupStatusText, value);
        }

        public SettingsViewModel(ToolbarConfigService service)
        {
            _service = service;
            _saveDebounce.Elapsed += async (s, e) =>
            {
                await SaveAsync();
            };

            Groups.CollectionChanged += Groups_CollectionChanged;
            WorkspaceButtons.CollectionChanged += WorkspaceButtons_CollectionChanged;
        }

        public async Task LoadAsync(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
            var toolbarConfig = await _service.LoadAsync();
            var workspaceConfig = await _workspaceStore.LoadAsync();

            void Apply()
            {
                Groups.Clear();
                foreach (var g in toolbarConfig.Groups)
                {
                    Groups.Add(g);
                    HookGroup(g);
                }

                if (SelectedGroup == null && Groups.Count > 0)
                {
                    SelectedGroup = Groups[0];
                    SelectedButton = SelectedGroup.Buttons.FirstOrDefault();
                }

                _suppressWorkspaceSave = true;
                try
                {
                    LoadWorkspaceButtons(workspaceConfig);
                }
                finally
                {
                    _suppressWorkspaceSave = false;
                }
            }

            if (dispatcher.HasThreadAccess)
            {
                Apply();
            }
            else
            {
                var tcs = new TaskCompletionSource();
                dispatcher.TryEnqueue(() =>
                {
                    Apply();
                    tcs.SetResult();
                });
                await tcs.Task;
            }
        }

        public async Task SaveAsync()
        {
            // Ensure we mutate bound properties on UI thread
            if (_dispatcher != null && !_dispatcher.HasThreadAccess)
            {
                var tcs = new TaskCompletionSource();
                _dispatcher.TryEnqueue(async () =>
                {
                    await SaveCoreAsync();
                    tcs.SetResult();
                });
                await tcs.Task;
                return;
            }

            await SaveCoreAsync();
        }

        private async Task SaveCoreAsync()
        {
            // Ensure exe icons extracted before save (robustness if user hit Save quickly)
            AppLogger.LogInfo("SaveCoreAsync: begin icon extraction sweep");
            foreach (var g in Groups)
            {
                foreach (var b in g.Buttons)
                {
                    TryUpdateIconFromCommand(b);
                }
            }

            // Create config with only valid buttons (non-empty command)
            // Don't modify the UI Groups collection - just filter for saving
            // Preserve group Id to avoid duplication on reload
            var cfg = new ToolbarConfig
            {
                Groups = Groups.Select(g => new ButtonGroup
                {
                    Id = g.Id,
                    Name = g.Name,
                    Description = g.Description,
                    IsEnabled = g.IsEnabled,
                    Layout = g.Layout,
                    Providers = g.Providers,
                    StaticActions = g.StaticActions,
                    Filter = g.Filter,
                    AutoRefresh = g.AutoRefresh,
                    Buttons = new System.Collections.ObjectModel.ObservableCollection<ToolbarButton>(
                        g.Buttons.Where(b => !string.IsNullOrWhiteSpace(b.Action?.Command))),
                }).ToList(),
            };

            await _service.SaveAsync(cfg);
            await SaveWorkspaceConfigAsync();
            AppLogger.LogInfo("SaveCoreAsync: configs saved");
        }

        private void ScheduleSave()
        {
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        private void Groups_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.Cast<ButtonGroup>())
                {
                    HookGroup(item);
                }
            }

            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.Cast<ButtonGroup>())
                {
                    UnhookGroup(item);
                }
            }

            ScheduleSave();
        }

        private void WorkspaceButtons_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.Cast<WorkspaceButtonViewModel>())
                {
                    HookWorkspaceButton(item);
                }
            }

            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.Cast<WorkspaceButtonViewModel>())
                {
                    UnhookWorkspaceButton(item);
                }
            }

            if (_suppressWorkspaceSave)
            {
                return;
            }

            ScheduleSave();
        }

        private void LoadWorkspaceButtons(WorkspaceProviderConfig config)
        {
            foreach (var existing in WorkspaceButtons.ToList())
            {
                UnhookWorkspaceButton(existing);
            }

            WorkspaceButtons.Clear();

            if (config == null)
            {
                SelectedWorkspace = null;
                return;
            }

            config.Data ??= new WorkspaceProviderData();
            config.Data.Workspaces ??= new System.Collections.Generic.List<WorkspaceDefinition>();
            config.Buttons ??= new System.Collections.Generic.List<WorkspaceButtonConfig>();

            var definitionLookup = config.Data.Workspaces
                .Where(ws => ws != null && !string.IsNullOrWhiteSpace(ws.Id))
                .ToDictionary(ws => ws.Id.Trim(), ws => ws, StringComparer.OrdinalIgnoreCase);

            var orderedButtons = (config.Buttons.Count > 0)
                ? config.Buttons
                    .Where(button => button != null)
                    .OrderBy(button => button.SortOrder ?? double.MaxValue)
                    .ThenBy(button => button.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : definitionLookup.Values
                    .Select(definition => new WorkspaceButtonConfig
                    {
                        Id = BuildWorkspaceButtonId(definition.Id),
                        WorkspaceId = definition.Id,
                        Name = string.IsNullOrWhiteSpace(definition.Name) ? definition.Id : definition.Name,
                        Description = definition.Id,
                        Enabled = true,
                        Icon = new ProviderIcon { Type = ProviderIconType.Glyph, Glyph = "\uE7F4" },
                    })
                    .ToList();

            foreach (var buttonConfig in orderedButtons)
            {
                if (buttonConfig == null)
                {
                    continue;
                }

                var workspaceId = !string.IsNullOrWhiteSpace(buttonConfig.WorkspaceId)
                    ? buttonConfig.WorkspaceId.Trim()
                    : ExtractWorkspaceId(buttonConfig.Id);

                if (string.IsNullOrWhiteSpace(workspaceId))
                {
                    continue;
                }

                if (!definitionLookup.TryGetValue(workspaceId, out var definition))
                {
                    definition = new WorkspaceDefinition
                    {
                        Id = workspaceId,
                        Name = buttonConfig.Name ?? workspaceId,
                        CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        Applications = new System.Collections.Generic.List<ApplicationDefinition>(),
                        Monitors = new System.Collections.Generic.List<MonitorDefinition>(),
                    };
                    definitionLookup[workspaceId] = definition;
                }

                var viewModel = new WorkspaceButtonViewModel(buttonConfig, definition);
                WorkspaceButtons.Add(viewModel);
            }

            SelectedWorkspace = WorkspaceButtons.FirstOrDefault();
        }

        private async Task SaveWorkspaceConfigAsync()
        {
            var config = new WorkspaceProviderConfig
            {
                SchemaVersion = 1,
                ProviderId = "WorkspaceProvider",
                DisplayName = "Workspaces",
                Description = "Snapshot and restore desktop layouts",
                Author = "Microsoft",
                Version = "1.0.0",
                Enabled = true,
                Buttons = new System.Collections.Generic.List<WorkspaceButtonConfig>(),
                Data = new WorkspaceProviderData { Workspaces = new System.Collections.Generic.List<WorkspaceDefinition>() },
            };

            foreach (var workspace in WorkspaceButtons)
            {
                var buttonConfig = new WorkspaceButtonConfig
                {
                    Id = string.IsNullOrWhiteSpace(workspace.Config.Id) ? BuildWorkspaceButtonId(workspace.WorkspaceId) : workspace.Config.Id,
                    WorkspaceId = workspace.WorkspaceId,
                    Name = workspace.Name ?? string.Empty,
                    Description = workspace.Description ?? string.Empty,
                    Enabled = workspace.Enabled,
                    SortOrder = workspace.Config.SortOrder,
                    Icon = CloneIcon(workspace.Icon),
                };

                config.Buttons.Add(buttonConfig);

                var definitionClone = DeepClone(workspace.Definition) ?? new WorkspaceDefinition();
                definitionClone.Id = workspace.WorkspaceId;
                definitionClone.Name = workspace.Definition.Name ?? workspace.WorkspaceId;
                definitionClone.Applications = workspace.Apps
                    .Select(DeepClone)
                    .Where(app => app != null)
                    .ToList();
                definitionClone.Monitors = workspace.Definition.Monitors != null
                    ? workspace.Definition.Monitors.Select(DeepClone).Where(m => m != null).ToList()
                    : new System.Collections.Generic.List<MonitorDefinition>();

                config.Data.Workspaces.Add(definitionClone);
            }

            await _workspaceStore.SaveAsync(config);
        }

        private void HookWorkspaceButton(WorkspaceButtonViewModel workspace)
        {
            if (workspace == null)
            {
                return;
            }

            workspace.PropertyChanged += Workspace_PropertyChanged;
            workspace.Definition.Applications = workspace.Apps.ToList();
        }

        private void UnhookWorkspaceButton(WorkspaceButtonViewModel workspace)
        {
            if (workspace == null)
            {
                return;
            }

            workspace.PropertyChanged -= Workspace_PropertyChanged;
        }

        private void Workspace_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suppressWorkspaceSave)
            {
                return;
            }

            ScheduleSave();
        }

        public WorkspaceButtonViewModel AddWorkspace(string name)
        {
            var id = Guid.NewGuid().ToString("N");
            var displayName = string.IsNullOrWhiteSpace(name) ? "New workspace" : name.Trim();

            var definition = new WorkspaceDefinition
            {
                Id = id,
                Name = displayName,
                CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Applications = new System.Collections.Generic.List<ApplicationDefinition>(),
                Monitors = new System.Collections.Generic.List<MonitorDefinition>(),
            };

            var buttonConfig = new WorkspaceButtonConfig
            {
                Id = BuildWorkspaceButtonId(id),
                WorkspaceId = id,
                Name = displayName,
                Description = id,
                Enabled = true,
                SortOrder = WorkspaceButtons.Count + 1,
                Icon = new ProviderIcon { Type = ProviderIconType.Glyph, Glyph = "\uE7F4" },
            };

            var workspace = new WorkspaceButtonViewModel(buttonConfig, definition);
            WorkspaceButtons.Add(workspace);
            SelectedWorkspace = workspace;
            ScheduleSave();
            return workspace;
        }

        public void RemoveWorkspace(WorkspaceButtonViewModel workspace)
        {
            if (workspace == null)
            {
                return;
            }

            WorkspaceButtons.Remove(workspace);
            if (ReferenceEquals(SelectedWorkspace, workspace))
            {
                SelectedWorkspace = WorkspaceButtons.FirstOrDefault();
            }

            ScheduleSave();
        }

        public void RemoveWorkspaceApp(WorkspaceButtonViewModel workspace, ApplicationDefinition app)
        {
            if (workspace == null || app == null)
            {
                return;
            }

            workspace.RemoveApp(app);
            ScheduleSave();
        }

        public bool TrySetWorkspaceCatalogIcon(WorkspaceButtonViewModel workspace, string catalogId)
        {
            if (workspace == null || string.IsNullOrWhiteSpace(catalogId))
            {
                return false;
            }

            if (IconCatalogService.TryGetById(catalogId, out var entry))
            {
                workspace.SetCatalogIcon(entry.Id);
                ScheduleSave();
                return true;
            }

            return false;
        }

        public bool TrySetWorkspaceGlyphIcon(WorkspaceButtonViewModel workspace, string glyph)
        {
            if (workspace == null)
            {
                return false;
            }

            workspace.SetGlyph(glyph);
            ScheduleSave();
            return true;
        }

        public Task<bool> TrySetWorkspaceImageIconFromFileAsync(WorkspaceButtonViewModel workspace, string sourcePath)
        {
            if (workspace == null || string.IsNullOrWhiteSpace(sourcePath))
            {
                return Task.FromResult(false);
            }

            var targetPath = CopyIconAsset(workspace.WorkspaceId, sourcePath);
            workspace.SetImage(targetPath);
            ScheduleSave();
            return Task.FromResult(true);
        }

        public void ResetWorkspaceIcon(WorkspaceButtonViewModel workspace)
        {
            if (workspace == null)
            {
                return;
            }

            workspace.ResetToDefaultIcon();
            ScheduleSave();
        }

        private string CopyIconAsset(string assetId, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(assetId) || string.IsNullOrWhiteSpace(sourcePath))
            {
                return sourcePath ?? string.Empty;
            }

            try
            {
                var iconsDirectory = AppPaths.IconsDirectory;
                Directory.CreateDirectory(iconsDirectory);

                var extension = Path.GetExtension(sourcePath);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".png";
                }

                if (!extension.StartsWith('.'))
                {
                    extension = "." + extension;
                }

                var targetPath = Path.Combine(iconsDirectory, $"{assetId}_custom{extension}");

                if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, targetPath, true);
                }

                return targetPath;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"CopyIconAsset failed: {ex.Message}");
                return sourcePath;
            }
        }

        private static ProviderIcon CloneIcon(ProviderIcon icon)
        {
            if (icon == null)
            {
                return new ProviderIcon { Type = ProviderIconType.Glyph, Glyph = "\uE7F4" };
            }

            return new ProviderIcon
            {
                Type = icon.Type,
                Path = icon.Path ?? string.Empty,
                Glyph = icon.Glyph ?? string.Empty,
                CatalogId = icon.CatalogId ?? string.Empty,
            };
        }

        private static T DeepClone<T>(T value)
        {
            if (value == null)
            {
                return default;
            }

            // AOT-compatible deep clone for known types
            if (value is WorkspaceDefinition wd)
            {
                return (T)(object)JsonSerializer.Deserialize(
                    JsonSerializer.Serialize(wd, DeepCloneJsonContext.Default.WorkspaceDefinition),
                    DeepCloneJsonContext.Default.WorkspaceDefinition);
            }

            if (value is ApplicationDefinition ad)
            {
                return (T)(object)JsonSerializer.Deserialize(
                    JsonSerializer.Serialize(ad, DeepCloneJsonContext.Default.ApplicationDefinition),
                    DeepCloneJsonContext.Default.ApplicationDefinition);
            }

            if (value is MonitorDefinition md)
            {
                return (T)(object)JsonSerializer.Deserialize(
                    JsonSerializer.Serialize(md, DeepCloneJsonContext.Default.MonitorDefinition),
                    DeepCloneJsonContext.Default.MonitorDefinition);
            }

            // Fallback for other types (will not work with AOT but keeps existing behavior)
            throw new NotSupportedException($"DeepClone does not support type {typeof(T).Name} in AOT mode.");
        }

        private static string BuildWorkspaceButtonId(string workspaceId)
        {
            return string.IsNullOrWhiteSpace(workspaceId) ? string.Empty : $"workspace::{workspaceId}";
        }

        private static string ExtractWorkspaceId(string buttonId)
        {
            if (string.IsNullOrWhiteSpace(buttonId))
            {
                return string.Empty;
            }

            const string prefix = "workspace::";
            return buttonId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? buttonId.Substring(prefix.Length)
                : buttonId;
        }

        private void HookGroup(ButtonGroup group)
        {
            if (group == null)
            {
                return;
            }

            group.PropertyChanged += Group_PropertyChanged;
            group.Buttons.CollectionChanged += Buttons_CollectionChanged;
            foreach (var b in group.Buttons)
            {
                HookButton(b);
            }
        }

        private void UnhookGroup(ButtonGroup group)
        {
            if (group == null)
            {
                return;
            }

            group.PropertyChanged -= Group_PropertyChanged;
            group.Buttons.CollectionChanged -= Buttons_CollectionChanged;
            foreach (var b in group.Buttons)
            {
                UnhookButton(b);
            }
        }

        private void Buttons_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.Cast<ToolbarButton>())
                {
                    HookButton(item);
                }
            }

            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.Cast<ToolbarButton>())
                {
                    UnhookButton(item);
                }
                // Only save when buttons are removed, not added
                // New buttons with empty command should not trigger save
                ScheduleSave();
            }
        }

        private void Group_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ScheduleSave();
        }

        private void HookButton(ToolbarButton b)
        {
            if (b == null)
            {
                return;
            }

            b.PropertyChanged += Button_PropertyChanged;
            if (b.Action != null)
            {
                AppLogger.LogInfo($"HookButton: hooking Action.PropertyChanged for button '{b.Name}'");
                b.Action.PropertyChanged += (s, e) => OnActionPropertyChanged(b, e);
            }
            else
            {
                AppLogger.LogWarning($"HookButton: button '{b.Name}' has no Action");
            }
        }

        private void UnhookButton(ToolbarButton b)
        {
            if (b == null)
            {
                return;
            }

            b.PropertyChanged -= Button_PropertyChanged;
        }

        private void Button_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ScheduleSave();
        }

        private void OnActionPropertyChanged(ToolbarButton button, PropertyChangedEventArgs e)
        {
            AppLogger.LogInfo($"OnActionPropertyChanged: property={e.PropertyName}, button='{button?.Name}'");
            if (e.PropertyName == nameof(ToolbarAction.Command))
            {
                AppLogger.LogInfo($"OnActionPropertyChanged: Command changed to '{button?.Action?.Command}'");
                // Ensure property changes occur on UI thread
                if (_dispatcher != null && !_dispatcher.HasThreadAccess)
                {
                    _dispatcher.TryEnqueue(() =>
                    {
                        TryUpdateIconFromCommand(button);
                        ScheduleSave();
                    });
                }
                else
                {
                    TryUpdateIconFromCommand(button);
                    ScheduleSave();
                }
            }
        }

        private void TryUpdateIconFromCommand(ToolbarButton button)
        {
            var cmd = button?.Action?.Command;
            if (string.IsNullOrWhiteSpace(cmd))
            {
                return;
            }

            string path = cmd.Trim();
            path = Environment.ExpandEnvironmentVariables(path);

            // Check if icon is user-managed (not auto-managed)
            var configDirectory = Path.GetDirectoryName(_service.ConfigPath);
            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                return;
            }

            var iconsDir = Path.Combine(configDirectory, "icons");
            Directory.CreateDirectory(iconsDir);

            if (!ShouldAutoManageIcon(button, iconsDir))
            {
                AppLogger.LogDebug("Skipping auto icon because icon is user-managed.");
                return;
            }

            AppLogger.LogInfo($"TryUpdateIconFromCommand: cmd='{cmd}'");

            // Smart icon detection based on command type

            // 1. URL detection - use link/globe icon
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                SetSmartIcon(button, "glyph-E774", "\uE774"); // Globe icon
                AppLogger.LogInfo("Smart icon: URL detected, using globe icon");
                return;
            }

            // 2. mailto: detection - use mail icon
            if (path.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                SetSmartIcon(button, "glyph-E715", "\uE715"); // Mail icon
                AppLogger.LogInfo("Smart icon: mailto detected, using mail icon");
                return;
            }

            // 3. Folder detection - use folder icon
            if (Directory.Exists(path))
            {
                SetSmartIcon(button, "glyph-E188", "\uE188"); // Folder icon
                AppLogger.LogInfo("Smart icon: folder detected, using folder icon");
                return;
            }

            // 4. File detection - use icon based on extension
            if (File.Exists(path))
            {
                var ext = Path.GetExtension(path)?.ToLowerInvariant();
                
                // Document types
                if (ext == ".txt" || ext == ".log" || ext == ".md" || ext == ".json" || ext == ".xml" || ext == ".csv")
                {
                    SetSmartIcon(button, "glyph-E8A5", "\uE8A5"); // Document icon
                    AppLogger.LogInfo($"Smart icon: text file detected ({ext}), using document icon");
                    return;
                }

                // Image types
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp" || ext == ".ico" || ext == ".svg")
                {
                    SetSmartIcon(button, "glyph-EB9F", "\uEB9F"); // Photo icon
                    AppLogger.LogInfo($"Smart icon: image file detected ({ext}), using photo icon");
                    return;
                }

                // Video types
                if (ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".wmv")
                {
                    SetSmartIcon(button, "glyph-E714", "\uE714"); // Video icon
                    AppLogger.LogInfo($"Smart icon: video file detected ({ext}), using video icon");
                    return;
                }

                // Audio types
                if (ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".m4a" || ext == ".wma")
                {
                    SetSmartIcon(button, "glyph-E8D6", "\uE8D6"); // Music icon
                    AppLogger.LogInfo($"Smart icon: audio file detected ({ext}), using music icon");
                    return;
                }

                // Code/script types
                if (ext == ".ps1" || ext == ".bat" || ext == ".cmd" || ext == ".sh")
                {
                    SetSmartIcon(button, "glyph-E756", "\uE756"); // Code/CommandPrompt icon
                    AppLogger.LogInfo($"Smart icon: script file detected ({ext}), using terminal icon");
                    return;
                }

                // Executable - try to extract icon
                if (ext == ".exe")
                {
                    var target = Path.Combine(iconsDir, button.Id + ".png");
                    if (IconExtractionService.TryExtractExeIconToPng(path, target))
                    {
                        button.IconType = ToolbarIconType.Image;
                        button.IconPath = target;
                        AppLogger.LogInfo($"Smart icon: extracted exe icon -> '{target}'");
                        return;
                    }
                }

                // Other files - use generic file icon
                SetSmartIcon(button, "glyph-E7C3", "\uE7C3"); // Page icon
                AppLogger.LogInfo($"Smart icon: file detected ({ext}), using file icon");
                return;
            }

            // 5. Try to resolve as executable name (e.g., "notepad", "code")
            if (path.StartsWith('"'))
            {
                int end = path.IndexOf('"', 1);
                if (end > 1)
                {
                    path = path.Substring(1, end - 1);
                }
            }
            else
            {
                int exeIdx = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIdx >= 0)
                {
                    path = path.Substring(0, exeIdx + 4);
                }
            }

            var workingDirectory = button?.Action?.WorkingDirectory ?? string.Empty;
            var resolved = ResolveCommandToFilePath(path, workingDirectory);
            
            if (!string.IsNullOrEmpty(resolved) && resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(resolved))
            {
                var target = Path.Combine(iconsDir, button.Id + ".png");
                if (IconExtractionService.TryExtractExeIconToPng(resolved, target))
                {
                    button.IconType = ToolbarIconType.Image;
                    button.IconPath = target;
                    AppLogger.LogInfo($"Smart icon: extracted exe icon from resolved path -> '{target}'");
                    return;
                }
            }

            // Fallback: use default app icon
            SetSmartIcon(button, "glyph-E7AC", "\uE7AC"); // App icon
            AppLogger.LogInfo("Smart icon: using default app icon");
        }

        private void SetSmartIcon(ToolbarButton button, string catalogId, string glyph)
        {
            if (button == null)
            {
                return;
            }

            var glyphHex = string.IsNullOrEmpty(glyph) ? "empty" : $"U+{(int)glyph[0]:X4}";
            AppLogger.LogInfo($"SetSmartIcon: catalogId='{catalogId}', glyph={glyphHex}");

            // Try catalog first
            if (IconCatalogService.TryGetById(catalogId, out var entry))
            {
                AppLogger.LogInfo($"SetSmartIcon: found catalog entry '{catalogId}', entryGlyph=U+{(entry.Glyph != null && entry.Glyph.Length > 0 ? ((int)entry.Glyph[0]).ToString("X4") : "null")}");
                button.IconType = ToolbarIconType.Catalog;
                button.IconPath = IconCatalogService.BuildCatalogPath(entry.Id);
                button.IconGlyph = entry.Glyph ?? glyph;
            }
            else
            {
                // Fallback to glyph directly
                AppLogger.LogInfo($"SetSmartIcon: catalog '{catalogId}' not found, using glyph fallback {glyphHex}");
                button.IconType = ToolbarIconType.Catalog;
                button.IconGlyph = glyph;
                button.IconPath = string.Empty;
            }
            var finalGlyphHex = string.IsNullOrEmpty(button.IconGlyph) ? "empty" : $"U+{(int)button.IconGlyph[0]:X4}";
            AppLogger.LogInfo($"SetSmartIcon: button '{button.Name}' final state: type={button.IconType}, glyph={finalGlyphHex}, path='{button.IconPath}'");
        }

        private static bool ShouldAutoManageIcon(ToolbarButton button, string iconsDir)
        {
            if (button == null)
            {
                return false;
            }

            // If user has customized the icon, don't auto-manage
            if (button.IsIconCustomized)
            {
                return false;
            }

            return true;
        }

        private static bool IsManagedImageIcon(ToolbarButton button, string iconsDir)
        {
            if (button == null || string.IsNullOrWhiteSpace(button.IconPath))
            {
                return true;
            }

            try
            {
                var iconFullPath = Path.GetFullPath(button.IconPath);
                var iconsFullPath = Path.GetFullPath(iconsDir);
                var expectedAutoPath = Path.Combine(iconsFullPath, button.Id + ".png");
                return string.Equals(iconFullPath, expectedAutoPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDefaultCatalogIcon(ToolbarButton button)
        {
            var defaultEntry = IconCatalogService.GetDefault();
            if (defaultEntry == null)
            {
                return false;
            }

            var currentEntry = IconCatalogService.ResolveFromPath(button.IconPath);
            if (currentEntry != null)
            {
                return string.Equals(currentEntry.Id, defaultEntry.Id, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(button.IconGlyph) && !string.IsNullOrWhiteSpace(defaultEntry.Glyph))
            {
                return string.Equals(button.IconGlyph, defaultEntry.Glyph, StringComparison.Ordinal);
            }

            return string.IsNullOrWhiteSpace(button.IconPath) && string.IsNullOrWhiteSpace(button.IconGlyph);
        }

        private static string ResolveCommandToFilePath(string file, string workingDir)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                return string.Empty;
            }

            try
            {
                var candidate = file.Trim();
                candidate = Environment.ExpandEnvironmentVariables(candidate);

                bool hasRoot = System.IO.Path.IsPathRooted(candidate);
                bool hasExt = System.IO.Path.HasExtension(candidate);

                // If absolute or contains directory, try directly and with PATHEXT if needed
                if (hasRoot || candidate.Contains('\\') || candidate.Contains('/'))
                {
                    if (System.IO.File.Exists(candidate))
                    {
                        return candidate;
                    }

                    if (!hasExt)
                    {
                        foreach (var ext in GetPathExtensions())
                        {
                            var p = candidate + ext;
                            if (System.IO.File.Exists(p))
                            {
                                return p;
                            }
                        }
                    }

                    // If a specific extension was provided but file not found, try alternate PATHEXT extensions
                    if (hasExt)
                    {
                        var dirName = System.IO.Path.GetDirectoryName(candidate) ?? string.Empty;
                        var nameNoExtOnly = System.IO.Path.GetFileNameWithoutExtension(candidate);
                        var nameNoExt = string.IsNullOrEmpty(dirName) ? nameNoExtOnly : System.IO.Path.Combine(dirName, nameNoExtOnly);
                        foreach (var ext in GetPathExtensions())
                        {
                            var p = nameNoExt + ext;
                            if (System.IO.File.Exists(p))
                            {
                                return p;
                            }
                        }
                    }

                    return string.Empty;
                }

                // Build search dirs: workingDir, current dir, PATH
                var dirs = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(workingDir) && System.IO.Directory.Exists(workingDir))
                {
                    dirs.Add(workingDir);
                }

                dirs.Add(Environment.CurrentDirectory);
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var d in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    dirs.Add(d);
                }

                foreach (var dir in dirs)
                {
                    var basePath = System.IO.Path.Combine(dir, candidate);
                    if (hasExt)
                    {
                        if (System.IO.File.Exists(basePath))
                        {
                            return basePath;
                        }

                        // Also try alternate extensions if the given one is not found in this dir
                        var nameNoExtOnly = System.IO.Path.GetFileNameWithoutExtension(candidate);
                        var nameNoExt = System.IO.Path.Combine(dir, nameNoExtOnly);
                        foreach (var ext in GetPathExtensions())
                        {
                            var p = nameNoExt + ext;
                            if (System.IO.File.Exists(p))
                            {
                                return p;
                            }
                        }
                    }
                    else
                    {
                        foreach (var ext in GetPathExtensions())
                        {
                            var p = basePath + ext;
                            if (System.IO.File.Exists(p))
                            {
                                return p;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static System.Collections.Generic.IEnumerable<string> GetPathExtensions()
        {
            var pathext = Environment.GetEnvironmentVariable("PATHEXT");
            if (string.IsNullOrWhiteSpace(pathext))
            {
                return new[] { ".COM", ".EXE", ".BAT", ".CMD", ".VBS", ".JS", ".WS", ".MSC", ".PS1" };
            }

            return pathext.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());
        }

        public void AddGroup()
        {
            Groups.Add(new ButtonGroup { Name = "New Group" });
            SelectedGroup = Groups.LastOrDefault();
            SelectedButton = SelectedGroup?.Buttons.FirstOrDefault();
            ScheduleSave();
        }

        public async Task LoadStartupStateAsync()
        {
            try
            {
                var startupTask = await StartupTask.GetAsync("TopToolbarStartup");
                switch (startupTask.State)
                {
                    case StartupTaskState.Enabled:
                        IsStartupEnabled = true;
                        IsStartupAvailable = true;
                        StartupStatusText = string.Empty;
                        break;
                    case StartupTaskState.Disabled:
                        IsStartupEnabled = false;
                        IsStartupAvailable = true;
                        StartupStatusText = string.Empty;
                        break;
                    case StartupTaskState.DisabledByUser:
                        IsStartupEnabled = false;
                        IsStartupAvailable = false;
                        StartupStatusText = "Disabled in Task Manager. Enable it there first.";
                        break;
                    case StartupTaskState.DisabledByPolicy:
                        IsStartupEnabled = false;
                        IsStartupAvailable = false;
                        StartupStatusText = "Disabled by group policy.";
                        break;
                    case StartupTaskState.EnabledByPolicy:
                        IsStartupEnabled = true;
                        IsStartupAvailable = false;
                        StartupStatusText = "Enabled by group policy.";
                        break;
                    default:
                        IsStartupEnabled = false;
                        IsStartupAvailable = false;
                        StartupStatusText = "Status unknown.";
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Failed to get startup task state: {ex.Message}");
                IsStartupEnabled = false;
                IsStartupAvailable = false;
                StartupStatusText = "Startup task not available (non-packaged app?).";
            }
        }

        public async Task<bool> SetStartupEnabledAsync(bool enabled)
        {
            try
            {
                var startupTask = await StartupTask.GetAsync("TopToolbarStartup");

                if (enabled)
                {
                    var newState = await startupTask.RequestEnableAsync();
                    IsStartupEnabled = newState == StartupTaskState.Enabled;

                    if (newState == StartupTaskState.DisabledByUser)
                    {
                        StartupStatusText = "Disabled in Task Manager. Enable it there first.";
                        IsStartupAvailable = false;
                    }
                    else if (newState == StartupTaskState.DisabledByPolicy)
                    {
                        StartupStatusText = "Disabled by group policy.";
                        IsStartupAvailable = false;
                    }
                    else
                    {
                        StartupStatusText = string.Empty;
                    }

                    return IsStartupEnabled;
                }
                else
                {
                    startupTask.Disable();
                    IsStartupEnabled = false;
                    StartupStatusText = string.Empty;
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Failed to set startup state: {ex.Message}");
                StartupStatusText = $"Failed: {ex.Message}";
                return false;
            }
        }

        public void RemoveGroup(ButtonGroup group)
        {
            Groups.Remove(group);
            if (SelectedGroup == group)
            {
                SelectedGroup = Groups.FirstOrDefault();
                SelectedButton = SelectedGroup?.Buttons.FirstOrDefault();
            }

            ScheduleSave();
        }

        public void AddButton(ButtonGroup group)
        {
            var button = new ToolbarButton
            {
                Name = "New Button",
                Action = new ToolbarAction { Command = string.Empty },
                IsExpanded = true,
            };

            ResetIconToDefault(button);

            group.Buttons.Add(button);

            SelectedGroup = group;
            SelectedButton = group.Buttons.LastOrDefault();
            // Don't schedule save - wait until user fills in command
        }

        public bool TrySetCatalogIcon(ToolbarButton button, string catalogId)
        {
            if (button == null || string.IsNullOrWhiteSpace(catalogId))
            {
                return false;
            }

            if (!IconCatalogService.TryGetById(catalogId, out var entry))
            {
                return false;
            }

            button.IconType = ToolbarIconType.Catalog;
            button.IconPath = IconCatalogService.BuildCatalogPath(entry.Id);
            button.IconGlyph = entry.Glyph ?? string.Empty;
            ScheduleSave();
            return true;
        }

        public bool TrySetImageIcon(ToolbarButton button, string iconPath)
        {
            if (button == null || string.IsNullOrWhiteSpace(iconPath))
            {
                return false;
            }

            button.IconType = ToolbarIconType.Image;
            button.IconPath = iconPath;
            ScheduleSave();
            return true;
        }

        public Task<bool> TrySetImageIconFromFileAsync(ToolbarButton button, string sourcePath)
        {
            if (button == null || string.IsNullOrWhiteSpace(sourcePath))
            {
                return Task.FromResult(false);
            }

            var targetPath = CopyIconAsset(button.Id, sourcePath);
            button.IconType = ToolbarIconType.Image;
            button.IconPath = targetPath;
            button.IconGlyph = string.Empty;
            ScheduleSave();
            return Task.FromResult(true);
        }

        public bool TrySetGlyphIcon(ToolbarButton button, string glyph)
        {
            if (button == null || string.IsNullOrWhiteSpace(glyph))
            {
                return false;
            }

            var trimmed = glyph.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            var catalogMatch = IconCatalogService.GetAll()
                .FirstOrDefault(entry => string.Equals(entry.Glyph, trimmed, StringComparison.Ordinal));

            if (catalogMatch != null)
            {
                return TrySetCatalogIcon(button, catalogMatch.Id);
            }

            button.IconType = ToolbarIconType.Catalog;
            button.IconGlyph = trimmed;
            button.IconPath = string.Empty;
            ScheduleSave();
            return true;
        }

        public void ResetIconToDefault(ToolbarButton button)
        {
            if (button == null)
            {
                return;
            }

            var defaultEntry = IconCatalogService.GetDefault();
            if (defaultEntry != null)
            {
                TrySetCatalogIcon(button, defaultEntry.Id);
            }
            else
            {
                TrySetGlyphIcon(button, "\uE10F");
            }
        }

        public void RemoveButton(ButtonGroup group, ToolbarButton button)
        {
            var removedIndex = group.Buttons.IndexOf(button);
            if (removedIndex < 0)
            {
                return;
            }

            group.Buttons.RemoveAt(removedIndex);

            if (SelectedButton == button)
            {
                if (group.Buttons.Count > 0)
                {
                    var newIndex = Math.Min(removedIndex, group.Buttons.Count - 1);
                    SelectedButton = group.Buttons[newIndex];
                }
                else
                {
                    SelectedButton = null;
                }
            }

            ScheduleSave();
        }

        public void Dispose()
        {
            _saveDebounce?.Stop();
            _saveDebounce?.Dispose();

            WorkspaceButtons.CollectionChanged -= WorkspaceButtons_CollectionChanged;
            foreach (var workspace in WorkspaceButtons.ToList())
            {
                UnhookWorkspaceButton(workspace);
            }

            Groups.CollectionChanged -= Groups_CollectionChanged;

            foreach (var g in Groups)
            {
                UnhookGroup(g);
            }

            System.GC.SuppressFinalize(this);
        }
    }
}
