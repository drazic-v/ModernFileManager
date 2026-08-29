using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Providers
{

    public static class FolderInfoCalculator
    {
        private const int ProgressInterval = 500;

        public readonly record struct FolderInfo(
            long Size,
            int Files,
            int Folders);

        public static async Task<FolderInfo> GetFolderInfo(
            IStorageProvider provider,
            StoragePath folder,
            IProgress<FolderInfo>? progress = null,
            CancellationToken ct = default)
        {
            var accumulator = new SizeAccumulator();

            await CalculateSizeInBytesRecursive(
                provider,
                folder,
                accumulator,
                progress,
                ct);

            var result = accumulator.ToFolderInfo();

            // Always report the final state.
            progress?.Report(result);

            return result;
        }

        private static async Task CalculateSizeInBytesRecursive(
            IStorageProvider provider,
            StoragePath folder,
            SizeAccumulator accumulator,
            IProgress<FolderInfo>? progress,
            CancellationToken ct)
        {
            await foreach (var item in provider.ListAsync(folder, ct))
            {
                ct.ThrowIfCancellationRequested();

                if (item.Kind == StorageItemKind.File)
                {
                    accumulator.Files++;

                    if (item.SizeInBytes is { } size)
                    {
                        accumulator.SizeInBytes += size;
                    }
                }
                else
                {
                    accumulator.Folders++;

                    await CalculateSizeInBytesRecursive(
                        provider,
                        item.Path,
                        accumulator,
                        progress,
                        ct);
                }

                accumulator.ItemsProcessed++;

                if (accumulator.ItemsProcessed % ProgressInterval == 0)
                {
                    progress?.Report(accumulator.ToFolderInfo());
                }
            }
        }

        private sealed class SizeAccumulator
        {
            public long SizeInBytes;
            public int Files;
            public int Folders;
            public int ItemsProcessed;

            public FolderInfo ToFolderInfo() =>
                new(SizeInBytes, Files, Folders);
        }
    }

}
