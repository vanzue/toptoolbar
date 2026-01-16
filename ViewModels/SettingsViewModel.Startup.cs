// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using TopToolbar.Logging;
using Windows.ApplicationModel;

namespace TopToolbar.ViewModels
{
    public partial class SettingsViewModel
    {
        private bool _isStartupEnabled;

        public bool IsStartupEnabled
        {
            get => _isStartupEnabled;
            set => SetProperty(ref _isStartupEnabled, value);
        }

        private bool _isStartupAvailable = true;

        public bool IsStartupAvailable
        {
            get => _isStartupAvailable;
            set => SetProperty(ref _isStartupAvailable, value);
        }

        private string _startupStatusText = string.Empty;

        public string StartupStatusText
        {
            get => _startupStatusText;
            set => SetProperty(ref _startupStatusText, value);
        }

        public async Task LoadStartupStateAsync()
        {
            try
            {
                var startupTask = await StartupTask.GetAsync("TopToolbarStartup");
                switch (startupTask.State)
                {
                    case StartupTaskState.Enabled:
                        IsStartupEnabled = true;
                        IsStartupAvailable = true;
                        StartupStatusText = string.Empty;
                        break;
                    case StartupTaskState.Disabled:
                        IsStartupEnabled = false;
                        IsStartupAvailable = true;
                        StartupStatusText = string.Empty;
                        break;
                    case StartupTaskState.DisabledByUser:
                        IsStartupEnabled = false;
                        IsStartupAvailable = false;
                        StartupStatusText = "Disabled in Task Manager. Enable it there first.";
                        break;
                    case StartupTaskState.DisabledByPolicy:
                        IsStartupEnabled = false;
                        IsStartupAvailable = false;
                        StartupStatusText = "Disabled by group policy.";
                        break;
                    case StartupTaskState.EnabledByPolicy:
                        IsStartupEnabled = true;
                        IsStartupAvailable = false;
                        StartupStatusText = "Enabled by group policy.";
                        break;
                    default:
                        IsStartupEnabled = false;
                        IsStartupAvailable = false;
                        StartupStatusText = "Status unknown.";
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Failed to get startup task state: {ex.Message}");
                IsStartupEnabled = false;
                IsStartupAvailable = false;
                StartupStatusText = "Startup task not available (non-packaged app?).";
            }
        }

        public async Task<bool> SetStartupEnabledAsync(bool enabled)
        {
            try
            {
                var startupTask = await StartupTask.GetAsync("TopToolbarStartup");

                if (enabled)
                {
                    var newState = await startupTask.RequestEnableAsync();
                    IsStartupEnabled = newState == StartupTaskState.Enabled;

                    if (newState == StartupTaskState.DisabledByUser)
                    {
                        StartupStatusText = "Disabled in Task Manager. Enable it there first.";
                        IsStartupAvailable = false;
                    }
                    else if (newState == StartupTaskState.DisabledByPolicy)
                    {
                        StartupStatusText = "Disabled by group policy.";
                        IsStartupAvailable = false;
                    }
                    else
                    {
                        StartupStatusText = string.Empty;
                    }

                    return IsStartupEnabled;
                }
                else
                {
                    startupTask.Disable();
                    IsStartupEnabled = false;
                    StartupStatusText = string.Empty;
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Failed to set startup state: {ex.Message}");
                StartupStatusText = $"Failed: {ex.Message}";
                return false;
            }
        }
    }
}
