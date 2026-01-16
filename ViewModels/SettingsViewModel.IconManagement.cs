// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TopToolbar.Logging;
using TopToolbar.Models;
using TopToolbar.Services;

namespace TopToolbar.ViewModels
{
    public partial class SettingsViewModel
    {
        private void TryUpdateIconFromCommand(ToolbarButton button)
        {
            var cmd = button?.Action?.Command;
            if (string.IsNullOrWhiteSpace(cmd))
            {
                return;
            }

            string path = cmd.Trim();
            path = Environment.ExpandEnvironmentVariables(path);

            // Check if icon is user-managed (not auto-managed)
            var configDirectory = Path.GetDirectoryName(_service.ConfigPath);
            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                return;
            }

            var iconsDir = Path.Combine(configDirectory, "icons");
            Directory.CreateDirectory(iconsDir);

            if (!ShouldAutoManageIcon(button, iconsDir))
            {
                AppLogger.LogDebug("Skipping auto icon because icon is user-managed.");
                return;
            }

            AppLogger.LogInfo($"TryUpdateIconFromCommand: cmd='{cmd}'");

            // Smart icon detection based on command type

            // 1. URL detection - use link/globe icon
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                SetSmartIcon(button, "glyph-E774", "\uE774"); // Globe icon
                AppLogger.LogInfo("Smart icon: URL detected, using globe icon");
                return;
            }

            // 2. mailto: detection - use mail icon
            if (path.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                SetSmartIcon(button, "glyph-E715", "\uE715"); // Mail icon
                AppLogger.LogInfo("Smart icon: mailto detected, using mail icon");
                return;
            }

            // 3. Folder detection - use folder icon
            if (Directory.Exists(path))
            {
                SetSmartIcon(button, "glyph-E188", "\uE188"); // Folder icon
                AppLogger.LogInfo("Smart icon: folder detected, using folder icon");
                return;
            }

            // 4. File detection - use icon based on extension
            if (File.Exists(path))
            {
                var ext = Path.GetExtension(path)?.ToLowerInvariant();

                // Document types
                if (ext == ".txt" || ext == ".log" || ext == ".md" || ext == ".json" || ext == ".xml" || ext == ".csv")
                {
                    SetSmartIcon(button, "glyph-E8A5", "\uE8A5"); // Document icon
                    AppLogger.LogInfo($"Smart icon: text file detected ({ext}), using document icon");
                    return;
                }

                // Image types
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp" || ext == ".ico" || ext == ".svg")
                {
                    SetSmartIcon(button, "glyph-EB9F", "\uEB9F"); // Photo icon
                    AppLogger.LogInfo($"Smart icon: image file detected ({ext}), using photo icon");
                    return;
                }

                // Video types
                if (ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".wmv")
                {
                    SetSmartIcon(button, "glyph-E714", "\uE714"); // Video icon
                    AppLogger.LogInfo($"Smart icon: video file detected ({ext}), using video icon");
                    return;
                }

                // Audio types
                if (ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".m4a" || ext == ".wma")
                {
                    SetSmartIcon(button, "glyph-E8D6", "\uE8D6"); // Music icon
                    AppLogger.LogInfo($"Smart icon: audio file detected ({ext}), using music icon");
                    return;
                }

                // Code/script types
                if (ext == ".ps1" || ext == ".bat" || ext == ".cmd" || ext == ".sh")
                {
                    SetSmartIcon(button, "glyph-E756", "\uE756"); // Code/CommandPrompt icon
                    AppLogger.LogInfo($"Smart icon: script file detected ({ext}), using terminal icon");
                    return;
                }

                // Executable - try to extract icon
                if (ext == ".exe")
                {
                    var target = Path.Combine(iconsDir, button.Id + ".png");
                    if (IconExtractionService.TryExtractExeIconToPng(path, target))
                    {
                        button.IconType = ToolbarIconType.Image;
                        button.IconPath = target;
                        AppLogger.LogInfo($"Smart icon: extracted exe icon -> '{target}'");
                        return;
                    }
                }

                // Other files - use generic file icon
                SetSmartIcon(button, "glyph-E7C3", "\uE7C3"); // Page icon
                AppLogger.LogInfo($"Smart icon: file detected ({ext}), using file icon");
                return;
            }

