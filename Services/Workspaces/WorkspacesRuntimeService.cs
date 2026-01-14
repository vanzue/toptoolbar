// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TopToolbar.Logging;

namespace TopToolbar.Services.Workspaces
{
    internal sealed partial class WorkspacesRuntimeService : IDisposable
    {
        private const string ApplicationFrameHostProcessName = "ApplicationFrameHost.exe";
        private static readonly HashSet<string> ExcludedWindowClasses = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "TaskListThumbnailWnd",
            "Progman",
            "WorkerW",
            "NotifyIconOverflowWindow",
            "SysShadow",
            "SearchPane",
            "SearchHost",
            "Windows.UI.Core.CoreWindow",
            "NativeHWNDHost",
            "ApplicationManager_DesktopShellWindow",
            "LauncherTipWndClass",
        };

        private static readonly HashSet<string> ExcludedWindowTitles = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            "Program Manager",
        };

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

        public async Task<WorkspaceDefinition> SnapshotAsync(
            string workspaceName,
            CancellationToken cancellationToken
        )
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(WorkspacesRuntimeService));

            if (string.IsNullOrWhiteSpace(workspaceName))
            {
                throw new ArgumentException(
                    "Workspace name cannot be null or empty.",
                    nameof(workspaceName)
                );
            }

            var trimmedName = workspaceName.Trim();
            var monitorSnapshots = CaptureMonitorSnapshots();
            var windows = _windowTracker.GetSnapshot();
            var applications = new List<ApplicationDefinition>();

            foreach (var window in windows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!ShouldCaptureWindow(window, windows, out var effectiveProcessPath))
                {
                    continue;
                }

                var app = CreateApplicationDefinitionFromWindow(window, monitorSnapshots);
                if (app != null)
                {
                    if (!string.IsNullOrWhiteSpace(effectiveProcessPath))
                    {
                        app.Path = effectiveProcessPath;

                        var fileName = Path.GetFileName(effectiveProcessPath);
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            app.Name = fileName;
                        }
                    }

                    applications.Add(app);
                }
            }

            if (applications.Count == 0)
            {
                return null;
            }

            var workspace = new WorkspaceDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = trimmedName,
                CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsShortcutNeeded = false,
                MoveExistingWindows = true,
                Applications = applications,
            };

            if (monitorSnapshots.Count > 0)
            {
                var monitorDefinitions = new List<MonitorDefinition>(monitorSnapshots.Count);
                foreach (var snapshot in monitorSnapshots)
                {
                    monitorDefinitions.Add(snapshot.Definition);
                }

                workspace.Monitors = monitorDefinitions;
            }
            else
            {
                workspace.Monitors = new List<MonitorDefinition>();
            }

            await _fileLoader
                .SaveWorkspaceAsync(workspace, cancellationToken)
                .ConfigureAwait(false);

            // Bind snapshotted windows to their app configurations
            BindSnapshotWindows(workspace);

            return workspace;
        }

        private void BindSnapshotWindows(WorkspaceDefinition workspace)
        {
            if (workspace?.Applications == null)
            {
                return;
            }

            var windows = _windowTracker.GetSnapshot();
            foreach (var app in workspace.Applications)
            {
                if (string.IsNullOrWhiteSpace(app?.Id))
                {
                    continue;
                }

                // Find the window that matches this app by title (most specific match)
                foreach (var window in windows)
                {
                    if (window == null || window.Handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    // Match by title first (most specific)
                    if (!string.IsNullOrWhiteSpace(app.Title) && 
                        !string.IsNullOrWhiteSpace(window.Title) &&
                        string.Equals(window.Title, app.Title, StringComparison.Ordinal))
                    {
                        _managedWindows.BindWindow(workspace.Id, app.Id, window.Handle);
                        break;
                    }
                }
            }
        }

        private bool ShouldCaptureWindow(
            WindowInfo window,
            IReadOnlyList<WindowInfo> snapshot,
            out string resolvedProcessPath
        )
        {
            resolvedProcessPath = string.Empty;

            if (window == null)
            {
                return false;
            }

            if (!window.IsVisible || window.ProcessId == (uint)Environment.ProcessId)
            {
                return false;
            }

            if (window.Bounds.IsEmpty)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(window.Title))
            {
                return false;
            }

            resolvedProcessPath = ResolveProcessPath(window, snapshot);
            if (string.IsNullOrWhiteSpace(resolvedProcessPath))
            {
                return false;
            }

            if (IsExcludedWindow(window))
            {
                return false;
            }

            return true;
        }

        private static string ResolveProcessPath(
            WindowInfo window,
            IReadOnlyList<WindowInfo> snapshot
        )
        {
            if (window == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(window.ProcessPath) && !IsApplicationFrameHost(window))
            {
                return window.ProcessPath;
            }

            if (!IsApplicationFrameHost(window) || snapshot == null || snapshot.Count == 0)
            {
                return window.ProcessPath ?? string.Empty;
            }

            var title = window.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                return window.ProcessPath ?? string.Empty;
            }

            foreach (var candidate in snapshot)
            {
                if (candidate == null || candidate.Handle == window.Handle)
                {
                    continue;
                }

                if (!string.Equals(candidate.Title, title, StringComparison.Ordinal))
                {
                    continue;
                }

                if (
                    candidate.ProcessId != window.ProcessId
                    && !string.IsNullOrWhiteSpace(candidate.ProcessPath)
                )
                {
                    return candidate.ProcessPath;
                }
            }

            return window.ProcessPath ?? string.Empty;
        }

        private static bool IsApplicationFrameHost(WindowInfo window)
        {
            if (window == null)
            {
                return false;
            }

            return string.Equals(
                window.ProcessFileName,
                ApplicationFrameHostProcessName,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static bool IsExcludedWindow(WindowInfo window)
        {
            if (window == null)
            {
                return true;
            }

            if (
                !string.IsNullOrWhiteSpace(window.ClassName)
                && ExcludedWindowClasses.Contains(window.ClassName)
            )
            {
                return true;
            }

            if (
                !string.IsNullOrWhiteSpace(window.Title)
                && ExcludedWindowTitles.Contains(window.Title)
            )
            {
                return true;
            }

            if (NativeWindowHelper.HasToolWindowStyle(window.Handle))
            {
                return true;
            }

            return false;
        }

        private List<MonitorSnapshot> CaptureMonitorSnapshots()
        {
            var snapshots = new List<MonitorSnapshot>();

            try
            {
                int index = 0;
                MonitorEnumProc callback = (
                    IntPtr hMonitor,
                    IntPtr hdcMonitor,
                    ref NativeMonitorRect rect,
                    IntPtr data
                ) =>
                {
                    try
                    {
                        var info = new MONITORINFOEX
                        {
                            CbSize = Marshal.SizeOf<MONITORINFOEX>(),
                            SzDevice = string.Empty,
                        };

                        if (!GetMonitorInfo(hMonitor, ref info))
                        {
                            return true;
                        }

                        uint dpiX = 96;
                        uint dpiY = 96;
                        try
                        {
                            var hr = GetDpiForMonitor(
                                hMonitor,
                                MonitorDpiType.EffectiveDpi,
                                out dpiX,
                                out dpiY
                            );
                            if (hr != 0)
                            {
                                dpiX = dpiY = 96;
                            }
                        }
                        catch
                        {
                            dpiX = dpiY = 96;
                        }

                        var bounds = new MonitorBounds(
                            info.RcMonitor.Left,
                            info.RcMonitor.Top,
                            info.RcMonitor.Right,
                            info.RcMonitor.Bottom
                        );
                        var definition = new MonitorDefinition
                        {
                            Id = string.IsNullOrWhiteSpace(info.SzDevice)
                                ? $"DISPLAY{index}"
                                : info.SzDevice.Trim(),
                            InstanceId = string.IsNullOrWhiteSpace(info.SzDevice)
                                ? $"DISPLAY{index}"
                                : info.SzDevice.Trim(),
                            Number = index,
                            Dpi = (int)dpiX,
                            DpiAwareRect = new MonitorDefinition.MonitorRect
                            {
                                Left = info.RcMonitor.Left,
                                Top = info.RcMonitor.Top,
                                Width = info.RcMonitor.Right - info.RcMonitor.Left,
                                Height = info.RcMonitor.Bottom - info.RcMonitor.Top,
                            },
                            DpiUnawareRect = new MonitorDefinition.MonitorRect
                            {
                                Left = info.RcMonitor.Left,
                                Top = info.RcMonitor.Top,
                                Width = info.RcMonitor.Right - info.RcMonitor.Left,
                                Height = info.RcMonitor.Bottom - info.RcMonitor.Top,
                            },
                        };

                        snapshots.Add(new MonitorSnapshot(definition, bounds));
                        index++;
                    }
                    catch { }

                    return true;
                };

                _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            }
            catch { }

            return snapshots;
        }

        private ApplicationDefinition CreateApplicationDefinitionFromWindow(
            WindowInfo window,
            IReadOnlyList<MonitorSnapshot> monitors
        )
        {
            if (window == null)
            {
                return null;
            }

            var bounds = window.Bounds;
            if (bounds.IsEmpty)
            {
                return null;
            }

            var normalBounds = bounds;
            var isMinimized = false;
            var isMaximized = false;

            try
            {
                if (
                    NativeWindowHelper.TryGetWindowPlacement(
                        window.Handle,
                        out var placement,
                        out var minimized,
                        out var maximized
                    )
                )
                {
                    if (!placement.IsEmpty)
                    {
                        normalBounds = placement;
                    }

                    isMinimized = minimized;
                    isMaximized = maximized;
                }
            }
            catch { }

            if (normalBounds.IsEmpty)
            {
                return null;
            }

            var position = new ApplicationDefinition.ApplicationPosition
            {
                X = normalBounds.Left,
                Y = normalBounds.Top,
                Width = normalBounds.Width,
                Height = normalBounds.Height,
            };

            if (position.Width <= 0 || position.Height <= 0)
            {
                return null;
            }

            var definition = new ApplicationDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = !string.IsNullOrWhiteSpace(window.ProcessFileName)
                    ? window.ProcessFileName
                    : window.ProcessName,
                Title = window.Title,
                Path = window.ProcessPath ?? string.Empty,
                AppUserModelId = window.AppUserModelId ?? string.Empty,
                MonitorIndex = FindMonitorIndex(normalBounds, monitors),
                Minimized = isMinimized,
                Maximized = isMaximized,
                Position = position,
                CommandLineArguments = string.Empty,
                PackageFullName = window.PackageFullName ?? string.Empty,
                PwaAppId = string.Empty,
                Version = string.Empty,
                IsElevated = false,
                CanLaunchElevated = false,
            };

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                definition.Name = string.Empty;
            }

            return definition;
        }

        private static int FindMonitorIndex(
            WindowBounds bounds,
            IReadOnlyList<MonitorSnapshot> monitors
        )
        {
            if (monitors == null || monitors.Count == 0)
            {
                return 0;
            }

            var centerX = bounds.Left + (bounds.Width / 2);
            var centerY = bounds.Top + (bounds.Height / 2);

            var bestIndex = 0;
            long bestArea = -1;

            for (int i = 0; i < monitors.Count; i++)
            {
                var monitor = monitors[i].Bounds;
                if (
                    centerX >= monitor.Left
                    && centerX < monitor.Right
                    && centerY >= monitor.Top
                    && centerY < monitor.Bottom
                )
                {
                    return i;
                }

                var area = CalculateIntersectionArea(bounds, monitor);
                if (area > bestArea)
                {
                    bestArea = area;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static long CalculateIntersectionArea(WindowBounds window, MonitorBounds monitor)
        {
            var left = Math.Max(window.Left, monitor.Left);
            var top = Math.Max(window.Top, monitor.Top);
            var right = Math.Min(window.Right, monitor.Right);
            var bottom = Math.Min(window.Bottom, monitor.Bottom);

            var width = right - left;
            var height = bottom - top;
            if (width <= 0 || height <= 0)
            {
                return 0;
            }

            return (long)width * height;
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

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumProc lpfnEnum,
            IntPtr dwData
        );

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(
            IntPtr hmonitor,
            MonitorDpiType dpiType,
            out uint dpiX,
            out uint dpiY
        );

        private sealed class MonitorSnapshot
        {
            public MonitorSnapshot(MonitorDefinition definition, MonitorBounds bounds)
            {
                Definition = definition;
                Bounds = bounds;
            }

            public MonitorDefinition Definition { get; }

            public MonitorBounds Bounds { get; }
        }

        private readonly struct MonitorBounds
        {
            public MonitorBounds(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            public int Left { get; }

            public int Top { get; }

            public int Right { get; }

            public int Bottom { get; }

            public int Width => Right - Left;

            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMonitorRect
        {
            public int Left;

            public int Top;

            public int Right;

            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int CbSize;

            public NativeMonitorRect RcMonitor;

            public NativeMonitorRect RcWork;

            public uint DwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string SzDevice;
        }

        private delegate bool MonitorEnumProc(
            IntPtr hMonitor,
            IntPtr hdcMonitor,
            ref NativeMonitorRect lprcMonitor,
            IntPtr dwData
        );

        private enum MonitorDpiType
        {
            EffectiveDpi = 0,
            AngularDpi = 1,
            RawDpi = 2,
        }
    }
}
