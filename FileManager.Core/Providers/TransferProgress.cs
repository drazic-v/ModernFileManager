using FileManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Providers
{
    /// <summary>
    /// Snapshot of an in-progress copy or move, reported once per file
    /// completed - not per byte within a file, which would need streaming
    /// through File.Copy by hand instead of using it directly. Coarser, but
    /// consistent with the granularity FolderSizeCalculator already reports at.
    /// </summary>
    public readonly record struct TransferProgress(long BytesCopied, int FilesCopied);
}