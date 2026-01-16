// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TopToolbar.Actions;
using TopToolbar.Models;
using TopToolbar.Providers;
using Timer = System.Timers.Timer;
using Path = System.IO.Path;

namespace TopToolbar
{
    public sealed partial class ToolbarWindow
    {
        // Track which groups we've already hooked to avoid duplicate handlers
        private readonly HashSet<string> _enabledChangeHooked = new(StringComparer.OrdinalIgnoreCase);

        private void HookAllGroupsForEnabledChanges()
        {
            foreach (var g in _store.Groups)
            {
                if (g != null)
                {
                    HookGroupForEnabledChanges(g.Id);
                }
            }
        }

        private void HookGroupForEnabledChanges(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            var group = _store.GetGroup(groupId);
            if (group == null)
            {
                return;
            }

            if (_enabledChangeHooked.Contains(group.Id))
            {
                return; // already hooked
            }

            group.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ButtonGroup.IsEnabled))
                {
                    try
                    {
                        if (!DispatcherQueue.TryEnqueue(() =>
                        {
                            BuildToolbarFromStore();
                            ResizeToContent();
                        }))
                        {
                            BuildToolbarFromStore();
                            ResizeToContent();
                        }
                    }
                    catch
                    {
                    }
                }
            };
            _enabledChangeHooked.Add(group.Id);
        }

        private void RegisterProviders()
        {
            try
            {
                // Initialize and register all built-in providers (workspace and MCP providers)
                _builtinProvider.Initialize();
                _builtinProvider.RegisterProvidersTo(_providerRuntime);
            }
            catch (Exception ex)
            {
                // Log error but continue
                try
                {
                    Debug.WriteLine($"ToolbarWindow: Failed to register built-in providers: {ex.Message}");
                }
                catch
                {
                    // Ignore logging errors
                }
            }
        }

        private void StartWatchingConfig()
        {
            try
            {
                var path = _configService.ConfigPath;
                var dir = Path.GetDirectoryName(path);
                var file = Path.GetFileName(path);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file))
                {
                    return;
                }

                _configWatcherDebounce = new Timer(250) { AutoReset = false };
                _configWatcherDebounce.Elapsed += async (s, e) =>
                {
                    await _vm.LoadAsync(this.DispatcherQueue);
                    await RefreshWorkspaceGroupAsync();
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        SyncStaticGroupsIntoStore();
                        BuildToolbarFromStore();
                        ResizeToContent();
                    });
                };

                _configWatcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true,
                };

                FileSystemEventHandler onChanged = (s, e) =>
                {
                    _configWatcherDebounce.Stop();
                    _configWatcherDebounce.Start();
                };
                RenamedEventHandler onRenamed = (s, e) =>
                {
                    _configWatcherDebounce.Stop();
                    _configWatcherDebounce.Start();
                };

                _configWatcher.Changed += onChanged;
                _configWatcher.Created += onChanged;
                _configWatcher.Deleted += onChanged;
                _configWatcher.Renamed += onRenamed;
            }
            catch
            {
                // ignore watcher failures
            }
        }

        // Sync static (config) groups into the central store so subsequent dynamic rebuilds retain them.
        private void SyncStaticGroupsIntoStore()
        {
            try
            {
                foreach (var g in _vm.Groups)
                {
                    if (g == null)
                    {
                        continue;
                    }

                    // Workspace group (dynamic) will arrive via provider path
                    if (string.Equals(g.Id, "WorkspaceProvider", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Upsert static group into store (reuse provider upsert since it is id-based)
                    _store.UpsertProviderGroup(g);
                }
            }
            catch
            {
            }
        }

        private async System.Threading.Tasks.Task RefreshWorkspaceGroupAsync()
        {
            try
            {
                var context = new ActionContext();
                var group = await _providerService.CreateGroupAsync("WorkspaceProvider", context, CancellationToken.None).ConfigureAwait(true);
                if (group == null)
                {
                    return;
                }

                void Apply()
                {
                    try
                    {
                        _store.UpsertProviderGroup(group);
                    }
                    catch
                    {
                    }
                }

                if (DispatcherQueue == null)
                {
                    Apply();
                }
                else if (DispatcherQueue.HasThreadAccess)
                {
                    Apply();
                }
                else if (!DispatcherQueue.TryEnqueue(Apply))
                {
                    Apply();
                }
            }
            catch
            {
            }
        }

        // Incremental diff update for workspace group to avoid full rebuild.
        // Obsolete: old incremental workspace diff path (replaced by store). Retained temporarily for reference.
        private void ReplaceOrInsertWorkspaceGroup(ButtonGroup newGroup, ProviderChangedEventArgs changeArgs = null)
        {
            // Intentionally left empty (legacy path). Will be removed after confirming store path stability.
        }
    }
}
