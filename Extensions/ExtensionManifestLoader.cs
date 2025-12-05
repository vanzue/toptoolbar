// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TopToolbar.Serialization;

namespace TopToolbar.Extensions
{
    public static class ExtensionManifestLoader
    {
        public static async Task<ExtensionManifest> LoadAsync(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new ArgumentException("Manifest path must be provided.", nameof(manifestPath));
            }

            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync(stream, ExtensionManifestJsonContext.Default.ExtensionManifest).ConfigureAwait(false);
            return manifest ?? new ExtensionManifest();
        }
    }
}
