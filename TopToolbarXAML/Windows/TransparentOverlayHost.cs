// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinUIEx;

namespace TopToolbar
{
    /// <summary>
    /// Provides a reusable transparent overlay window that hosts ad-hoc XAML content.
    /// </summary>
    internal sealed class TransparentOverlayHost : IDisposable
    {
        private readonly bool _ownsHost;

        private TransparentOverlayHost(WindowEx host, Grid root, bool ownsHost)
        {
            Host = host;
            Root = root;
            _ownsHost = ownsHost;
        }

        public WindowEx Host { get; }

        public Grid Root { get; }

        public static async Task<TransparentOverlayHost> CreateAsync(WindowEx owner, bool fullscreen = true)
        {
            var host = new WindowEx
            {
                Title = string.Empty,
                IsTitleBarVisible = false,
                ExtendsContentIntoTitleBar = true,
            };

            host.SystemBackdrop = new TransparentTintBackdrop(Color.FromArgb(0, 0, 0, 0));

            var root = new Grid
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };

            host.Content = root;

            if (host.AppWindow is AppWindow appWindow)
            {
                ConfigureAppWindowChrome(owner, appWindow, fullscreen);
            }

            host.Activate();
            await EnsureXamlRootAsync(root).ConfigureAwait(true);

            if (root.XamlRoot == null)
            {
                host.Close();
                return null;
            }

            return new TransparentOverlayHost(host, root, ownsHost: true);
        }

        public void Dispose()
        {
            if (_ownsHost)
            {
                try
                {
                    Host?.Close();
                }
                catch
                {
                }
            }
        }

        private static void ConfigureAppWindowChrome(WindowEx owner, AppWindow appWindow, bool fullscreen)
        {
            appWindow.IsShownInSwitchers = false;
            appWindow.SetIcon(null);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }

            try
            {
                var displayArea = owner != null
                    ? DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary)
                    : DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);

                if (displayArea != null)
                {
                    var workArea = displayArea.WorkArea;

                    if (fullscreen)
                    {
                        appWindow.Move(new PointInt32(workArea.X, workArea.Y));
                        appWindow.Resize(new SizeInt32(workArea.Width, workArea.Height));
                    }
                }
            }
            catch
            {
            }
        }

        private static async Task EnsureXamlRootAsync(FrameworkElement element)
        {
            if (element.XamlRoot != null)
            {
                return;
            }

            if (element.IsLoaded)
            {
                element.UpdateLayout();
                return;
            }

            var tcs = new TaskCompletionSource<object>();
            RoutedEventHandler handler = null;
            handler = (_, __) =>
            {
                element.Loaded -= handler;
                tcs.TrySetResult(null);
            };

            element.Loaded += handler;
            await tcs.Task.ConfigureAwait(true);
        }
    }
}
