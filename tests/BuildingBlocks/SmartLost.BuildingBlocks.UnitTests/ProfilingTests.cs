using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Application;
using SmartLost.BuildingBlocks.Application.Profiling;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class ProfilingTests
{
    [Fact]
    public async Task SharedPipelineWorksWithoutAnyMicroserviceAndIsOptIn()
    {
        ServiceCollection disabled = new();
        disabled.AddLogging();
        disabled.AddBuildingBlocksApplication(typeof(ProfilingTests).Assembly);
        using (ServiceProvider provider = disabled.BuildServiceProvider())
        {
            Assert.Null(provider.GetService<IProfilingContext>());
        }

        ServiceCollection services = new();
        services.AddLogging();
        services.AddBuildingBlocksApplication(typeof(ProfilingTests).Assembly, useProfiling: true);
        using ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = root.CreateScope();
        IProfilingContext context = scope.ServiceProvider.GetRequiredService<IProfilingContext>();
        Assert.IsType<ProfilingContext>(context);
        Assert.Equal("ok", (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Probe())).Value);
        Assert.Null(context.Current);
        Assert.Throws<InvalidOperationException>(new ProfilingOptions { SlowOperationThreshold = TimeSpan.FromSeconds(-1) }.Validate);
    }

    [Fact]
    public async Task ContextRestoresNestedProfilesAndIsolatesConcurrentFlows()
    {
        ProfilingContext context = new();
        OperationProfile outer = new("outer", "one");
        using (context.Begin(outer))
        {
            using (context.Begin(new OperationProfile("inner", "two")))
            {
                Assert.Equal("inner", context.Current!.OperationName);
            }

            Assert.Same(outer, context.Current);
            await Task.WhenAll(Enumerable.Range(0, 10).Select(async index =>
            {
                OperationProfile nested = new("parallel", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using (context.Begin(nested))
                {
                    await Task.Yield();
                    Assert.Same(nested, context.Current);
                }
            }));
            Assert.Same(outer, context.Current);
        }

        Assert.Null(context.Current);
        Assert.Throws<ArgumentNullException>(() => context.Begin(null!));
    }

    public sealed record Probe : IRequest<Result<string>>, ICorrelatedRequest
    {
        public string? CorrelationId => "shared-probe";
    }

    public sealed class ProbeHandler(IProfilingContext context) : IRequestHandler<Probe, Result<string>>
    {
        public Task<Result<string>> Handle(Probe request, CancellationToken cancellationToken)
        {
            Assert.Equal(request.CorrelationId, context.Current!.CorrelationId);
            return Task.FromResult(Result<string>.Success("ok"));
        }
    }
}
