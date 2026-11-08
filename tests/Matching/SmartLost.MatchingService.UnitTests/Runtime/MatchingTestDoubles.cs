using Microsoft.Extensions.Logging;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.Contracts.Listings;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.UnitTests.Runtime;

internal static class MatchingData
{
    public static readonly DateOnly Day = new(2026, 10, 20);

    public static SearchListing Listing(
        ListingType type,
        Guid? id = null,
        Guid? authorId = null,
        PhotoReference? photo = null,
        string[]? categories = null,
        string? colour = "Black",
        DateOnly? occurredOn = null,
        string location = "Library",
        bool eligible = true,
        int revision = 1)
    {
        return SearchListing.Create(id ?? Guid.NewGuid(), revision, 1, type, authorId ?? Guid.NewGuid(), photo,
            categories ?? ["Backpack"], colour, ["zipper"], occurredOn ?? Day, location, eligible);
    }

    public static float[] Unit(int dimensions, int hot)
    {
        float[] vector = new float[dimensions];
        vector[hot] = 1;
        return vector;
    }

    public static float[] Normalized(params float[] values)
    {
        double norm = Math.Sqrt(values.Sum(value => (double)value * value));
        return [.. values.Select(value => (float)(value / norm))];
    }
}

internal sealed class FakeListings : ISearchListingRepository
{
    public List<SearchListing> Items { get; } = [];

    public Task<SearchListing?> GetAsync(Guid listingId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Items.FirstOrDefault(listing => listing.Id == listingId));
    }

    public Task<IReadOnlyList<SearchListing>> GetCandidatePoolAsync(SearchListing source, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SearchListing>>([.. Items.Where(listing => listing.Id != source.Id)]);
    }
}

internal sealed class FakeEmbeddings : IImageEmbeddingRepository
{
    public List<ImageEmbedding> Items { get; } = [];

    public int Reads
    {
        get; private set;
    }

    public Task<IReadOnlyList<ImageEmbedding>> GetAsync(
        IReadOnlyCollection<PhotoReference> photos, EmbeddingVersion version, CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<ImageEmbedding>>([.. Items.Where(item => photos.Contains(item.Photo))]);
    }

    public Task UpsertAsync(ImageEmbedding embedding, CancellationToken cancellationToken)
    {
        Items.RemoveAll(item => item.ListingId == embedding.ListingId && item.Photo == embedding.Photo && item.Version == embedding.Version);
        Items.Add(embedding);
        return Task.CompletedTask;
    }
}

internal sealed class FakeTasks : IEmbeddingPreparationTaskRepository
{
    public List<EmbeddingPreparationTask> Items { get; } = [];

    public Task<EmbeddingPreparationTask?> GetAsync(Guid taskId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Items.FirstOrDefault(task => task.Id == taskId));
    }
}

internal sealed class FakePhotos : IListingPhotoReader
{
    public PhotoContent? Content { get; set; } = new(new byte[] { 1, 2, 3 }, "image/jpeg");

    public Task<PhotoContent?> ReadAsync(Guid listingId, PhotoReference photo, CancellationToken cancellationToken)
    {
        return Task.FromResult(Content);
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves
    {
        get; private set;
    }

    public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeGenerator(Func<CancellationToken, Task<GeneratedEmbedding>> behavior) : IImageEmbeddingGenerator
{
    public int Calls
    {
        get; private set;
    }

    public Task<GeneratedEmbedding> GenerateAsync(PhotoContent photo, CancellationToken cancellationToken)
    {
        Calls++;
        return behavior(cancellationToken);
    }
}

internal sealed class FakeAnalyzer(Func<CancellationToken, Task<PhotoAnalysis>> behavior) : IPhotoAnalyzer
{
    public Task<PhotoAnalysis> AnalyzeAsync(PhotoContent photo, CancellationToken cancellationToken)
    {
        return behavior(cancellationToken);
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, eventId, formatter(state, exception)));
        }
    }
}

internal sealed class SharedLoggerProvider : ILoggerProvider
{
    public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName)
    {
        return new SharedLogger(Entries);
    }

    public void Dispose()
    {
    }

    private sealed class SharedLogger(List<(LogLevel Level, EventId EventId, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (entries)
            {
                entries.Add((logLevel, eventId, formatter(state, exception)));
            }
        }
    }
}
