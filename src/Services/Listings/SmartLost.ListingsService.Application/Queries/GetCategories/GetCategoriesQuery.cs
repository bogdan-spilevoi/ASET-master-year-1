using MediatR;
using SmartLost.BuildingBlocks.Application.Caching;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Queries.GetCategories;

[CacheableQuery("listings:categories", TtlSeconds = 300)]
public sealed record GetCategoriesQuery : IRequest<Result<IReadOnlyList<CategoryDto>>>;
