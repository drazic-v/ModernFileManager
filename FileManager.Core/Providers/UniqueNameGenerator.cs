using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Providers
{
    public static class UniqueNameGenerator
    {
        public static async Task<string> GenerateAsync(
        IStorageProvider provider,
        StoragePath parentFolder,
        string desiredName,
        StorageItemKind type,
        CancellationToken ct = default)
        {
            var extension = type == StorageItemKind.File ? Path.GetExtension(desiredName) : "";
            var baseName = extension.Length > 0 
                ? desiredName[..^extension.Length] 
                : desiredName;

            var existingNames = new HashSet<string>(StringComparer.Ordinal);
            await foreach (var item in provider.ListAsync(parentFolder, ct))
            {
                ct.ThrowIfCancellationRequested();
                existingNames.Add(item.Name);
            }

            if (!existingNames.Contains(desiredName))
                return desiredName;

            var counter = 2;
            string candidateName;
            do 
            {
                candidateName = $"{baseName} ({counter}){extension}";
                counter++;
            }
            while (existingNames.Contains(candidateName));

            return candidateName;
        }
    }
}
