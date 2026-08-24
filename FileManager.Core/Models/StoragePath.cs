using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace FileManager.Core.Models
{
    internal sealed record StoragePath
    {
        /// <summary>
        /// Identifies which IStorageProvider instance this path belongs to,
        /// e.g. "local-windows", "azure-personal", "azure-work". Multiple
        /// accounts of the same provider type just get different ids, which is
        /// how "add multiple accounts" from the brainstorm falls out for free.
        /// </summary>
        public required string ProviderId { get; init; }
        public required string Value { get; init; }
        public string Name => Value.TrimEnd('/').Split('/').Last();

        public StoragePath Combine(string childName) =>
            this with { Value = $"{Value.TrimEnd('/')}/{childName}" };

        /// <summary>
        /// Null at a provider's root - nothing sits above "C:" or a storage
        /// account root.
        /// </summary>
        public StoragePath? Parent()
        {
            var trimmed = Value.TrimEnd("/");
            var lastSlash = trimmed.LastIndexOf('/');
            return lastSlash <= 0 ? null : this with { Value = trimmed[..lastSlash].ToString() };
        }

        public override string ToString() => $"{ProviderId}:{Value}";
    }
}
