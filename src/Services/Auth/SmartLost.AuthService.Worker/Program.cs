using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartLost.AuthService.Infrastructure;

namespace SmartLost.AuthService.Worker;

internal static class WorkerProgram
{
    private static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        string? settingsFile = builder.Configuration["SettingsFile"];
        builder.Configuration
            .AddJsonFile(settingsFile ?? Path.Combine(AppContext.BaseDirectory, "outboxsettings.json"), optional: settingsFile is null)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        builder.Services.AddAuthOutboxWorker(builder.Configuration);
        builder.Services.AddOptions<AuthOutboxWorkerOptions>().Bind(builder.Configuration.GetSection("Worker"))
            .Validate(settings => settings.MaximumBatches is >= 1 and <= 100, "Worker:MaximumBatches must be between 1 and 100.")
            .ValidateOnStart();
        builder.Services.AddSingleton<AuthOutboxJob>();

        using IHost host = builder.Build();
        await host.StartAsync();
        try
        {
            IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            await host.Services.GetRequiredService<AuthOutboxJob>().RunAsync(lifetime.ApplicationStopping);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
