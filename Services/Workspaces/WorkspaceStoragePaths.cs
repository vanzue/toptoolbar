// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;

namespace TopToolbar.Services.Workspaces
{
    /// <summary>
    /// Provides shared workspace storage locations used by TopToolbar modules.
    /// </summary>
    internal static class WorkspaceStoragePaths
    {
        private const string WorkspaceProviderFileName = "WorkspaceProvider.json";
        private const string WorkspaceDefinitionsFileName = "workspaces.json";

        internal static string GetProviderConfigPath()
        {
            return Path.Combine(AppPaths.ProvidersDirectory, WorkspaceProviderFileName);
        }

        internal static string GetDefaultWorkspacesPath()
        {
            return GetProviderConfigPath();
        }

        internal static string GetWorkspaceDefinitionsPath(string providerConfigPath = null)
        {
            if (!string.IsNullOrWhiteSpace(providerConfigPath))
            {
                var defaultConfigPath = GetProviderConfigPath();
                if (string.Equals(providerConfigPath, defaultConfigPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(AppPaths.ConfigDirectory, WorkspaceDefinitionsFileName);
                }

                var directory = Path.GetDirectoryName(providerConfigPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return Path.Combine(directory, WorkspaceDefinitionsFileName);
                }
            }

            return Path.Combine(AppPaths.ConfigDirectory, WorkspaceDefinitionsFileName);
        }

        internal static string GetLegacyPowerToysPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "PowerToys",
                "Workspaces",
                "workspaces.json");
        }
    }
}
