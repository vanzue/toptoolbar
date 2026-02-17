// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
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

        private static void LogPerf(string message)
        {
            try
            {
                AppLogger.LogInfo(message);
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

        private WindowPlacement ResolveTargetPlacement(WorkspaceDefinition workspace, ApplicationDefinition app)
        {
            if (app?.Position == null || app.Position.IsEmpty)
            {
                return default;
            }

            var basePlacement = new WindowPlacement(
                app.Position.X,
                app.Position.Y,
                app.Position.Width,
                app.Position.Height);

            if (_displayManager == null || workspace?.Monitors == null || workspace.Monitors.Count == 0)
            {
                return basePlacement;
            }

            var sourceMonitor = workspace.Monitors.FirstOrDefault(m => m?.Number == app.MonitorIndex)
                ?? workspace.Monitors.FirstOrDefault();
            if (sourceMonitor == null)
            {
                return basePlacement;
            }

            var currentMonitors = _displayManager.GetSnapshot();
            if (currentMonitors.Count == 0)
            {
                return basePlacement;
            }

            var targetMonitor = FindTargetMonitor(currentMonitors, sourceMonitor);
            if (targetMonitor == null)
            {
                return basePlacement;
            }

            var srcRect = GetSourceMonitorRect(sourceMonitor);
            if (srcRect == null || srcRect.Width <= 0 || srcRect.Height <= 0)
            {
                return basePlacement;
            }

            var dstRect = !targetMonitor.DpiAwareRect.IsEmpty
                ? targetMonitor.DpiAwareRect
                : targetMonitor.DpiUnawareRect;
            if (dstRect.Width <= 0 || dstRect.Height <= 0)
            {
                return basePlacement;
            }

            var scaleX = (double)dstRect.Width / srcRect.Width;
            var scaleY = (double)dstRect.Height / srcRect.Height;

            var relX = basePlacement.X - srcRect.Left;
            var relY = basePlacement.Y - srcRect.Top;

            var newX = dstRect.Left + (int)Math.Round(relX * scaleX);
            var newY = dstRect.Top + (int)Math.Round(relY * scaleY);
            var newW = (int)Math.Round(basePlacement.Width * scaleX);
            var newH = (int)Math.Round(basePlacement.Height * scaleY);

            return new WindowPlacement(newX, newY, newW, newH);
        }

        private static DisplayMonitor FindTargetMonitor(
            IReadOnlyList<DisplayMonitor> monitors,
            MonitorDefinition source)
        {
            if (monitors == null || monitors.Count == 0 || source == null)
            {
                return null;
            }

            var match = !string.IsNullOrWhiteSpace(source.Id)
                ? monitors.FirstOrDefault(m => string.Equals(m.Id, source.Id, StringComparison.OrdinalIgnoreCase))
                : null;

            if (match == null && !string.IsNullOrWhiteSpace(source.InstanceId))
            {
                match = monitors.FirstOrDefault(m => string.Equals(m.InstanceId, source.InstanceId, StringComparison.OrdinalIgnoreCase));
            }

            if (match == null)
            {
                match = monitors.FirstOrDefault(m => m.Index == source.Number);
            }

            return match ?? monitors[0];
        }

        private static MonitorDefinition.MonitorRect GetSourceMonitorRect(MonitorDefinition monitor)
        {
            if (monitor == null)
            {
                return null;
            }

            if (monitor.DpiAwareRect != null
                && monitor.DpiAwareRect.Width > 0
                && monitor.DpiAwareRect.Height > 0)
            {
                return monitor.DpiAwareRect;
            }

            if (monitor.DpiUnawareRect != null
                && monitor.DpiUnawareRect.Width > 0
                && monitor.DpiUnawareRect.Height > 0)
            {
                return monitor.DpiUnawareRect;
            }

            return null;
        }

        private async Task TryUpdateLastLaunchedTimeAsync(
            WorkspaceDefinition workspace,
            CancellationToken cancellationToken)
        {
            if (workspace == null || _definitionStore == null)
            {
                return;
            }

            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await _definitionStore.UpdateLastLaunchedTimeAsync(
                    workspace.Id,
                    timestamp,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private static bool IsWindowOnCurrentDesktop(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            if (NativeWindowHelper.TryIsWindowOnCurrentVirtualDesktop(handle, out var isOnCurrentDesktop))
            {
                return isOnCurrentDesktop;
            }

            return false;
        }

        private bool EnsureWindowOnCurrentDesktop(
            IntPtr handle,
            string appLabel,
            string stage)
        {
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var querySucceeded = NativeWindowHelper.TryIsWindowOnCurrentVirtualDesktop(handle, out var isOnCurrentDesktop);
            if (querySucceeded && isOnCurrentDesktop)
            {
                return true;
            }

            if (!querySucceeded)
            {
                LogPerf($"WorkspaceRuntime: [{appLabel}] {stage} - desktop query unavailable for handle={handle}; treating as off-desktop");
            }

            LogPerf($"WorkspaceRuntime: [{appLabel}] {stage} - window handle={handle} is not on current virtual desktop");
            return false;
        }
    }
}
