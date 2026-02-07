// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using TopToolbar.Logging;
using TopToolbar.Services.Pinning;
using TopToolbar.Services.ShellIntegration;
using Windows.ApplicationModel;

namespace TopToolbar
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                AppLogger.Initialize(AppPaths.Logs);
                EnsureAppDirectories();
                AppLogger.LogInfo($"Logger initialized. Logs directory: {AppPaths.Logs}");
                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    try
                    {
                        var message =
                            $"AppDomain unhandled exception (IsTerminating={e.IsTerminating})";
                        if (e.ExceptionObject is Exception exception)
                        {
                            AppLogger.LogError(message, exception);
                        }
                        else
                        {
                            AppLogger.LogError($"{message} - {e.ExceptionObject}");
                        }
                    }
                    catch { }
                };
                TaskScheduler.UnobservedTaskException += (_, e) =>
                {
                    try
                    {
                        AppLogger.LogError("Unobserved task exception", e.Exception);
                        e.SetObserved();
                    }
                    catch { }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AppLogger init failed: {ex.Message}");
            }

            if (TryHandleCommandLine(args))
            {
                return;
            }

            if (!IsRunningPackaged())
            {
                try
                {
                    var executablePath = Environment.ProcessPath ?? string.Empty;
                    ContextMenuRegistrationService.EnsureRegisteredForCurrentUser(executablePath);
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"ContextMenuRegistration: startup registration failed - {ex.Message}");
                }
            }
            else
            {
                ContextMenuRegistrationService.RemoveRegistrationForCurrentUser();
            }

            Application.Start(args =>
            {
                _ = new App();
            });
        }

        private static bool TryHandleCommandLine(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--pin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    AppLogger.LogWarning("PinCommand: '--pin' argument requires a path.");
                    return true;
                }

                var inputPath = args[i + 1];
                var ok = ToolbarPinService.TryPinPath(inputPath, out var message);
                if (ok)
                {
                    AppLogger.LogInfo($"PinCommand: '{inputPath}' => {message}");
                    Environment.ExitCode = 0;
                }
                else
                {
                    AppLogger.LogWarning($"PinCommand: '{inputPath}' failed - {message}");
                    Environment.ExitCode = 1;
                }

                return true;
            }

            return false;
        }

        private static void EnsureAppDirectories()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                Directory.CreateDirectory(AppPaths.IconsDirectory);
                Directory.CreateDirectory(AppPaths.ProfilesDirectory);
                Directory.CreateDirectory(AppPaths.ProvidersDirectory);
                Directory.CreateDirectory(AppPaths.ConfigDirectory);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to ensure data directories", ex);
            }
        }

        private static bool IsRunningPackaged()
        {
            try
            {
                _ = Package.Current;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
