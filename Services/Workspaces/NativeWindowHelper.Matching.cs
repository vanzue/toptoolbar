// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;

namespace TopToolbar.Services.Workspaces
{
    internal static partial class NativeWindowHelper
    {
        public static bool IsMatch(WindowInfo window, ApplicationDefinition app)
        {
            if (window == null || app == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(app.AppUserModelId))
            {
                if (
                    !string.IsNullOrWhiteSpace(window.AppUserModelId)
                    && string.Equals(
                        window.AppUserModelId,
                        app.AppUserModelId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            var normalizedAppPath = NormalizePath(app.Path);
            if (
                !string.IsNullOrWhiteSpace(normalizedAppPath)
                && !string.IsNullOrWhiteSpace(window.ProcessPath)
                && string.Equals(
                    window.ProcessPath,
                    normalizedAppPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(app.Path))
            {
                var appFileName = NormalizeFileName(app.Path);
                if (
                    !string.IsNullOrWhiteSpace(appFileName)
                    && !string.IsNullOrWhiteSpace(window.ProcessFileName)
                    && string.Equals(
                        window.ProcessFileName,
                        appFileName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            if (
                !string.IsNullOrWhiteSpace(app.Name)
                && !string.IsNullOrWhiteSpace(window.ProcessName)
                && string.Equals(
                    NormalizeProcessName(window.ProcessName),
                    NormalizeProcessName(app.Name),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            if (
                !string.IsNullOrWhiteSpace(app.Title)
                && !string.IsNullOrWhiteSpace(window.Title)
                && string.Equals(window.Title, app.Title, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            return false;
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
