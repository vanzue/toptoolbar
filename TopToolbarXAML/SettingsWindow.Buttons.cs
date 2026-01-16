// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TopToolbar.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TopToolbar
{
    public sealed partial class SettingsWindow
    {
        private async void OnRemoveButton(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedGroup == null)
            {
                return;
            }

            var targetButton =
                (sender as FrameworkElement)?.DataContext as ToolbarButton ?? _vm.SelectedButton;
            if (targetButton == null)
            {
                return;
            }

            _vm.RemoveButton(_vm.SelectedGroup, targetButton);
            await _vm.SaveAsync();
        }

        private async void OnBrowseIcon(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedButton == null)
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
            if (file != null)
            {
                _vm.SelectedButton.IsIconCustomized = true;
                if (await _vm.TrySetImageIconFromFileAsync(_vm.SelectedButton, file.Path))
                {
                    await _vm.SaveAsync();
                }
            }
        }
    }
}
