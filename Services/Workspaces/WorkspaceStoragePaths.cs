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

        internal static string GetDefaultWorkspacesPath()
        {
            return Path.Combine(AppPaths.ProvidersDirectory, WorkspaceProviderFileName);
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
