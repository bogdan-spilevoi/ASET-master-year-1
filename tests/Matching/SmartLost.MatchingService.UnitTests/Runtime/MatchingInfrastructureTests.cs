using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.Contracts.Listings;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.ValueObjects;
using SmartLost.MatchingService.Infrastructure.Demo;
using SmartLost.MatchingService.Infrastructure.Mock;
using SmartLost.MatchingService.Infrastructure.Persistence;
using SmartLost.MatchingService.Infrastructure.Persistence.Repositories;
using Xunit;

namespace SmartLost.MatchingService.UnitTests.Runtime;

public sealed class MatchingInfrastructureTests
{
    private static readonly IOptions<MockAdapterOptions> _mockOptions =
        Options.Create(new MockAdapterOptions { Enabled = true, SlowDelay = TimeSpan.FromMilliseconds(10) });

    [Fact]
    public void InitialMigrationCreatesTheThreeMatchingTables()
    {
        using MatchingDbContext context = new MatchingDbContextFactory().CreateDbContext([]);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.False(context.Database.HasPendingModelChanges());

        IMigrationsAssembly assembly = context.GetService<IMigrationsAssembly>();
        Assert.NotNull(assembly.ModelSnapshot);
        KeyValuePair<string, System.Reflection.TypeInfo> entry = Assert.Single(assembly.Migrations);
        Assert.EndsWith("_InitialCreate", entry.Key, StringComparison.Ordinal);
        Migration migration = assembly.CreateMigration(entry.Value, context.Database.ProviderName!);
        string[] tables = [.. migration.UpOperations.OfType<CreateTableOperation>().Select(table => table.Name).Order()];
        Assert.Equal(["EmbeddingPreparationTasks", "ImageEmbeddings", "SearchListings"], tables);
        CreateTableOperation embeddings = migration.UpOperations.OfType<CreateTableOperation>().Single(table => table.Name == "ImageEmbeddings");
        Assert.Contains(embeddings.Columns, column => column.Name == "Vector" && column.ColumnType == "real[]");
        Assert.Equal(2, migration.UpOperations.OfType<CreateIndexOperation>().Count(index => index.IsUnique));
        Assert.Equal(3, migration.DownOperations.OfType<DropTableOperation>().Count());
        Assert.NotNull(migration.TargetModel!.FindEntityType(typeof(SearchListing).FullName!));

        string script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE UNIQUE INDEX \"UX_ImageEmbeddings_Target\"", script, StringComparison.Ordinal);
        Assert.Contains("CK_SearchListings_CurrentPhoto", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepositoriesFilterPoolsAndCurrentPhotoRevisions()
    {
        await using MatchingDbContext context = CreateInMemoryContext();
        var photo = new PhotoReference(Guid.NewGuid(), 2);
        SearchListing source = MatchingData.Listing(ListingType.Lost, photo: photo);
        SearchListing candidate = MatchingData.Listing(ListingType.Found);
        context.SearchListings.AddRange(source, candidate, MatchingData.Listing(ListingType.Found, eligible: false),
            MatchingData.Listing(ListingType.Lost), MatchingData.Listing(ListingType.Found, authorId: source.AuthorId));
        EmbeddingVersion version = MatchingOptions.Dinov3Version;
        var embeddings = new ImageEmbeddingRepository(context);
        await embeddings.UpsertAsync(ImageEmbedding.Create(source.Id, new PhotoReference(photo.PhotoId, 1), version, [1f]), CancellationToken.None);
        await embeddings.UpsertAsync(ImageEmbedding.Create(source.Id, photo, version, [1f]), CancellationToken.None);
        await context.SaveChangesAsync();

        var listings = new SearchListingRepository(context);
        Assert.Equal(photo, (await listings.GetAsync(source.Id, CancellationToken.None))!.CurrentPhoto);
        Assert.Equal([candidate.Id], (await listings.GetCandidatePoolAsync(source, CancellationToken.None)).Select(item => item.Id));
        ImageEmbedding current = Assert.Single(await embeddings.GetAsync([photo], version, CancellationToken.None));
        Assert.Equal(2, current.PhotoRevision);
        Assert.Empty(await embeddings.GetAsync([photo], new EmbeddingVersion("other", "v0"), CancellationToken.None));

        var replacement = ImageEmbedding.Create(source.Id, photo, version, MatchingData.Normalized(1, 1));
        await embeddings.UpsertAsync(replacement, CancellationToken.None);
        await context.SaveChangesAsync();
        Assert.Equal(replacement.Id, Assert.Single(await embeddings.GetAsync([photo], version, CancellationToken.None)).Id);
    }

    [Fact]
    public async Task TaskRepositoryAndUnitOfWorkPersistChanges()
    {
        await using MatchingDbContext context = CreateInMemoryContext();
        var task = EmbeddingPreparationTask.Create(Guid.NewGuid(), new PhotoReference(Guid.NewGuid(), 1), MatchingOptions.Dinov3Version,
            DateTime.UtcNow);
        context.EmbeddingPreparationTasks.Add(task);
        var unitOfWork = new MatchingUnitOfWork(context);
        await using (ITransaction transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None))
        {
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        await using (ITransaction transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None))
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }

