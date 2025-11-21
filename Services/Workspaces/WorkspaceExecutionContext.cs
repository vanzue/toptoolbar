// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace TopToolbar.Services.Workspaces
{
    internal sealed class WorkspaceExecutionContext
    {
        private readonly Dictionary<ApplicationDefinition, ApplicationState> _applicationStates =
            new();
        private readonly HashSet<IntPtr> _workspaceHandles = new();
        private readonly object _gate = new();

        public WorkspaceExecutionContext(WindowTracker tracker, WorkspaceDefinition workspace)
        {
            if (workspace?.Applications == null)
            {
                return;
            }

            foreach (var app in workspace.Applications)
            {
                var matches = tracker.FindMatches(app);
                if (matches.Count == 0)
                {
                    continue;
                }

                lock (_gate)
                {
                    var state = GetOrCreateState(app);
                    state.Merge(matches, markLaunched: false);
                    foreach (var window in matches)
                    {
                        if (window != null && window.Handle != IntPtr.Zero)
                        {
                            _workspaceHandles.Add(window.Handle);
                        }
                    }
                }
            }
        }

        public IReadOnlyList<WindowInfo> GetWorkspaceWindows(ApplicationDefinition app)
        {
            lock (_gate)
            {
                return GetOrCreateState(app).GetWindowsSnapshot();
            }
        }

        public IReadOnlyCollection<IntPtr> GetKnownHandles(ApplicationDefinition app)
        {
            lock (_gate)
            {
                return GetOrCreateState(app).GetHandleSnapshot();
            }
        }

        public HashSet<IntPtr> GetWorkspaceHandles()
        {
            lock (_gate)
            {
                return new HashSet<IntPtr>(_workspaceHandles);
            }
        }

        public void MergeWindows(
            ApplicationDefinition app,
            IReadOnlyList<WindowInfo> windows,
            bool markLaunched
        )
        {
            if (app == null || windows == null || windows.Count == 0)
            {
                return;
            }

            lock (_gate)
            {
                var state = GetOrCreateState(app);
                state.Merge(windows, markLaunched);
                foreach (var window in windows)
                {
                    if (window != null && window.Handle != IntPtr.Zero)
                    {
                        _workspaceHandles.Add(window.Handle);
                    }
                }
            }
        }

        private ApplicationState GetOrCreateState(ApplicationDefinition app)
        {
            if (app == null)
            {
                return new ApplicationState();
            }

            if (_applicationStates.TryGetValue(app, out var state))
            {
                return state;
            }

            state = new ApplicationState();
            _applicationStates[app] = state;
            return state;
        }

        private sealed class ApplicationState
        {
            private readonly Dictionary<IntPtr, WindowInfo> _windows = new();
            private bool _launched;

            public IReadOnlyList<WindowInfo> GetWindowsSnapshot()
            {
                return new List<WindowInfo>(_windows.Values);
            }

            public IReadOnlyCollection<IntPtr> GetHandleSnapshot()
            {
                return new List<IntPtr>(_windows.Keys);
            }

            public bool WasLaunched => _launched;

            public void Merge(IEnumerable<WindowInfo> windows, bool markLaunched)
            {
                if (windows != null)
                {
                    foreach (var window in windows)
                    {
                        if (window == null || window.Handle == IntPtr.Zero)
                        {
                            continue;
                        }

                        _windows[window.Handle] = window;
                    }
                }

                if (markLaunched)
                {
                    _launched = true;
                }
            }
        }
    }
}
