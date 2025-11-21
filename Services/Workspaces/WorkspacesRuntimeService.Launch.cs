// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
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
        private static readonly TimeSpan WindowWaitTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan WindowPollInterval = TimeSpan.FromMilliseconds(200);

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

            var context = new WorkspaceExecutionContext(_windowTracker, workspace);
            var appCount = workspace.Applications?.Count ?? 0;
            LogPerf($"WorkspaceRuntime: Starting launch of {appCount} app(s) for '{workspaceId}'");

            var launchTasks = new List<Task<bool>>(appCount);
            foreach (var app in workspace.Applications)
            {
                cancellationToken.ThrowIfCancellationRequested();
                launchTasks.Add(LaunchSingleAppAsync(app, context, cancellationToken));
            }

            var launchResults = await Task.WhenAll(launchTasks).ConfigureAwait(false);
            var anySuccess = Array.Exists(launchResults, succeeded => succeeded);

            var swMinimize = Stopwatch.StartNew();
            MinimizeExtraneousWindows(context);
            swMinimize.Stop();
            LogPerf(
                $"WorkspaceRuntime: MinimizeExtraneousWindows took {swMinimize.ElapsedMilliseconds} ms"
            );

            swTotal.Stop();
            LogPerf(
                $"WorkspaceRuntime: LaunchWorkspace('{workspaceId}') total {swTotal.ElapsedMilliseconds} ms; anySuccess={anySuccess}"
            );
            return anySuccess;
        }

        private async Task<bool> LaunchSingleAppAsync(
            ApplicationDefinition app,
            WorkspaceExecutionContext context,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var appLabel = DescribeApp(app);
                var swEnsure = Stopwatch.StartNew();
                LogPerf($"WorkspaceRuntime: [{appLabel}] Ensure app alive - begin");
                var result = await MakeSureAppAliveAsync(app, context, cancellationToken)
                    .ConfigureAwait(false);
                swEnsure.Stop();
                LogPerf(
                    $"WorkspaceRuntime: [{appLabel}] Ensure app alive - done in {swEnsure.ElapsedMilliseconds} ms; success={result.Succeeded}; windows={result.Windows.Count}; launchedNew={result.LaunchedNewInstance}"
                );
                if (!result.Succeeded || result.Windows.Count == 0)
                {
                    return false;
                }

                var window = result.Windows[0];
                var swPlace = Stopwatch.StartNew();
                NativeWindowHelper.SetWindowPlacement(
                    window.Handle,
                    app.Position,
                    app.Maximized,
                    app.Minimized,
                    result.LaunchedNewInstance
                );
                swPlace.Stop();
                LogPerf(
                    $"WorkspaceRuntime: [{appLabel}] SetWindowPlacement took {swPlace.ElapsedMilliseconds} ms"
                );
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(
                    $"WorkspaceRuntime: failed to launch '{DescribeApp(app)}' - {ex.Message}"
                );
                return false;
            }
        }

        private async Task<AppLauncher.AppWindowResult> MakeSureAppAliveAsync(
            ApplicationDefinition app,
            WorkspaceExecutionContext context,
            CancellationToken cancellationToken
        )
        {
            if (app == null)
            {
                return AppLauncher.AppWindowResult.Failed;
            }

            var existingWindows = context.GetWorkspaceWindows(app);
            if (existingWindows.Count > 0)
            {
                return new AppLauncher.AppWindowResult(true, false, existingWindows);
            }

            return await AppLauncher.LaunchAsync(app, context, _windowTracker, WindowWaitTimeout, WindowPollInterval, cancellationToken)
                .ConfigureAwait(false);
        }

        private void MinimizeExtraneousWindows(WorkspaceExecutionContext context)
        {
            try
            {
                var currentProcessId = (uint)Environment.ProcessId;
                var keepHandles = context.GetWorkspaceHandles();
                var snapshot = _windowTracker.GetSnapshot();

                foreach (var window in snapshot)
                {
                    if (window.ProcessId == currentProcessId)
                    {
                        continue;
                    }

                    if (keepHandles.Contains(window.Handle))
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
