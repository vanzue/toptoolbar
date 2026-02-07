// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using TopToolbar.Logging;
using TopToolbar.Models;

namespace TopToolbar.Services.Pinning
{
    internal static class ToolbarPinService
    {
        private const string PinMutexName = @"Local\TopToolbar.PinService.Mutex";
        private const string PinnedGroupName = "Pinned";
        private const string PinnedGroupDescription = "Items pinned from Explorer";

        internal static bool TryPinPath(string rawPath, out string message)
        {
            message = string.Empty;

            if (!TryNormalizeInputPath(rawPath, out var path))
            {
                message = "Path is empty or invalid.";
                return false;
            }

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                message = $"Path does not exist: {path}";
                return false;
            }

            try
            {
                using var mutex = new Mutex(false, PinMutexName);
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    message = "Timed out while waiting to update toolbar config.";
                    return false;
                }

                try
                {
                    return TryPinPathCore(path, out message);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                AppLogger.LogError("ToolbarPinService: pin failed", ex);
                return false;
            }
        }

        private static bool TryPinPathCore(string path, out string message)
        {
            var configService = new ToolbarConfigService();
            var config = configService.LoadAsync().ConfigureAwait(false).GetAwaiter().GetResult();

            var normalizedPath = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                message = "Failed to normalize path.";
                return false;
            }

            if (ContainsPinnedTarget(config, normalizedPath))
            {
                message = "Already pinned.";
                return true;
            }

            var group = config.Groups.FirstOrDefault(g =>
                g != null && string.Equals(g.Name, PinnedGroupName, StringComparison.OrdinalIgnoreCase));

            if (group == null)
            {
                group = new ButtonGroup
                {
                    Name = PinnedGroupName,
                    Description = PinnedGroupDescription,
                    Layout = new ToolbarGroupLayout
                    {
                        Style = ToolbarGroupLayoutStyle.Icon,
                        Overflow = ToolbarGroupOverflowMode.Wrap,
                    },
                };

                config.Groups.Insert(0, group);
            }

            var isDirectory = Directory.Exists(normalizedPath);
            var button = new ToolbarButton
            {
                Name = GetDisplayName(normalizedPath, isDirectory),
                Description = normalizedPath,
                IconType = ToolbarIconType.Catalog,
                IconGlyph = isDirectory ? "\uE8B7" : "\uE8A5",
                IsEnabled = true,
                IsIconCustomized = false,
                Action = new ToolbarAction
                {
                    Type = ToolbarActionType.CommandLine,
                    Command = BuildCommand(normalizedPath, isDirectory),
                    Arguments = string.Empty,
                    WorkingDirectory = BuildWorkingDirectory(normalizedPath, isDirectory),
                    RunAsAdmin = false,
                },
            };

            group.Buttons.Add(button);
            configService.SaveAsync(config).ConfigureAwait(false).GetAwaiter().GetResult();

            AppLogger.LogInfo($"ToolbarPinService: pinned '{normalizedPath}'");
            message = "Pinned.";
            return true;
        }

        private static bool ContainsPinnedTarget(ToolbarConfig config, string normalizedPath)
        {
            if (config?.Groups == null || string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            foreach (var group in config.Groups)
            {
                if (group?.Buttons == null)
                {
                    continue;
                }

                foreach (var button in group.Buttons)
                {
                    if (!TryExtractTargetPath(button?.Action, out var target))
                    {
                        continue;
                    }

                    if (string.Equals(target, normalizedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryExtractTargetPath(ToolbarAction action, out string normalizedPath)
        {
            normalizedPath = string.Empty;

            if (action == null || action.Type != ToolbarActionType.CommandLine)
            {
                return false;
            }

            var command = action.Command?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            var candidate = string.Empty;

            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = command.IndexOf('"', 1);
                if (end > 1)
                {
                    candidate = command.Substring(1, end - 1);
                }
            }
            else if (command.StartsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                var firstQuote = command.IndexOf('"');
                if (firstQuote >= 0)
                {
                    var secondQuote = command.IndexOf('"', firstQuote + 1);
                    if (secondQuote > firstQuote)
                    {
                        candidate = command.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                    }
                }
            }
            else if (Path.IsPathRooted(command))
            {
                candidate = command;
            }

            normalizedPath = NormalizePath(candidate);
            return !string.IsNullOrWhiteSpace(normalizedPath);
        }

        private static string BuildCommand(string normalizedPath, bool isDirectory)
        {
            if (isDirectory)
            {
                return $"explorer.exe \"{normalizedPath}\"";
            }

            return $"\"{normalizedPath}\"";
        }

        private static string BuildWorkingDirectory(string normalizedPath, bool isDirectory)
        {
            if (isDirectory)
            {
                return normalizedPath;
            }

            try
            {
                return Path.GetDirectoryName(normalizedPath) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetDisplayName(string normalizedPath, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return "Pinned Item";
            }

            try
            {
                if (isDirectory)
                {
                    var name = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    return string.IsNullOrWhiteSpace(name) ? normalizedPath : name;
                }

                var fileName = Path.GetFileNameWithoutExtension(normalizedPath);
                return string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(normalizedPath) : fileName;
            }
            catch
            {
                return normalizedPath;
            }
        }

        private static bool TryNormalizeInputPath(string input, out string normalizedPath)
        {
            normalizedPath = NormalizePath(input);
            return !string.IsNullOrWhiteSpace(normalizedPath);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                var trimmed = Environment.ExpandEnvironmentVariables(path).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    return string.Empty;
                }

                return Path.GetFullPath(trimmed);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
