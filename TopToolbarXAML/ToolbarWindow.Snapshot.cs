// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TopToolbar.Providers;
using WinUIEx;

namespace TopToolbar
{
    public sealed partial class ToolbarWindow
    {
        private async System.Threading.Tasks.Task HandleSnapshotButtonClickAsync(Button triggerButton)
        {
            if (_snapshotInProgress)
            {
                return;
            }

            _snapshotInProgress = true;
            await SetButtonEnabledAsync(triggerButton, false).ConfigureAwait(true);

            try
            {
                var workspaceName = await SnapshotPromptWindow.ShowAsync(this).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(workspaceName))
                {
                    return;
                }

                try
                {
                    if (!_providerRuntime.TryGetProvider("WorkspaceProvider", out var provider)
                        || provider is not WorkspaceProvider workspaceProvider)
                    {
                        await ShowSimpleMessageOnUiThreadAsync(
                            "Snapshot failed",
                            "Workspace provider is not available."
                        );
                        return;
                    }

                    var workspace = await workspaceProvider.SnapshotAsync(workspaceName, CancellationToken.None).ConfigureAwait(false);
                    if (workspace == null)
                    {
                        await ShowSimpleMessageOnUiThreadAsync("Snapshot failed", "No eligible windows were detected to capture.");
                        return;
                    }

                    await ShowSimpleMessageOnUiThreadAsync("Snapshot saved", $"Workspace \"{workspace.Name}\" has been saved.");

                    var dispatcher = DispatcherQueue;
                    if (dispatcher != null && !dispatcher.HasThreadAccess)
                    {
                        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                        if (!dispatcher.TryEnqueue(async () =>
                        {
                            await RefreshWorkspaceGroupAsync();
                            tcs.TrySetResult(true);
                        }))
                        {
                            // Fallback, run synchronously if enqueue fails
                            await RefreshWorkspaceGroupAsync();
                        }
                        else
                        {
                            await tcs.Task.ConfigureAwait(true);
                        }
                    }
                    else
                    {
                        await RefreshWorkspaceGroupAsync();
                    }
                }
                catch (Exception ex)
                {
                    await ShowSimpleMessageOnUiThreadAsync("Snapshot failed", ex.Message);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                await SetButtonEnabledAsync(triggerButton, true).ConfigureAwait(true);

                _snapshotInProgress = false;
            }
        }

        private System.Threading.Tasks.Task SetButtonEnabledAsync(Button btn, bool enabled)
        {
            if (btn == null)
            {
                return System.Threading.Tasks.Task.CompletedTask;
            }

            var dispatcher = btn.DispatcherQueue ?? DispatcherQueue;

            void Apply()
            {
                try
                {
                    // Only touch UI element if it is still loaded/attached
                    if (btn.IsLoaded)
                    {
                        btn.IsEnabled = enabled;
                    }
                }
                catch
                {
                    // Control may have been disposed/recycled during UI rebuild; ignore
                }
            }

            if (dispatcher == null)
            {
                Apply();
                return System.Threading.Tasks.Task.CompletedTask;
            }

            if (dispatcher.HasThreadAccess)
            {
                Apply();
                return System.Threading.Tasks.Task.CompletedTask;
            }

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            if (!dispatcher.TryEnqueue(() =>
            {
                Apply();
                tcs.TrySetResult(true);
            }))
            {
                // Fallback: apply directly if enqueue fails
                Apply();
                return System.Threading.Tasks.Task.CompletedTask;
            }

            return tcs.Task;
        }

        private System.Threading.Tasks.Task ShowSimpleMessageOnUiThreadAsync(string title, string message)
        {
            var dispatcher = DispatcherQueue;
            if (dispatcher == null)
            {
                return System.Threading.Tasks.Task.CompletedTask;
            }

            if (dispatcher.HasThreadAccess)
            {
                return ShowSimpleMessageAsync(this, title, message);
            }

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            if (!dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await ShowSimpleMessageAsync(this, title, message);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
            {
                return System.Threading.Tasks.Task.CompletedTask;
            }

            return tcs.Task;
        }

        private static async System.Threading.Tasks.Task ShowSimpleMessageAsync(WindowEx owner, string title, string message)
        {
            using var overlay = await TransparentOverlayHost.CreateAsync(owner).ConfigureAwait(true);
            if (overlay == null)
            {
                return;
            }

            using var overlayScope = ContentDialogOverlayScope.Transparent();

            var dialog = new ContentDialog
            {
                XamlRoot = overlay.Root.XamlRoot,
                Title = title ?? string.Empty,
                Content = new TextBlock
                {
                    Text = message ?? string.Empty,
                    TextWrapping = TextWrapping.Wrap,
                },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
            };

            await dialog.ShowAsync(ContentDialogPlacement.Popup);
        }
    }
}
