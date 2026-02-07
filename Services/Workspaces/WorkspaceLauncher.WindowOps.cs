// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
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
        /// Phase 2: Resize a window to its target position
        /// </summary>
        private async Task ResizeWindowAsync(
            IntPtr handle,
            ApplicationDefinition app,
            WindowPlacement targetPosition,
            bool launchedNew,
            CancellationToken cancellationToken
        )
        {
            if (handle == IntPtr.Zero || app == null)
            {
                return;
            }

            // Yield immediately to ensure parallel execution
            await Task.Yield();

            var appLabel = DescribeApp(app);
            var sw = Stopwatch.StartNew();

            try
            {
                if (NativeWindowHelper.IsWindowCloaked(handle))
                {
                    return;
                }

                if (NativeWindowHelper.TryIsWindowOnCurrentVirtualDesktop(handle, out var isOnCurrentDesktop)
                    && !isOnCurrentDesktop)
                {
                    return;
                }

                LogPerf($"WorkspaceRuntime: [{appLabel}] Resize - begin: minimized={app.Minimized}, maximized={app.Maximized}, position=({app.Position?.X},{app.Position?.Y},{app.Position?.Width},{app.Position?.Height})");

                var position = !targetPosition.IsEmpty ? targetPosition : default;

                await NativeWindowHelper.SetWindowPlacementAsync(
                    handle,
                    position,
                    app.Maximized,
                    app.Minimized,
                    launchedNew,
                    cancellationToken
                ).ConfigureAwait(false);

                if (launchedNew && !position.IsEmpty)
                {
                    await MakeSureWindowArrangedAsync(handle, position, app.Maximized, app.Minimized, cancellationToken)
                        .ConfigureAwait(false);
                    await PostSettleWindowAsync(handle, position, app.Maximized, app.Minimized, cancellationToken)
                        .ConfigureAwait(false);
                }

                sw.Stop();
                LogPerf($"WorkspaceRuntime: [{appLabel}] Resize - done in {sw.ElapsedMilliseconds} ms");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AppLogger.LogWarning($"WorkspaceRuntime: [{appLabel}] Resize failed in {sw.ElapsedMilliseconds} ms - {ex.Message}");
            }
        }

        private void MinimizeExtraneousWindows(HashSet<IntPtr> workspaceHandles)
        {
            try
            {
                var currentProcessId = (uint)Environment.ProcessId;
                var snapshot = _windowManager.GetSnapshot();

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

                    if (NativeWindowHelper.IsWindowCloaked(window.Handle))
                    {
                        continue;
                    }

                    if (NativeWindowHelper.TryIsWindowOnCurrentVirtualDesktop(window.Handle, out var isOnCurrentDesktop)
                        && !isOnCurrentDesktop)
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

        private async Task MakeSureWindowArrangedAsync(
            IntPtr handle,
            WindowPlacement position,
            bool maximize,
            bool minimize,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var stableChecks = 0;

            while (sw.Elapsed < WindowArrangeTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!NativeWindowHelper.TryGetWindowPlacement(
                    handle,
                    out var bounds,
                    out var isMinimized,
                    out var isMaximized))
                {
                    return;
                }

                if (IsExpectedPlacement(bounds, position, isMinimized, isMaximized, minimize, maximize))
                {
                    stableChecks++;
                    if (stableChecks >= WindowArrangeStableChecks)
                    {
                        return;
                    }
                }
                else
                {
                    stableChecks = 0;
                    await NativeWindowHelper.SetWindowPlacementAsync(
                        handle,
                        position,
                        maximize,
                        minimize,
                        waitForInputIdle: false,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(WindowArrangePollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task PostSettleWindowAsync(
            IntPtr handle,
            WindowPlacement position,
            bool maximize,
            bool minimize,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();

            while (sw.Elapsed < WindowPostSettleTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (NativeWindowHelper.IsWindowCloaked(handle))
                {
                    return;
                }

                if (NativeWindowHelper.TryIsWindowOnCurrentVirtualDesktop(handle, out var isOnCurrentDesktop)
                    && !isOnCurrentDesktop)
                {
                    return;
                }

                if (!NativeWindowHelper.TryGetWindowPlacement(
                    handle,
                    out var bounds,
                    out var isMinimized,
                    out var isMaximized))
                {
                    return;
                }

                if (!IsExpectedPlacement(bounds, position, isMinimized, isMaximized, minimize, maximize))
                {
                    await NativeWindowHelper.SetWindowPlacementAsync(
                        handle,
                        position,
                        maximize,
                        minimize,
                        waitForInputIdle: false,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(WindowPostSettlePollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool IsExpectedPlacement(
            WindowBounds bounds,
            WindowPlacement target,
            bool isMinimized,
            bool isMaximized,
            bool expectMinimized,
            bool expectMaximized)
        {
            if (expectMinimized)
            {
                return isMinimized;
            }

            if (expectMaximized)
            {
                return isMaximized;
            }

            if (target.IsEmpty || bounds.IsEmpty)
            {
                return false;
            }

            return Math.Abs(bounds.Left - target.X) <= WindowArrangeTolerancePixels
                && Math.Abs(bounds.Top - target.Y) <= WindowArrangeTolerancePixels
                && Math.Abs(bounds.Width - target.Width) <= WindowArrangeTolerancePixels
                && Math.Abs(bounds.Height - target.Height) <= WindowArrangeTolerancePixels;
        }
    }
}
