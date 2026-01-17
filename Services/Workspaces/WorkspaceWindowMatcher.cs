// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;

using TopToolbar.Services.Windowing;

namespace TopToolbar.Services.Workspaces
{
    internal static class WorkspaceWindowMatcher
    {
        public static bool IsMatch(WindowInfo window, ApplicationDefinition app)
        {
            if (window == null || app == null)
            {
                return false;
            }

            if (MatchesAppUserModelId(window, app))
            {
                return true;
            }

            if (MatchesPackageIdentity(window, app))
            {
                return true;
            }

            if (MatchesPwaIdentity(window, app))
            {
                return true;
            }

            if (!IsApplicationFrameHostPath(app.Path))
            {
                if (MatchesProcessPath(window, app))
                {
                    return true;
                }

                if (MatchesProcessName(window, app))
                {
                    return true;
                }
            }

            if (MatchesTitle(window, app))
            {
                return true;
            }

            return false;
        }

        private static bool MatchesAppUserModelId(WindowInfo window, ApplicationDefinition app)
        {
            if (string.IsNullOrWhiteSpace(app.AppUserModelId))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(window.AppUserModelId)
                && string.Equals(
                    window.AppUserModelId,
                    app.AppUserModelId,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesPackageIdentity(WindowInfo window, ApplicationDefinition app)
        {
            if (string.IsNullOrWhiteSpace(app.PackageFullName))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(window.PackageFullName)
                && string.Equals(window.PackageFullName, app.PackageFullName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var appFamily = GetPackageFamilyName(app.PackageFullName);
            if (string.IsNullOrWhiteSpace(appFamily))
            {
                return false;
            }

            var windowFamily = GetPackageFamilyName(window.PackageFullName);
            return !string.IsNullOrWhiteSpace(windowFamily)
                && string.Equals(windowFamily, appFamily, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesPwaIdentity(WindowInfo window, ApplicationDefinition app)
        {
            if (string.IsNullOrWhiteSpace(app.PwaAppId))
            {
                return false;
            }

            if (!IsBrowserProcess(window))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(window.AppUserModelId)
                && window.AppUserModelId.Contains(app.PwaAppId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool MatchesProcessPath(WindowInfo window, ApplicationDefinition app)
        {
            var normalizedAppPath = NormalizePath(app.Path);
            if (string.IsNullOrWhiteSpace(normalizedAppPath) || string.IsNullOrWhiteSpace(window.ProcessPath))
            {
                return false;
            }

            if (string.Equals(window.ProcessPath, normalizedAppPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var appFileName = NormalizeFileName(app.Path);
            return !string.IsNullOrWhiteSpace(appFileName)
                && !string.IsNullOrWhiteSpace(window.ProcessFileName)
                && string.Equals(window.ProcessFileName, appFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesProcessName(WindowInfo window, ApplicationDefinition app)
        {
            return !string.IsNullOrWhiteSpace(app.Name)
                && !string.IsNullOrWhiteSpace(window.ProcessName)
                && string.Equals(
                    NormalizeProcessName(window.ProcessName),
                    NormalizeProcessName(app.Name),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesTitle(WindowInfo window, ApplicationDefinition app)
        {
            return !string.IsNullOrWhiteSpace(app.Title)
                && !string.IsNullOrWhiteSpace(window.Title)
                && string.Equals(window.Title, app.Title, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBrowserProcess(WindowInfo window)
        {
            var processName = NormalizeProcessName(window.ProcessName);
            if (string.IsNullOrWhiteSpace(processName))
            {
                processName = NormalizeProcessName(window.ProcessFileName);
            }

            return string.Equals(processName, "msedge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "chrome", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsApplicationFrameHostPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var fileName = Path.GetFileName(path);
            return string.Equals(fileName, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPackageFamilyName(string packageFullName)
        {
            if (string.IsNullOrWhiteSpace(packageFullName))
            {
                return string.Empty;
            }

            var parts = packageFullName.Split('_');
            if (parts.Length < 2)
            {
                return string.Empty;
            }

            var name = parts[0];
            var publisher = parts[^1];
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(publisher))
            {
                return string.Empty;
            }

            return $"{name}_{publisher}";
        }

        private static string NormalizeFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(path).Trim('"');
                return Path.GetFileName(expanded);
            }
            catch
            {
                return Path.GetFileName(path.Trim('"'));
            }
        }

        private static string NormalizeProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return string.Empty;
            }

            var trimmed = processName.Trim();
            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 4);
            }

            return trimmed;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(path).Trim('"');
                if (expanded.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    return expanded;
                }

                return Path.GetFullPath(expanded);
            }
            catch
            {
                return path.Trim('"');
            }
        }
    }
}
