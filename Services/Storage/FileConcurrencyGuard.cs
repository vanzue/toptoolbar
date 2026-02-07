// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TopToolbar.Services.Storage
{
    internal static class FileConcurrencyGuard
    {
        private const int LockRetryCount = 100;
        private const int LockRetryDelayMilliseconds = 50;

        internal static long GetFileVersionUtcTicks(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return 0;
            }

            try
            {
                return File.GetLastWriteTimeUtc(filePath).Ticks;
            }
            catch
            {
                return 0;
            }
        }

        internal static async Task<FileStream> AcquireWriteLockAsync(
            string targetFilePath,
            CancellationToken cancellationToken)
        {
            var lockPath = targetFilePath + ".lck";
            var lockDirectory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrWhiteSpace(lockDirectory))
            {
                Directory.CreateDirectory(lockDirectory);
            }

            for (var attempt = 0; attempt < LockRetryCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException) when (attempt + 1 < LockRetryCount)
                {
                }
                catch (UnauthorizedAccessException) when (attempt + 1 < LockRetryCount)
                {
                }

                await Task.Delay(LockRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            throw new IOException($"Unable to acquire file write lock for '{targetFilePath}'.");
        }
    }
}
