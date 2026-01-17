// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Xaml;

namespace TopToolbar
{
    public static class ToolbarMetrics
    {
        public const double ToolbarHeight = 140d;
        public const double ToolbarShadowPaddingValue = 24d;
        public const double ToolbarButtonSize = 52d;
        public const double ToolbarSeparatorHeight = 44d;
        public const double ToolbarLabelFontSize = 13d;
        public const double ToolbarIconFontSize = 22d;
        public const double ToolbarStackSpacing = 16d;

        public static Thickness ToolbarShadowPadding => new(ToolbarShadowPaddingValue);

        public static Thickness ToolbarChromePadding => new(28, 20, 28, 20);

        public static CornerRadius ToolbarCornerRadius => new(32d);

        public static double ButtonContainerWidth => ToolbarButtonSize + 42d;

        public static double ButtonLabelMaxWidth => ToolbarButtonSize + 36d;

        public static double ProgressRingSize => ToolbarButtonSize - 20d;
    }
}
