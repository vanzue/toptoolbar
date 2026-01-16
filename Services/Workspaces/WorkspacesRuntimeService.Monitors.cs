// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TopToolbar.Services.Workspaces
{
    internal sealed partial class WorkspacesRuntimeService
    {
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
