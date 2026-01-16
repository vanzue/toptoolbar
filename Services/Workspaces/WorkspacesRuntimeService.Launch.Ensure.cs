// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TopToolbar.Logging;

namespace TopToolbar.Services.Workspaces
{
    internal sealed partial class WorkspacesRuntimeService
    {
        /// <summary>
        /// Result of ensuring an app is alive
        /// </summary>
        private readonly struct EnsureAppResult
        {
            public static EnsureAppResult Failed(ApplicationDefinition app) => new(false, app, IntPtr.Zero, false);

            public EnsureAppResult(bool success, ApplicationDefinition app, IntPtr handle, bool launchedNew)
            {
                Success = success;
                App = app;
                Handle = handle;
                LaunchedNew = launchedNew;
            }

            public bool Success { get; }
            public ApplicationDefinition App { get; }
            public IntPtr Handle { get; }
            public bool LaunchedNew { get; }
        }

        /// <summary>
        /// Phase 1 Pass 1: Try to assign an existing window to the app (no launching)
        /// </summary>
        private async Task<EnsureAppResult> TryAssignExistingWindowAsync(
            ApplicationDefinition app,
            string workspaceId,
            CancellationToken cancellationToken
        )
        {
            await Task.Yield();

            var appLabel = DescribeApp(app);
            var sw = Stopwatch.StartNew();

            try
            {
                LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - begin");

                // Step 1: Check if we already have a managed window bound to this app
                var boundHandle = _managedWindows.GetBoundWindow(app.Id);
                if (boundHandle != IntPtr.Zero)
                {
                    // Verify the window still exists AND matches the app (process must match)
                    if (NativeWindowHelper.TryCreateWindowInfo(boundHandle, out var windowInfo)
                        && NativeWindowHelper.IsMatch(windowInfo, app))
                    {
                        sw.Stop();
                        LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - found cached window handle={boundHandle} in {sw.ElapsedMilliseconds} ms");
                        return new EnsureAppResult(true, app, boundHandle, false);
                    }

                    // Window was destroyed or no longer matches - clear binding
                    _managedWindows.UnbindWindow(boundHandle);
                    LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - cached window invalid, cleared");
                }

                // Step 2: Try to find an unmanaged window that matches
                var snapshot = _windowTracker.GetSnapshot();

                foreach (var window in snapshot)
                {
                    if (window == null || window.Handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (_managedWindows.GetBoundAppId(window.Handle) != null)
                    {
                        continue;
                    }

                    if (NativeWindowHelper.IsMatch(window, app))
                    {
                        if (_managedWindows.TryBindWindow(workspaceId, app.Id, window.Handle))
                        {
                            sw.Stop();
                            LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - claimed existing window handle={window.Handle} in {sw.ElapsedMilliseconds} ms");
                            return new EnsureAppResult(true, app, window.Handle, false);
                        }
                    }
                }

                // Special handling for ApplicationFrameHost (UWP apps) - try to match by title
                if (IsApplicationFrameHostPath(app.Path) && !string.IsNullOrWhiteSpace(app.Title))
                {
                    foreach (var window in snapshot)
                    {
                        if (window == null || window.Handle == IntPtr.Zero)
                        {
                            continue;
                        }

                        if (_managedWindows.GetBoundAppId(window.Handle) != null)
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(window.Title)
                            && window.Title.Equals(app.Title, StringComparison.OrdinalIgnoreCase))
                        {
                            if (_managedWindows.TryBindWindow(workspaceId, app.Id, window.Handle))
                            {
                                sw.Stop();
                                LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - claimed UWP window by title in {sw.ElapsedMilliseconds} ms");
                                return new EnsureAppResult(true, app, window.Handle, false);
                            }
                        }
                    }
                }

                sw.Stop();
                LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - no existing window found in {sw.ElapsedMilliseconds} ms");
                return EnsureAppResult.Failed(app);
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppLogger.LogWarning($"WorkspaceRuntime: [{appLabel}] TryAssignExisting failed - {ex.Message}");
                return EnsureAppResult.Failed(app);
            }
        }

