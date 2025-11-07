// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace TopToolbar.Models.Providers
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProviderIconType
    {
        Glyph,
        Image,
        Catalog,
    }

    public sealed class ProviderIcon
    {
        public ProviderIconType Type { get; set; } = ProviderIconType.Glyph;

        public string Path { get; set; } = string.Empty;

        public string Glyph { get; set; } = string.Empty;

        public string CatalogId { get; set; } = string.Empty;
    }
}
