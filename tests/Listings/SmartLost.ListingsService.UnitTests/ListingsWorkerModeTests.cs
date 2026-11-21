using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using SmartLost.BuildingBlocks.Messaging.RabbitMq;
using SmartLost.ListingsService.Infrastructure.Persistence;
using SmartLost.ListingsService.Worker;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed class ListingsWorkerModeTests
{
    [Fact]
    public void PublishModeRegistersDeliveryAndRabbitMqInsteadOfTheInspectionHost()
    {
        using IHost host = WorkerProgram.BuildHost([
            "--Worker:Mode", "Publish", "--ConnectionStrings:ListingsDatabase", "Host=unused;Database=worker",
            "--RabbitMq:UserName", "test-user", "--RabbitMq:Password", "test-only-password",
            "--RabbitMq:Exchange", "test.events", "--RabbitMq:Bindings:notifications:0", "listings.*"]);
        using IServiceScope scope = host.Services.CreateScope();
        Assert.IsType<RabbitMqOutboxPublisher>(scope.ServiceProvider.GetRequiredService<IOutboxMessagePublisher>());
        Assert.IsType<OutboxProcessor<ListingsDbContext>>(scope.ServiceProvider.GetRequiredService<IOutboxProcessor>());
        Assert.IsType<OutboxDeliveryService>(Assert.Single(host.Services.GetServices<IHostedService>()));
        Assert.Null(scope.ServiceProvider.GetService<IOutboxReader>());
        Assert.Throws<InvalidOperationException>(() => WorkerProgram.BuildHost(["--Worker:Mode", "Unknown"]));
    }
}
