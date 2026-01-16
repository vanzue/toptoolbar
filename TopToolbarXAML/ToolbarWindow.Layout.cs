// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TopToolbar.Logging;
using TopToolbar.Models;
using TopToolbar.Services.Workspaces;
using Windows.UI;

namespace TopToolbar
{
    public sealed partial class ToolbarWindow
    {
        private void CreateToolbarShell()
        {
            // Create a completely transparent root container with symmetric padding for shadow
            var rootGrid = new Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                UseLayoutRounding = true,
                IsHitTestVisible = true,
                Padding = new Thickness(ToolbarShadowPadding),
            };

            // Create the toolbar content with modern macOS-style design and default shadow
            var border = new Border
            {
                Name = "ToolbarContainer",
                CornerRadius = new CornerRadius(ToolbarCornerRadius),

                // Semi-transparent white to let the DesktopAcrylicBackdrop show through
                // The blur effect comes from DesktopAcrylicBackdrop, this just adds a tint
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(120, 255, 255, 255)), // 47% opacity - lets backdrop blur show through

                // Alternative tint options (change alpha value to adjust transparency):
                // More transparent (more blur visible):
                // Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                //     Windows.UI.Color.FromArgb(80, 255, 255, 255)), // 31% opacity

                // More opaque (less blur, more solid):
                // Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                //     Windows.UI.Color.FromArgb(160, 255, 255, 255)), // 63% opacity

                // Dark mode option:
                // Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                //     Windows.UI.Color.FromArgb(140, 30, 30, 30)), // Dark with blur

                MinHeight = ToolbarHeight,
                Padding = ToolbarChromePadding,
                VerticalAlignment = VerticalAlignment.Center, // Back to center
                HorizontalAlignment = HorizontalAlignment.Center,
                UseLayoutRounding = true,
                IsHitTestVisible = true,     // the pill remains interactive
            };

            // Apply default shadow
            var themeShadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
            border.Shadow = themeShadow;

