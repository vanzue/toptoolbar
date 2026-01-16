// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace TopToolbar
{
    public sealed partial class SettingsWindow
    {
        #if false // Profile management removed
        // Profile management event handlers
        private async void OnAddProfile(object sender, RoutedEventArgs e)
        {
            try
            {
                var newProfileId =
                    "profile-"
                    + DateTime.Now.Ticks.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                var newProfileName = $"Profile {DateTime.Now:HH:mm}";

                // Create empty profile
                var newProfile = _profileFileService.CreateEmptyProfile(
                    newProfileId,
                    newProfileName
                );

                // Save the profile
                _profileFileService.SaveProfile(newProfile);

                // Refresh the UI
                await RefreshProfilesList();

                SafeLogWarning($"Created new profile: {newProfileName}");
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Failed to add profile: {ex.Message}");
            }
        }

        private void OnRemoveSelectedProfile(object sender, RoutedEventArgs e)
        {
            if (_profileManager == null)
            {
                return;
            }

            try
            {
                // For now, we'll implement this differently since we can't easily access ProfilesList
                // Will be implemented once we have a proper reference
                SafeLogWarning("Profile removal not yet implemented");
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Failed to remove profile: {ex.Message}");
            }
        }

        private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ProfilesEnabled)
            {
                return;
            }

            try
            {
                var listView = sender as ListView;
                var selectedProfileMeta =
                    listView?.SelectedItem as Services.Profiles.Models.ProfileMeta;
                if (selectedProfileMeta != null)
                {
                    SafeLogWarning(
                        $"Profile selection changed to: {selectedProfileMeta.Id} - {selectedProfileMeta.Name}"
                    );

                    // Load full profile from ProfileFileService
                    var fullProfile = _profileFileService.GetProfile(selectedProfileMeta.Id);
                    SafeLogWarning(
                        $"Loaded full profile: {fullProfile?.Name ?? "null"} with {fullProfile?.Groups?.Count ?? 0} groups"
                    );

                    SelectedProfile = fullProfile;

                    // Also update legacy profile manager if available
                    _profileManager?.SwitchProfile(selectedProfileMeta.Id);
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Failed to switch profile: {ex.Message}");
            }
        }

        private void OnStartRenameProfile(object sender, RoutedEventArgs e)
        {
            // Profile rename logic similar to group rename
            var button = sender as Button;
            var profile = button?.DataContext as Services.Profiles.Models.ProfileMeta;
            if (profile != null)
            {
                // Find the corresponding UI elements and switch to edit mode
                // Implementation similar to OnStartRenameGroup
            }
        }

        private async void OnProfileNameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!ProfilesEnabled)
            {
                return;
            }

            if (
                e.Key == Windows.System.VirtualKey.Enter
                || e.Key == Windows.System.VirtualKey.Escape
            )
            {
                var textBox = sender as TextBox;
                if (textBox != null)
                {
                    await CommitProfileNameEdit(textBox, e.Key == Windows.System.VirtualKey.Enter);
                }
            }
        }

        private async void OnProfileNameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (!ProfilesEnabled)
            {
                return;
            }

            var textBox = sender as TextBox;
            if (textBox != null)
            {
                await CommitProfileNameEdit(textBox, true);
            }
        }

        private async System.Threading.Tasks.Task CommitProfileNameEdit(TextBox textBox, bool save)
        {
            if (!ProfilesEnabled)
            {
                return;
            }

            if (_profileManager == null)
            {
                return;
            }

            try
            {
                if (save && textBox.DataContext is Services.Profiles.Models.ProfileMeta profile)
                {
                    var newName = textBox.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(newName) && newName != profile.Name)
                    {
                        _profileManager.RenameProfile(profile.Id, newName);
                        await RefreshProfilesList();
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Failed to rename profile: {ex.Message}");
            }
            finally
            {
                // Switch back to display mode (similar to group rename logic)
            }
        }

        private void UpdateProfileUI()
        {
            if (!ProfilesEnabled)
            {
                return;
            }

            try
            {
                // Find and update profile-related UI elements
                if (this.Content is FrameworkElement root)
                {
                    var profileSettingsCard =
                        FindChildByName(root, "ProfileSettingsCard") as FrameworkElement;
                    var selectedProfileNameText =
                        FindChildByName(root, "SelectedProfileNameText") as TextBlock;
                    var actionsHeaderPanel =
                        FindChildByName(root, "ActionsHeaderPanel") as FrameworkElement;
                    var actionsScrollViewer =
                        FindChildByName(root, "ActionsScrollViewer") as FrameworkElement;

                    if (_selectedProfile != null)
                    {
                        // Show profile-related UI
                        if (profileSettingsCard != null)
                        {
                            profileSettingsCard.Visibility = Visibility.Visible;
                        }

                        if (selectedProfileNameText != null)
                        {
                            selectedProfileNameText.Text =
                                _selectedProfile.Name ?? "Unknown Profile";
                        }

                        if (actionsHeaderPanel != null)
                        {
                            actionsHeaderPanel.Visibility = Visibility.Visible;
                        }

                        if (actionsScrollViewer != null)
                        {
                            actionsScrollViewer.Visibility = Visibility.Visible;
                        }

                        SafeLogWarning($"Profile UI updated for: {_selectedProfile.Name}");
                    }
                    else
                    {
                        // Hide profile-related UI
                        if (profileSettingsCard != null)
                        {
                            profileSettingsCard.Visibility = Visibility.Collapsed;
                        }

                        if (actionsHeaderPanel != null)
                        {
                            actionsHeaderPanel.Visibility = Visibility.Collapsed;
                        }

                        if (actionsScrollViewer != null)
                        {
                            actionsScrollViewer.Visibility = Visibility.Collapsed;
                        }

                        SafeLogWarning("Profile UI updated for: None");
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Failed to update profile UI: {ex.Message}");
            }
        }

        private void UpdateActionsUI()
        {
            try
            {
                // Ensure we're on the UI thread
                if (!this.DispatcherQueue.HasThreadAccess)
                {
                    if (_disposed || _isClosed)
                    {
                        return;
                    }

                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_disposed || _isClosed)
                        {
                            return;
                        }

                        try
                        {
                            UpdateActionsUI();
                        }
                        catch (Exception ex)
                        {
                            SafeLogWarning($"Deferred UpdateActionsUI failed: {ex.Message}");
                        }
                    });
                    return;
                }

                // Find the Actions UI elements
                if (!_disposed && !_isClosed && this.Content is FrameworkElement root)
                {
                    // First make sure the ActionsScrollViewer is visible
                    var actionsScrollViewer =
                        FindChildByName(root, "ActionsScrollViewer") as ScrollViewer;
                    if (actionsScrollViewer != null)
                    {
                        actionsScrollViewer.Visibility = Visibility.Visible;
                        SafeLogWarning("Made ActionsScrollViewer visible");
                    }
                    else
                    {
                        SafeLogWarning("ActionsScrollViewer control not found");
                    }

                    var actionsPanel = FindChildByName(root, "ActionsPanel") as StackPanel;
                    if (actionsPanel != null)
                    {
                        SafeLogWarning(
                            $"Found ActionsPanel, clearing {actionsPanel.Children.Count} existing children"
                        );

                        // Clear existing content
                        actionsPanel.Children.Clear();

                        // Add groups and their actions
                        foreach (var group in _currentProfileGroups)
                        {
                            // Create group header
                            var groupExpander = new CommunityToolkit.WinUI.Controls.SettingsExpander
                            {
                                Header = group.Name,
                                Description = group.Description,
                                IsExpanded = true,
                                Margin = new Thickness(0, 0, 0, 16),
                            };

                            // Add group toggle
                            var groupToggle = new ToggleSwitch
                            {
                                IsOn = group.IsEnabled,
                                Tag = group,
                            };
                            groupToggle.Toggled += OnGroupToggled;
                            groupExpander.HeaderIcon = new FontIcon { Glyph = "\uE8A5" }; // Group icon

                            // Add actions to group
                            foreach (var action in group.Actions)
                            {
                                var actionCard = new CommunityToolkit.WinUI.Controls.SettingsCard
                                {
                                    Header = action.DisplayName,
                                    Description = action.Description,
                                };

                                var actionToggle = new ToggleSwitch
                                {
                                    IsOn = action.IsEnabled,
                                    Tag = action,
                                };
                                actionToggle.Toggled += OnActionToggled;
                                actionCard.Content = actionToggle;

                                groupExpander.Items.Add(actionCard);
                            }

                            // Set group toggle as main content
                            groupExpander.Content = groupToggle;
                            actionsPanel.Children.Add(groupExpander);
                        }

                        SafeLogWarning(
                            $"Actions UI updated with {_currentProfileGroups.Count} groups"
                        );
                    }
                    else
                    {
                        SafeLogWarning(
                            "ActionsPanel control not found - checking if parent container is collapsed"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Failed to update actions UI: {ex.Message}");
            }
        }

        private void OnGroupToggled(object sender, RoutedEventArgs e)
        {
            try
            {
                var toggleSwitch = sender as ToggleSwitch;
                var group = toggleSwitch?.Tag as Models.ProfileGroup;

                if (group != null && _selectedProfile != null)
                {
                    group.IsEnabled = toggleSwitch.IsOn;

                    // Save the updated profile
                    _profileFileService.SaveProfile(_selectedProfile);

                    // Notify ToolbarWindow that the profile has been updated
                    System.Diagnostics.Debug.WriteLine(
                        $"SettingsWindow.OnGroupToggled: Group {group.Name} toggled to {toggleSwitch.IsOn}, notifying profile runtime"
                    );
                    _profileRuntime?.NotifyActiveProfileUpdated();

                    SafeLogWarning(
                        $"Group {group.Name} is now {(group.IsEnabled ? "enabled" : "disabled")}"
                    );
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Failed to toggle group: {ex.Message}");
            }
        }

        private void OnActionToggled(object sender, RoutedEventArgs e)
        {
            try
            {
                var toggleSwitch = sender as ToggleSwitch;
                var action = toggleSwitch?.Tag as Models.ProfileAction;

                if (action != null && _selectedProfile != null)
                {
                    action.IsEnabled = toggleSwitch.IsOn;

                    // Save the updated profile
                    _profileFileService.SaveProfile(_selectedProfile);

                    // Notify ToolbarWindow that the profile has been updated
                    System.Diagnostics.Debug.WriteLine(
                        $"SettingsWindow.OnActionToggled: Action {action.DisplayName} toggled to {toggleSwitch.IsOn}, notifying profile runtime"
                    );
                    _profileRuntime?.NotifyActiveProfileUpdated();

                    SafeLogWarning(
                        $"Action {action.DisplayName} is now {(action.IsEnabled ? "enabled" : "disabled")}"
                    );
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Failed to toggle action: {ex.Message}");
            }
        }
        #endif

        #if false
        private async System.Threading.Tasks.Task RefreshProfilesList()
        {
            if (_disposed || _isClosed)
            {
                return;
            }

            if (!ProfilesEnabled)
            {
                return;
            }

            try
            {
                // Get profiles from ProfileFileService
                var profiles = _profileFileService.GetAllProfiles();

                // Convert to ProfileMeta for ListView binding (maintaining compatibility)
                var profileMetas = profiles
                    .Select(p => new Services.Profiles.Models.ProfileMeta
                    {
                        Id = p.Id,
                        Name = p.Name,
                    })
                    .ToList();

                // Try to find and update the ListView
                var success = await TryUpdateProfilesList(profileMetas);

                if (!success)
                {
                    // Retry after a short delay if UI is not ready
                    await System.Threading.Tasks.Task.Delay(100);
                    await TryUpdateProfilesList(profileMetas);
                }

                SafeLogWarning($"Profile list refreshed with {profiles.Count} profiles");
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Failed to refresh profiles list: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task<bool> TryUpdateProfilesList(
            System.Collections.Generic.List<Services.Profiles.Models.ProfileMeta> profileMetas
        )
        {
            if (_disposed || _isClosed)
            {
                return false;
            }

            if (!ProfilesEnabled)
            {
                return false;
            }

            try
            {
                var result = false;

                // Fast exit if window already torn down or dispatcher unavailable
                if (this.AppWindow == null || this.DispatcherQueue == null)
                {
                    return false;
                }

                // Guard dispatch with IsWindowClosed style check
                if (
                    !this.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            if (_disposed || _isClosed)
                            {
                                return;
                            }

                            var content = this.Content; // may throw if closed
                            if (content is FrameworkElement root)
                            {
                                var profilesList =
                                    FindChildByName(root, "ProfilesList") as ListView;
                                if (profilesList != null)
                                {
                                    profilesList.ItemsSource = profileMetas;
                                    result = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            SafeLogWarning($"TryUpdateProfilesList enqueue failed: {ex.Message}");
                        }
                    })
                )
                {
                    return false;
                }

                await System.Threading.Tasks.Task.Delay(40);
                return result;
            }
            catch (Exception ex)
            {
                SafeLogWarning($"TryUpdateProfilesList outer failed: {ex.Message}");
                return false;
            }
        }

        // Helper method to find controls by name in the visual tree
        private FrameworkElement FindChildByName(DependencyObject parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement element && element.Name == name)
                {
                    return element;
                }

                var result = FindChildByName(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private async void InitializeProfilesList()
        {
            try
            {
                await RefreshProfilesList();
            }
            catch (Exception ex)
            {
                SafeLogWarning($"Failed to initialize profiles list: {ex.Message}");
            }
        }

        // Wire a one-time Loaded handler on the root content element (WindowEx itself has no Loaded event)
        private void AttachRootLoadedHandler()
        {
            try
            {
                if (this.Content is FrameworkElement fe)
                {
                    RoutedEventHandler handler = null;
                    handler = (s, e) =>
                    {
                        fe.Loaded -= handler;
                        if (_disposed || _isClosed)
                        {
                            return;
                        }

                        SafeLogWarning("Root Loaded event fired; ensuring ProfilesList binds.");
                        InitializeProfilesList();
                    };
                    fe.Loaded += handler;
                }
                else
                {
                    // If content not yet assigned, schedule a retry shortly.
                    this.DispatcherQueue.TryEnqueue(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(100);
                        if (_disposed || _isClosed)
                        {
                            return;
                        }

                        AttachRootLoadedHandler();
                    });
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning("AttachRootLoadedHandler failed: " + ex.Message);
            }
        }
        #endif

        // Profile feature removed; keep stub handlers for XAML.
        private async void OnAddProfile(object sender, RoutedEventArgs e) => await Task.CompletedTask;
        private void OnRemoveSelectedProfile(object sender, RoutedEventArgs e) { }
        private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void OnStartRenameProfile(object sender, RoutedEventArgs e) { }
        private async void OnProfileNameTextBoxKeyDown(object sender, KeyRoutedEventArgs e) => await Task.CompletedTask;
        private async void OnProfileNameTextBoxLostFocus(object sender, RoutedEventArgs e) => await Task.CompletedTask;
    }
}
