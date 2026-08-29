using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Providers
{
    /// <summary>
    /// Decides how to resolve one specific naming conflict. Invoked once per
    /// actual conflict encountered - not once for the whole operation - which
    /// is what lets a caller make a different choice for each colliding item
    /// instead of being locked into one blanket policy. A resolver that
    /// ignores its arguments and always returns the same value behaves
    /// exactly like today's flat NameCollisionPolicy default.
    /// </summary>
    public delegate Task<NameCollisionPolicy> ConflictResolver(
        StoragePath destinationPath,
        StorageItemKind conflictingKind,
        CancellationToken ct);
}
