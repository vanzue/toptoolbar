// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using TopToolbar.Logging;

namespace TopToolbar.Services.Workspaces
{
    internal sealed partial class WorkspacesRuntimeService
    {
        private static string DescribeApp(ApplicationDefinition app)
        {
            if (app == null)
            {
                return "<null>";
            }

            var parts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(app.Name))
            {
                parts.Add(app.Name.Trim());
            }

            if (!string.IsNullOrWhiteSpace(app.Path))
            {
                parts.Add(app.Path.Trim());
            }

            if (!string.IsNullOrWhiteSpace(app.AppUserModelId))
            {
                parts.Add(app.AppUserModelId.Trim());
            }

            if (parts.Count == 0 && !string.IsNullOrWhiteSpace(app.Title))
            {
                parts.Add(app.Title.Trim());
            }

            var identity = string.Join(" | ", parts);
            var id = string.IsNullOrWhiteSpace(app.Id) ? "<no-id>" : app.Id;
            return string.IsNullOrWhiteSpace(identity) ? $"id={id}" : $"{identity} (id={id})";
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

        private static string ExpandPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Environment.ExpandEnvironmentVariables(path).Trim('"');
            }
            catch
            {
                return path.Trim('"');
            }
        }

        private static string DetermineWorkingDirectory(string path, bool useShellExecute)
        {
            if (useShellExecute)
            {
                return AppContext.BaseDirectory;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return directory;
                }
            }
            catch { }

            return AppContext.BaseDirectory;
        }

        private static void LogPerf(string message)
        {
            try
            {
                AppLogger.LogInfo(message);
                try
                {
                    var ts = DateTime.Now.ToString(
                        "HH:mm:ss.fff",
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                    System.Console.WriteLine("[" + ts + "] " + message);
                }
                catch { }
            }
            catch { }
        }

        [ComImport]
        [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            int ActivateApplication(
                string appUserModelId,
                string arguments,
                ActivateOptions options,
                out uint processId
            );

            int ActivateForFile(
                string appUserModelId,
                IntPtr itemArray,
                string verb,
                out uint processId
            );

            int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
        }

        [ComImport]
        [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
        private class ApplicationActivationManager { }

        [Flags]
        private enum ActivateOptions
        {
            None = 0x0,
            DesignMode = 0x1,
            NoErrorUI = 0x2,
            NoSplashScreen = 0x4,
        }
    }
}
