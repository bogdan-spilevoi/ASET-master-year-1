using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Enums;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.ValueObjects;
using SmartLost.MatchingService.Infrastructure.Mock;
using SmartLost.MatchingService.Infrastructure.Persistence;
using SmartLost.MatchingService.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;
using Xunit;

namespace SmartLost.MatchingService.IntegrationTests;

public sealed class EmbeddingProcessingTests
{
    [Fact]
    public async Task ConcurrentClaimsReturnOneSuccessAndOneConflictWithoutDuplicateEmbedding()
    {
        await using MatchingFactory factory = new();
        await factory.InitializeAsync();
        Guid taskId = await factory.SeedTaskAsync(claim: false);
        factory.Gate.Enabled = true;
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage[] responses = await Task.WhenAll(
            client.PostAsync($"/api/matching/embedding-tasks/{taskId}/process", null),
            client.PostAsync($"/api/matching/embedding-tasks/{taskId}/process", null)).WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            HttpResponseMessage conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            using var problem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
            Assert.Equal(MatchingErrors.ConcurrentUpdate.Code, problem.RootElement.GetProperty("code").GetString());
            using IServiceScope scope = factory.Services.CreateScope();
            MatchingDbContext context = scope.ServiceProvider.GetRequiredService<MatchingDbContext>();
            EmbeddingPreparationTask task = await context.EmbeddingPreparationTasks.SingleAsync();
            Assert.Equal(EmbeddingPreparationState.Ready, task.State);
            Assert.Equal(1, task.AttemptCount);
            Assert.Equal(1, await context.ImageEmbeddings.CountAsync());
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task FinalAbandonedLeaseFailsDurablyAndActiveLeaseRemainsProtected()
    {
        await using MatchingFactory factory = new();
        await factory.InitializeAsync();
        Guid taskId = await factory.SeedTaskAsync(claim: true);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage active = await client.PostAsync($"/api/matching/embedding-tasks/{taskId}/process", null);
        Assert.Equal(HttpStatusCode.Conflict, active.StatusCode);
        factory.Clock.Now = factory.Clock.Now.AddMinutes(6);
        for (int request = 0; request < 2; request++)
        {
            using HttpResponseMessage response = await client.PostAsync($"/api/matching/embedding-tasks/{taskId}/process", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var state = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Failed", state.RootElement.GetString());
        }

        using IServiceScope scope = factory.Services.CreateScope();
        MatchingDbContext context = scope.ServiceProvider.GetRequiredService<MatchingDbContext>();
        EmbeddingPreparationTask task = await context.EmbeddingPreparationTasks.SingleAsync();
        Assert.Equal(EmbeddingPreparationState.Failed, task.State);
        Assert.Equal(1, task.AttemptCount);
        Assert.Equal(MatchingErrors.AttemptsExhausted.Code, task.LastError);
        Assert.Null(task.LeaseId);
        Assert.Null(task.NextAttemptAtUtc);
        Assert.Equal(0, await context.ImageEmbeddings.CountAsync());
    }

    private sealed class MatchingFactory : WebApplicationFactory<Program>
    {
        private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("matching_review_tests").WithUsername("matching_tests").WithPassword("test-only-database-password").Build();

        public ClaimGate Gate { get; } = new();
        public TestClock Clock { get; } = new();

        public async Task InitializeAsync()
        {
            await _database.StartAsync();
            using IServiceScope scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MatchingDbContext>().Database.MigrateAsync();
        }

        public async Task<Guid> SeedTaskAsync(bool claim)
        {
            using IServiceScope scope = Services.CreateScope();
            MatchingDbContext context = scope.ServiceProvider.GetRequiredService<MatchingDbContext>();
            PhotoReference photo = new(Guid.NewGuid(), 1);
            var listing = SearchListing.Create(Guid.NewGuid(), 1, 1, ListingType.Lost, Guid.NewGuid(), photo,
                ["backpack"], "black", [], new DateOnly(2026, 11, 8), "Library", true);
            var task = EmbeddingPreparationTask.Create(listing.Id, photo, MatchingOptions.Dinov3Version, Clock.Now.UtcDateTime);
            if (claim)
            {
                task.Claim(Guid.NewGuid(), Clock.Now.UtcDateTime, TimeSpan.FromMinutes(5), 1);
            }

            context.SearchListings.Add(listing);
            context.EmbeddingPreparationTasks.Add(task);
            await context.SaveChangesAsync();
            return task.Id;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(Settings()));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(Settings()));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<MatchingDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<MatchingDbContext>>();
                services.AddDbContext<MatchingDbContext>(options => options.UseNpgsql(_database.GetConnectionString()));
                services.RemoveAll<MatchingOptions>();
                services.AddSingleton(new MatchingOptions { MaximumAttempts = 1 });
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddScoped<IEmbeddingPreparationTaskRepository>(provider => new GatedTasks(
                    new EmbeddingPreparationTaskRepository(provider.GetRequiredService<MatchingDbContext>()), Gate));
                services.AddSingleton<IListingPhotoReader, TestPhotos>();
            });
        }

        private Dictionary<string, string?> Settings()
        {
            return new Dictionary<string, string?>
            {
                ["ConnectionStrings:MatchingDatabase"] = _database.GetConnectionString(),
                ["MockAdapters:Enabled"] = "true"
            };
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    private sealed class TestPhotos : IListingPhotoReader
    {
        public Task<PhotoContent?> ReadAsync(Guid listingId, PhotoReference photo, CancellationToken cancellationToken)
        {
            return Task.FromResult<PhotoContent?>(new PhotoContent(Encoding.UTF8.GetBytes("black-backpack"), MockAppearance.MediaType));
        }
    }

    private sealed class ClaimGate
    {
        private int _arrived;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Enabled
        {
            get; set;
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (!Enabled)
            {
                return;
            }

            if (Interlocked.Increment(ref _arrived) == 2)
            {
                _ready.TrySetResult();
            }

            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
    }

    private sealed class GatedTasks(IEmbeddingPreparationTaskRepository inner, ClaimGate gate) : IEmbeddingPreparationTaskRepository
    {
        public async Task<EmbeddingPreparationTask?> GetAsync(Guid taskId, CancellationToken cancellationToken)
        {
            EmbeddingPreparationTask? result = await inner.GetAsync(taskId, cancellationToken);
            await gate.WaitAsync(cancellationToken);
            return result;
        }
    }
}
