// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace TopToolbar.Services.Windowing
{
    internal sealed class WindowInfo
    {
        public WindowInfo(
            IntPtr handle,
            uint processId,
            string processPath,
            string processFileName,
            string processName,
            string packageFullName,
            string title,
            string appUserModelId,
            bool isVisible,
            WindowBounds bounds,
            string className,
            string monitorId,
            int monitorIndex)
        {
            Handle = handle;
            ProcessId = processId;
            ProcessPath = processPath ?? string.Empty;
            ProcessFileName = processFileName ?? string.Empty;
            ProcessName = processName ?? string.Empty;
            PackageFullName = packageFullName ?? string.Empty;
            Title = title ?? string.Empty;
            AppUserModelId = appUserModelId ?? string.Empty;
            IsVisible = isVisible;
            Bounds = bounds;
            ClassName = className ?? string.Empty;
            MonitorId = monitorId ?? string.Empty;
            MonitorIndex = monitorIndex;
        }

        public IntPtr Handle { get; }

        public uint ProcessId { get; }

        public string ProcessPath { get; }

        public string ProcessFileName { get; }

        public string ProcessName { get; }

        public string PackageFullName { get; }

        public string Title { get; }

        public string AppUserModelId { get; }

        public bool IsVisible { get; }

        public WindowBounds Bounds { get; }

        public string ClassName { get; }

        public string MonitorId { get; }

        public int MonitorIndex { get; }

        public WindowInfo WithMonitor(string monitorId, int monitorIndex)
        {
            if (string.Equals(MonitorId, monitorId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && MonitorIndex == monitorIndex)
            {
                return this;
            }

            return new WindowInfo(
                Handle,
                ProcessId,
                ProcessPath,
                ProcessFileName,
                ProcessName,
                PackageFullName,
                Title,
                AppUserModelId,
                IsVisible,
                Bounds,
                ClassName,
                monitorId ?? string.Empty,
                monitorIndex);
        }
    }
}
