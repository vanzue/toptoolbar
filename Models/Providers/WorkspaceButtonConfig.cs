// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace TopToolbar.Models.Providers
{
    public sealed class WorkspaceButtonConfig
    {
        public string Id { get; set; } = string.Empty;

        public string WorkspaceId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public double? SortOrder { get; set; }

        public ProviderIcon Icon { get; set; } = new();
    }
}
