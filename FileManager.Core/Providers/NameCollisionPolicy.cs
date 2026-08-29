using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Providers
{
    /// <summary>
    /// How a copy or move operation should resolve a name that already exists
    /// at the destination. The provider only ever executes whichever policy
    /// it's given — deciding which one to use, including asking the user, is
    /// an Application/UI-layer concern, never the provider's own.
    /// </summary>
    public enum NameCollisionPolicy
    {
        /// <summary>Append " (2)", " (3)", etc. until a free name is found. The safe default.</summary>
        GenerateUnique,

        /// <summary>Delete or overwrite whatever already occupies the destination name.</summary>
        Replace,

        /// <summary>Directories only — combine contents with what's already there instead of replacing it wholesale.</summary>
        Merge,

        /// <summary>Skip the item if anything already occupies the destination name.</summary>
        Skip,

        /// <summary>Throw immediately if anything already occupies the destination name.</summary>
        Fail
    }
}
