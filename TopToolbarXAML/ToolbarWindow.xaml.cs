// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TopToolbar.Actions;
using TopToolbar.Logging;
using TopToolbar.Models;
using TopToolbar.Providers;
using TopToolbar.Services;
using TopToolbar.ViewModels;
using WinUIEx;
using Timer = System.Timers.Timer;

namespace TopToolbar
{
    public sealed partial class ToolbarWindow : WindowEx, IDisposable
    {
        private const int TriggerZoneHeight = 2;
        private readonly ToolbarConfigService _configService;
        private readonly ActionProviderRuntime _providerRuntime;
        private readonly ActionProviderService _providerService;
        private readonly ActionContextFactory _contextFactory;
        private readonly ToolbarActionExecutor _actionExecutor;
        private readonly BuiltinProvider _builtinProvider;
        private readonly ToolbarViewModel _vm;
        private readonly ToolbarItemsViewModel _itemsViewModel;

        private readonly TopToolbar.Stores.ToolbarStore _store = new();
        private Timer _monitorTimer;
        private Timer _configWatcherDebounce;
        private bool _isVisible;
        private bool _builtConfigOnce;
        private IntPtr _hwnd;
        private bool _initializedLayout;
        private FileSystemWatcher _configWatcher;
        private IntPtr _oldWndProc;
        private DpiWndProcDelegate _newWndProc;

        private bool _snapshotInProgress;

        private delegate IntPtr DpiWndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public ToolbarItemsViewModel ItemsViewModel => _itemsViewModel;

        public ToolbarWindow()
        {
            _configService = new ToolbarConfigService();
            _contextFactory = new ActionContextFactory();
            _providerRuntime = new ActionProviderRuntime();
            _providerService = new ActionProviderService(_providerRuntime);
            _builtinProvider = new BuiltinProvider();
            _vm = new ToolbarViewModel(_configService, _providerService, _contextFactory);
            _itemsViewModel = new ToolbarItemsViewModel(_store);
            _itemsViewModel.LayoutChanged += (_, __) =>
            {
                void Apply()
                {
                    ResizeToContent();
                    if (!_isVisible)
                    {
                        PositionAtTopCenter();
                    }
                }

                if (DispatcherQueue == null || DispatcherQueue.HasThreadAccess)
                {
                    Apply();
                }
                else
                {
                    _ = DispatcherQueue.TryEnqueue(Apply);
                }
            };
            InitializeComponent();
            _actionExecutor = new ToolbarActionExecutor(_providerService, _contextFactory, DispatcherQueue);
            EnsurePerMonitorV2();
            RegisterProviders();

            _providerRuntime.ProvidersChanged += async (_, args) =>
            {
                if (args == null)
                {
                    return;
                }

                // Only handle WorkspaceProvider for now (other providers not yet dynamic)
                if (!string.Equals(args.ProviderId, "WorkspaceProvider", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                try
                {
                    var kindsNeedingGroup = args.Kind == ProviderChangeKind.ActionsUpdated ||
                                            args.Kind == ProviderChangeKind.ActionsAdded ||
                                            args.Kind == ProviderChangeKind.ActionsRemoved ||
                                            args.Kind == ProviderChangeKind.GroupUpdated ||
                                            args.Kind == ProviderChangeKind.BulkRefresh ||
                                            args.Kind == ProviderChangeKind.Reset ||
                                            args.Kind == ProviderChangeKind.ProviderRegistered;

                    if (!kindsNeedingGroup)
                    {
                        return; // other change kinds (progress, execution) not yet surfaced
                    }

                    // Build new group off the UI thread
                    var ctx = new ActionContext();
                    ButtonGroup newGroup;
                    try
                    {
                        newGroup = await _providerService.CreateGroupAsync("WorkspaceProvider", ctx, CancellationToken.None);
                    }
                    catch
                    {
                        // TODO: log: failed to create workspace group
                        return;
                    }

                    void ApplyStore()
                    {
                        try
                        {
                            _store.UpsertProviderGroup(newGroup);
                        }
                        catch
                        {
                            // TODO: log: store upsert failed
                        }
                    }

                    if (DispatcherQueue == null || DispatcherQueue.HasThreadAccess)
                    {
                        ApplyStore();
                    }
                    else
                    {
                        _ = DispatcherQueue.TryEnqueue(ApplyStore);
                    }
                }
                catch (Exception)
                {
                    // TODO: log: provider change handling wrapper failure
                }
            };

            Title = "Top Toolbar";

            // Make window background completely transparent
            this.SystemBackdrop = new WinUIEx.TransparentTintBackdrop(
                Windows.UI.Color.FromArgb(0, 0, 0, 0));

            // Apply styles immediately after activation as backup
            this.Activated += (s, e) => MakeTopMost();

            StartMonitoring();
            StartWatchingConfig();

            // Load config and build UI when window activates
            this.Activated += async (s, e) =>
            {
                if (_builtConfigOnce)
                {
                    return;
                }

                await _vm.LoadAsync(this.DispatcherQueue);
                await RefreshWorkspaceGroupAsync();

                // Ensure UI-thread access for XAML object tree
                if (DispatcherQueue == null || DispatcherQueue.HasThreadAccess)
                {
                    SyncStaticGroupsIntoStore();
                    ResizeToContent();
                    PositionAtTopCenter();
                    _builtConfigOnce = true;
                }
                else
                {
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        SyncStaticGroupsIntoStore();
                        ResizeToContent();
                        PositionAtTopCenter();
                        _builtConfigOnce = true;
                    });
                }
            };
        }

