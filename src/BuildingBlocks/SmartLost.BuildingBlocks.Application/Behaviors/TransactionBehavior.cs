using MediatR;
using Microsoft.Extensions.Logging;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.Application.Behaviors;

public sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResult<TResponse>
{
    private static readonly Action<ILogger, string, Exception?> _logRollbackFailure = LoggerMessage.Define<string>(
        LogLevel.Error, new EventId(1001, "RollbackFailure"), "Rollback failed while handling {Request}.");

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ITransactionalCommand)
        {
            return await next(cancellationToken);
        }

        await using ITransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            TResponse response = await next(cancellationToken);
            if (response.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return response;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                _logRollbackFailure(logger, typeof(TRequest).Name, rollbackException);
            }

            throw;
        }
    }
}
