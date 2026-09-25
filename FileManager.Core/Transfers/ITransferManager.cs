using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Transfers
{
    public interface ITransferManager
    {
        Guid Submit(TransferRequest request, IProgress<TransferUpdate> progress);
        void Cancel(Guid transferId);
        void CancelAll();
        void Retry(Guid transferId);
    }
}