            var mainStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = ToolbarStackSpacing,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = true,
                Name = "MainStack",
            };

            _scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Enabled,
                VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled,
                Content = mainStack,
            };

            border.Child = _scrollViewer;
            rootGrid.Children.Add(border);
            this.Content = rootGrid;
            _toolbarContainer = border;
        }

        // Transitional full rebuild method (will be replaced by ItemsRepeater binding in Phase 2)
        private void BuildToolbarFromStore()
        {
            StackPanel mainStack = (_toolbarContainer?.Child as ScrollViewer)?.Content as StackPanel
                                   ?? _toolbarContainer?.Child as StackPanel;
            if (mainStack == null)
            {
                return;
            }

            mainStack.Children.Clear();

            // Filter enabled groups and buttons directly from the store
            var activeGroups = _store.Groups
                .Where(g => g != null && g.IsEnabled)
                .Select(g => new { Group = g, EnabledButtons = g.Buttons.Where(b => b.IsEnabled).ToList() })
                .Where(x => x.EnabledButtons.Count > 0)
                .ToList();

            for (int gi = 0; gi < activeGroups.Count; gi++)
            {
                var group = activeGroups[gi].Group;
                var enabledButtons = activeGroups[gi].EnabledButtons;

                var groupContainer = new Border
                {
                    CornerRadius = new CornerRadius(15),

                    // Default fully transparent so it doesn't show until hover
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(2, 0, 2, 0),
                    Tag = group.Id,
                };

                var groupShadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
                groupContainer.Shadow = groupShadow;
                groupContainer.Translation = new System.Numerics.Vector3(0, 0, 1);

                // Background stays transparent; hover effect intentionally disabled.

                var groupPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                foreach (var btn in enabledButtons)
                {
                    var iconButton = CreateIconButton(group, btn);
                    groupPanel.Children.Add(iconButton);
                }

                groupContainer.Child = groupPanel;
                mainStack.Children.Add(groupContainer);

                if (gi != activeGroups.Count - 1)
                {
                    var separatorContainer = new Border
                    {
                        Width = 1,
                        Height = ToolbarSeparatorHeight,
                        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 0, 0, 0)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 8, 0),
                        IsHitTestVisible = false,
                        CornerRadius = new CornerRadius(0.5),
                    };
                    var separatorShadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
                    separatorContainer.Shadow = separatorShadow;
                    separatorContainer.Translation = new System.Numerics.Vector3(0, 0, 2);
                    mainStack.Children.Add(separatorContainer);
                }
            }

            var settingsSeparatorContainer = new Border
            {
                Width = 1,
                Height = ToolbarSeparatorHeight,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 0, 0, 0)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                IsHitTestVisible = false,
                CornerRadius = new CornerRadius(0.5),
                Tag = "__SETTINGS_SEPARATOR__",
            };
            var settingsSeparatorShadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
            settingsSeparatorContainer.Shadow = settingsSeparatorShadow;
            settingsSeparatorContainer.Translation = new System.Numerics.Vector3(0, 0, 2);
            mainStack.Children.Add(settingsSeparatorContainer);

            var snapshotButton = CreateIconButton("\uE114", "Snapshot workspace", async (s, e) => await HandleSnapshotButtonClickAsync(s as Button), "Snapshot");
            mainStack.Children.Add(snapshotButton);

            var settingsButton = CreateIconButton("\uE713", "Toolbar Settings", (s, e) =>
            {
                try
                {
                    var win = new SettingsWindow();
                    win.AppWindow.Move(new Windows.Graphics.PointInt32(this.AppWindow.Position.X + 50, this.AppWindow.Position.Y + 60));
                    win.Activate();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Open SettingsWindow failed", ex);
                }
            });
            mainStack.Children.Add(settingsButton);
        }

        private FrameworkElement CreateIconButton(string content, string tooltip, RoutedEventHandler clickHandler, string labelText = "Settings")
        {
            var button = new Button
            {
                Content = new FontIcon { Glyph = content, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"), FontSize = ToolbarIconFontSize, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0)) },
                Width = ToolbarButtonSize,
                Height = ToolbarButtonSize,
                CornerRadius = new CornerRadius(8),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)), // Transparent base
                BorderBrush = null,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                UseLayoutRounding = true,
            };

            // Create text label for button
            var textLabel = new TextBlock
            {
                Text = labelText,
                FontSize = ToolbarLabelFontSize,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = ToolbarButtonSize + 36,
                Margin = new Thickness(0, 4, 0, 0),
                UseLayoutRounding = true,
            };

            // Create container stack panel for button + text
            var containerStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 6,
                Width = ToolbarButtonSize + 42, // Slightly wider to accommodate text
                Margin = new Thickness(2),
            };

            // Use WinUI button visual state resources to ensure stable hover/pressed visuals
            var hoverBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(60, 0, 0, 0));
            var pressedBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(100, 0, 0, 0));
            var normalBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

            // Override per-button theme resources so the default template keeps our visuals
            button.Resources["ButtonBackground"] = normalBrush;
            button.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
            button.Resources["ButtonBackgroundPressed"] = pressedBrush;
            button.Resources["ButtonBackgroundDisabled"] = normalBrush;

            // Add button and text to the container stack
            containerStack.Children.Add(button);
            containerStack.Children.Add(textLabel);

            ToolTipService.SetToolTip(button, tooltip);
            button.Click += clickHandler;
            return containerStack;
        }

        private FrameworkElement CreateIconButton(ButtonGroup group, ToolbarButton model)
        {
            var dispatcher = DispatcherQueue;

            const double iconSize = ToolbarIconFontSize;
            var containerWidth = ToolbarButtonSize + 42d;
            var maxLabelWidth = ToolbarButtonSize + 36d;
            var progressRingSize = ToolbarButtonSize - 20d;

            var button = new Button
            {
                Width = ToolbarButtonSize,
                Height = ToolbarButtonSize,
                CornerRadius = new CornerRadius(8),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderBrush = null,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                UseLayoutRounding = true,
            };

            var textLabel = new TextBlock
            {
                Text = model.Name ?? string.Empty,
                FontSize = ToolbarLabelFontSize,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = maxLabelWidth,
                Margin = new Thickness(0, 4, 0, 0),
                UseLayoutRounding = true,
            };

            var containerStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 6,
                Width = containerWidth,
                Margin = new Thickness(2),
            };

            var hoverBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
            var pressedBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
            var normalBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            button.Resources["ButtonBackground"] = normalBrush;
            button.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
            button.Resources["ButtonBackgroundPressed"] = pressedBrush;
            button.Resources["ButtonBackgroundDisabled"] = normalBrush;

            var iconPresenter = new Controls.ToolbarIconPresenter
            {
                IconSize = iconSize,
                Foreground = Color.FromArgb(255, 0, 0, 0),
                Button = model,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var iconHost = new Grid
            {
                Width = ToolbarButtonSize,
                Height = ToolbarButtonSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var progressRing = new ProgressRing
            {
                Width = progressRingSize,
                Height = progressRingSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsActive = false,
                Visibility = Visibility.Collapsed,
            };
            iconHost.Children.Add(iconPresenter);
            iconHost.Children.Add(progressRing);

            button.Content = iconHost;

            containerStack.Children.Add(button);
            containerStack.Children.Add(textLabel);

            void UpdateVisualState()
            {
                void ApplyState()
                {
                    button.IsEnabled = model.IsEnabled && !model.IsExecuting;
                    progressRing.IsActive = model.IsExecuting;
                    progressRing.Visibility = model.IsExecuting ? Visibility.Visible : Visibility.Collapsed;
                    if (iconPresenter != null)
                    {
                        iconPresenter.Opacity = model.IsExecuting ? 0.4 : 1.0;
                    }
                }

                if (dispatcher != null && !dispatcher.HasThreadAccess)
                {
                    dispatcher.TryEnqueue(ApplyState);
                }
                else
                {
                    ApplyState();
                }
            }

            string BuildTooltip()
            {
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(model.Name))
                {
                    parts.Add(model.Name);
                }

                if (!string.IsNullOrWhiteSpace(model.Description))
                {
                    parts.Add(model.Description);
                }

                if (!string.IsNullOrWhiteSpace(model.ProgressMessage))
                {
                    parts.Add(model.ProgressMessage);
                }
                else if (!string.IsNullOrWhiteSpace(model.StatusMessage))
                {
                    parts.Add(model.StatusMessage);
                }

                if (parts.Count == 0)
                {
                    return model.Name ?? string.Empty;
                }

                return string.Join(Environment.NewLine, parts);
            }

            void UpdateToolTip()
            {
                var tooltip = BuildTooltip();

                void Apply()
                {
                    Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(button, tooltip);
                }

                if (dispatcher != null && !dispatcher.HasThreadAccess)
                {
                    dispatcher.TryEnqueue(Apply);
                }
                else
                {
                    Apply();
                }
            }

            async void OnClick(object sender, RoutedEventArgs e)
            {
                if (model.IsExecuting)
                {
                    return;
                }

                try
                {
                    await _actionExecutor.ExecuteAsync(group, model);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    model.StatusMessage = ex.Message;
                }
            }

            void OnRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
            {
                e.Handled = true;
                var flyout = new MenuFlyout();

                // Delete menu item
                var deleteItem = new MenuFlyoutItem
                {
                    Text = "Remove Button",
                    Icon = new FontIcon { Glyph = "\uE74D" }, // Trash icon
                };

                deleteItem.Click += async (s, args) =>
                {
                    try
                    {
                        // Check if this is a workspace button (from Provider)
                        if (model.Action?.Type == ToolbarActionType.Provider &&
                            string.Equals(model.Action.ProviderId, "WorkspaceProvider", StringComparison.OrdinalIgnoreCase))
                        {
                            // Extract workspace ID from the button ID or action
                            string workspaceId = null;
                            if (!string.IsNullOrEmpty(model.Id) && model.Id.StartsWith("workspace::", StringComparison.OrdinalIgnoreCase))
                            {
                                workspaceId = model.Id.Substring("workspace::".Length);
                            }
                            else if (!string.IsNullOrEmpty(model.Action.ProviderActionId) && model.Action.ProviderActionId.StartsWith("workspace::", StringComparison.OrdinalIgnoreCase))
                            {
                                workspaceId = model.Action.ProviderActionId.Substring("workspace::".Length);
                            }

                            if (!string.IsNullOrWhiteSpace(workspaceId))
                            {
                                // Delete from workspaces.json
                                var workspaceLoader = new WorkspaceFileLoader();
                                var deleted = await workspaceLoader.DeleteWorkspaceAsync(workspaceId, CancellationToken.None);

                                if (deleted)
                                {
                                    AppLogger.LogInfo($"Deleted workspace '{workspaceId}' from workspaces.json");

                                    // Reload ViewModel to refresh providers and update UI
                                    await _vm.LoadAsync(DispatcherQueue);
                                    await RefreshWorkspaceGroupAsync();
                                }

                                return;
                            }
                        }

                        // For non-workspace buttons, use the original deletion logic
                        // Find the corresponding group in _vm (the source of truth)
                        var vmGroup = _vm.Groups.FirstOrDefault(g =>
                            string.Equals(g.Id, group.Id, StringComparison.OrdinalIgnoreCase));

                        if (vmGroup != null)
                        {
                            // Remove from _vm.Groups (the actual data source)
                            vmGroup.Buttons.Remove(model);

                            // Save to config file
                            await _vm.SaveAsync();

                            // Re-sync Store from ViewModel to reflect the deletion
                            SyncStaticGroupsIntoStore();

                            // Rebuild UI
                            BuildToolbarFromStore();
                            ResizeToContent();
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError($"Failed to delete button '{model.Name}': {ex.Message}");
                    }
                };

                flyout.Items.Add(deleteItem);
                flyout.ShowAt(button, e.GetPosition(button));
            }

            UpdateVisualState();
            UpdateToolTip();

            model.PropertyChanged += (s, e) =>
            {
                if (e == null)
                {
                    UpdateVisualState();
                    UpdateToolTip();
                    return;
                }

                if (e.PropertyName == nameof(ToolbarButton.IsEnabled) ||
                    e.PropertyName == nameof(ToolbarButton.IsExecuting))
                {
                    UpdateVisualState();
                }

                if (e.PropertyName == nameof(ToolbarButton.ProgressMessage) ||
                    e.PropertyName == nameof(ToolbarButton.StatusMessage) ||
                    e.PropertyName == nameof(ToolbarButton.Description) ||
                    e.PropertyName == nameof(ToolbarButton.Name) ||
                    e.PropertyName == nameof(ToolbarButton.IconType) ||
                    e.PropertyName == nameof(ToolbarButton.IconPath) ||
                    e.PropertyName == nameof(ToolbarButton.IconGlyph))
                {
                    UpdateToolTip();
                }

                if (e.PropertyName == nameof(ToolbarButton.Name))
                {
                    void Apply()
                    {
                        textLabel.Text = model.Name ?? string.Empty;
                    }

                    if (dispatcher != null && !dispatcher.HasThreadAccess)
                    {
                        dispatcher.TryEnqueue(Apply);
                    }
                    else
                    {
                        Apply();
                    }
                }
            };
            button.Click += OnClick;
            button.RightTapped += OnRightTapped;
            return containerStack;
        }

        // Build or replace a single group's visual container in-place; falls back to full rebuild if structure missing.
        // Transitional incremental update path prior to full data binding migration
        private void BuildOrReplaceSingleGroup(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            StackPanel mainStack = (_toolbarContainer?.Child as ScrollViewer)?.Content as StackPanel
                                   ?? _toolbarContainer?.Child as StackPanel;
            if (mainStack == null)
            {
                return;
            }

            var group = _store.Groups.FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase));
            if (group == null || !group.IsEnabled)
            {
                // Removed or disabled group: trigger full rebuild to also clean separators coherently.
                BuildToolbarFromStore();
                ResizeToContent();
                return;
            }

            // Locate existing group container (Border) tagged with group id.
            Border existingContainer = null;
            for (int i = 0; i < mainStack.Children.Count; i++)
            {
                if (mainStack.Children[i] is Border b && b.Tag is string tag && string.Equals(tag, groupId, StringComparison.OrdinalIgnoreCase))
                {
                    existingContainer = b;
                    break;
                }
            }

            var enabledButtons = group.Buttons.Where(b => b.IsEnabled).ToList();
            if (enabledButtons.Count == 0)
            {
                // If no buttons remain for the group, treat as removal
                BuildToolbarFromStore();
                ResizeToContent();
                return;
            }

            Border newContainer = new Border
            {
                CornerRadius = new CornerRadius(15),

                // Default transparent; hover will apply highlight
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(2, 0, 2, 0),
                Tag = group.Id,
            };
            var groupShadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
            newContainer.Shadow = groupShadow;
            newContainer.Translation = new System.Numerics.Vector3(0, 0, 1);

            // Background stays transparent; hover effect intentionally disabled.

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            foreach (var btn in enabledButtons)
            {
                panel.Children.Add(CreateIconButton(group, btn));
            }

            newContainer.Child = panel;

            if (existingContainer == null)
            {
                // Find settings separator anchor
                int anchorIndex = -1;
                for (int i = 0; i < mainStack.Children.Count; i++)
                {
                    if (mainStack.Children[i] is Border b && b.Tag as string == "__SETTINGS_SEPARATOR__")
                    {
                        anchorIndex = i;
                        break;
                    }
                }

                int insertIndex = anchorIndex >= 0 ? anchorIndex : mainStack.Children.Count;
                mainStack.Children.Insert(insertIndex, newContainer);
            }
            else
            {
                int idx = mainStack.Children.IndexOf(existingContainer);
                if (idx >= 0)
                {
                    mainStack.Children.RemoveAt(idx);
                    mainStack.Children.Insert(idx, newContainer);
                }
                else
                {
                    mainStack.Children.Add(newContainer);
                }
            }

            ResizeToContent();
        }

        private void ResizeToContent()
        {
            if (_toolbarContainer != null)
            {
                // Measure content desired width independent of current constraints
                StackPanel mainStack = (_toolbarContainer.Child as ScrollViewer)?.Content as StackPanel
                                       ?? _toolbarContainer.Child as StackPanel;
                if (mainStack == null)
                {
                    return;
                }

                mainStack.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

                double scale = _toolbarContainer.XamlRoot?.RasterizationScale ?? 1.0;

                double desiredWidthDip = mainStack.DesiredSize.Width + _toolbarContainer.Padding.Left + _toolbarContainer.Padding.Right;
                double desiredHeightDip = mainStack.DesiredSize.Height + _toolbarContainer.Padding.Top + _toolbarContainer.Padding.Bottom;
                if (double.IsNaN(desiredHeightDip) || desiredHeightDip <= 0)
                {
                    desiredHeightDip = _toolbarContainer.ActualHeight > 0 ? _toolbarContainer.ActualHeight : ToolbarHeight;
                }
                else
                {
                    desiredHeightDip = Math.Max(desiredHeightDip, ToolbarHeight);
                }

                var displayArea = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Primary);
                double maxWidthDip = desiredWidthDip;
                double maxHeightDip = desiredHeightDip;
                if (displayArea != null)
                {
                    maxWidthDip = Math.Max(ToolbarButtonSize, (displayArea.WorkArea.Width / scale) - (ToolbarShadowPadding * 2));
                    maxHeightDip = Math.Max(ToolbarButtonSize, (displayArea.WorkArea.Height / scale) - (ToolbarShadowPadding * 2));
                }

                double widthDip = Math.Min(desiredWidthDip, maxWidthDip);
                double heightDip = Math.Min(desiredHeightDip, maxHeightDip);
                double widthWithShadowDip = widthDip + (ToolbarShadowPadding * 2);
                double heightWithShadowDip = heightDip + (ToolbarShadowPadding * 2);

                int widthPx = (int)Math.Ceiling(widthWithShadowDip * scale);
                int heightPx = (int)Math.Ceiling(heightWithShadowDip * scale);

                this.AppWindow.Resize(new Windows.Graphics.SizeInt32(widthPx, heightPx));
            }
        }

        private void PositionAtTopCenter()
        {
            var displayArea = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            // Work area coordinates are already in effective (DIP) on WinUI 3; keep logic but centralize for clarity
            int width = this.AppWindow.Size.Width;
            int height = this.AppWindow.Size.Height;
            int x = workArea.X + ((workArea.Width - width) / 2);
            int y = workArea.Y - height; // hidden above top
            this.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
    }
}
