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
    /// Temporarily overrides the global ContentDialog scrim/overlay brushes.
    /// </summary>
    internal sealed class ContentDialogOverlayScope : IDisposable
    {
        private readonly ResourceDictionary _resources;
        private readonly bool _hadScrim;
        private readonly object _previousScrim;
        private readonly bool _hadOverlay;
        private readonly object _previousOverlay;

        private ContentDialogOverlayScope(ResourceDictionary resources, bool hadScrim, object previousScrim, bool hadOverlay, object previousOverlay)
        {
            _resources = resources;
            _hadScrim = hadScrim;
            _previousScrim = previousScrim;
            _hadOverlay = hadOverlay;
            _previousOverlay = previousOverlay;
        }

        public static ContentDialogOverlayScope Transparent()
        {
            var resources = Application.Current?.Resources;
            if (resources == null)
            {
                return new ContentDialogOverlayScope(null, false, null, false, null);
            }

            var brush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            var hadScrim = resources.TryGetValue("ContentDialogScrimBackground", out var previousScrim);
            var hadOverlay = resources.TryGetValue("ContentDialogLightDismissOverlayBackground", out var previousOverlay);

            resources["ContentDialogScrimBackground"] = brush;
            resources["ContentDialogLightDismissOverlayBackground"] = brush;

            return new ContentDialogOverlayScope(resources, hadScrim, previousScrim, hadOverlay, previousOverlay);
        }

        public void Dispose()
        {
            if (_resources == null)
            {
                return;
            }

            if (_hadScrim)
            {
                _resources["ContentDialogScrimBackground"] = _previousScrim;
            }
            else
            {
                _resources.Remove("ContentDialogScrimBackground");
            }

            if (_hadOverlay)
            {
                _resources["ContentDialogLightDismissOverlayBackground"] = _previousOverlay;
            }
            else
            {
                _resources.Remove("ContentDialogLightDismissOverlayBackground");
            }
        }
    }
}
