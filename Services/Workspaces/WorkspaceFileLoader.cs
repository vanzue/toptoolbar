// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TopToolbar.Models.Providers;
using TopToolbar.Services.Providers;

namespace TopToolbar.Services.Workspaces
{
    internal sealed class WorkspaceFileLoader
    {
        private readonly WorkspaceProviderConfigStore _configStore;

        public WorkspaceFileLoader(string providerConfigPath = null)
        {
            _configStore = new WorkspaceProviderConfigStore(providerConfigPath);
        }

        public async Task<IReadOnlyList<WorkspaceDefinition>> LoadAllAsync(
            CancellationToken cancellationToken
        )
        {
            var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (config.Data?.Workspaces == null || config.Data.Workspaces.Count == 0)
            {
                return Array.Empty<WorkspaceDefinition>();
            }

            return CloneWorkspaces(config.Data.Workspaces);
        }

        public async Task<WorkspaceDefinition> LoadByIdAsync(
            string workspaceId,
            CancellationToken cancellationToken
        )
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return null;
            }

            var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var match = config.Data?.Workspaces?.FirstOrDefault(ws =>
                string.Equals(ws.Id, workspaceId, StringComparison.OrdinalIgnoreCase)
            );
            return match != null ? CloneWorkspace(match) : null;
        }

        public async Task SaveWorkspaceAsync(
            WorkspaceDefinition workspace,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            config.Data ??= new WorkspaceProviderData();
            config.Data.Workspaces ??= new List<WorkspaceDefinition>();

            config.Data.Workspaces.RemoveAll(ws =>
                string.Equals(ws.Id, workspace.Id, StringComparison.OrdinalIgnoreCase)
                || (
                    !string.IsNullOrWhiteSpace(ws.Name)
                    && !string.IsNullOrWhiteSpace(workspace.Name)
                    && string.Equals(ws.Name, workspace.Name, StringComparison.OrdinalIgnoreCase)
                )
            );

            config.Data.Workspaces.Insert(0, workspace);

            EnsureWorkspaceButton(config, workspace);

            await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> DeleteWorkspaceAsync(
            string workspaceId,
            CancellationToken cancellationToken
        )
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return false;
            }

            var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (config.Data?.Workspaces == null || config.Data.Workspaces.Count == 0)
            {
                return false;
            }

            var removed = config.Data.Workspaces.RemoveAll(ws =>
                string.Equals(ws.Id, workspaceId, StringComparison.OrdinalIgnoreCase)
            );
            if (removed == 0)
            {
                return false;
            }

            if (config.Buttons != null)
            {
                config.Buttons.RemoveAll(button =>
                    string.Equals(
                        button.WorkspaceId,
                        workspaceId,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        button.Id,
                        BuildButtonId(workspaceId),
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            }

            await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
            return true;
        }

        private static void EnsureWorkspaceButton(
            WorkspaceProviderConfig config,
            WorkspaceDefinition workspace
        )
        {
            config.Buttons ??= new List<WorkspaceButtonConfig>();

            var buttonId = BuildButtonId(workspace.Id);
            var button = config.Buttons.FirstOrDefault(b =>
                string.Equals(b.WorkspaceId, workspace.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(b.Id, buttonId, StringComparison.OrdinalIgnoreCase)
            );

            if (button == null)
            {
                button = new WorkspaceButtonConfig
                {
                    Id = buttonId,
                    WorkspaceId = workspace.Id,
                    Name = string.IsNullOrWhiteSpace(workspace.Name)
                        ? workspace.Id
                        : workspace.Name,
                    Description = workspace.Id,
                    Enabled = true,
                    Icon = new ProviderIcon { Type = ProviderIconType.Glyph, Glyph = "\uE7F1" },
                };

                config.Buttons.Add(button);
            }
            else
            {
                button.WorkspaceId = workspace.Id;
                if (string.IsNullOrWhiteSpace(button.Name))
                {
                    button.Name = string.IsNullOrWhiteSpace(workspace.Name)
                        ? workspace.Id
                        : workspace.Name;
                }

                if (string.IsNullOrWhiteSpace(button.Description))
                {
                    button.Description = workspace.Id;
                }

                button.Enabled = true;
            }
        }

        private static string BuildButtonId(string workspaceId) =>
            string.IsNullOrWhiteSpace(workspaceId) ? string.Empty : $"workspace::{workspaceId}";

        private static IReadOnlyList<WorkspaceDefinition> CloneWorkspaces(
            IEnumerable<WorkspaceDefinition> source
        )
        {
            var list = new List<WorkspaceDefinition>();
            foreach (var workspace in source)
            {
                var clone = CloneWorkspace(workspace);
                if (clone != null)
                {
                    list.Add(clone);
                }
            }

            return list;
        }

        private static WorkspaceDefinition CloneWorkspace(WorkspaceDefinition workspace)
        {
            if (workspace == null)
            {
                return null;
            }

            var json = JsonSerializer.Serialize(workspace);
            return JsonSerializer.Deserialize<WorkspaceDefinition>(json);
        }
    }
}
