// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TopToolbar.Logging;
using TopToolbar.Services.Windowing;

namespace TopToolbar.Services.Workspaces
{
    internal sealed partial class WorkspaceLauncher
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
                    if (!NativeWindowHelper.TryCreateWindowInfo(boundHandle, out var windowInfo))
                    {
                        _managedWindows.UnbindWindow(boundHandle);
                        LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - cached window handle={boundHandle} missing, cleared");
                    }
                    else if (WorkspaceWindowMatcher.IsMatch(windowInfo, app))
                    {
                        if (NativeWindowHelper.TryIsWindowOnCurrentVirtualDesktop(boundHandle, out var isOnCurrentDesktop)
                            && !isOnCurrentDesktop)
                        {
                            _managedWindows.UnbindApp(app.Id);
                            LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - cached window handle={boundHandle} on another virtual desktop, unbound");
                        }
                        else
                        {
                            sw.Stop();
                            LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - found cached window handle={boundHandle} in {sw.ElapsedMilliseconds} ms");
                            return new EnsureAppResult(true, app, boundHandle, false);
                        }
                    }
                    else
                    {
                        _managedWindows.UnbindWindow(boundHandle);
                        LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - cached window handle={boundHandle} no longer matches, cleared");
                    }
                }

                // Step 2: Try to find an unmanaged window that matches
                var snapshot = _windowManager.GetSnapshot();
                var loggedBoundCandidate = false;

                foreach (var window in snapshot)
                {
                    if (window == null || window.Handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    var boundAppId = _managedWindows.GetBoundAppId(window.Handle);
                    if (boundAppId != null)
                    {
                        if (!loggedBoundCandidate && WorkspaceWindowMatcher.IsMatch(window, app))
                        {
                            loggedBoundCandidate = true;
                            var boundWorkspaceId = _managedWindows.GetWorkspaceIdForApp(boundAppId) ?? "<unknown>";
                            LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - candidate window handle={window.Handle} already bound to appId={boundAppId} workspaceId={boundWorkspaceId}");
                        }

                        continue;
                    }

                    if (WorkspaceWindowMatcher.IsMatch(window, app))
                    {
                        if (NativeWindowHelper.TryIsWindowOnCurrentVirtualDesktop(window.Handle, out var isOnCurrentDesktop)
                            && !isOnCurrentDesktop)
                        {
                            LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - candidate window handle={window.Handle} on another virtual desktop");
                            continue;
                        }

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

                        var boundAppId = _managedWindows.GetBoundAppId(window.Handle);
                        if (boundAppId != null)
                        {
                            if (!loggedBoundCandidate
                                && !string.IsNullOrWhiteSpace(window.Title)
                                && window.Title.Equals(app.Title, StringComparison.OrdinalIgnoreCase))
                            {
                                loggedBoundCandidate = true;
                                var boundWorkspaceId = _managedWindows.GetWorkspaceIdForApp(boundAppId) ?? "<unknown>";
                                LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - title match window handle={window.Handle} already bound to appId={boundAppId} workspaceId={boundWorkspaceId}");
                            }

                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(window.Title)
                            && window.Title.Equals(app.Title, StringComparison.OrdinalIgnoreCase))
                        {
                            if (NativeWindowHelper.TryIsWindowOnCurrentVirtualDesktop(window.Handle, out var isOnCurrentDesktop)
                                && !isOnCurrentDesktop)
                            {
                                LogPerf($"WorkspaceRuntime: [{appLabel}] TryAssignExisting - title match window handle={window.Handle} on another virtual desktop");
                                continue;
                            }

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

                var excludedHandles = _managedWindows.GetAllBoundWindows();
                var launchResult = await AppLauncher.LaunchAppAsync(
                    app,
                    _windowManager,
                    WindowWaitTimeout,
                    WindowPollInterval,
                    excludedHandles,
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
                    var boundAppId = _managedWindows.GetBoundAppId(newHandle) ?? "<unknown>";
                    var boundWorkspaceId = _managedWindows.GetWorkspaceIdForApp(boundAppId) ?? "<unknown>";
                    LogPerf($"WorkspaceRuntime: [{appLabel}] LaunchNew - launched but window already claimed (handle={newHandle} appId={boundAppId} workspaceId={boundWorkspaceId}) in {sw.ElapsedMilliseconds} ms");
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

    }
}
