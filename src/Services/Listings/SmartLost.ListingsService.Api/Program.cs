using System.Text.Json.Serialization;
using SmartLost.BuildingBlocks.AspNetCore;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application;
using SmartLost.ListingsService.Infrastructure;
using SmartLost.ListingsService.Infrastructure.Seeding;

namespace SmartLost.ListingsService.Api;

public partial class Program
{
    private static readonly Action<ILogger, Exception?> _seedCompleted = LoggerMessage.Define(
        LogLevel.Information, new EventId(2200, "ListingsSeedCompleted"), "Listings development seed completed.");

    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<ListingType>()));
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "SmartLost Listings API";
            document.Info.Version = "v1";
            document.Info.Description = "Public category and open lost/found listing reads.";
            return Task.CompletedTask;
        }));
        builder.Services.AddBuildingBlocksApi();
        builder.Services.AddListingsApplication();
        builder.Services.AddListingsPersistence(builder.Configuration);

        await using WebApplication app = builder.Build();
        bool seedOnly = app.Configuration.GetValue<bool>("SeedData:Only");
        if (seedOnly && !app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("SeedData:Only is available only in Development.");
        }

        app.UseExceptionHandler();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("../openapi/v1.json", "SmartLost Listings v1");
                options.DocumentTitle = "SmartLost Listings API";
            });
            if (seedOnly || app.Configuration.GetValue<bool>("SeedData:Enabled"))
            {
                await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ListingsDevelopmentSeeder>().SeedAsync();
                _seedCompleted(app.Logger, null);
            }
        }
        else
        {
            app.UseHttpsRedirection();
        }

        if (seedOnly)
        {
            return;
        }

        app.MapControllers();
        await app.RunAsync();
    }
}
