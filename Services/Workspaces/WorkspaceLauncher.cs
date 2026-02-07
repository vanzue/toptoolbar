// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TopToolbar.Logging;
using TopToolbar.Services.Display;
using TopToolbar.Services.Windowing;

namespace TopToolbar.Services.Workspaces
{
    internal sealed partial class WorkspaceLauncher
    {
        private static readonly TimeSpan WindowWaitTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan WindowPollInterval = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan WindowArrangeTimeout = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan WindowArrangePollInterval = TimeSpan.FromMilliseconds(300);
        private const int WindowArrangeStableChecks = 2;
        private const int WindowArrangeTolerancePixels = 8;
        private static readonly TimeSpan WindowPostSettleTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan WindowPostSettlePollInterval = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan LaunchWindowSettleTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan LaunchWindowSettlePollInterval = TimeSpan.FromMilliseconds(150);

        private readonly WorkspaceDefinitionStore _definitionStore;
        private readonly WindowManager _windowManager;
        private readonly ManagedWindowRegistry _managedWindows;
        private readonly DisplayManager _displayManager;

        public WorkspaceLauncher(
            WorkspaceDefinitionStore definitionStore,
            WindowManager windowManager,
            ManagedWindowRegistry managedWindows,
            DisplayManager displayManager)
        {
            _definitionStore = definitionStore ?? throw new ArgumentNullException(nameof(definitionStore));
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
            _managedWindows = managedWindows ?? throw new ArgumentNullException(nameof(managedWindows));
            _displayManager = displayManager;
        }

        public async Task<bool> LaunchWorkspaceAsync(
            string workspaceId,
            CancellationToken cancellationToken
        )
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                throw new ArgumentException(
                    "Workspace ID cannot be null or empty",
                    nameof(workspaceId)
                );
            }

            var swTotal = Stopwatch.StartNew();
            var swLoad = Stopwatch.StartNew();
            var workspace = await _definitionStore
                .LoadByIdAsync(workspaceId, cancellationToken)
                .ConfigureAwait(false);
            swLoad.Stop();
            LogPerf(
                $"WorkspaceRuntime: Loaded workspace '{workspaceId}' in {swLoad.ElapsedMilliseconds} ms"
            );
            if (workspace == null)
            {
                AppLogger.LogWarning($"WorkspaceRuntime: workspace '{workspaceId}' not found.");
                return false;
            }

            {
                var apps = new List<ApplicationDefinition>();
                if (workspace.Applications != null)
                {
                    for (var i = 0; i < workspace.Applications.Count; i++)
                    {
                        var app = workspace.Applications[i];
                        if (app == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(app.Id))
                        {
                            app.Id = Guid.NewGuid().ToString("N");
                        }

                        apps.Add(app);
                    }
                }

                var appCount = apps.Count;
                LogPerf($"WorkspaceRuntime: Starting launch of {appCount} app(s) for '{workspaceId}'");

                if (appCount == 0)
                {
                    AppLogger.LogWarning($"WorkspaceRuntime: workspace '{workspaceId}' has no applications to launch.");
                    return false;
                }

                // ============================================================
                // Phase 1: Ensure all apps alive (two-pass approach)
                // - Pass 1: Assign existing windows to apps (no launching)
                // - Pass 2: Launch new windows for apps that didn't get one
                // ============================================================
                var swPhase1 = Stopwatch.StartNew();
                LogPerf($"WorkspaceRuntime: Phase 1 - Ensure all apps alive");
                
                var allResults = new List<EnsureAppResult>();
                var appsNeedingLaunch = new List<ApplicationDefinition>();
                
                if (workspace.MoveExistingWindows)
                {
                    var snapshot = _windowManager.GetSnapshot();
                    var snapshotIndex = BuildWindowSnapshotIndex(snapshot);

                    // Pass 1: Try to assign existing windows to all apps (parallel, no launching)
                    LogPerf($"WorkspaceRuntime: Phase 1 Pass 1 - Assign existing windows");
                    var assignTasks = apps
                        .Select(app => TryAssignExistingWindowAsync(app, workspaceId, snapshot, snapshotIndex, cancellationToken))
                        .ToList();
                    
                    var assignResults = await Task.WhenAll(assignTasks).ConfigureAwait(false);
                    
                    for (int i = 0; i < apps.Count; i++)
                    {
                        var result = assignResults[i];
                        if (result.Success)
                        {
                            allResults.Add(result);
                        }
                        else
                        {
                            appsNeedingLaunch.Add(apps[i]);
                        }
                    }
                    
                    LogPerf($"WorkspaceRuntime: Phase 1 Pass 1 done - {allResults.Count} apps got existing windows, {appsNeedingLaunch.Count} need launch");
                }
                else
                {
                    appsNeedingLaunch.AddRange(apps);
                }
                
                // Pass 2: Launch new windows for remaining apps (sequential to avoid race)
                if (appsNeedingLaunch.Count > 0)
                {
                    LogPerf($"WorkspaceRuntime: Phase 1 Pass 2 - Launch {appsNeedingLaunch.Count} new windows");
                    foreach (var app in appsNeedingLaunch)
                    {
                        var result = await LaunchNewWindowAsync(app, workspaceId, cancellationToken).ConfigureAwait(false);
                        allResults.Add(result);
                    }
                }
                
                swPhase1.Stop();
                
                var successfulApps = allResults.Where(r => r.Success).ToList();
                LogPerf($"WorkspaceRuntime: Phase 1 done in {swPhase1.ElapsedMilliseconds} ms - {successfulApps.Count}/{appCount} apps ready");

                if (successfulApps.Count == 0)
                {
                    swTotal.Stop();
                    LogPerf($"WorkspaceRuntime: LaunchWorkspace('{workspaceId}') total {swTotal.ElapsedMilliseconds} ms; anySuccess=False");
                    return false;
                }

                // ============================================================
                // Phase 2: Resize all windows (parallel)
                // - All windows resize simultaneously
                // - No competition since each window is independent
                // ============================================================
                var swPhase2 = Stopwatch.StartNew();
                LogPerf($"WorkspaceRuntime: Phase 2 - Resize all windows (parallel)");
                
                var resizeTasks = successfulApps
                    .Select(r =>
                        ResizeWindowAsync(
                            r.Handle,
                            r.App,
                            ResolveTargetPlacement(workspace, r.App),
                            r.LaunchedNew,
                            cancellationToken))
                    .ToList();
                
                await Task.WhenAll(resizeTasks).ConfigureAwait(false);
                swPhase2.Stop();
                LogPerf($"WorkspaceRuntime: Phase 2 done in {swPhase2.ElapsedMilliseconds} ms");

                // ============================================================
                // Phase 3: Minimize extraneous windows
                // ============================================================
                if (workspace.MoveExistingWindows)
                {
                    var workspaceHandles = new HashSet<IntPtr>(successfulApps.Select(r => r.Handle));
                    var swPhase3 = Stopwatch.StartNew();
                    MinimizeExtraneousWindows(workspaceHandles);
                    swPhase3.Stop();
                    LogPerf($"WorkspaceRuntime: Phase 3 - MinimizeExtraneousWindows took {swPhase3.ElapsedMilliseconds} ms");
                }

                swTotal.Stop();
                LogPerf(
                    $"WorkspaceRuntime: LaunchWorkspace('{workspaceId}') total {swTotal.ElapsedMilliseconds} ms; anySuccess=True"
                );
                await TryUpdateLastLaunchedTimeAsync(workspace, cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

    }
}
