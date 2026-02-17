// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using TopToolbar.Models;
using System;

namespace TopToolbar.ViewModels
{
    public partial class SettingsViewModel
    {
        private ToolbarTheme _theme = ToolbarTheme.WarmFrosted;

        public ToolbarTheme Theme
        {
            get => _theme;
            set
            {
                if (_theme != value)
                {
                    SetProperty(ref _theme, value);
                    OnPropertyChanged(nameof(ThemeIndex));
                    if (!_suppressGeneralSave)
                    {
                        ScheduleSave();
                    }
                }
            }
        }

        public int ThemeIndex
        {
            get => (int)Theme;
            set
            {
                Theme = Enum.IsDefined(typeof(ToolbarTheme), value)
                    ? (ToolbarTheme)value
                    : ToolbarTheme.WarmFrosted;
            }
        }
    }
}
