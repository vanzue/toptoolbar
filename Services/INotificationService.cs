// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace TopToolbar.Services
{
    public interface INotificationService
    {
        void ShowError(string message);

        void ShowWarning(string message);

        void ShowInfo(string message);
    }
}