            // 5. Try to resolve as executable name (e.g., "notepad", "code")
            if (path.StartsWith('"'))
            {
                int end = path.IndexOf('"', 1);
                if (end > 1)
                {
                    path = path.Substring(1, end - 1);
                }
            }
            else
            {
                int exeIdx = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIdx >= 0)
                {
                    path = path.Substring(0, exeIdx + 4);
                }
            }

            var workingDirectory = button?.Action?.WorkingDirectory ?? string.Empty;
            var resolved = ResolveCommandToFilePath(path, workingDirectory);

            if (!string.IsNullOrEmpty(resolved) && resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(resolved))
            {
                var target = Path.Combine(iconsDir, button.Id + ".png");
                if (IconExtractionService.TryExtractExeIconToPng(resolved, target))
                {
                    button.IconType = ToolbarIconType.Image;
                    button.IconPath = target;
                    AppLogger.LogInfo($"Smart icon: extracted exe icon from resolved path -> '{target}'");
                    return;
                }
            }

            // Fallback: use default app icon
            SetSmartIcon(button, "glyph-E7AC", "\uE7AC"); // App icon
            AppLogger.LogInfo("Smart icon: using default app icon");
        }

        private void SetSmartIcon(ToolbarButton button, string catalogId, string glyph)
        {
            if (button == null)
            {
                return;
            }

            var glyphHex = string.IsNullOrEmpty(glyph) ? "empty" : $"U+{(int)glyph[0]:X4}";
            AppLogger.LogInfo($"SetSmartIcon: catalogId='{catalogId}', glyph={glyphHex}");

            // Try catalog first
            if (IconCatalogService.TryGetById(catalogId, out var entry))
            {
                AppLogger.LogInfo($"SetSmartIcon: found catalog entry '{catalogId}', entryGlyph=U+{(entry.Glyph != null && entry.Glyph.Length > 0 ? ((int)entry.Glyph[0]).ToString("X4") : "null")}");
                button.IconType = ToolbarIconType.Catalog;
                button.IconPath = IconCatalogService.BuildCatalogPath(entry.Id);
                button.IconGlyph = entry.Glyph ?? glyph;
            }
            else
            {
                // Fallback to glyph directly
                AppLogger.LogInfo($"SetSmartIcon: catalog '{catalogId}' not found, using glyph fallback {glyphHex}");
                button.IconType = ToolbarIconType.Catalog;
                button.IconGlyph = glyph;
                button.IconPath = string.Empty;
            }
            var finalGlyphHex = string.IsNullOrEmpty(button.IconGlyph) ? "empty" : $"U+{(int)button.IconGlyph[0]:X4}";
            AppLogger.LogInfo($"SetSmartIcon: button '{button.Name}' final state: type={button.IconType}, glyph={finalGlyphHex}, path='{button.IconPath}'");
        }

        private static bool ShouldAutoManageIcon(ToolbarButton button, string iconsDir)
        {
            if (button == null)
            {
                return false;
            }

            // If user has customized the icon, don't auto-manage
            if (button.IsIconCustomized)
            {
                return false;
            }

            return true;
        }

        private static bool IsManagedImageIcon(ToolbarButton button, string iconsDir)
        {
            if (button == null || string.IsNullOrWhiteSpace(button.IconPath))
            {
                return true;
            }

            try
            {
                var iconFullPath = Path.GetFullPath(button.IconPath);
                var iconsFullPath = Path.GetFullPath(iconsDir);
                var expectedAutoPath = Path.Combine(iconsFullPath, button.Id + ".png");
                return string.Equals(iconFullPath, expectedAutoPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDefaultCatalogIcon(ToolbarButton button)
        {
            var defaultEntry = IconCatalogService.GetDefault();
            if (defaultEntry == null)
            {
                return false;
            }

            var currentEntry = IconCatalogService.ResolveFromPath(button.IconPath);
            if (currentEntry != null)
            {
                return string.Equals(currentEntry.Id, defaultEntry.Id, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(button.IconGlyph) && !string.IsNullOrWhiteSpace(defaultEntry.Glyph))
            {
                return string.Equals(button.IconGlyph, defaultEntry.Glyph, StringComparison.Ordinal);
            }

            return string.IsNullOrWhiteSpace(button.IconPath) && string.IsNullOrWhiteSpace(button.IconGlyph);
        }

        private static string ResolveCommandToFilePath(string file, string workingDir)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                return string.Empty;
            }

            try
            {
                var candidate = file.Trim();
                candidate = Environment.ExpandEnvironmentVariables(candidate);

                bool hasRoot = System.IO.Path.IsPathRooted(candidate);
                bool hasExt = System.IO.Path.HasExtension(candidate);

                // If absolute or contains directory, try directly and with PATHEXT if needed
                if (hasRoot || candidate.Contains('\\') || candidate.Contains('/'))
                {
                    if (System.IO.File.Exists(candidate))
                    {
                        return candidate;
                    }

                    if (!hasExt)
                    {
                        foreach (var ext in GetPathExtensions())
                        {
                            var p = candidate + ext;
                            if (System.IO.File.Exists(p))
                            {
                                return p;
                            }
                        }
                    }

                    // If a specific extension was provided but file not found, try alternate PATHEXT extensions
                    if (hasExt)
                    {
                        var dirName = System.IO.Path.GetDirectoryName(candidate) ?? string.Empty;
                        var nameNoExtOnly = System.IO.Path.GetFileNameWithoutExtension(candidate);
                        var nameNoExt = string.IsNullOrEmpty(dirName) ? nameNoExtOnly : System.IO.Path.Combine(dirName, nameNoExtOnly);
                        foreach (var ext in GetPathExtensions())
                        {
                            var p = nameNoExt + ext;
                            if (System.IO.File.Exists(p))
                            {
                                return p;
                            }
                        }
                    }

                    return string.Empty;
                }

                // Build search dirs: workingDir, current dir, PATH
                var dirs = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(workingDir) && System.IO.Directory.Exists(workingDir))
                {
                    dirs.Add(workingDir);
                }

                dirs.Add(Environment.CurrentDirectory);
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var d in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    dirs.Add(d);
                }

                foreach (var dir in dirs)
                {
                    var basePath = System.IO.Path.Combine(dir, candidate);
                    if (hasExt)
                    {
                        if (System.IO.File.Exists(basePath))
                        {
                            return basePath;
                        }

                        // Also try alternate extensions if the given one is not found in this dir
                        var nameNoExtOnly = System.IO.Path.GetFileNameWithoutExtension(candidate);
                        var nameNoExt = System.IO.Path.Combine(dir, nameNoExtOnly);
                        foreach (var ext in GetPathExtensions())
                        {
                            var p = nameNoExt + ext;
                            if (System.IO.File.Exists(p))
                            {
                                return p;
                            }
                        }
                    }
                    else
                    {
                        foreach (var ext in GetPathExtensions())
                        {
                            var p = basePath + ext;
                            if (System.IO.File.Exists(p))
                            {
                                return p;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static System.Collections.Generic.IEnumerable<string> GetPathExtensions()
        {
            var pathext = Environment.GetEnvironmentVariable("PATHEXT");
            if (string.IsNullOrWhiteSpace(pathext))
            {
                return new[] { ".COM", ".EXE", ".BAT", ".CMD", ".VBS", ".JS", ".WS", ".MSC", ".PS1" };
            }

            return pathext.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());
        }

        public bool TrySetCatalogIcon(ToolbarButton button, string catalogId)
        {
            if (button == null || string.IsNullOrWhiteSpace(catalogId))
            {
                return false;
            }

            if (!IconCatalogService.TryGetById(catalogId, out var entry))
            {
                return false;
            }

            button.IconType = ToolbarIconType.Catalog;
            button.IconPath = IconCatalogService.BuildCatalogPath(entry.Id);
            button.IconGlyph = entry.Glyph ?? string.Empty;
            ScheduleSave();
            return true;
        }

        public bool TrySetImageIcon(ToolbarButton button, string iconPath)
        {
            if (button == null || string.IsNullOrWhiteSpace(iconPath))
            {
                return false;
            }

            button.IconType = ToolbarIconType.Image;
            button.IconPath = iconPath;
            ScheduleSave();
            return true;
        }

        public Task<bool> TrySetImageIconFromFileAsync(ToolbarButton button, string sourcePath)
        {
            if (button == null || string.IsNullOrWhiteSpace(sourcePath))
            {
                return Task.FromResult(false);
            }

            var targetPath = CopyIconAsset(button.Id, sourcePath);
            button.IconType = ToolbarIconType.Image;
            button.IconPath = targetPath;
            button.IconGlyph = string.Empty;
            ScheduleSave();
            return Task.FromResult(true);
        }

        public bool TrySetGlyphIcon(ToolbarButton button, string glyph)
        {
            if (button == null || string.IsNullOrWhiteSpace(glyph))
            {
                return false;
            }

            var trimmed = glyph.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            var catalogMatch = IconCatalogService.GetAll()
                .FirstOrDefault(entry => string.Equals(entry.Glyph, trimmed, StringComparison.Ordinal));

            if (catalogMatch != null)
            {
                return TrySetCatalogIcon(button, catalogMatch.Id);
            }

            button.IconType = ToolbarIconType.Catalog;
            button.IconGlyph = trimmed;
            button.IconPath = string.Empty;
            ScheduleSave();
            return true;
        }

        public void ResetIconToDefault(ToolbarButton button)
        {
            if (button == null)
            {
                return;
            }

            var defaultEntry = IconCatalogService.GetDefault();
            if (defaultEntry != null)
            {
                TrySetCatalogIcon(button, defaultEntry.Id);
            }
            else
            {
                TrySetGlyphIcon(button, "\uE10F");
            }
        }

        private string CopyIconAsset(string assetId, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(assetId) || string.IsNullOrWhiteSpace(sourcePath))
            {
                return sourcePath ?? string.Empty;
            }

            try
            {
                var iconsDirectory = AppPaths.IconsDirectory;
                Directory.CreateDirectory(iconsDirectory);

                var extension = Path.GetExtension(sourcePath);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".png";
                }

                if (!extension.StartsWith('.'))
                {
                    extension = "." + extension;
                }

                var targetPath = Path.Combine(iconsDirectory, $"{assetId}_custom{extension}");

                if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, targetPath, true);
                }

                return targetPath;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"CopyIconAsset failed: {ex.Message}");
                return sourcePath;
            }
        }
    }
}