        /// <summary>
        /// Phase 1 Pass 2: Launch a new window for the app
        /// </summary>
        private async Task<EnsureAppResult> LaunchNewWindowAsync(
            ApplicationDefinition app,
            string workspaceId,
            CancellationToken cancellationToken
        )
        {
            var appLabel = DescribeApp(app);
            var sw = Stopwatch.StartNew();

            try
            {
                LogPerf($"WorkspaceRuntime: [{appLabel}] LaunchNew - begin");

                // ApplicationFrameHost cannot be launched directly
                if (IsApplicationFrameHostPath(app.Path))
                {
                    sw.Stop();
                    LogPerf($"WorkspaceRuntime: [{appLabel}] LaunchNew - cannot launch ApplicationFrameHost directly");
                    return EnsureAppResult.Failed(app);
                }

                var launchResult = await AppLauncher.LaunchAppAsync(
                    app,
                    _windowTracker,
                    WindowWaitTimeout,
                    WindowPollInterval,
                    cancellationToken
                ).ConfigureAwait(false);

                if (!launchResult.Succeeded || launchResult.Windows.Count == 0)
                {
                    sw.Stop();
                    LogPerf($"WorkspaceRuntime: [{appLabel}] LaunchNew - launch failed in {sw.ElapsedMilliseconds} ms");
                    return EnsureAppResult.Failed(app);
                }

                var newHandle = launchResult.Windows[0].Handle;
                if (_managedWindows.TryBindWindow(workspaceId, app.Id, newHandle))
                {
                    sw.Stop();
                    LogPerf($"WorkspaceRuntime: [{appLabel}] LaunchNew - launched and claimed window handle={newHandle} in {sw.ElapsedMilliseconds} ms");
                    return new EnsureAppResult(true, app, newHandle, true);
                }
                else
                {
                    sw.Stop();
                    LogPerf($"WorkspaceRuntime: [{appLabel}] LaunchNew - launched but window already claimed in {sw.ElapsedMilliseconds} ms");
                    return EnsureAppResult.Failed(app);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppLogger.LogWarning($"WorkspaceRuntime: [{appLabel}] LaunchNew failed - {ex.Message}");
                return EnsureAppResult.Failed(app);
            }
        }

        /// <summary>
        /// Phase 1: Ensure app is alive and has a window bound
        /// </summary>
        private async Task<EnsureAppResult> EnsureAppAliveAsync(
            ApplicationDefinition app,
            string workspaceId,
            CancellationToken cancellationToken
        )
        {
            // Yield immediately to ensure parallel execution
            await Task.Yield();

            var appLabel = DescribeApp(app);
            var sw = Stopwatch.StartNew();

            try
            {
                LogPerf($"WorkspaceRuntime: [{appLabel}] EnsureAlive - begin");

                // Step 1: Check if we have a managed window bound to this app
                var boundHandle = _managedWindows.GetBoundWindow(app.Id);

                if (boundHandle != IntPtr.Zero)
                {
                    // Verify the window still exists
                    if (NativeWindowHelper.TryCreateWindowInfo(boundHandle, out _))
                    {
                        sw.Stop();
                        LogPerf($"WorkspaceRuntime: [{appLabel}] EnsureAlive - found cached window handle={boundHandle} in {sw.ElapsedMilliseconds} ms");
                        return new EnsureAppResult(true, app, boundHandle, false);
                    }

                    // Window was destroyed, clear binding
                    _managedWindows.UnbindWindow(boundHandle);
                    LogPerf($"WorkspaceRuntime: [{appLabel}] EnsureAlive - cached window destroyed, will find/launch new");
                }

                // Step 2: No managed window, need to launch or find one
                var result = await LaunchOrFindAppAsync(app, workspaceId, cancellationToken)
                    .ConfigureAwait(false);

                sw.Stop();
                if (result.Succeeded)
                {
                    LogPerf($"WorkspaceRuntime: [{appLabel}] EnsureAlive - done in {sw.ElapsedMilliseconds} ms; handle={result.Handle}; launchedNew={result.LaunchedNew}");
                    return new EnsureAppResult(true, app, result.Handle, result.LaunchedNew);
                }
                else
                {
                    LogPerf($"WorkspaceRuntime: [{appLabel}] EnsureAlive - failed in {sw.ElapsedMilliseconds} ms");
                    return EnsureAppResult.Failed(app);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppLogger.LogWarning($"WorkspaceRuntime: [{appLabel}] EnsureAlive failed - {ex.Message}");
                return EnsureAppResult.Failed(app);
            }
        }

        private readonly struct LaunchResult
        {
            public static LaunchResult Failed => new(false, IntPtr.Zero, false);

            public LaunchResult(bool succeeded, IntPtr handle, bool launchedNew)
            {
                Succeeded = succeeded;
                Handle = handle;
                LaunchedNew = launchedNew;
            }

            public bool Succeeded { get; }
            public IntPtr Handle { get; }
            public bool LaunchedNew { get; }
        }

        private async Task<LaunchResult> LaunchOrFindAppAsync(
            ApplicationDefinition app,
            string workspaceId,
            CancellationToken cancellationToken
        )
        {
            if (app == null)
            {
                return LaunchResult.Failed;
            }

            // Try to find an existing unmanaged window that matches
            var snapshot = _windowTracker.GetSnapshot();

            // Try to find a matching window (by process path/name)
            foreach (var window in snapshot)
            {
                if (window == null || window.Handle == IntPtr.Zero)
                {
                    continue;
                }

                // Skip windows already managed by another app (quick check)
                if (_managedWindows.GetBoundAppId(window.Handle) != null)
                {
                    continue;
                }

                // Check if this window matches the app (by process)
                if (NativeWindowHelper.IsMatch(window, app))
                {
                    // Try to atomically claim this window
                    if (_managedWindows.TryBindWindow(workspaceId, app.Id, window.Handle))
                    {
                        LogPerf($"WorkspaceRuntime: Found and claimed window for '{app.Name}' (handle={window.Handle})");
                        return new LaunchResult(true, window.Handle, false);
                    }
                    // If claim failed, another task got it first - continue looking
                }
            }

            // Special handling for ApplicationFrameHost (UWP apps) - try to match by title
            if (IsApplicationFrameHostPath(app.Path))
            {
                // For UWP apps wrapped by ApplicationFrameHost, we can't really "launch" ApplicationFrameHost.exe
                // Instead, try to find any window with a matching title
                if (!string.IsNullOrWhiteSpace(app.Title))
                {
                    foreach (var window in snapshot)
                    {
                        if (window == null || window.Handle == IntPtr.Zero)
                        {
                            continue;
                        }

                        if (_managedWindows.GetBoundAppId(window.Handle) != null)
                        {
                            continue;
                        }

                        // Match by title for UWP apps
                        if (!string.IsNullOrWhiteSpace(window.Title)
                            && window.Title.Equals(app.Title, StringComparison.OrdinalIgnoreCase))
                        {
                            if (_managedWindows.TryBindWindow(workspaceId, app.Id, window.Handle))
                            {
                                LogPerf($"WorkspaceRuntime: Found and claimed UWP window by title match: '{app.Title}'");
                                return new LaunchResult(true, window.Handle, false);
                            }
                        }
                    }
                }

                // Cannot launch ApplicationFrameHost directly
                LogPerf($"WorkspaceRuntime: Cannot launch ApplicationFrameHost UWP app - no matching window found");
                return LaunchResult.Failed;
            }

            // No existing window found, need to launch the app
            var launchResult = await AppLauncher.LaunchAppAsync(
                app,
                _windowTracker,
                WindowWaitTimeout,
                WindowPollInterval,
                cancellationToken
            ).ConfigureAwait(false);

            if (!launchResult.Succeeded || launchResult.Windows.Count == 0)
            {
                return LaunchResult.Failed;
            }

            // Try to claim the newly launched window
            var newHandle = launchResult.Windows[0].Handle;
            if (_managedWindows.TryBindWindow(workspaceId, app.Id, newHandle))
            {
                LogPerf($"WorkspaceRuntime: Launched and claimed new window for '{app.Name}' (handle={newHandle})");
                return new LaunchResult(true, newHandle, true);
            }
            else
            {
                // Another task already claimed this window - this can happen when multiple apps
                // of the same type are launched in parallel and they all create/find the same window
                LogPerf($"WorkspaceRuntime: Launched window for '{app.Name}' but another app already claimed it (handle={newHandle})");
                return LaunchResult.Failed;
            }
        }
    }
}
