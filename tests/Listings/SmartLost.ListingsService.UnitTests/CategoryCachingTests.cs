using System.Data.Common;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Queries.GetCategories;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed class CategoryCachingTests
{
    [Fact]
    public async Task HttpRequestsReuseOneCategoryReadUntilAbsoluteExpirationAndKeepSnapshotsReadOnly()
    {
        ManualClock clock = new();
        CategoryReads reads = new();
        using ListingsApiFactory factory = new(clock: clock, interceptor: reads);
        using HttpClient client = factory.CreateClient();
        reads.Reset();
        using JsonDocument first = await GetCategoriesAsync(client);
        using JsonDocument second = await GetCategoriesAsync(client);
        Assert.Equal(3, first.RootElement.GetArrayLength());
        Assert.Equal(first.RootElement.GetRawText(), second.RootElement.GetRawText());
        Assert.Equal(1, reads.Count);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            Result<IReadOnlyList<CategoryDto>> cached = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetCategoriesQuery());
            Assert.Throws<NotSupportedException>(() => ((IList<CategoryDto>)cached.Value).Clear());
            ListingsDbContext context = scope.ServiceProvider.GetRequiredService<ListingsDbContext>();
            context.Categories.Add(Category.Create("charger"));
            await context.SaveChangesAsync();
        }

        clock.Advance(TimeSpan.FromSeconds(299));
        using JsonDocument stillCached = await GetCategoriesAsync(client);
        Assert.Equal(3, stillCached.RootElement.GetArrayLength());
        Assert.Equal(1, reads.Count);
        clock.Advance(TimeSpan.FromSeconds(1));
        using JsonDocument refreshed = await GetCategoriesAsync(client);
        Assert.Equal(4, refreshed.RootElement.GetArrayLength());
        Assert.Equal(2, reads.Count);
    }

    private static async Task<JsonDocument> GetCategoriesAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/categories");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed class CategoryReads : DbCommandInterceptor
    {
        private int _count;

        internal int Count => Volatile.Read(ref _count);

        internal void Reset()
        {
            Interlocked.Exchange(ref _count, 0);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Categories\"", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        internal void Advance(TimeSpan duration)
        {
            _now += duration;
        }
    }
}
