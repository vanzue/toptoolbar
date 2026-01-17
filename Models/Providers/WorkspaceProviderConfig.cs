// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace TopToolbar.Models.Providers
{
    public sealed class WorkspaceProviderConfig
    {
        public int SchemaVersion { get; set; } = 1;

        public string ProviderId { get; set; } = "WorkspaceProvider";

        public string DisplayName { get; set; } = "Workspaces";

        public string Description { get; set; } = "Snapshot and restore desktop layouts";

        public string Author { get; set; } = "Microsoft";

        public string Version { get; set; } = "1.0.0";

        public bool Enabled { get; set; } = true;

        public DateTimeOffset? LastUpdated { get; set; }
            = DateTimeOffset.UtcNow;

        public List<WorkspaceButtonConfig> Buttons { get; set; } = new();

        public WorkspaceProviderData Data { get; set; }
    }
}
