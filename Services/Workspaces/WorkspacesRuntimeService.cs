// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace TopToolbar.Services.Workspaces
{
    internal sealed partial class WorkspacesRuntimeService : IDisposable
    {
        private readonly WorkspaceFileLoader _fileLoader;
        private readonly WindowTracker _windowTracker;
        private readonly ManagedWindowRegistry _managedWindows;
        private bool _disposed;

        public WorkspacesRuntimeService(string workspacesPath = null)
        {
            _fileLoader = new WorkspaceFileLoader(workspacesPath);
            _windowTracker = new WindowTracker();
            _managedWindows = new ManagedWindowRegistry();

            // Subscribe to window destruction events to keep ManagedWindowRegistry in sync
            _windowTracker.WindowDestroyed += OnWindowDestroyed;
        }

        private void OnWindowDestroyed(IntPtr hwnd)
        {
            // When a window is destroyed, remove it from the managed registry
            _managedWindows.UnbindWindow(hwnd);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _windowTracker.WindowDestroyed -= OnWindowDestroyed;
            _windowTracker.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
