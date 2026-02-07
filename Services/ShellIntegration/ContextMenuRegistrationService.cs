// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.Win32;
using TopToolbar.Logging;

namespace TopToolbar.Services.ShellIntegration
{
    internal static class ContextMenuRegistrationService
    {
        private const string MenuText = "Pin to TopToolbar";

        private static readonly string[] MenuKeyPaths =
        {
            @"Software\Classes\*\shell\TopToolbar.Pin",
            @"Software\Classes\Directory\shell\TopToolbar.Pin",
            @"Software\Classes\exefile\shell\TopToolbar.Pin",
        };

        internal static void EnsureRegisteredForCurrentUser(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            try
            {
                for (var i = 0; i < MenuKeyPaths.Length; i++)
                {
                    CreateOrUpdateMenuKey(MenuKeyPaths[i], executablePath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"ContextMenuRegistration: registration failed - {ex.Message}");
            }
        }

        internal static void RemoveRegistrationForCurrentUser()
        {
            try
            {
                for (var i = 0; i < MenuKeyPaths.Length; i++)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(MenuKeyPaths[i], false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"ContextMenuRegistration: unregister failed - {ex.Message}");
            }
        }

        private static void CreateOrUpdateMenuKey(string keyPath, string executablePath)
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (key == null)
            {
                return;
            }

            key.SetValue("MUIVerb", MenuText, RegistryValueKind.String);
            key.SetValue("Icon", ResolveMenuIconPath(executablePath), RegistryValueKind.String);
            key.SetValue("Position", "Top", RegistryValueKind.String);

            using var command = key.CreateSubKey("command");
            if (command == null)
            {
                return;
            }

            var commandLine = $"\"{executablePath}\" --pin \"%1\"";
            command.SetValue(string.Empty, commandLine, RegistryValueKind.String);
        }

        private static string ResolveMenuIconPath(string executablePath)
        {
            try
            {
                var executableDirectory = Path.GetDirectoryName(executablePath);
                if (!string.IsNullOrWhiteSpace(executableDirectory))
                {
                    var iconPath = Path.Combine(executableDirectory, "Assets", "Logos", "ContextMenuIcon.ico");
                    if (File.Exists(iconPath))
                    {
                        return iconPath;
                    }
                }
            }
            catch
            {
            }

            return executablePath;
        }
    }
}
