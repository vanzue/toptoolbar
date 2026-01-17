// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using TopToolbar.Models;

namespace TopToolbar.ViewModels
{
    public sealed class ToolbarButtonItem
    {
        public ToolbarButtonItem(ButtonGroup group, ToolbarButton button)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            Button = button ?? throw new ArgumentNullException(nameof(button));
        }

        public ButtonGroup Group { get; }

        public ToolbarButton Button { get; }
    }
}
