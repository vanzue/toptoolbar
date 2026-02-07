// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace TopToolbar.ViewModels
{
    public enum NotificationKind
    {
        Error,
        Warning,
        Info,
    }

    public sealed class NotificationItem
    {
        public NotificationItem(NotificationKind kind, string message)
        {
            Id = Guid.NewGuid();
            Kind = kind;
            Message = message ?? string.Empty;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public Guid Id { get; }

        public NotificationKind Kind { get; }

        public string Message { get; }

        public DateTimeOffset CreatedAt { get; }
    }
}
