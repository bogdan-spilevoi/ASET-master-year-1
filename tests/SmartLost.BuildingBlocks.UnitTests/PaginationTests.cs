using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Application;
using SmartLost.BuildingBlocks.Application.Validation;
using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class PaginationTests
{
    [Fact]
    public void RepositoryPageCopiesItemsAndMapsWithoutLosingTotalCount()
    {
        List<int> source = [1, 2];
        var slice = new PageSlice<int>(source, 25);
        source.Clear();

        Assert.Equal(2, slice.Items.Count);
        Assert.Equal(1, slice.Items[0]);
        Assert.Equal(25, slice.TotalCount);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)slice.Items)[0] = 99);

        PageSlice<string> mapped = slice.Map(item => $"item-{item}");
        Assert.Equal("item-1", mapped.Items[0]);
        Assert.Equal("item-2", mapped.Items[1]);
        Assert.Equal(25, mapped.TotalCount);
        Assert.Throws<ArgumentNullException>(() => slice.Map<string>(null!));

        var empty = new PageSlice<int>([], 25);
        Assert.Empty(empty.Map(item => item * 2).Items);
        Assert.Equal(25, empty.TotalCount);
    }

    [Fact]
    public void RepositoryPageRejectsInconsistentCountsAndMissingItems()
    {
        Assert.Throws<ArgumentNullException>(() => new PageSlice<int>(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageSlice<int>([], -1));
        Assert.Throws<ArgumentException>(() => new PageSlice<int>([1, 2], 1));
        Assert.Empty(new PageSlice<int>([], 0).Items);
    }

    [Fact]
    public void ApplicationPagePreservesZeroBasedMetadataAndSnapshots()
    {
        List<int> source = [7, 8];
        var page = new PagedResult<int>(source, 42, 3, 10);
        source.Clear();

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(7, page.Items[0]);
        Assert.Equal(42, page.TotalCount);
        Assert.Equal(3, page.CurrentPage);
        Assert.Equal(10, page.PageSize);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)page.Items).Clear());

        var slice = new PageSlice<int>([9], 1);
        var first = new PagedResult<int>(slice, 0, 100);
        Assert.Same(slice.Items, first.Items);
        Assert.Equal(0, first.CurrentPage);
        Assert.Equal(100, first.PageSize);

        var beyondLastPage = new PagedResult<int>([], 1, 99, 10);
        Assert.Empty(beyondLastPage.Items);
        Assert.Equal(1, beyondLastPage.TotalCount);
    }

    [Fact]
    public void ApplicationPageRejectsInvalidMetadata()
    {
        var slice = new PageSlice<int>([1, 2], 2);
        Assert.Throws<ArgumentNullException>(() => new PagedResult<int>((PageSlice<int>)null!, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<int>(slice, -1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<int>(slice, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<int>(slice, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<int>(slice, 0, 101));
        Assert.Throws<ArgumentException>(() => new PagedResult<int>(slice, 0, 1));
    }

    [Theory]
    [InlineData(-1, 0, 0, 10)]
    [InlineData(0, -1, 0, 10)]
    [InlineData(2, 1, 2, 1)]
    [InlineData(3, 100, 3, 100)]
    [InlineData(4, int.MaxValue, 4, 100)]
    public void RepositoryDefaultsClampToTheSharedLimits(int pageIndex, int pageSize, int expectedIndex, int expectedSize)
    {
        PagedDataGuard.Clamp(ref pageIndex, ref pageSize);
        Assert.Equal(expectedIndex, pageIndex);
        Assert.Equal(expectedSize, pageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 10)]
    [InlineData(2, 100)]
    public async Task BaseValidatorAcceptsValidPaging(int pageIndex, int pageSize)
    {
        var validator = new BasePaginatedValidator<PaginationProbe>();
        Assert.True((await validator.ValidateAsync(new PaginationProbe(pageIndex, pageSize, "valid"))).IsValid);
    }

    [Theory]
    [InlineData(-1, 10, "PageIndex", "pagination.page_index_invalid")]
    [InlineData(0, 0, "PageSize", "pagination.page_size_invalid")]
    [InlineData(0, -1, "PageSize", "pagination.page_size_invalid")]
    [InlineData(0, 101, "PageSize", "pagination.page_size_invalid")]
    public async Task BaseValidatorReturnsStableFieldErrors(int pageIndex, int pageSize, string property, string code)
    {
        var validator = new BasePaginatedValidator<PaginationProbe>();
        FluentValidation.Results.ValidationResult validation = await validator.ValidateAsync(new PaginationProbe(pageIndex, pageSize, "valid"));
        FluentValidation.Results.ValidationFailure error = Assert.Single(validation.Errors);
        Assert.Equal(property, error.PropertyName);
        Assert.Equal(code, error.ErrorCode);
    }

    [Fact]
    public async Task DerivedValidatorIsDiscoveredAndBlocksInvalidMediatorQueries()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddBuildingBlocksApplication(typeof(PaginationProbe).Assembly);
        using ServiceProvider provider = services.BuildServiceProvider();
        ISender sender = provider.GetRequiredService<ISender>();

        // The handler constructs a page, which would throw if validation let this through.
        Result<PagedResult<string>> invalid = await sender.Send(new PaginationProbe(-1, 101, ""));
        Assert.True(invalid.IsFailure);
        Assert.Equal(3, invalid.Errors.Count);
        Assert.All(invalid.Errors, error => Assert.Equal(ErrorKind.Validation, error.Kind));
        Assert.Contains(invalid.Errors, error => error.PropertyName == "SearchTerm" && error.Code == "search.required");

        Result<PagedResult<string>> valid = await sender.Send(new PaginationProbe(0, 10, "found"));
        Assert.True(valid.IsSuccess);
        Assert.Equal("found", Assert.Single(valid.Value.Items));
        Assert.Equal(1, valid.Value.TotalCount);
        Assert.Equal(0, valid.Value.CurrentPage);
        Assert.Equal(10, valid.Value.PageSize);
    }
}

public sealed record PaginationProbe(int PageIndex, int PageSize, string SearchTerm)
    : IRequest<Result<PagedResult<string>>>, IPaginatedRequest;

public sealed class PaginationProbeValidator : BasePaginatedValidator<PaginationProbe>
{
    public PaginationProbeValidator()
    {
        RuleFor(request => request.SearchTerm).NotEmpty().WithErrorCode("search.required");
    }
}

public sealed class PaginationProbeHandler : IRequestHandler<PaginationProbe, Result<PagedResult<string>>>
{
    public Task<Result<PagedResult<string>>> Handle(PaginationProbe request, CancellationToken cancellationToken)
    {
        PageSlice<string> slice = new([request.SearchTerm], 1);
        return Task.FromResult(Result<PagedResult<string>>.Success(new PagedResult<string>(slice, request.PageIndex, request.PageSize)));
    }
}