        Assert.Equal(task.Id, (await new EmbeddingPreparationTaskRepository(context).GetAsync(task.Id, CancellationToken.None))!.Id);
        Assert.Null(await new EmbeddingPreparationTaskRepository(context).GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task SeederReplacesRowsAndFindsCurrentTasks()
    {
        await using MatchingDbContext context = CreateInMemoryContext();
        var seeder = new MatchingDemoSeeder(context, new MatchingOptions(), TimeProvider.System);
        await seeder.SeedAsync(CancellationToken.None);
        DemoScenario scenario = await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(10, await context.SearchListings.CountAsync());
        Assert.Equal(7, await context.EmbeddingPreparationTasks.CountAsync());
        Assert.Equal(scenario.Listings[0].EmbeddingTaskId, await seeder.FindCurrentTaskIdAsync(scenario.SourceListingId, CancellationToken.None));
        Assert.Null(await seeder.FindCurrentTaskIdAsync(Guid.Parse("20000000-0000-0000-0000-000000000003"), CancellationToken.None));
        Assert.Null(await seeder.FindCurrentTaskIdAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(7, MatchingDemoSeeder.DemoPhotos.Count);
    }

    [Fact]
    public async Task MockEmbeddingsAreDeterministicAndShareTokenDirections()
    {
        var generator = new MockImageEmbeddingGenerator(_mockOptions, TimeProvider.System);
        GeneratedEmbedding first = await generator.GenerateAsync(Photo("black-backpack"), CancellationToken.None);
        GeneratedEmbedding same = await generator.GenerateAsync(Photo("BLACK-backpack"), CancellationToken.None);
        GeneratedEmbedding partial = await generator.GenerateAsync(Photo("blue-backpack"), CancellationToken.None);
        GeneratedEmbedding empty = await generator.GenerateAsync(Photo("slow:"), CancellationToken.None);

        Assert.Equal(MatchingOptions.Dinov3Version, first.Version);
        Assert.Equal(MockImageEmbeddingGenerator.Dimensions, first.Vector.Count);
        Assert.Equal(first.Vector, same.Vector);
        var left = ImageEmbedding.Create(Guid.NewGuid(), new PhotoReference(Guid.NewGuid(), 1), first.Version, first.Vector);
        var right = ImageEmbedding.Create(Guid.NewGuid(), new PhotoReference(Guid.NewGuid(), 1), partial.Version, partial.Vector);
        Assert.InRange(left.CosineSimilarity(right)!.Value, 0.2, 0.8);
        Assert.Equal(MockImageEmbeddingGenerator.Dimensions, empty.Vector.Count);
        await Assert.ThrowsAsync<AiInferenceException>(() => generator.GenerateAsync(Photo("error:bag"), CancellationToken.None));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GenerateAsync(Photo("timeout:bag"), cancellation.Token));
    }

    [Fact]
    public async Task MockAnalyzerAndPhotoReaderUseAppearanceText()
    {
        var analyzer = new MockPhotoAnalyzer(_mockOptions, TimeProvider.System);
        PhotoAnalysis labels = await analyzer.AnalyzeAsync(Photo("black-wallet-logo"), CancellationToken.None);
        DetectedObject detected = Assert.Single(labels.Objects);
        Assert.Equal(("accessories", "wallet"), (detected.Category, detected.ObjectType));
        Assert.Equal(["black"], detected.Colors);
        Assert.Equal(["logo"], detected.DistinctiveFeatures);
        PhotoAnalysis unknown = await analyzer.AnalyzeAsync(Photo("red-thing"), CancellationToken.None);
        Assert.Empty(unknown.Objects);
        Assert.Single(unknown.Uncertainties);

        var photo = new PhotoReference(Guid.NewGuid(), 1);
        var reader = new MockListingPhotoReader(new MockPhotoCatalog(new Dictionary<PhotoReference, string> { [photo] = "grey-keys" }));
        PhotoContent content = (await reader.ReadAsync(Guid.NewGuid(), photo, CancellationToken.None))!;
        Assert.Equal("grey-keys", Encoding.UTF8.GetString(content.Content.Span));
        Assert.Equal(MockAppearance.MediaType, content.MediaType);
        Assert.Null(await reader.ReadAsync(Guid.NewGuid(), new PhotoReference(Guid.NewGuid(), 1), CancellationToken.None));
        Assert.True(new MockPhotoCatalog().TryGet(MatchingDemoSeeder.DemoPhotos.Keys.First(), out _));
        Assert.Equal(MockAiBehavior.Timeout, MockAppearance.Parse(Encoding.UTF8.GetBytes(" Timeout:bag")).Behavior);
    }

    private static PhotoContent Photo(string appearance)
    {
        return new PhotoContent(Encoding.UTF8.GetBytes(appearance), MockAppearance.MediaType);
    }

    private static MatchingDbContext CreateInMemoryContext()
    {
        DbContextOptions<MatchingDbContext> options = new DbContextOptionsBuilder<MatchingDbContext>()
            .UseInMemoryDatabase($"MatchingInfrastructureTests-{Guid.NewGuid()}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new MatchingDbContext(options);
    }
}
