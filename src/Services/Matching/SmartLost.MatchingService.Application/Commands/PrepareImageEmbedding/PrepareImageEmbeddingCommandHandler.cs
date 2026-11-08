using MediatR;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Enums;
using SmartLost.MatchingService.Domain.Errors;

namespace SmartLost.MatchingService.Application.Commands.PrepareImageEmbedding;

public sealed class PrepareImageEmbeddingCommandHandler(
    IEmbeddingPreparationTaskRepository tasks,
    ISearchListingRepository listings,
    IImageEmbeddingRepository embeddings,
    IListingPhotoReader photos,
    IImageEmbeddingGenerator generator,
    IUnitOfWork unitOfWork,
    MatchingOptions options,
    TimeProvider timeProvider) : IRequestHandler<PrepareImageEmbeddingCommand, Result<EmbeddingPreparationState>>
{
    public async Task<Result<EmbeddingPreparationState>> Handle(PrepareImageEmbeddingCommand request, CancellationToken cancellationToken)
    {
        EmbeddingPreparationTask? task = await tasks.GetAsync(request.TaskId, cancellationToken);
        if (task is null)
        {
            return Result<EmbeddingPreparationState>.Failure(MatchingErrors.PreparationTaskNotFound);
        }

        if (task.State is EmbeddingPreparationState.Ready or EmbeddingPreparationState.Failed or EmbeddingPreparationState.Obsolete)
        {
            return Result<EmbeddingPreparationState>.Success(task.State);
        }

        SearchListing? listing = await listings.GetAsync(task.ListingId, cancellationToken);
        if (listing is null || listing.CurrentPhoto != task.Photo || task.Version != options.EmbeddingVersion)
        {
            task.MarkObsolete();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<EmbeddingPreparationState>.Success(task.State);
        }

        if (!task.IsDue(UtcNow()))
        {
            return Result<EmbeddingPreparationState>.Failure(MatchingErrors.TaskNotDue);
        }

        if (task.FailIfAttemptsExhausted(UtcNow(), options.MaximumAttempts))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<EmbeddingPreparationState>.Success(task.State);
        }

        // Commit the claim before inference so competing workers observe the lease.
        var leaseId = Guid.NewGuid();
        task.Claim(leaseId, UtcNow(), options.LeaseDuration, options.MaximumAttempts);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        PhotoContent? photo = await photos.ReadAsync(task.ListingId, task.Photo, cancellationToken);
        if (photo is null)
        {
            return await FailAsync(task, leaseId, MatchingErrors.PhotoUnavailable, cancellationToken);
        }

        GeneratedEmbedding generated;
        try
        {
            generated = await generator.GenerateAsync(photo, cancellationToken);
        }
        catch (TimeoutException)
        {
            return await FailAsync(task, leaseId, MatchingErrors.InferenceTimedOut, cancellationToken);
        }
        catch (AiInferenceException)
        {
            return await FailAsync(task, leaseId, MatchingErrors.InferenceUnavailable, cancellationToken);
        }

        if (generated.Version != task.Version)
        {
            return await FailAsync(task, leaseId, MatchingErrors.EmbeddingVersionMismatch, cancellationToken);
        }

        ImageEmbedding embedding;
        try
        {
            embedding = ImageEmbedding.Create(task.ListingId, task.Photo, generated.Version, generated.Vector);
        }
        catch (DomainException exception)
        {
            return await FailAsync(task, leaseId, exception.Error, cancellationToken);
        }

        // The vector upsert and Ready state are committed together.
        await embeddings.UpsertAsync(embedding, cancellationToken);
        task.Complete(leaseId, UtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<EmbeddingPreparationState>.Success(task.State);
    }

    private async Task<Result<EmbeddingPreparationState>> FailAsync(
        EmbeddingPreparationTask task, Guid leaseId, Error error, CancellationToken cancellationToken)
    {
        task.Fail(leaseId, error.Code, UtcNow(), options.RetryDelay, options.MaximumAttempts);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<EmbeddingPreparationState>.Failure(error);
    }

    private DateTime UtcNow()
    {
        return timeProvider.GetUtcNow().UtcDateTime;
    }
}
