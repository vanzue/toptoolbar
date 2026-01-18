// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TopToolbar.Models;
using TopToolbar.Models.Providers;
using TopToolbar.Serialization;
using TopToolbar.Services;
using TopToolbar.Services.Providers;
using TopToolbar.Services.Workspaces;

namespace TopToolbar.ViewModels
{
    public partial class SettingsViewModel
    {
        private readonly WorkspaceProviderConfigStore _workspaceConfigStore = new();
        private readonly WorkspaceDefinitionStore _workspaceDefinitionStore;
        private bool _suppressWorkspaceSave;

        public ObservableCollection<WorkspaceButtonViewModel> WorkspaceButtons { get; } = new();

        private WorkspaceButtonViewModel _selectedWorkspace;

        public WorkspaceButtonViewModel SelectedWorkspace
        {
            get => _selectedWorkspace;
            set
            {
                if (!ReferenceEquals(_selectedWorkspace, value))
                {
                    _selectedWorkspace = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSelectedWorkspace));
                    OnPropertyChanged(nameof(IsWorkspaceSelected));

                    if (value != null)
                    {
                        if (IsGeneralSelected)
                        {
                            IsGeneralSelected = false;
                        }

                        if (SelectedGroup != null)
                        {
                            SelectedGroup = null;
                        }
                    }
                }
            }
        }

        public bool HasSelectedWorkspace => SelectedWorkspace != null;

        public bool IsWorkspaceSelected => !IsGeneralSelected && SelectedWorkspace != null && SelectedGroup == null;

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

        private void LoadWorkspaceButtons(
            WorkspaceProviderConfig config,
            System.Collections.Generic.IReadOnlyList<WorkspaceDefinition> definitions)
        {
            var selectedId = SelectedWorkspace?.WorkspaceId;

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

            config.Buttons ??= new System.Collections.Generic.List<WorkspaceButtonConfig>();

            var definitionLookup = (definitions ?? System.Array.Empty<WorkspaceDefinition>())
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
                        Description = string.Empty,
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

            SelectedWorkspace = !string.IsNullOrWhiteSpace(selectedId)
                ? WorkspaceButtons.FirstOrDefault(ws =>
                    string.Equals(ws.WorkspaceId, selectedId, StringComparison.OrdinalIgnoreCase))
                : null;
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
            };

            var definitions = new System.Collections.Generic.List<WorkspaceDefinition>();

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

                definitions.Add(definitionClone);
            }

            await _workspaceDefinitionStore.SaveAllAsync(definitions, System.Threading.CancellationToken.None);
            await _workspaceConfigStore.SaveAsync(config);
        }

        private void HookWorkspaceButton(WorkspaceButtonViewModel workspace)
        {
            if (workspace == null)
            {
                return;
            }

            workspace.PropertyChanged += Workspace_PropertyChanged;
            workspace.Apps.CollectionChanged += WorkspaceApps_CollectionChanged;
            foreach (var app in workspace.Apps)
            {
                HookWorkspaceApp(app);
            }
            workspace.Definition.Applications = workspace.Apps.ToList();
        }

        private void UnhookWorkspaceButton(WorkspaceButtonViewModel workspace)
        {
            if (workspace == null)
            {
                return;
            }

            workspace.PropertyChanged -= Workspace_PropertyChanged;
            workspace.Apps.CollectionChanged -= WorkspaceApps_CollectionChanged;
            foreach (var app in workspace.Apps)
            {
                UnhookWorkspaceApp(app);
            }
        }

        private void Workspace_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suppressWorkspaceSave)
            {
                return;
            }

            ScheduleSave();
        }

        private void WorkspaceApps_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var app in e.NewItems.Cast<ApplicationDefinition>())
                {
                    HookWorkspaceApp(app);
                }
            }

            if (e.OldItems != null)
            {
                foreach (var app in e.OldItems.Cast<ApplicationDefinition>())
                {
                    UnhookWorkspaceApp(app);
                }
            }

            if (_suppressWorkspaceSave)
            {
                return;
            }

            ScheduleSave();
        }

        private void HookWorkspaceApp(ApplicationDefinition app)
        {
            if (app == null)
            {
                return;
            }

            app.PropertyChanged += WorkspaceApp_PropertyChanged;
        }

        private void UnhookWorkspaceApp(ApplicationDefinition app)
        {
            if (app == null)
            {
                return;
            }

            app.PropertyChanged -= WorkspaceApp_PropertyChanged;
        }

        private void WorkspaceApp_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suppressWorkspaceSave)
            {
                return;
            }

            if (string.Equals(e.PropertyName, nameof(ApplicationDefinition.IsExpanded), StringComparison.Ordinal)
                || string.Equals(e.PropertyName, nameof(ApplicationDefinition.DisplayName), StringComparison.Ordinal))
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
                Description = string.Empty,
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

        public ApplicationDefinition AddWorkspaceApp(WorkspaceButtonViewModel workspace)
        {
            if (workspace == null)
            {
                return null;
            }

            var app = new ApplicationDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "New application",
                IsExpanded = true,
            };

            workspace.Apps.Add(app);
            ScheduleSave();
            return app;
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
    }
}
