using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Application.Queries.AnalyzeListingPhoto;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.ValueObjects;
using Xunit;

namespace SmartLost.MatchingService.UnitTests.Runtime;

public sealed class PhotoAnalysisCachingTests
{
    [Fact]
    public async Task HitsIgnoreCorrelationButAlwaysRecheckPhotoAccessAndValidation()
    {
        var photos = new CountingPhotos();
        int calls = 0;
        using ServiceProvider provider = Create(photos, new FakeAnalyzer(_ =>
        {
            calls++;
            return Task.FromResult(new PhotoAnalysis([], ["blurry"]));
        }));
        using IServiceScope scope = provider.CreateScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var query = new AnalyzeListingPhotoQuery(Guid.NewGuid(), Guid.NewGuid(), 1, "first");

        PhotoAnalysis first = (await sender.Send(query)).Value;
        Assert.Same(first, (await sender.Send(query with
        {
            CorrelationId = "second"
        })).Value);
        Assert.Equal(1, calls);
        Assert.Equal(2, photos.Reads);

        photos.Content = null;
        Assert.Equal(MatchingErrors.PhotoUnavailable, Assert.Single((await sender.Send(query)).Errors));
        Assert.Equal(3, photos.Reads);
        Assert.Equal(1, calls);

        Assert.True((await sender.Send(query with
        {
            PhotoRevision = 0
        })).IsFailure);
        Assert.Equal(3, photos.Reads);
    }

    [Fact]
    public async Task KeySeparatesPhotoVersionsContentAndMediaTypeAndExpiresAfterFiveMinutes()
    {
        var photos = new CountingPhotos();
        var clock = new ManualClock();
        int calls = 0;
        using ServiceProvider provider = Create(photos, new FakeAnalyzer(_ =>
        {
            calls++;
            return Task.FromResult(new PhotoAnalysis([], []));
        }), clock);
        using IServiceScope scope = provider.CreateScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var query = new AnalyzeListingPhotoQuery(Guid.NewGuid(), Guid.NewGuid(), 1);

        await sender.Send(query);
        await sender.Send(query with
        {
            ListingId = Guid.NewGuid()
        });
        await sender.Send(query with
        {
            PhotoId = Guid.NewGuid()
        });
        await sender.Send(query with
        {
            PhotoRevision = 2
        });
        photos.Content = new PhotoContent(new byte[] { 4, 5, 6 }, "image/jpeg");
        await sender.Send(query);
        photos.Content = photos.Content with
        {
            MediaType = "image/png"
        };
        await sender.Send(query);
        Assert.Equal(6, calls);
        clock.Advance(TimeSpan.FromSeconds(299));
        await sender.Send(query);
        Assert.Equal(6, calls);
        clock.Advance(TimeSpan.FromSeconds(1));
        await sender.Send(query);
        Assert.Equal(7, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InferenceFailuresAreRetriedInsteadOfCached(bool timeout)
    {
        int calls = 0;
        using ServiceProvider provider = Create(new CountingPhotos(), new FakeAnalyzer(_ =>
        {
            calls++;
            if (calls < 3)
            {
                throw timeout ? new TimeoutException() : new AiInferenceException();
            }

            return Task.FromResult(new PhotoAnalysis([], []));
        }));
        using IServiceScope scope = provider.CreateScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var query = new AnalyzeListingPhotoQuery(Guid.NewGuid(), Guid.NewGuid(), 1);
        Error expected = timeout ? MatchingErrors.InferenceTimedOut : MatchingErrors.InferenceUnavailable;
        Assert.Equal(expected, Assert.Single((await sender.Send(query)).Errors));
        Assert.Equal(expected, Assert.Single((await sender.Send(query)).Errors));
        Assert.Empty((await sender.Send(query)).Value.Objects);
        Assert.Empty((await sender.Send(query)).Value.Objects);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task CachedSnapshotsDoNotExposeMutableAdapterCollections()
    {
        var colors = new List<string> { "black" };
        var texts = new List<string> { "label" };
        var features = new List<string> { "zipper" };
        var tags = new List<string> { "bag" };
        var uncertainties = new List<string> { "blurry" };
        var objects = new List<DetectedObject> { new("containers", "bag", colors, null, null, null, texts, features, tags) };
        using ServiceProvider provider = Create(new CountingPhotos(),
            new FakeAnalyzer(_ => Task.FromResult(new PhotoAnalysis(objects, uncertainties))));
        using IServiceScope scope = provider.CreateScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var query = new AnalyzeListingPhotoQuery(Guid.NewGuid(), Guid.NewGuid(), 1);
        PhotoAnalysis first = (await sender.Send(query)).Value;
        colors.Clear();
        texts.Clear();
        features.Clear();
        tags.Clear();
        uncertainties.Clear();
        objects.Clear();

        PhotoAnalysis cached = (await sender.Send(query)).Value;
        Assert.Same(first, cached);
        DetectedObject item = Assert.Single(cached.Objects);
        Assert.Equal("black", Assert.Single(item.Colors));
        Assert.Equal("label", Assert.Single(item.VisibleText));
        Assert.Equal("zipper", Assert.Single(item.DistinctiveFeatures));
        Assert.Equal("bag", Assert.Single(item.Tags));
        Assert.Equal("blurry", Assert.Single(cached.Uncertainties));
        Assert.Throws<NotSupportedException>(() => ((IList<DetectedObject>)cached.Objects).Clear());
        foreach (IReadOnlyList<string> values in new[] { item.Colors, item.VisibleText, item.DistinctiveFeatures, item.Tags, cached.Uncertainties })
        {
            Assert.Throws<NotSupportedException>(() => ((IList<string>)values).Clear());
        }
    }

    private static ServiceProvider Create(CountingPhotos photos, IPhotoAnalyzer analyzer, TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock ?? TimeProvider.System);
        SmartLost.BuildingBlocks.AspNetCore.DependencyInjection.AddBuildingBlocksApi(services);
        SmartLost.BuildingBlocks.Infrastructure.DependencyInjection.AddBuildingBlocksQueryCache(services);
        services.AddApplication();
        services.AddSingleton<IListingPhotoReader>(photos);
        services.AddSingleton(analyzer);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class CountingPhotos : IListingPhotoReader
    {
        public PhotoContent? Content { get; set; } = new(new byte[] { 1, 2, 3 }, "image/jpeg");
        public int Reads
        {
            get; private set;
        }

        public Task<PhotoContent?> ReadAsync(Guid listingId, PhotoReference photo, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Content);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan interval)
        {
            _now += interval;
        }
    }
}
