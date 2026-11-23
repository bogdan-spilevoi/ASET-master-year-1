using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RabbitMQ.Client;
using SmartLost.Contracts.Listings;
using SmartLost.MatchingService.Infrastructure.Ai;
using SmartLost.MatchingService.Infrastructure.Analysis;
using SmartLost.MatchingService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.MatchingService.UnitTests.Runtime;

public sealed class DraftAnalysisTests
{
    [Fact]
    public async Task WorkerAnalyzesUploadedPhotoAndReturnsPrivateDurableResults()
    {
        var target = new DraftPhotoTarget(Guid.NewGuid(), Guid.NewGuid(), 1);
        string key = Guid.NewGuid().ToString();
        using var factory = new MatchingApiFactory(overrides: new Dictionary<string, string?>
        {
            ["ListingsPhotos:ApiKey"] = key,
            ["DraftAnalysis:WorkerEnabled"] = "true",
            ["RabbitMq:UserName"] = "test",
            ["RabbitMq:Password"] = Guid.NewGuid().ToString(),
            ["RabbitMq:Exchange"] = "test-events",
            ["RabbitMq:Bindings:smartlost.matching.draft-analysis:0"] = DraftAnalysisRequest.RoutingKey
        });
        IConnectionFactory broker = Substitute.For<IConnectionFactory>();
        IConnection connection = Substitute.For<IConnection>();
        IChannel channel = Substitute.For<IChannel>();
        broker.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(connection));
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(channel));
        channel.BasicGetAsync(DraftAnalysisRequest.Queue, false, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<BasicGetResult?>(new BasicGetResult(1, false, "test-events", DraftAnalysisRequest.RoutingKey, 0, new BasicProperties(), "invalid"u8.ToArray())),
            Task.FromResult<BasicGetResult?>(null));
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(broker);
            services.AddHttpClient(nameof(ListingsPhotoClient)).ConfigurePrimaryHttpMessageHandler(() => new PhotoHandler());
        }));
        using HttpClient client = host.CreateClient();
        using IServiceScope scope = host.Services.CreateScope();
        MatchingDbContext db = scope.ServiceProvider.GetRequiredService<MatchingDbContext>();
        foreach (DraftAnalysisKind kind in Enum.GetValues<DraftAnalysisKind>())
        {
            db.Add(new PhotoAnalysisWork
            {
                Id = target.RequestId(kind),
                ListingId = target.ListingId,
                PhotoId = target.PhotoId,
                PhotoRevision = 1,
                Kind = kind
            });
        }
        await db.SaveChangesAsync();
        string route = $"/internal/analysis/{target.ListingId}/{target.PhotoId}/1";
        using HttpResponseMessage denied = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-Listings-Photo-Key", key);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        DraftAnalysisDto? result;
        do
        {
            await Task.Delay(100, timeout.Token);
            result = await client.GetFromJsonAsync<DraftAnalysisDto>(route, timeout.Token);
        }
        while (result!.Traits.State != "Ready" || result.Embedding.State != "Ready");
        Assert.Contains(channel.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IChannel.BasicRejectAsync));
        Assert.Equal("backpack", Assert.Single(result.Suggestions!.Objects).ObjectType);
        Assert.Equal(1024, result.Embedding.Dimensions);
        Assert.Single(await db.ImageEmbeddings.ToListAsync(timeout.Token));
        Assert.Empty(await db.SearchListings.ToListAsync(timeout.Token));
        DraftAnalysisInbox inbox = scope.ServiceProvider.GetRequiredService<DraftAnalysisInbox>();
        Assert.False(await inbox.AcceptAsync("null"u8.ToArray(), timeout.Token));
    }

    private sealed class PhotoHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("black-backpack", null, "image/png")
            });
        }
    }
}
