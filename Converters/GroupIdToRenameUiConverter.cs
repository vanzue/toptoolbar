// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.UI.Xaml.Data;

namespace TopToolbar.Converters
{
    public sealed partial class GroupIdToRenameUiConverter : IValueConverter
    {
        private const string LockedGroupId = "default-groups";
        private const string PencilGlyph = "\uE70F";
        private const string LockGlyph = "\uE72E";

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var groupId = value as string;
            var mode = (parameter as string ?? string.Empty).Trim().ToLowerInvariant();
            var isLocked = string.Equals(groupId, LockedGroupId, StringComparison.OrdinalIgnoreCase);

            return mode switch
            {
                "glyph" => isLocked ? LockGlyph : PencilGlyph,
                "tooltip" => isLocked ? "Locked" : "Rename",
                "enabled" => !isLocked,
                _ => isLocked ? LockGlyph : PencilGlyph,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}
