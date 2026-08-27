using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Providers
{
    public static class FolderInfoCalculator
    {
        public static async Task<SizeAccumulator> GetFolderInfo(
        IStorageProvider provider,
        StoragePath folder,
        CancellationToken ct = default) { 
            var acc = new SizeAccumulator();
            await CalculateSizeInBytesRecursive(provider, folder, acc, ct);
            return acc;

        }

        private static async Task CalculateSizeInBytesRecursive(
        IStorageProvider provider,
        StoragePath folder,
        SizeAccumulator acc,
        CancellationToken ct)
        {
            await foreach(var item in provider.ListAsync(folder, ct)){
                ct.ThrowIfCancellationRequested();
                if (item.Kind == StorageItemKind.File) {
                    acc.Files++;
                    if (item.SizeInBytes is { } size)
                        acc.Size += size;
                }
                else // it must be a directory
                {
                    acc.Folders++;
                    await CalculateSizeInBytesRecursive(provider, item.Path, acc, ct);
                }
            }
        }
    }
    public sealed class SizeAccumulator
    {
        public long Size;
        public int Files;
        public int Folders;
    }
}
