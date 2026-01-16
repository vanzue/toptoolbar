// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Timers;
using Microsoft.UI.Windowing;
using Timer = System.Timers.Timer;

namespace TopToolbar
{
    public sealed partial class ToolbarWindow
    {
        private void StartMonitoring()
        {
            _monitorTimer = new Timer(120);
            _monitorTimer.Elapsed += MonitorTimer_Elapsed;
            _monitorTimer.AutoReset = true;
            _monitorTimer.Start();
        }

        private void MonitorTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            GetCursorPos(out var pt);
            var displayArea = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(pt.X, pt.Y), DisplayAreaFallback.Primary);
            var topEdge = displayArea.WorkArea.Y;
            bool inTrigger = pt.Y <= topEdge + TriggerZoneHeight;

            if (inTrigger && !_isVisible)
            {
                DispatcherQueue.TryEnqueue(() => ShowToolbar());
            }
            else if (!inTrigger && _isVisible)
            {
                // hide when cursor is not over the toolbar rectangle
                DispatcherQueue.TryEnqueue(() =>
                {
                    var winPos = this.AppWindow.Position;
                    var winSize = this.AppWindow.Size;
                    bool overToolbar = pt.X >= winPos.X && pt.X <= winPos.X + winSize.Width &&
                                       pt.Y >= winPos.Y && pt.Y <= winPos.Y + winSize.Height;
                    if (!overToolbar)
                    {
                        HideToolbar();
                    }
                });
            }
        }

        private void ShowToolbar()
        {
            _isVisible = true;

            // Reposition to current monitor top edge
            GetCursorPos(out var ptPx);
            var da = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(ptPx.X, ptPx.Y), DisplayAreaFallback.Primary);
            var work = da.WorkArea;
            var size = AppWindow.Size;
            int x = work.X + ((work.Width - size.Width) / 2);
            int y = work.Y; // flush with top
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
            AppWindow.Show(false); // show without activation
            MakeTopMost();
        }

        private void HideToolbar(bool initial = false)
        {
            _isVisible = false;
            AppWindow.Hide();
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
