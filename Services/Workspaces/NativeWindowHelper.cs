// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TopToolbar.Services.Workspaces
{
    internal static partial class NativeWindowHelper
    {
        private const int SwShowNormal = 1;
        private const int SwShowMinimized = 2;
        private const int SwShowMaximized = 3;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint GwOwner = 4;
        private const int GwlpStyle = -16;
        private const int GwlpExStyle = -20;
        private const long WsDisabled = 0x08000000L;
        private const long WsVisible = 0x10000000L;
        private const long WsMinimize = 0x20000000L;
        private const long WsChild = 0x40000000L;
        private const long WsMinimizeBox = 0x00020000L;
        private const long WsExToolWindow = 0x00000080L;
        private const ushort ValueTypeLpwstr = 31;
        private const int WaitForInputIdleTimeoutMilliseconds = 5000;
        private const int WindowVisibilityTimeoutMilliseconds = 5000;
        private const int WindowVisibilityPollMilliseconds = 50;
        private const int WindowPlacementRetryDelayMilliseconds = 150;
        private const int WindowPlacementRetryAttempts = 30;
        private const int WindowPlacementTolerancePixels = 8;

        public static IReadOnlyList<IntPtr> EnumerateProcessWindows(int processId)
        {
            if (processId <= 0)
            {
                return Array.Empty<IntPtr>();
            }

            var windows = new List<IntPtr>();
            _ = EnumWindows(
                (hwnd, _) =>
                {
                    if (!IsTopLevelWindow(hwnd))
                    {
                        return true;
                    }

                    var threadId = GetWindowThreadProcessId(hwnd, out var windowProcessId);
                    if (threadId == 0 || windowProcessId == 0)
                    {
                        return true;
                    }

                    if ((int)windowProcessId == processId)
                    {
                        windows.Add(hwnd);
                    }

                    return true;
                },
                IntPtr.Zero
            );

            return windows;
        }

        public static bool TryCreateWindowInfo(IntPtr hwnd, out WindowInfo info)
        {
            info = null;

            if (!IsTopLevelWindow(hwnd))
            {
                return false;
            }

            var threadId = GetWindowThreadProcessId(hwnd, out var processId);
            if (threadId == 0 || processId == 0)
            {
                return false;
            }

            var title = GetWindowTitle(hwnd);
            var bounds = GetWindowBounds(hwnd);
            var isVisible = IsWindowVisible(hwnd);
            var processPath = TryGetProcessPath(processId);
            var processFileName = string.IsNullOrWhiteSpace(processPath)
                ? string.Empty
                : Path.GetFileName(processPath);
            var processName = string.IsNullOrWhiteSpace(processFileName)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(processFileName);
            var packageFullName = TryGetPackageFullName(processId);
            var appUserModelId = TryGetAppUserModelId(hwnd);
            var className = GetWindowClassName(hwnd);

            info = new WindowInfo(
                hwnd,
                processId,
                processPath,
                processFileName,
                processName,
                packageFullName,
                title,
                appUserModelId,
                isVisible,
                bounds,
                className
            );
            return true;
        }

        public static bool HasToolWindowStyle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var exStyle = GetWindowLongPtr(hwnd, GwlpExStyle).ToInt64();
            return (exStyle & WsExToolWindow) == WsExToolWindow;
        }

        public static bool IsTopLevelWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || hwnd == GetShellWindow())
            {
                return false;
            }

            if (GetWindow(hwnd, GwOwner) != IntPtr.Zero)
            {
                return false;
            }

            var style = GetWindowLongPtr(hwnd, GwlpStyle).ToInt64();
            if ((style & WsChild) == WsChild)
            {
                return false;
            }

            if ((style & WsVisible) != WsVisible)
            {
                return false;
            }

            if ((style & WsDisabled) == WsDisabled)
            {
                return false;
            }

            return IsWindow(hwnd);
        }

        public static bool IsMatch(WindowInfo window, ApplicationDefinition app)
        {
            if (window == null || app == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(app.AppUserModelId))
            {
                if (
                    !string.IsNullOrWhiteSpace(window.AppUserModelId)
                    && string.Equals(
                        window.AppUserModelId,
                        app.AppUserModelId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            var normalizedAppPath = NormalizePath(app.Path);
            if (
                !string.IsNullOrWhiteSpace(normalizedAppPath)
                && !string.IsNullOrWhiteSpace(window.ProcessPath)
                && string.Equals(
                    window.ProcessPath,
                    normalizedAppPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(app.Path))
            {
                var appFileName = NormalizeFileName(app.Path);
                if (
                    !string.IsNullOrWhiteSpace(appFileName)
                    && !string.IsNullOrWhiteSpace(window.ProcessFileName)
                    && string.Equals(
                        window.ProcessFileName,
                        appFileName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            if (
                !string.IsNullOrWhiteSpace(app.Name)
                && !string.IsNullOrWhiteSpace(window.ProcessName)
                && string.Equals(
                    NormalizeProcessName(window.ProcessName),
                    NormalizeProcessName(app.Name),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            if (
                !string.IsNullOrWhiteSpace(app.Title)
                && !string.IsNullOrWhiteSpace(window.Title)
                && string.Equals(window.Title, app.Title, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            return false;
        }

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

        private static string NormalizeFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(path).Trim('"');
                return Path.GetFileName(expanded);
            }
            catch
            {
                return Path.GetFileName(path.Trim('"'));
            }
        }

        private static string NormalizeProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return string.Empty;
            }

            var trimmed = processName.Trim();
            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 4);
            }

            return trimmed;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(path).Trim('"');
                if (expanded.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    return expanded;
                }

                return Path.GetFullPath(expanded);
            }
            catch
            {
                return path.Trim('"');
            }
        }

        private static string TryGetProcessPath(uint processId)
        {
            if (processId == 0)
            {
                return string.Empty;
            }

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(ProcessAccess.QueryLimitedInformation, false, processId);
                if (handle == IntPtr.Zero)
                {
                    return string.Empty;
                }

                var capacity = 1024u;
                var builder = new StringBuilder((int)capacity);
                if (QueryFullProcessImageName(handle, 0, builder, ref capacity))
                {
                    return builder.ToString();
                }
            }
            catch { }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    _ = CloseHandle(handle);
                }
            }

            return string.Empty;
        }

        private static WindowBounds GetWindowBounds(IntPtr hwnd)
        {
            if (GetWindowRect(hwnd, out var rect))
            {
                return new WindowBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }

            return new WindowBounds(0, 0, 0, 0);
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            try
            {
                var length = GetWindowTextLength(hwnd);
                if (length <= 0)
                {
                    return string.Empty;
                }

                var builder = new StringBuilder(length + 1);
                _ = GetWindowText(hwnd, builder, builder.Capacity);
                return builder.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetWindowClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(256);
            var read = GetClassName(hwnd, builder, builder.Capacity);
            if (read <= 0)
            {
                return string.Empty;
            }

            return builder.ToString();
        }

        private static string TryGetAppUserModelId(IntPtr hwnd)
        {
            IntPtr propertyStorePtr = IntPtr.Zero;
            try
            {
                var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
                var hr = SHGetPropertyStoreForWindow(hwnd, ref iid, out propertyStorePtr);
                if (hr != 0 || propertyStorePtr == IntPtr.Zero)
                {
                    return string.Empty;
                }

                var key = new PropertyKey
                {
                    FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                    PropertyId = 5,
                };

                var value = new PropVariant();
                hr = PropertyStoreGetValue(propertyStorePtr, ref key, ref value);
                if (hr != 0)
                {
                    return string.Empty;
                }

                try
                {
                    return ExtractString(value);
                }
                finally
                {
                    _ = PropVariantClear(ref value);
                }
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                if (propertyStorePtr != IntPtr.Zero)
                {
                    _ = Marshal.Release(propertyStorePtr);
                }
            }
        }

        // IPropertyStore::GetValue is at vtable index 5 (after IUnknown's 3 methods + GetCount + GetAt)
        private static int PropertyStoreGetValue(IntPtr propertyStore, ref PropertyKey key, ref PropVariant value)
        {
            // Get the vtable pointer
            var vtable = Marshal.ReadIntPtr(propertyStore);
            // GetValue is at index 5 in the vtable (0=QueryInterface, 1=AddRef, 2=Release, 3=GetCount, 4=GetAt, 5=GetValue)
            var getValuePtr = Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size);
            var getValueDelegate = Marshal.GetDelegateForFunctionPointer<PropertyStoreGetValueDelegate>(getValuePtr);
            return getValueDelegate(propertyStore, ref key, ref value);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PropertyStoreGetValueDelegate(IntPtr thisPtr, ref PropertyKey key, ref PropVariant value);

        private static string ExtractString(PropVariant value)
        {
            if (value.ValueType == ValueTypeLpwstr && value.PointerValue != IntPtr.Zero)
            {
                return Marshal.PtrToStringUni(value.PointerValue) ?? string.Empty;
            }

            return string.Empty;
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags
        );

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr hWnd,
            StringBuilder lpClassName,
            int nMaxCount
        );

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowPlacement(
            IntPtr hWnd,
            ref NativeWindowPlacement lpwndpl
        );

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetPackageFullName(
            IntPtr hProcess,
            ref int packageFullNameLength,
            StringBuilder packageFullName
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            ProcessAccess desiredAccess,
            bool inheritHandle,
            uint processId
        );

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(
            IntPtr hProcess,
            int dwFlags,
            StringBuilder lpExeName,
            ref uint lpdwSize
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHGetPropertyStoreForWindow(
            IntPtr hwnd,
            ref Guid riid,
            out IntPtr propertyStore
        );

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeWindowPlacement
        {
            public int Length;

            public int Flags;

            public int ShowCmd;

            public NativePoint MinPosition;

            public NativePoint MaxPosition;

            public NativeRect NormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;

            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [Flags]
        private enum ProcessAccess : uint
        {
            QueryLimitedInformation = 0x1000,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public uint PropertyId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort ValueType;
            public ushort Reserved1;
            public ushort Reserved2;
            public ushort Reserved3;
            public IntPtr PointerValue;
            public int IntValue;
            public int Reserved4;
            public int Reserved5;
        }

        private static string TryGetPackageFullName(uint processId)
        {
            if (processId == 0)
            {
                return string.Empty;
            }

            var handle = OpenProcess(ProcessAccess.QueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                // First call to get the required length.
                int length = 0;
                var hr = GetPackageFullName(handle, ref length, null);
                const int ErrorInsufficientBuffer = 122;
                const int AppModelErrorNoPackage = 15700;
                if (hr == AppModelErrorNoPackage)
                {
                    return string.Empty;
                }

                if (hr != ErrorInsufficientBuffer || length <= 0)
                {
                    return string.Empty;
                }

                var builder = new StringBuilder(length);
                hr = GetPackageFullName(handle, ref length, builder);
                if (hr == 0)
                {
                    return builder.ToString();
                }
            }
            catch
            {
            }
            finally
            {
                _ = CloseHandle(handle);
            }

            return string.Empty;
        }
    }
}
