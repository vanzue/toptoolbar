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
using TopToolbar.Serialization;

namespace TopToolbar.Services.Workspaces
{
    internal sealed class WorkspaceDefinitionStore
    {
        private readonly string _filePath;
        private readonly WorkspaceProviderConfigStore _configStore;

        public WorkspaceDefinitionStore(
            string filePath = null,
            WorkspaceProviderConfigStore configStore = null)
        {
            _configStore = configStore;
            _filePath = string.IsNullOrWhiteSpace(filePath)
                ? WorkspaceStoragePaths.GetWorkspaceDefinitionsPath(_configStore?.FilePath)
                : filePath;
        }

        public string FilePath => _filePath;

        public async Task<IReadOnlyList<WorkspaceDefinition>> LoadAllAsync(
            CancellationToken cancellationToken
        )
        {
            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            if (document.Workspaces == null || document.Workspaces.Count == 0)
            {
                return Array.Empty<WorkspaceDefinition>();
            }

            return CloneWorkspaces(document.Workspaces);
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

            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            var match = document.Workspaces?.FirstOrDefault(ws =>
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

            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            document.Workspaces ??= new List<WorkspaceDefinition>();

            document.Workspaces.RemoveAll(ws =>
                string.Equals(ws.Id, workspace.Id, StringComparison.OrdinalIgnoreCase)
                || (
                    !string.IsNullOrWhiteSpace(ws.Name)
                    && !string.IsNullOrWhiteSpace(workspace.Name)
                    && string.Equals(ws.Name, workspace.Name, StringComparison.OrdinalIgnoreCase)
                )
            );

            document.Workspaces.Insert(0, workspace);

            await SaveDocumentAsync(document, cancellationToken).ConfigureAwait(false);
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

            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            if (document.Workspaces == null || document.Workspaces.Count == 0)
            {
                return false;
            }

            var removed = document.Workspaces.RemoveAll(ws =>
                string.Equals(ws.Id, workspaceId, StringComparison.OrdinalIgnoreCase)
            );
            if (removed == 0)
            {
                return false;
            }

            await SaveDocumentAsync(document, cancellationToken).ConfigureAwait(false);
            return true;
        }

        public Task SaveAllAsync(
            IReadOnlyList<WorkspaceDefinition> workspaces,
            CancellationToken cancellationToken)
        {
            var list = workspaces != null
                ? workspaces.Where(ws => ws != null).ToList()
                : new List<WorkspaceDefinition>();

            var document = new WorkspaceDocument { Workspaces = list };
            return SaveDocumentAsync(document, cancellationToken);
        }
        
        private async Task<WorkspaceDocument> LoadDocumentAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_filePath))
            {
                var migrated = await TryMigrateFromProviderConfigAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (migrated != null && migrated.Workspaces != null && migrated.Workspaces.Count > 0)
                {
                    await SaveDocumentAsync(migrated, cancellationToken).ConfigureAwait(false);
                    await TryClearLegacyConfigAsync(cancellationToken).ConfigureAwait(false);
                    return migrated;
                }

                return new WorkspaceDocument();
            }

            try
            {
                await using var stream = new FileStream(
                    _filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
                var document = await JsonSerializer.DeserializeAsync(
                    stream,
                    WorkspaceProviderJsonContext.Default.WorkspaceDocument,
                    cancellationToken).ConfigureAwait(false);
                return document ?? new WorkspaceDocument();
            }
            catch (JsonException)
            {
                return new WorkspaceDocument();
            }
            catch (IOException)
            {
                return new WorkspaceDocument();
            }
        }

        private async Task SaveDocumentAsync(
            WorkspaceDocument document,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _filePath + ".tmp";
            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    WorkspaceProviderJsonContext.Default.WorkspaceDocument,
                    cancellationToken).ConfigureAwait(false);
            }

            File.Copy(tempPath, _filePath, overwrite: true);
            File.Delete(tempPath);
        }

        private async Task<WorkspaceDocument> TryMigrateFromProviderConfigAsync(
            CancellationToken cancellationToken)
        {
            if (_configStore == null)
            {
                return null;
            }

            WorkspaceProviderConfig config;
            try
            {
                config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }

            if (config.Data?.Workspaces == null || config.Data.Workspaces.Count == 0)
            {
                return null;
            }

            var workspaces = CloneWorkspaces(config.Data.Workspaces).ToList();
            return new WorkspaceDocument { Workspaces = workspaces };
        }

        private async Task TryClearLegacyConfigAsync(CancellationToken cancellationToken)
        {
            if (_configStore == null)
            {
                return;
            }

            try
            {
                var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (config.Data?.Workspaces == null || config.Data.Workspaces.Count == 0)
                {
                    return;
                }

                config.Data = null;
                await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

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

            var json = JsonSerializer.Serialize(workspace, DefaultJsonContext.Default.WorkspaceDefinition);
            return JsonSerializer.Deserialize(json, DefaultJsonContext.Default.WorkspaceDefinition);
        }
    }
}
