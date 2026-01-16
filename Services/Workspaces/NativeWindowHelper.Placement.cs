// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TopToolbar.Services.Workspaces
{
    internal static partial class NativeWindowHelper
    {
        public static void SetWindowPlacement(
            IntPtr hwnd,
            ApplicationDefinition.ApplicationPosition position,
            bool maximize,
            bool minimize,
            bool waitForInputIdle = false
        )
        {
            if (hwnd == IntPtr.Zero || position == null || position.IsEmpty)
            {
                TopToolbar.Logging.AppLogger.LogInfo($"SetWindowPlacement: skipped - hwnd={hwnd}, position={position?.X},{position?.Y},{position?.Width},{position?.Height}, isEmpty={position?.IsEmpty}");
                return;
            }

            if (waitForInputIdle)
            {
                WaitForWindowInputIdle(hwnd);
            }

            if (!EnsureWindowVisible(hwnd))
            {
                TopToolbar.Logging.AppLogger.LogInfo($"SetWindowPlacement: EnsureWindowVisible failed for hwnd={hwnd}");
                return;
            }

            var placementApplied = SetWindowPos(
                hwnd,
                IntPtr.Zero,
                position.X,
                position.Y,
                position.Width,
                position.Height,
                SwpNoActivate | SwpNoZOrder
            );

            if (!placementApplied)
            {
                TopToolbar.Logging.AppLogger.LogInfo($"SetWindowPlacement: SetWindowPos failed for hwnd={hwnd}, but will still try ShowWindow");
                // Don't return here - still try to minimize/maximize even if position failed
                // This can happen with elevated windows (UIPI)
            }

            TopToolbar.Logging.AppLogger.LogInfo($"SetWindowPlacement: hwnd={hwnd}, minimize={minimize}, maximize={maximize}");
            if (minimize)
            {
                var result = ShowWindow(hwnd, SwShowMinimized);
                TopToolbar.Logging.AppLogger.LogInfo($"SetWindowPlacement: ShowWindow(minimized) result={result}");
            }
            else if (maximize)
            {
                var result = ShowWindow(hwnd, SwShowMaximized);
                TopToolbar.Logging.AppLogger.LogInfo($"SetWindowPlacement: ShowWindow(maximized) result={result}");
            }
            else
            {
                var result = ShowWindow(hwnd, SwShowNormal);
                TopToolbar.Logging.AppLogger.LogInfo($"SetWindowPlacement: ShowWindow(normal) result={result}");
                if (placementApplied)
                {
                    VerifyWindowPlacementWithRetry(hwnd, position);
                }
            }
        }

        private static bool EnsureWindowVisible(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                return false;
            }

            if (IsWindowVisible(hwnd))
            {
                return true;
            }

            _ = ShowWindow(hwnd, SwShowNormal);
            return WaitForWindowVisible(hwnd);
        }

        private static void VerifyWindowPlacementWithRetry(
            IntPtr hwnd,
            ApplicationDefinition.ApplicationPosition position
        )
        {
            if (hwnd == IntPtr.Zero || position == null || position.IsEmpty)
            {
                return;
            }

            for (var attempt = 0; attempt < WindowPlacementRetryAttempts; attempt++)
            {
                Task.Delay(WindowPlacementRetryDelayMilliseconds).Wait();

                if (!IsWindow(hwnd))
                {
                    return;
                }

                if (IsWindowAtExpectedPlacement(hwnd, position))
                {
                    return;
                }

                _ = SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    position.X,
                    position.Y,
                    position.Width,
                    position.Height,
                    SwpNoActivate | SwpNoZOrder
                );
            }
        }

        private static bool IsWindowAtExpectedPlacement(
            IntPtr hwnd,
            ApplicationDefinition.ApplicationPosition position
        )
        {
            var bounds = GetWindowBounds(hwnd);
            if (bounds.IsEmpty)
            {
                return false;
            }

            return Math.Abs(bounds.Left - position.X) <= WindowPlacementTolerancePixels
                && Math.Abs(bounds.Top - position.Y) <= WindowPlacementTolerancePixels
                && Math.Abs(bounds.Width - position.Width) <= WindowPlacementTolerancePixels
                && Math.Abs(bounds.Height - position.Height) <= WindowPlacementTolerancePixels;
        }

        private static bool WaitForWindowVisible(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                return false;
            }

            if (IsWindowVisible(hwnd))
            {
                return true;
            }

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < WindowVisibilityTimeoutMilliseconds)
            {
                if (!IsWindow(hwnd))
                {
                    return false;
                }

                if (IsWindowVisible(hwnd))
                {
                    return true;
                }

                Task.Delay(WindowVisibilityPollMilliseconds).Wait();
            }

            return IsWindowVisible(hwnd);
        }

        private static void WaitForWindowInputIdle(IntPtr hwnd)
        {
            try
            {
                var threadId = GetWindowThreadProcessId(hwnd, out var processId);
                if (threadId == 0 || processId == 0)
                {
                    return;
                }

                using var process = Process.GetProcessById((int)processId);
                if (process.HasExited)
                {
                    return;
                }

                _ = process.WaitForInputIdle(WaitForInputIdleTimeoutMilliseconds);
            }
            catch (ArgumentException)
            {
                // Process exited before it became idle.
            }
            catch (InvalidOperationException)
            {
                // Process has no graphical interface to wait on.
            }
            catch (Win32Exception)
            {
                // Access denied or process information unavailable.
            }
        }

        public static bool TryGetWindowPlacement(
            IntPtr hwnd,
            out WindowBounds normalBounds,
            out bool isMinimized,
            out bool isMaximized
        )
        {
            normalBounds = default;
            isMinimized = false;
            isMaximized = false;

            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var placement = new NativeWindowPlacement
            {
                Length = Marshal.SizeOf<NativeWindowPlacement>(),
            };

            if (!GetWindowPlacement(hwnd, ref placement))
            {
                return false;
            }

            isMinimized = placement.ShowCmd == SwShowMinimized;
            isMaximized = placement.ShowCmd == SwShowMaximized;

            var rect = placement.NormalPosition;
            normalBounds = new WindowBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return true;
        }

        public static void MinimizeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            _ = ShowWindow(hwnd, SwShowMinimized);
        }

        public static bool CanMinimizeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var style = GetWindowLongPtr(hwnd, GwlpStyle).ToInt64();
            if ((style & WsMinimize) == WsMinimize)
            {
                return false;
            }

            if ((style & WsMinimizeBox) != WsMinimizeBox)
            {
                return false;
            }

            return true;
        }
    }
}
