using Microsoft.EntityFrameworkCore.Storage;

namespace SmartLost.BuildingBlocks.Infrastructure.Transactions;

internal interface IEfTransactionParticipant
{
    void OnTransactionStarted(IDbContextTransaction transaction);

    void OnTransactionCompleted(bool committed);
}
