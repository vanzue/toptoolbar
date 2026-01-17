// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using TopToolbar.Models;
using System;

namespace TopToolbar.Stores
{
    public class ToolbarStore
    {
        private readonly object _gate = new();

        /// <summary>
        /// Gets the ordered collection of groups (mutations are guarded by a local lock).
        /// </summary>
        public ObservableCollection<ButtonGroup> Groups { get; } = new ObservableCollection<ButtonGroup>();

        /// <summary>
        /// Replace (or insert) a provider-supplied group.
        /// </summary>
        public void UpsertProviderGroup(ButtonGroup group)
        {
            if (group == null)
            {
                return;
            }

            lock (_gate)
            {
                int index = FindGroupIndex(group.Id);
                if (index < 0)
                {
                    Groups.Add(group);
                }
                else if (!ReferenceEquals(Groups[index], group))
                {
                    Groups[index] = group;
                }
            }
        }

        /// <summary>
        /// Remove a provider group by id.
        /// </summary>
        public void RemoveGroup(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            lock (_gate)
            {
                int index = FindGroupIndex(id);
                if (index >= 0)
                {
                    Groups.RemoveAt(index);
                }
            }
        }

        private int FindGroupIndex(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return -1;
            }

            for (int i = 0; i < Groups.Count; i++)
            {
                var group = Groups[i];
                if (group == null)
                {
                    continue;
                }

                if (string.Equals(group.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
