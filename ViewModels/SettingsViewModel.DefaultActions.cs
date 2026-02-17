// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace TopToolbar.ViewModels
{
    public partial class SettingsViewModel
    {
        private bool _systemControlsEnabled = true;
        private bool _mediaPlayPauseEnabled = true;

        public bool SystemControlsEnabled
        {
            get => _systemControlsEnabled;
            set
            {
                if (_systemControlsEnabled != value)
                {
                    SetProperty(ref _systemControlsEnabled, value);
                    if (!_suppressGeneralSave)
                    {
                        ScheduleSave();
                    }
                }
            }
        }

        public bool MediaPlayPauseEnabled
        {
            get => _mediaPlayPauseEnabled;
            set
            {
                if (_mediaPlayPauseEnabled != value)
                {
                    SetProperty(ref _mediaPlayPauseEnabled, value);
                    if (!_suppressGeneralSave)
                    {
                        ScheduleSave();
                    }
                }
            }
        }
    }
}
