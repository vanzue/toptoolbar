// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using TopToolbar.Models;

namespace TopToolbar.ViewModels
{
    public partial class SettingsViewModel
    {
        private ToolbarDisplayMode _displayMode = ToolbarDisplayMode.TopBar;

        public ToolbarDisplayMode DisplayMode
        {
            get => _displayMode;
            set
            {
                if (_displayMode != value)
                {
                    SetProperty(ref _displayMode, value);
                    OnPropertyChanged(nameof(DisplayModeIndex));
                    if (!_suppressGeneralSave)
                    {
                        ScheduleSave();
                    }
                }
            }
        }

        public int DisplayModeIndex
        {
            get => DisplayMode == ToolbarDisplayMode.RadialMenu ? 1 : 0;
            set => DisplayMode = value == 1 ? ToolbarDisplayMode.RadialMenu : ToolbarDisplayMode.TopBar;
        }
    }
}
