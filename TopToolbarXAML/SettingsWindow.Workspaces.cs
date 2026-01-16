// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TopToolbar.Models;
using TopToolbar.Providers;
using TopToolbar.Services.Workspaces;
using TopToolbar.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TopToolbar
{
    public sealed partial class SettingsWindow
    {
        private async void OnAddWorkspace(object sender, RoutedEventArgs e)
        {
            var workspace = _vm.AddWorkspace("New workspace");
            if (workspace != null)
            {
                await _vm.SaveAsync();
            }
        }

        private async void OnRemoveWorkspace(object sender, RoutedEventArgs e)
        {
            var context = (sender as FrameworkElement)?.DataContext as WorkspaceButtonViewModel;
            var workspace = context ?? _vm.SelectedWorkspace;
            if (workspace == null)
            {
                return;
            }

            _vm.RemoveWorkspace(workspace);
            await _vm.SaveAsync();
        }

        private async void OnBrowseWorkspaceIcon(object sender, RoutedEventArgs e)
        {
            var workspace =
                (sender as FrameworkElement)?.DataContext as WorkspaceButtonViewModel
                ?? _vm.SelectedWorkspace;
            if (workspace == null)
            {
                return;
            }

            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".ico");

            var file = await picker.PickSingleFileAsync();
            if (file == null)
            {
                return;
            }

            await _vm.TrySetWorkspaceImageIconFromFileAsync(workspace, file.Path)
                .ConfigureAwait(true);
        }

        private void OnResetWorkspaceIcon(object sender, RoutedEventArgs e)
        {
            var workspace =
                (sender as FrameworkElement)?.DataContext as WorkspaceButtonViewModel
                ?? _vm.SelectedWorkspace;
            if (workspace == null)
            {
                return;
            }

            _vm.ResetWorkspaceIcon(workspace);
        }

        private void OnRemoveWorkspaceApp(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedWorkspace == null)
            {
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is ApplicationDefinition app)
            {
                _vm.RemoveWorkspaceApp(_vm.SelectedWorkspace, app);
            }
        }

        private static async Task ShowSimpleMessageAsync(
            XamlRoot xamlRoot,
            string title,
            string message
        )
        {
            if (xamlRoot == null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title ?? string.Empty,
                Content = new TextBlock
                {
                    Text = message ?? string.Empty,
                    TextWrapping = TextWrapping.Wrap,
                },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
            };

            await dialog.ShowAsync();
        }

        private async void OnSnapshotWorkspace(object sender, RoutedEventArgs e)
        {
            if (_disposed || _isClosed)
            {
                return;
            }

            if (Content is not FrameworkElement root)
            {
                return;
            }

            var nameBox = new TextBox { PlaceholderText = "Workspace name" };

            if (
                root.Resources != null
                && root.Resources.TryGetValue("StandardTextBoxStyle", out var styleObj)
                && styleObj is Style textBoxStyle
            )
            {
                nameBox.Style = textBoxStyle;
            }

            var dialogContent = new StackPanel { Spacing = 12 };
            dialogContent.Children.Add(
                new TextBlock
                {
                    Text = "Enter a name for the new workspace snapshot.",
                    TextWrapping = TextWrapping.Wrap,
                }
            );
            dialogContent.Children.Add(nameBox);

            var dialog = new ContentDialog
            {
                XamlRoot = root.XamlRoot,
                Title = "Create workspace snapshot",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                Content = dialogContent,
                IsPrimaryButtonEnabled = false,
            };

            nameBox.TextChanged += (_, __) =>
            {
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var workspaceName = nameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(workspaceName))
            {
                return;
            }

            try
            {
                using var workspaceProvider = new WorkspaceProvider();
                var workspace = await workspaceProvider
                    .SnapshotAsync(workspaceName, CancellationToken.None)
                    .ConfigureAwait(true);
                if (workspace == null)
                {
                    await ShowSimpleMessageAsync(
                        root.XamlRoot,
                        "Snapshot failed",
                        "No eligible windows were detected to capture."
                    );
                    return;
                }

                await ShowSimpleMessageAsync(
                    root.XamlRoot,
                    "Snapshot saved",
                    $"Workspace \"{workspace.Name}\" has been saved."
                );
            }
            catch (Exception ex)
            {
                await ShowSimpleMessageAsync(root.XamlRoot, "Snapshot failed", ex.Message);
            }
        }
    }
}
