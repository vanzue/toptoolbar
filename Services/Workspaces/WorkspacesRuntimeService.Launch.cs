// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TopToolbar.Logging;
using Windows.Management.Deployment;
using Windows.ApplicationModel.Core;
using System.Linq;

namespace TopToolbar.Services.Workspaces
{
    internal sealed partial class WorkspacesRuntimeService
    {
        private static readonly TimeSpan WindowWaitTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan WindowPollInterval = TimeSpan.FromMilliseconds(200);
        private const int MaxParallelLaunches = 3;

        public async Task<bool> LaunchWorkspaceAsync(
            string workspaceId,
            CancellationToken cancellationToken
        )
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(WorkspacesRuntimeService));

            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                throw new ArgumentException(
                    "Workspace ID cannot be null or empty",
                    nameof(workspaceId)
                );
            }

            var swTotal = Stopwatch.StartNew();
            var swLoad = Stopwatch.StartNew();
            var workspace = await _fileLoader
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

            var appCount = workspace.Applications?.Count ?? 0;
            LogPerf($"WorkspaceRuntime: Starting launch of {appCount} app(s) for '{workspaceId}'");

            // ============================================================
            // Phase 1: Ensure all apps alive (two-pass approach)
            // - Pass 1: Assign existing windows to apps (no launching)
            // - Pass 2: Launch new windows for apps that didn't get one
            // ============================================================
            var swPhase1 = Stopwatch.StartNew();
            LogPerf($"WorkspaceRuntime: Phase 1 - Ensure all apps alive");
            
            var allResults = new List<EnsureAppResult>();
            var appsNeedingLaunch = new List<ApplicationDefinition>();
            
            // Pass 1: Try to assign existing windows to all apps (parallel, no launching)
            LogPerf($"WorkspaceRuntime: Phase 1 Pass 1 - Assign existing windows");
            var assignTasks = workspace.Applications
                .Select(app => TryAssignExistingWindowAsync(app, workspaceId, cancellationToken))
                .ToList();
            
            var assignResults = await Task.WhenAll(assignTasks).ConfigureAwait(false);
            
            for (int i = 0; i < workspace.Applications.Count; i++)
            {
                var result = assignResults[i];
                if (result.Success)
                {
                    allResults.Add(result);
                }
                else
                {
                    appsNeedingLaunch.Add(workspace.Applications[i]);
                }
            }
            
            LogPerf($"WorkspaceRuntime: Phase 1 Pass 1 done - {allResults.Count} apps got existing windows, {appsNeedingLaunch.Count} need launch");
            
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
                .Select(r => ResizeWindowAsync(r.Handle, r.App, r.LaunchedNew, cancellationToken))
                .ToList();
            
            await Task.WhenAll(resizeTasks).ConfigureAwait(false);
            swPhase2.Stop();
            LogPerf($"WorkspaceRuntime: Phase 2 done in {swPhase2.ElapsedMilliseconds} ms");

            // ============================================================
            // Phase 3: Minimize extraneous windows
            // ============================================================
            var workspaceHandles = new HashSet<IntPtr>(successfulApps.Select(r => r.Handle));
            var swPhase3 = Stopwatch.StartNew();
            MinimizeExtraneousWindows(workspaceHandles);
            swPhase3.Stop();
            LogPerf($"WorkspaceRuntime: Phase 3 - MinimizeExtraneousWindows took {swPhase3.ElapsedMilliseconds} ms");

            swTotal.Stop();
            LogPerf(
                $"WorkspaceRuntime: LaunchWorkspace('{workspaceId}') total {swTotal.ElapsedMilliseconds} ms; anySuccess=True"
            );
            return true;
        }

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
                    if (NativeWindowHelper.TryCreateWindowInfo(boundHandle, out var windowInfo) &&
                        NativeWindowHelper.IsMatch(windowInfo, app))
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
                        continue;

                    if (_managedWindows.GetBoundAppId(window.Handle) != null)
                        continue;

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
                            continue;

                        if (_managedWindows.GetBoundAppId(window.Handle) != null)
                            continue;

                        if (!string.IsNullOrWhiteSpace(window.Title) &&
                            window.Title.Equals(app.Title, StringComparison.OrdinalIgnoreCase))
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

        /// <summary>
        /// Phase 2: Resize a window to its target position
        /// </summary>
        private async Task ResizeWindowAsync(
            IntPtr handle,
            ApplicationDefinition app,
            bool launchedNew,
            CancellationToken cancellationToken
        )
        {
            // Yield immediately to ensure parallel execution
            await Task.Yield();
            
            var appLabel = DescribeApp(app);
            var sw = Stopwatch.StartNew();
            
            try
            {
                LogPerf($"WorkspaceRuntime: [{appLabel}] Resize - begin: minimized={app.Minimized}, maximized={app.Maximized}, position=({app.Position?.X},{app.Position?.Y},{app.Position?.Width},{app.Position?.Height})");
                
                NativeWindowHelper.SetWindowPlacement(
                    handle,
                    app.Position,
                    app.Maximized,
                    app.Minimized,
                    launchedNew
                );
                
                sw.Stop();
                LogPerf($"WorkspaceRuntime: [{appLabel}] Resize - done in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppLogger.LogWarning($"WorkspaceRuntime: [{appLabel}] Resize failed in {sw.ElapsedMilliseconds} ms - {ex.Message}");
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
                        if (!string.IsNullOrWhiteSpace(window.Title) &&
                            window.Title.Equals(app.Title, StringComparison.OrdinalIgnoreCase))
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

        private void MinimizeExtraneousWindows(HashSet<IntPtr> workspaceHandles)
        {
            try
            {
                var currentProcessId = (uint)Environment.ProcessId;
                var snapshot = _windowTracker.GetSnapshot();

                foreach (var window in snapshot)
                {
                    if (window.ProcessId == currentProcessId)
                    {
                        continue;
                    }

                    if (workspaceHandles.Contains(window.Handle))
                    {
                        continue;
                    }

                    if (!NativeWindowHelper.CanMinimizeWindow(window.Handle))
                    {
                        continue;
                    }

                    NativeWindowHelper.MinimizeWindow(window.Handle);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(
                    $"WorkspaceRuntime: failed to minimize extraneous windows - {ex.Message}"
                );
            }
        }

        private static string DescribeApp(ApplicationDefinition app)
        {
            if (app == null)
            {
                return "<null>";
            }

            var parts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(app.Name))
            {
                parts.Add(app.Name.Trim());
            }

            if (!string.IsNullOrWhiteSpace(app.Path))
            {
                parts.Add(app.Path.Trim());
            }

            if (!string.IsNullOrWhiteSpace(app.AppUserModelId))
            {
                parts.Add(app.AppUserModelId.Trim());
            }

            if (parts.Count == 0 && !string.IsNullOrWhiteSpace(app.Title))
            {
                parts.Add(app.Title.Trim());
            }

            var identity = string.Join(" | ", parts);
            var id = string.IsNullOrWhiteSpace(app.Id) ? "<no-id>" : app.Id;
            return string.IsNullOrWhiteSpace(identity) ? $"id={id}" : $"{identity} (id={id})";
        }

        private static bool IsApplicationFrameHostPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var fileName = Path.GetFileName(path);
            return string.Equals(fileName, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExpandPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Environment.ExpandEnvironmentVariables(path).Trim('"');
            }
            catch
            {
                return path.Trim('"');
            }
        }

        private static string DetermineWorkingDirectory(string path, bool useShellExecute)
        {
            if (useShellExecute)
            {
                return AppContext.BaseDirectory;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return directory;
                }
            }
            catch { }

            return AppContext.BaseDirectory;
        }

        private static void LogPerf(string message)
        {
            try
            {
                TopToolbar.Logging.AppLogger.LogInfo(message);
                try
                {
                    var ts = DateTime.Now.ToString(
                        "HH:mm:ss.fff",
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                    System.Console.WriteLine("[" + ts + "] " + message);
                }
                catch { }
            }
            catch { }
        }

        [ComImport]
        [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            int ActivateApplication(
                string appUserModelId,
                string arguments,
                ActivateOptions options,
                out uint processId
            );

            int ActivateForFile(
                string appUserModelId,
                IntPtr itemArray,
                string verb,
                out uint processId
            );

            int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
        }

        [ComImport]
        [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
        private class ApplicationActivationManager { }

        [Flags]
        private enum ActivateOptions
        {
            None = 0x0,
            DesignMode = 0x1,
            NoErrorUI = 0x2,
            NoSplashScreen = 0x4,
        }
    }
}