        private void ToolbarContainer_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initializedLayout)
            {
                return;
            }

            _hwnd = this.GetWindowHandle();
            ApplyTransparentBackground();
            ApplyFramelessStyles();
            TryHookDpiMessages();
            ResizeToContent();
            PositionAtTopCenter();
            AppWindow.Hide();
            _isVisible = false;
            _initializedLayout = true;
        }

        private async void OnToolbarButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not ToolbarButtonItem item)
            {
                return;
            }

            if (item.Button.IsExecuting)
            {
                return;
            }

            try
            {
                await _actionExecutor.ExecuteAsync(item.Group, item.Button);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                item.Button.StatusMessage = ex.Message;
            }
        }

        private void OnToolbarButtonRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not FrameworkElement element || element.Tag is not ToolbarButtonItem item)
            {
                return;
            }

            var flyout = new MenuFlyout();
            var deleteItem = new MenuFlyoutItem
            {
                Text = "Remove Button",
                Icon = new FontIcon { Glyph = "\uE74D" },
            };

            deleteItem.Click += async (_, __) =>
            {
                try
                {
                    if (item.Button.Action?.Type == ToolbarActionType.Provider &&
                        string.Equals(item.Button.Action.ProviderId, "WorkspaceProvider", StringComparison.OrdinalIgnoreCase))
                    {
                        string workspaceId = null;
                        if (!string.IsNullOrEmpty(item.Button.Id) &&
                            item.Button.Id.StartsWith("workspace::", StringComparison.OrdinalIgnoreCase))
                        {
                            workspaceId = item.Button.Id.Substring("workspace::".Length);
                        }
                        else if (!string.IsNullOrEmpty(item.Button.Action.ProviderActionId) &&
                                 item.Button.Action.ProviderActionId.StartsWith("workspace::", StringComparison.OrdinalIgnoreCase))
                        {
                            workspaceId = item.Button.Action.ProviderActionId.Substring("workspace::".Length);
                        }

                        if (!string.IsNullOrWhiteSpace(workspaceId))
                        {
                            var definitionStore = new Services.Workspaces.WorkspaceDefinitionStore();
                            var buttonStore = new Services.Workspaces.WorkspaceButtonStore();
                            var deleted = await definitionStore.DeleteWorkspaceAsync(workspaceId, CancellationToken.None);

                            if (deleted)
                            {
                                _ = await buttonStore.RemoveWorkspaceButtonAsync(workspaceId, CancellationToken.None);
                                AppLogger.LogInfo($"Deleted workspace '{workspaceId}'");
                                await _vm.LoadAsync(DispatcherQueue);
                                await RefreshWorkspaceGroupAsync();
                            }

                            return;
                        }
                    }

                    var vmGroup = _vm.Groups.FirstOrDefault(g =>
                        string.Equals(g.Id, item.Group.Id, StringComparison.OrdinalIgnoreCase));

                    if (vmGroup != null)
                    {
                        vmGroup.Buttons.Remove(item.Button);
                        await _vm.SaveAsync();
                        SyncStaticGroupsIntoStore();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"Failed to delete button '{item.Button?.Name}': {ex.Message}");
                }
            };

            flyout.Items.Add(deleteItem);
            flyout.ShowAt(element, e.GetPosition(element));
        }

        private async void OnSnapshotClick(object sender, RoutedEventArgs e)
        {
            await HandleSnapshotButtonClickAsync(sender as Button);
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new SettingsWindow(_providerRuntime);
                win.AppWindow.Move(new Windows.Graphics.PointInt32(this.AppWindow.Position.X + 50, this.AppWindow.Position.Y + 60));
                win.Activate();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Open SettingsWindow failed", ex);
            }
        }

        public void Dispose()
        {
            _monitorTimer?.Stop();
            _monitorTimer?.Dispose();
            _configWatcherDebounce?.Stop();
            _configWatcherDebounce?.Dispose();
            if (_configWatcher != null)
            {
                _configWatcher.EnableRaisingEvents = false;
                _configWatcher.Dispose();
            }

            try
            {
                _itemsViewModel?.Dispose();
            }
            catch
            {
            }

            // Dispose the built-in provider which handles all provider disposals
            try
            {
                _builtinProvider?.Dispose();
            }
            catch (Exception)
            {
            }

            GC.SuppressFinalize(this);
        }
    }
}
