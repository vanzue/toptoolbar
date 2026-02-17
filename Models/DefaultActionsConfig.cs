// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace TopToolbar.Models;

public class DefaultActionsConfig
{
    public bool SystemControlsEnabled { get; set; } = true;

    public DefaultActionItemConfig MediaPlayPause { get; set; } = new();
}
