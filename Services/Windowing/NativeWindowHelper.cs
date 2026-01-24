// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TopToolbar.Services.Windowing
{
    internal static partial class NativeWindowHelper
    {
        private static readonly Lazy<IVirtualDesktopManager> VirtualDesktopManagerInstance = new(
            () =>
            {
                try
                {
                    return (IVirtualDesktopManager)new VirtualDesktopManager();
                }
                catch
                {
                    return null;
                }
            });

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
                className,
                string.Empty,
                0
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

        public static bool TryIsWindowOnCurrentVirtualDesktop(
            IntPtr hwnd,
            out bool isOnCurrentDesktop)
        {
            isOnCurrentDesktop = true;

            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var manager = VirtualDesktopManagerInstance.Value;
                if (manager == null)
                {
                    return false;
                }

                var hr = manager.IsWindowOnCurrentVirtualDesktop(hwnd, out isOnCurrentDesktop);
                return hr == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
