// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TopToolbar.Services.Workspaces
{
    /// <summary>
    /// Tracks which windows are bound to which workspace applications at runtime.
    /// The launcher creates exclusive bindings; snapshot can add shared bindings for reuse.
    /// Bindings are invalidated when windows are destroyed.
    /// </summary>
    internal sealed class ManagedWindowRegistry
    {
        private readonly object _gate = new();

        // Maps app ID -> window handle
        private readonly Dictionary<string, IntPtr> _appToWindow = new(StringComparer.OrdinalIgnoreCase);

        // Maps window handle -> app IDs (reverse lookup)
        private readonly Dictionary<IntPtr, HashSet<string>> _windowToApps = new();

        // Tracks which workspace each app belongs to
        private readonly Dictionary<string, string> _appToWorkspace = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Binds a window handle to an application definition, allowing shared bindings.
        /// Used by snapshot to record window reuse across workspaces.
        /// </summary>
        public void BindWindowShared(string workspaceId, string appId, IntPtr hwnd)
        {
            if (string.IsNullOrWhiteSpace(appId) || hwnd == IntPtr.Zero)
            {
                return;
            }

            lock (_gate)
            {
                // Remove any existing binding for this app (it might be bound to another window)
                if (_appToWindow.TryGetValue(appId, out var oldHwnd) && oldHwnd != hwnd)
                {
                    RemoveAppFromWindow(oldHwnd, appId);
                }

                _appToWindow[appId] = hwnd;
                AddAppToWindow(hwnd, appId);

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
                if (_windowToApps.TryGetValue(hwnd, out var existingApps) && existingApps.Count > 0)
                {
                    if (!existingApps.Contains(appId))
                    {
                        // Window is already bound to a different app
                        return false;
                    }

                    // Already bound to the same app - success
                    if (_appToWindow.TryGetValue(appId, out var oldHwnd) && oldHwnd != hwnd)
                    {
                        RemoveAppFromWindow(oldHwnd, appId);
                    }

                    _appToWindow[appId] = hwnd;
                    if (!string.IsNullOrWhiteSpace(workspaceId))
                    {
                        _appToWorkspace[appId] = workspaceId;
                    }

                    return true;
                }

                // Remove any existing binding for this app (app might have a different window bound)
                if (_appToWindow.TryGetValue(appId, out var previousHwnd))
                {
                    RemoveAppFromWindow(previousHwnd, appId);
                }

                _appToWindow[appId] = hwnd;
                AddAppToWindow(hwnd, appId);

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
                if (!_windowToApps.TryGetValue(hwnd, out var currentApps) || !currentApps.Contains(appId))
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
                    RemoveAppFromWindow(hwnd, appId);
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
                if (_windowToApps.TryGetValue(hwnd, out var appIds))
                {
                    foreach (var appId in appIds)
                    {
                        return appId;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets all app IDs that a window is bound to, if any.
        /// </summary>
        public IReadOnlyList<string> GetBoundAppIds(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return Array.Empty<string>();
            }

            lock (_gate)
            {
                if (_windowToApps.TryGetValue(hwnd, out var appIds) && appIds.Count > 0)
                {
                    return new List<string>(appIds);
                }
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// Gets the workspace ID for a bound app, if any.
        /// </summary>
        public string GetWorkspaceIdForApp(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
            {
                return null;
            }

            lock (_gate)
            {
                return _appToWorkspace.TryGetValue(appId, out var workspaceId) ? workspaceId : null;
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
        /// Gets all currently bound window handles.
        /// </summary>
        public HashSet<IntPtr> GetAllBoundWindows()
        {
            lock (_gate)
            {
                var handles = new HashSet<IntPtr>();
                foreach (var handle in _windowToApps.Keys)
                {
                    if (handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (IsWindow(handle))
                    {
                        handles.Add(handle);
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
                    RemoveAppFromWindow(hwnd, appId);
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
                if (_windowToApps.TryGetValue(hwnd, out var appIds))
                {
                    foreach (var appId in appIds)
                    {
                        _appToWindow.Remove(appId);
                        _appToWorkspace.Remove(appId);
                    }
                }

                _windowToApps.Remove(hwnd);
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
                        RemoveAppFromWindow(hwnd, appId);
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
                _windowToApps.Clear();
                _appToWorkspace.Clear();
            }
        }

        private void AddAppToWindow(IntPtr hwnd, string appId)
        {
            if (hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(appId))
            {
                return;
            }

            if (!_windowToApps.TryGetValue(hwnd, out var appIds))
            {
                appIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _windowToApps[hwnd] = appIds;
            }

            appIds.Add(appId);
        }

        private void RemoveAppFromWindow(IntPtr hwnd, string appId)
        {
            if (hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(appId))
            {
                return;
            }

            if (_windowToApps.TryGetValue(hwnd, out var appIds))
            {
                appIds.Remove(appId);
                if (appIds.Count == 0)
                {
                    _windowToApps.Remove(hwnd);
                }
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);
    }
}
