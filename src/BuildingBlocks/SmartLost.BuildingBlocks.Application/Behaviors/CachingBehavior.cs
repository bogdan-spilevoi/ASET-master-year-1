using System.Reflection;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartLost.BuildingBlocks.Application.Caching;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.Application.Behaviors;

public sealed class CachingBehavior<TRequest, TResponse>(IQueryCache cache, ILogger<CachingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : class, IResult<TResponse>
{
    private static readonly CacheableQueryAttribute? _policy = typeof(TRequest).GetCustomAttribute<CacheableQueryAttribute>();
    private static readonly Action<ILogger, string, Exception?> _hit = LoggerMessage.Define<string>(
        LogLevel.Debug, new EventId(2100, "QueryCacheHit"), "Query cache hit for {QueryType}.");
    private static readonly Action<ILogger, string, Exception?> _miss = LoggerMessage.Define<string>(
        LogLevel.Debug, new EventId(2101, "QueryCacheMiss"), "Query cache miss for {QueryType}.");

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_policy is null)
        {
            return await next(cancellationToken);
        }

        string key = QueryCacheKey.Create<TRequest, TResponse>(request, _policy.Prefix);
        bool executed = false;
        TResponse response = await cache.GetOrCreateAsync(key, TimeSpan.FromSeconds(_policy.TtlSeconds), async token =>
        {
            executed = true;
            _miss(logger, typeof(TRequest).Name, null);
            return await next(token);
        }, result => result.IsSuccess, cancellationToken);
        if (!executed)
        {
            _hit(logger, typeof(TRequest).Name, null);
        }

        return response;
    }
}
