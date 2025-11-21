// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUIEx;

namespace TopToolbar
{
    internal static class SnapshotPromptWindow
    {
        public static async Task<string> ShowAsync(WindowEx owner)
        {
            using var overlay = await TransparentOverlayHost
                .CreateAsync(owner)
                .ConfigureAwait(true);
            if (overlay == null)
            {
                return null;
            }

            var (dialog, nameBox) = CreateDialog(overlay.Root);

            using var overlayScope = ContentDialogOverlayScope.Transparent();

            try
            {
                var result = await dialog.ShowAsync(ContentDialogPlacement.Popup);
                if (result == ContentDialogResult.Primary)
                {
                    return nameBox.Text?.Trim();
                }
            }
            catch (Exception) { }

            return null;
        }

        private static (ContentDialog Dialog, TextBox NameBox) CreateDialog(
            FrameworkElement rootElement
        )
        {
            var nameBox = new TextBox
            {
                PlaceholderText = "Workspace name",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = 13,
                MinWidth = 220,
            };

            var errorText = new TextBlock
            {
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            };

            var content = new StackPanel { Spacing = 12 };

            content.Children.Add(
                new TextBlock
                {
                    Text = "Workspace name",
                    Margin = new Thickness(0, 0, 0, 2),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 13,
                }
            );
            content.Children.Add(nameBox);
            content.Children.Add(errorText);

            var dialog = new ContentDialog
            {
                Title = "Snapshot workspace",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
                XamlRoot = rootElement.XamlRoot,
                Content = content,
            };

            nameBox.TextChanged += (_, __) =>
            {
                var hasText = !string.IsNullOrWhiteSpace(nameBox.Text);
                dialog.IsPrimaryButtonEnabled = hasText;

                if (hasText && errorText.Visibility == Visibility.Visible)
                {
                    errorText.Visibility = Visibility.Collapsed;
                }
            };

            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    errorText.Text = "Workspace name is required.";
                    errorText.Visibility = Visibility.Visible;
                    args.Cancel = true;
                }
            };

            dialog.Opened += (_, __) =>
            {
                nameBox.Focus(FocusState.Programmatic);
            };

            return (dialog, nameBox);
        }
    }
}
