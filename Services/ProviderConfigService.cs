// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using TopToolbar.Logging;
using TopToolbar.Models;
using TopToolbar.Providers.Configuration;
using TopToolbar.Serialization;

namespace TopToolbar.Services;

public sealed class ProviderConfigService
{
    private readonly string _userDirectory;
    private readonly string _defaultDirectory;

    public ProviderConfigService(string userDirectory = null, string defaultDirectory = null)
    {
        _userDirectory = string.IsNullOrWhiteSpace(userDirectory)
            ? AppPaths.ProvidersDirectory
            : userDirectory;

        _defaultDirectory = string.IsNullOrWhiteSpace(defaultDirectory)
            ? Path.Combine(AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "TopToolbarProviders")
            : defaultDirectory;

        if (!string.IsNullOrEmpty(_userDirectory))
        {
            Directory.CreateDirectory(_userDirectory);
        }
    }

    public IReadOnlyList<ProviderConfig> LoadConfigs()
    {
        var results = new Dictionary<string, ProviderConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in EnumerateDirectories())
        {
            foreach (var file in EnumerateJsonFiles(directory))
            {
                try
                {
                    var config = LoadConfig(file);
                    if (config == null || string.IsNullOrWhiteSpace(config.Id))
                    {
                        continue;
                    }

                    results[config.Id] = Normalize(config);
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"ProviderConfigService: failed to load '{file}' - {ex.Message}.");
                }
            }
        }

        return new ReadOnlyCollection<ProviderConfig>(results.Values.ToList());
    }

    private IEnumerable<string> EnumerateDirectories()
    {
        if (!string.IsNullOrEmpty(_defaultDirectory) && Directory.Exists(_defaultDirectory))
        {
            yield return _defaultDirectory;
        }

        if (!string.IsNullOrEmpty(_userDirectory) && Directory.Exists(_userDirectory))
        {
            yield return _userDirectory;
        }
    }

    private static IEnumerable<string> EnumerateJsonFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static ProviderConfig LoadConfig(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, ProviderConfigJsonContext.Default.ProviderConfig);
    }

    private static ProviderConfig Normalize(ProviderConfig config)
    {
        config ??= new ProviderConfig();
        config.Id = config.Id?.Trim() ?? string.Empty;
        config.GroupName = config.GroupName?.Trim() ?? string.Empty;
        config.Description = config.Description?.Trim() ?? string.Empty;
        config.Actions ??= new List<ProviderActionConfig>();
        config.Layout ??= new ProviderLayoutConfig();
        config.External ??= new ExternalProviderConfig();

        for (var i = config.Actions.Count - 1; i >= 0; i--)
        {
            var action = config.Actions[i];
            if (action == null)
            {
                config.Actions.RemoveAt(i);
                continue;
            }

            action.Id = action.Id?.Trim() ?? string.Empty;
            action.Name = action.Name?.Trim() ?? string.Empty;
            action.Description = action.Description?.Trim() ?? string.Empty;
            action.IconGlyph = action.IconGlyph?.Trim() ?? string.Empty;
            action.IconPath = action.IconPath?.Trim() ?? string.Empty;
            action.Action ??= new ToolbarAction();

            if (action.Action.Type == ToolbarActionType.Provider)
            {
                action.Action.ProviderId = string.IsNullOrWhiteSpace(action.Action.ProviderId)
                    ? config.Id
                    : action.Action.ProviderId.Trim();

                action.Action.ProviderActionId = action.Action.ProviderActionId?.Trim() ?? string.Empty;
                action.Action.ProviderArgumentsJson = action.Action.ProviderArgumentsJson?.Trim();
            }
        }

        return config;
    }
}
