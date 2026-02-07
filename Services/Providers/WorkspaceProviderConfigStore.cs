// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TopToolbar.Logging;
using TopToolbar.Models.Providers;
using TopToolbar.Services.Storage;
using TopToolbar.Serialization;
using TopToolbar.Services.Workspaces;

namespace TopToolbar.Services.Providers
{
    internal sealed class WorkspaceProviderConfigStore
    {
        private const int SaveRetryCount = 6;
        private const int SaveRetryDelayMilliseconds = 60;
        private readonly string _filePath;

        public WorkspaceProviderConfigStore(string filePath = null)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath)
                ? WorkspaceStoragePaths.GetProviderConfigPath()
                : filePath;
        }

        public string FilePath => _filePath;

        public async Task<WorkspaceProviderConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_filePath))
            {
                return CreateDefaultConfig();
            }

            try
            {
                await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var config = await JsonSerializer.DeserializeAsync(stream, WorkspaceProviderJsonContext.Default.WorkspaceProviderConfig, cancellationToken).ConfigureAwait(false);
                return config ?? CreateDefaultConfig();
            }
            catch (JsonException)
            {
                return CreateDefaultConfig();
            }
            catch (IOException)
            {
                return CreateDefaultConfig();
            }
        }

        public async Task SaveAsync(WorkspaceProviderConfig config, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var workingConfig = CloneConfig(config);
            var previousLastUpdated = workingConfig.LastUpdated;
            var conflictLogged = false;

            for (var attempt = 0; attempt < SaveRetryCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!conflictLogged && previousLastUpdated != default)
                {
                    var latest = await LoadAsync(cancellationToken).ConfigureAwait(false);
                    if (latest.LastUpdated > previousLastUpdated)
                    {
                        var attemptedBase = previousLastUpdated;
                        workingConfig = MergeConfig(latest, workingConfig);
                        previousLastUpdated = latest.LastUpdated;
                        conflictLogged = true;
                        AppLogger.LogWarning(
                            $"WorkspaceProviderConfigStore: detected concurrent update. " +
                            $"existing={latest.LastUpdated:O}, attemptedBase={attemptedBase:O}");
                    }
                }

                var expectedVersion = FileConcurrencyGuard.GetFileVersionUtcTicks(_filePath);
                workingConfig.LastUpdated = DateTimeOffset.UtcNow;

                try
                {
                    if (await TrySaveConfigAsync(workingConfig, expectedVersion, cancellationToken).ConfigureAwait(false))
                    {
                        config.LastUpdated = workingConfig.LastUpdated;
                        config.Buttons = CloneButtons(workingConfig.Buttons);
                        config.Data = workingConfig.Data;
                        return;
                    }
                }
                catch (IOException) when (attempt + 1 < SaveRetryCount)
                {
                }
                catch (UnauthorizedAccessException) when (attempt + 1 < SaveRetryCount)
                {
                }

                if (attempt + 1 < SaveRetryCount)
                {
                    await Task.Delay(SaveRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new IOException("Failed to save workspace provider config after multiple retries.");
        }

        private WorkspaceProviderConfig MergeConfig(
            WorkspaceProviderConfig latest,
            WorkspaceProviderConfig incoming)
        {
            var merged = CloneConfig(latest);

            if (incoming == null)
            {
                return merged;
            }

            if (incoming.SchemaVersion != 0)
            {
                merged.SchemaVersion = incoming.SchemaVersion;
            }

            if (!string.IsNullOrWhiteSpace(incoming.ProviderId))
            {
                merged.ProviderId = incoming.ProviderId;
            }

            if (!string.IsNullOrWhiteSpace(incoming.DisplayName))
            {
                merged.DisplayName = incoming.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Description))
            {
                merged.Description = incoming.Description;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Author))
            {
                merged.Author = incoming.Author;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Version))
            {
                merged.Version = incoming.Version;
            }

            merged.Enabled = incoming.Enabled;
            if (incoming.Data != null)
            {
                merged.Data = incoming.Data;
            }

            merged.Buttons = MergeButtons(latest?.Buttons, incoming.Buttons);
            return merged;
        }

        private static System.Collections.Generic.List<WorkspaceButtonConfig> MergeButtons(
            System.Collections.Generic.IReadOnlyList<WorkspaceButtonConfig> latestButtons,
            System.Collections.Generic.IReadOnlyList<WorkspaceButtonConfig> incomingButtons)
        {
            var merged = new System.Collections.Generic.Dictionary<string, WorkspaceButtonConfig>(StringComparer.OrdinalIgnoreCase);

            if (latestButtons != null)
            {
                for (var i = 0; i < latestButtons.Count; i++)
                {
                    var button = latestButtons[i];
                    if (button == null)
                    {
                        continue;
                    }

                    merged[GetButtonKey(button)] = CloneButton(button);
                }
            }

            if (incomingButtons != null)
            {
                for (var i = 0; i < incomingButtons.Count; i++)
                {
                    var button = incomingButtons[i];
                    if (button == null)
                    {
                        continue;
                    }

                    merged[GetButtonKey(button)] = CloneButton(button);
                }
            }

            return new System.Collections.Generic.List<WorkspaceButtonConfig>(merged.Values);
        }

        private static string GetButtonKey(WorkspaceButtonConfig button)
        {
            if (button == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(button.WorkspaceId))
            {
                return "workspace:" + button.WorkspaceId.Trim();
            }

            return "id:" + (button.Id ?? string.Empty).Trim();
        }

        private WorkspaceProviderConfig CloneConfig(WorkspaceProviderConfig config)
        {
            if (config == null)
            {
                return CreateDefaultConfig();
            }

            var json = JsonSerializer.Serialize(config, WorkspaceProviderJsonContext.Default.WorkspaceProviderConfig);
            var clone = JsonSerializer.Deserialize(json, WorkspaceProviderJsonContext.Default.WorkspaceProviderConfig);
            if (clone == null)
            {
                return CreateDefaultConfig();
            }

            clone.Buttons ??= new System.Collections.Generic.List<WorkspaceButtonConfig>();
            return clone;
        }

        private static WorkspaceButtonConfig CloneButton(WorkspaceButtonConfig button)
        {
            if (button == null)
            {
                return new WorkspaceButtonConfig();
            }

            return new WorkspaceButtonConfig
            {
                Id = button.Id ?? string.Empty,
                WorkspaceId = button.WorkspaceId ?? string.Empty,
                Name = button.Name ?? string.Empty,
                Description = button.Description ?? string.Empty,
                Enabled = button.Enabled,
                SortOrder = button.SortOrder,
                Icon = button.Icon == null
                    ? null
                    : new TopToolbar.Models.Providers.ProviderIcon
                    {
                        Type = button.Icon.Type,
                        Path = button.Icon.Path ?? string.Empty,
                        Glyph = button.Icon.Glyph ?? string.Empty,
                        CatalogId = button.Icon.CatalogId ?? string.Empty,
                    },
            };
        }

        private static System.Collections.Generic.List<WorkspaceButtonConfig> CloneButtons(
            System.Collections.Generic.IReadOnlyList<WorkspaceButtonConfig> buttons)
        {
            var clones = new System.Collections.Generic.List<WorkspaceButtonConfig>();
            if (buttons == null)
            {
                return clones;
            }

            for (var i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                if (button != null)
                {
                    clones.Add(CloneButton(button));
                }
            }

            return clones;
        }

        private async Task<bool> TrySaveConfigAsync(
            WorkspaceProviderConfig config,
            long expectedVersionTicks,
            CancellationToken cancellationToken)
        {
            await using var writeLock = await FileConcurrencyGuard
                .AcquireWriteLockAsync(_filePath, cancellationToken)
                .ConfigureAwait(false);

            var currentVersion = FileConcurrencyGuard.GetFileVersionUtcTicks(_filePath);
            if (currentVersion != expectedVersionTicks)
            {
                return false;
            }

            var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        config,
                        WorkspaceProviderJsonContext.Default.WorkspaceProviderConfig,
                        cancellationToken).ConfigureAwait(false);
                }

                File.Copy(tempPath, _filePath, overwrite: true);
                return true;
            }
            finally
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        public WorkspaceProviderConfig CreateDefaultConfig()
        {
            return new WorkspaceProviderConfig
            {
                Buttons = new System.Collections.Generic.List<WorkspaceButtonConfig>(),
            };
        }
    }
}
