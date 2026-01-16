// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TopToolbar.Actions;
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
        private const double ToolbarHeight = 140d;                  // Increased height for better visual effect
        private static readonly Thickness ToolbarChromePadding = new(28, 20, 28, 20);  // Increased padding
        private const double ToolbarShadowPadding = 24d;           // Increased shadow padding
        private const double ToolbarCornerRadius = 32d;            // Increased corner radius to make it more visible
        private const double ToolbarButtonSize = 52d;              // Slightly increased button size
        private const double ToolbarSeparatorHeight = 44d;         // Adjusted separator height accordingly
        private const double ToolbarLabelFontSize = 13d;
        private const double ToolbarIconFontSize = 22d;
        private const double ToolbarStackSpacing = 16d;            // Increased button spacing
        private readonly ToolbarConfigService _configService;
        private readonly ActionProviderRuntime _providerRuntime;
        private readonly ActionProviderService _providerService;
        private readonly ActionContextFactory _contextFactory;
        private readonly ToolbarActionExecutor _actionExecutor;
        private readonly BuiltinProvider _builtinProvider;
        private readonly ToolbarViewModel _vm;

        private readonly TopToolbar.Stores.ToolbarStore _store = new();
        private readonly Dictionary<string, ButtonGroup> _groupMap = new(StringComparer.OrdinalIgnoreCase);
        private int _lastPartialUpdateTick;
        private Timer _monitorTimer;
        private Timer _configWatcherDebounce;
        private bool _isVisible;
        private bool _builtConfigOnce;
        private IntPtr _hwnd;
        private bool _initializedLayout;
        private Border _toolbarContainer;
        private ScrollViewer _scrollViewer;
        private FileSystemWatcher _configWatcher;
        private IntPtr _oldWndProc;
        private DpiWndProcDelegate _newWndProc;

        private bool _snapshotInProgress;

        private delegate IntPtr DpiWndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public ToolbarWindow()
        {
            _configService = new ToolbarConfigService();
            _contextFactory = new ActionContextFactory();
            _providerRuntime = new ActionProviderRuntime();
            _providerService = new ActionProviderService(_providerRuntime);
            _actionExecutor = new ToolbarActionExecutor(_providerService, _contextFactory);
            _builtinProvider = new BuiltinProvider();
            _vm = new ToolbarViewModel(_configService, _providerService, _contextFactory);
            EnsurePerMonitorV2();
            RegisterProviders();

            // Legacy _store.StoreChanged full rebuild removed; we now rely solely on detailed events.
            _store.StoreChangedDetailed += (s, e) =>
            {
                try
                {
                    if (e == null || e.Kind == TopToolbar.Stores.StoreChangeKind.Reset || string.IsNullOrWhiteSpace(e.GroupId))
                    {
                        // Rehook all groups (in case of reset) then rebuild
                        HookAllGroupsForEnabledChanges();
                        BuildToolbarFromStore();
                        ResizeToContent();
                        return;
                    }

                    // Attempt partial update for single group
                    if (!DispatcherQueue.TryEnqueue(() =>
                    {
                        BuildOrReplaceSingleGroup(e.GroupId);
                        _lastPartialUpdateTick = Environment.TickCount;

                        // Ensure hook exists for updated group id
                        HookGroupForEnabledChanges(e.GroupId);
                    }))
                    {
                        BuildOrReplaceSingleGroup(e.GroupId);
                        _lastPartialUpdateTick = Environment.TickCount;
                        HookGroupForEnabledChanges(e.GroupId);
                    }
                }
                catch
                {
                }
            };

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

                    if (!DispatcherQueue.TryEnqueue(ApplyStore))
                    {
                        ApplyStore();
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

            // Create the toolbar content programmatically with transparent root
            CreateToolbarShell();

            // Apply styles when content is loaded
            _toolbarContainer.Loaded += (s, e) =>
            {
                if (!_initializedLayout)
                {
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
            };

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
                DispatcherQueue.TryEnqueue(() =>
                {
                    SyncStaticGroupsIntoStore();
                    BuildToolbarFromStore();
                    HookAllGroupsForEnabledChanges();
                    ResizeToContent();
                    PositionAtTopCenter();
                    _builtConfigOnce = true;
                });
            };
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
