// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TopToolbar.Services.Workspaces
{
    /// <summary>
    /// Tracks which windows are managed by which workspace applications.
    /// When a workspace is snapshotted, windows are bound to their app configurations.
    /// When launching, we use these bindings to identify which window belongs to which app.
    /// </summary>
    internal sealed class ManagedWindowRegistry
    {
        private readonly object _gate = new();

        // Maps app ID -> window handle
        private readonly Dictionary<string, IntPtr> _appToWindow = new(StringComparer.OrdinalIgnoreCase);

        // Maps window handle -> app ID (reverse lookup)
        private readonly Dictionary<IntPtr, string> _windowToApp = new();

        // Tracks which workspace each app belongs to
        private readonly Dictionary<string, string> _appToWorkspace = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Binds a window handle to an application definition.
        /// </summary>
        public void BindWindow(string workspaceId, string appId, IntPtr hwnd)
        {
            if (string.IsNullOrWhiteSpace(appId) || hwnd == IntPtr.Zero)
            {
                return;
            }

            lock (_gate)
            {
                // Remove any existing binding for this app
                if (_appToWindow.TryGetValue(appId, out var oldHwnd))
                {
                    _windowToApp.Remove(oldHwnd);
                }

                // Remove any existing binding for this window (it might be bound to another app)
                if (_windowToApp.TryGetValue(hwnd, out var oldAppId))
                {
                    _appToWindow.Remove(oldAppId);
                    _appToWorkspace.Remove(oldAppId);
                }

                _appToWindow[appId] = hwnd;
                _windowToApp[hwnd] = appId;

                if (!string.IsNullOrWhiteSpace(workspaceId))
                {
                    _appToWorkspace[appId] = workspaceId;
                }
            }
        }

        /// <summary>
        /// Atomically tries to bind a window to an app if it's not already bound to another app.
        /// Returns true if successfully bound (or was already bound to the same app), false if bound to another app.
        /// </summary>
        public bool TryBindWindow(string workspaceId, string appId, IntPtr hwnd)
        {
            if (string.IsNullOrWhiteSpace(appId) || hwnd == IntPtr.Zero)
            {
                return false;
            }

            lock (_gate)
            {
                // Check if already bound to another app
                if (_windowToApp.TryGetValue(hwnd, out var existingAppId))
                {
                    if (!string.Equals(existingAppId, appId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Window is already bound to a different app
                        return false;
                    }
                    // Already bound to the same app - success
                    return true;
                }

                // Remove any existing binding for this app (app might have a different window bound)
                if (_appToWindow.TryGetValue(appId, out var oldHwnd))
                {
                    _windowToApp.Remove(oldHwnd);
                }

                _appToWindow[appId] = hwnd;
                _windowToApp[hwnd] = appId;

                if (!string.IsNullOrWhiteSpace(workspaceId))
                {
                    _appToWorkspace[appId] = workspaceId;
                }

                return true;
            }
        }

        /// <summary>
        /// Gets the window handle bound to an application, if any.
        /// Returns IntPtr.Zero if no window is bound, if the window no longer exists,
        /// or if the window has been rebound to another app.
        /// </summary>
        public IntPtr GetBoundWindow(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
            {
                return IntPtr.Zero;
            }

            lock (_gate)
            {
                if (!_appToWindow.TryGetValue(appId, out var hwnd))
                {
                    return IntPtr.Zero;
                }

                // Verify bidirectional consistency - the window should still be bound to this app
                if (_windowToApp.TryGetValue(hwnd, out var currentAppId))
                {
                    if (!string.Equals(currentAppId, appId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Window has been rebound to another app - clean up stale mapping
                        _appToWindow.Remove(appId);
                        _appToWorkspace.Remove(appId);
                        return IntPtr.Zero;
                    }
                }
                else
                {
                    // Inconsistent state - clean up
                    _appToWindow.Remove(appId);
                    _appToWorkspace.Remove(appId);
                    return IntPtr.Zero;
                }

                // Check if window still exists
                if (!IsWindow(hwnd))
                {
                    // Window was destroyed, clean up binding
                    _appToWindow.Remove(appId);
                    _windowToApp.Remove(hwnd);
                    _appToWorkspace.Remove(appId);
                    return IntPtr.Zero;
                }

                return hwnd;
            }
        }

        /// <summary>
        /// Gets the app ID that a window is bound to, if any.
        /// </summary>
        public string GetBoundAppId(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            lock (_gate)
            {
                return _windowToApp.TryGetValue(hwnd, out var appId) ? appId : null;
            }
        }

        /// <summary>
        /// Gets all window handles that belong to a specific workspace.
        /// </summary>
        public HashSet<IntPtr> GetWorkspaceWindows(string workspaceId)
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return new HashSet<IntPtr>();
            }

            lock (_gate)
            {
                var handles = new HashSet<IntPtr>();

                foreach (var kvp in _appToWorkspace)
                {
                    if (!string.Equals(kvp.Value, workspaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var appId = kvp.Key;
                    if (_appToWindow.TryGetValue(appId, out var hwnd) && hwnd != IntPtr.Zero)
                    {
                        if (IsWindow(hwnd))
                        {
                            handles.Add(hwnd);
                        }
                    }
                }

                return handles;
            }
        }

        /// <summary>
        /// Removes the binding for an application.
        /// </summary>
        public void UnbindApp(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
            {
                return;
            }

            lock (_gate)
            {
                if (_appToWindow.TryGetValue(appId, out var hwnd))
                {
                    _windowToApp.Remove(hwnd);
                }

                _appToWindow.Remove(appId);
                _appToWorkspace.Remove(appId);
            }
        }

        /// <summary>
        /// Removes the binding for a window handle.
        /// Called when a window is destroyed.
        /// </summary>
        public void UnbindWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            lock (_gate)
            {
                if (_windowToApp.TryGetValue(hwnd, out var appId))
                {
                    _appToWindow.Remove(appId);
                    _appToWorkspace.Remove(appId);
                }

                _windowToApp.Remove(hwnd);
            }
        }

        /// <summary>
        /// Clears all bindings for a workspace.
        /// </summary>
        public void ClearWorkspace(string workspaceId)
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return;
            }

            lock (_gate)
            {
                var appsToRemove = new List<string>();

                foreach (var kvp in _appToWorkspace)
                {
                    if (string.Equals(kvp.Value, workspaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        appsToRemove.Add(kvp.Key);
                    }
                }

                foreach (var appId in appsToRemove)
                {
                    if (_appToWindow.TryGetValue(appId, out var hwnd))
                    {
                        _windowToApp.Remove(hwnd);
                    }

                    _appToWindow.Remove(appId);
                    _appToWorkspace.Remove(appId);
                }
            }
        }

        /// <summary>
        /// Clears all bindings.
        /// </summary>
        public void Clear()
        {
            lock (_gate)
            {
                _appToWindow.Clear();
                _windowToApp.Clear();
                _appToWorkspace.Clear();
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);
    }
}
