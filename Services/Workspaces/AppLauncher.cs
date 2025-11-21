// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Management.Deployment;
using TopToolbar.Logging;

namespace TopToolbar.Services.Workspaces
{
    internal static class AppLauncher
    {
        internal readonly record struct AppWindowResult(bool Succeeded, bool LaunchedNewInstance, IReadOnlyList<WindowInfo> Windows)
        {
            public static AppWindowResult Failed => new(false, false, Array.Empty<WindowInfo>());
        }

        public static async Task<AppWindowResult> LaunchAsync(
            ApplicationDefinition app,
            WorkspaceExecutionContext context,
            WindowTracker windowTracker,
            TimeSpan windowWaitTimeout,
            TimeSpan windowPollInterval,
            CancellationToken cancellationToken)
        {
            if (app == null)
            {
                return AppWindowResult.Failed;
            }

            // Priority: AUMID -> PackageFullName -> Path.
            if (!string.IsNullOrWhiteSpace(app.AppUserModelId))
            {
                var result = await LaunchByAppUserModelIdAsync(app, context, windowTracker, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    return result;
                }
            }

            if (!string.IsNullOrWhiteSpace(app.PackageFullName))
            {
                var result = await LaunchByPackageFullNameAsync(app, context, windowTracker, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    return result;
                }
            }

            if (!string.IsNullOrWhiteSpace(app.Path))
            {
                return await LaunchWin32AppAsync(app, context, windowTracker, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
            }

            return AppWindowResult.Failed;
        }

        private static async Task<AppWindowResult> LaunchByAppUserModelIdAsync(
            ApplicationDefinition app,
            WorkspaceExecutionContext context,
            WindowTracker windowTracker,
            TimeSpan windowWaitTimeout,
            TimeSpan windowPollInterval,
            CancellationToken cancellationToken)
        {
            var knownHandles = context.GetKnownHandles(app);

            try
            {
                var activationManager = (IApplicationActivationManager)new ApplicationActivationManager();
                var hr = activationManager.ActivateApplication(
                    app.AppUserModelId,
                    string.IsNullOrWhiteSpace(app.CommandLineArguments) ? string.Empty : app.CommandLineArguments,
                    ActivateOptions.None,
                    out var processId);

                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                await WaitAndMergeWindowsAsync(app, context, windowTracker, knownHandles, 0, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
                return new AppWindowResult(true, true, context.GetWorkspaceWindows(app));
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x80040154)
            {
                AppLogger.LogWarning($"WorkspaceRuntime: ApplicationActivationManager not registered. Falling back to shell launch for '{DescribeApp(app)}'.");
                return await LaunchPackagedAppViaShellAsync(app, context, windowTracker, knownHandles, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (COMException ex)
            {
                AppLogger.LogWarning($"WorkspaceRuntime: ActivateApplication failed for '{DescribeApp(app)}' - 0x{ex.HResult:X8} {ex.Message}. Falling back to shell launch.");
                return await LaunchPackagedAppViaShellAsync(app, context, windowTracker, knownHandles, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"WorkspaceRuntime: Unexpected error launching '{DescribeApp(app)}' - {ex.Message}. Falling back to shell launch.");
                return await LaunchPackagedAppViaShellAsync(app, context, windowTracker, knownHandles, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<AppWindowResult> LaunchPackagedAppViaShellAsync(
            ApplicationDefinition app,
            WorkspaceExecutionContext context,
            WindowTracker windowTracker,
            IReadOnlyCollection<IntPtr> knownHandles,
            TimeSpan windowWaitTimeout,
            TimeSpan windowPollInterval,
            CancellationToken cancellationToken)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:appsFolder\\{app.AppUserModelId}",
                    UseShellExecute = true,
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return AppWindowResult.Failed;
                }

                await WaitAndMergeWindowsAsync(app, context, windowTracker, knownHandles, 0, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
                return new AppWindowResult(true, true, context.GetWorkspaceWindows(app));
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"WorkspaceRuntime: shell launch failed for '{app.AppUserModelId}' - {ex.Message}");
                return AppWindowResult.Failed;
            }
        }

        private static async Task<AppWindowResult> LaunchByPackageFullNameAsync(
            ApplicationDefinition app,
            WorkspaceExecutionContext context,
            WindowTracker windowTracker,
            TimeSpan windowWaitTimeout,
            TimeSpan windowPollInterval,
            CancellationToken cancellationToken)
        {
            var knownHandles = context.GetKnownHandles(app);

            try
            {
                var pm = new PackageManager();
                var package = pm.FindPackageForUser(string.Empty, app.PackageFullName);
                if (package == null)
                {
                    AppLogger.LogWarning($"WorkspaceRuntime: package '{app.PackageFullName}' not found for '{DescribeApp(app)}'.");
                    return AppWindowResult.Failed;
                }

                var entries = await package.GetAppListEntriesAsync().AsTask(cancellationToken).ConfigureAwait(false);
                var entry = entries.FirstOrDefault();
                if (entry == null)
                {
                    AppLogger.LogWarning($"WorkspaceRuntime: no AppListEntry for package '{app.PackageFullName}' ({DescribeApp(app)}).");
                    return AppWindowResult.Failed;
                }

                var launched = await entry.LaunchAsync().AsTask(cancellationToken).ConfigureAwait(false);
                if (!launched)
                {
                    AppLogger.LogWarning($"WorkspaceRuntime: LaunchAsync returned false for package '{app.PackageFullName}' ({DescribeApp(app)}).");
                    return AppWindowResult.Failed;
                }

                await WaitAndMergeWindowsAsync(app, context, windowTracker, knownHandles, 0, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
                return new AppWindowResult(true, true, context.GetWorkspaceWindows(app));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"WorkspaceRuntime: package launch failed for '{DescribeApp(app)}' ({app.PackageFullName}) - {ex.Message}");
                return AppWindowResult.Failed;
            }
        }

        private static async Task<AppWindowResult> LaunchWin32AppAsync(
            ApplicationDefinition app,
            WorkspaceExecutionContext context,
            WindowTracker windowTracker,
            TimeSpan windowWaitTimeout,
            TimeSpan windowPollInterval,
            CancellationToken cancellationToken)
        {
            var knownHandles = context.GetKnownHandles(app);
            var expandedPath = ExpandPath(app.Path);
            var useShellExecute =
                expandedPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(expandedPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = expandedPath,
                Arguments = string.IsNullOrWhiteSpace(app.CommandLineArguments) ? string.Empty : app.CommandLineArguments,
                UseShellExecute = useShellExecute,
                WorkingDirectory = DetermineWorkingDirectory(expandedPath, useShellExecute),
            };

            if (app.IsElevated && app.CanLaunchElevated)
            {
                startInfo.Verb = "runas";
                startInfo.UseShellExecute = true;
            }

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    AppLogger.LogWarning($"WorkspaceRuntime: process start returned null for '{DescribeApp(app)}' ({expandedPath}).");
                    return AppWindowResult.Failed;
                }

                const uint targetProcessId = 0;
                if (process.HasExited)
                {
                    var succeeded = process.ExitCode == 0;
                    await WaitAndMergeWindowsAsync(app, context, windowTracker, knownHandles, targetProcessId, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
                    return succeeded ? new AppWindowResult(true, true, context.GetWorkspaceWindows(app)) : AppWindowResult.Failed;
                }

                await WaitAndMergeWindowsAsync(app, context, windowTracker, knownHandles, targetProcessId, windowWaitTimeout, windowPollInterval, cancellationToken).ConfigureAwait(false);
                return new AppWindowResult(true, true, context.GetWorkspaceWindows(app));
            }
            catch (Win32Exception ex)
            {
                AppLogger.LogWarning($"WorkspaceRuntime: Win32Exception launching '{DescribeApp(app)}' ({expandedPath}) - {ex.Message} ({ex.NativeErrorCode}).");
                return AppWindowResult.Failed;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"WorkspaceRuntime: failed to start '{DescribeApp(app)}' ({expandedPath}) - {ex.Message}");
                return AppWindowResult.Failed;
            }
        }

        private static async Task WaitAndMergeWindowsAsync(
            ApplicationDefinition app,
            WorkspaceExecutionContext context,
            WindowTracker windowTracker,
            IReadOnlyCollection<IntPtr> knownHandles,
            uint expectedProcessId,
            TimeSpan windowWaitTimeout,
            TimeSpan windowPollInterval,
            CancellationToken cancellationToken)
        {
            var windows = await windowTracker
                .WaitForAppWindowsAsync(
                    app,
                    knownHandles,
                    expectedProcessId,
                    windowWaitTimeout,
                    windowPollInterval,
                    cancellationToken
                )
                .ConfigureAwait(false);

            IReadOnlyList<WindowInfo> matches = windows;
            if (matches.Count == 0)
            {
                matches = windowTracker.FindMatches(app);
            }

            if (matches.Count > 0)
            {
                context.MergeWindows(app, matches, markLaunched: true);
            }
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
            catch
            {
            }

            return AppContext.BaseDirectory;
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

        [ComImport]
        [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            int ActivateApplication(string appUserModelId, string arguments, ActivateOptions options, out uint processId);

            int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);

            int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
        }

        [ComImport]
        [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
        private class ApplicationActivationManager
        {
        }

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
