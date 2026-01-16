// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TopToolbar.Logging;

namespace TopToolbar.Services.Workspaces
{
    internal sealed partial class WorkspacesRuntimeService
    {
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
    }
}
