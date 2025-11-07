// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TopToolbar;
using TopToolbar.Models.Providers;

namespace TopToolbar.Services.Providers
{
    internal sealed class WorkspaceProviderConfigStore
    {
        private const string WorkspaceProviderFileName = "WorkspaceProvider.json";

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            },
        };

        private static readonly JsonSerializerOptions WriteOptions = new(ReadOptions)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly string _filePath;

        public WorkspaceProviderConfigStore(string filePath = null)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath)
                ? Path.Combine(AppPaths.ProvidersDirectory, WorkspaceProviderFileName)
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
                var config = await JsonSerializer.DeserializeAsync<WorkspaceProviderConfig>(stream, ReadOptions, cancellationToken).ConfigureAwait(false);
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

            config.LastUpdated = DateTimeOffset.UtcNow;

            var tempPath = _filePath + ".tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, config, WriteOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Copy(tempPath, _filePath, overwrite: true);
            File.Delete(tempPath);
        }

        public WorkspaceProviderConfig CreateDefaultConfig()
        {
            return new WorkspaceProviderConfig
            {
                Buttons = new System.Collections.Generic.List<WorkspaceButtonConfig>(),
                Data = new WorkspaceProviderData(),
            };
        }
    }
}
