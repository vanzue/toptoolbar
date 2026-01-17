// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace TopToolbar.Services.Display
{
    internal readonly struct DisplayRect : IEquatable<DisplayRect>
    {
        public DisplayRect(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public int Left { get; }

        public int Top { get; }

        public int Width { get; }

        public int Height { get; }

        public int Right => Left + Width;

        public int Bottom => Top + Height;

        public bool IsEmpty => Width <= 0 || Height <= 0;

        public bool Equals(DisplayRect other)
        {
            return Left == other.Left
                && Top == other.Top
                && Width == other.Width
                && Height == other.Height;
        }

        public override bool Equals(object obj)
        {
            return obj is DisplayRect other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Left, Top, Width, Height);
        }
    }
}
