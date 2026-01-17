// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace TopToolbar.Services.Workspaces
{
    internal sealed class WindowClaimer
    {
        private static readonly Lazy<WindowClaimer> InstanceValue = new(() => new WindowClaimer());
        private readonly ManagedWindowRegistry _registry = new ManagedWindowRegistry();

        private WindowClaimer()
        {
        }

        public static WindowClaimer Instance => InstanceValue.Value;

        public ManagedWindowRegistry Registry => _registry;
    }
}
