// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using TopToolbar.Services.Workspaces;

namespace TopToolbar.Models.Providers
{
    public sealed class WorkspaceProviderData
    {
        public List<WorkspaceDefinition> Workspaces { get; set; } = new();
    }
}
