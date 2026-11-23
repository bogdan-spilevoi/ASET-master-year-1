using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
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
        await using WebApplication app = BuildApplication(args);
        await RunApplicationAsync(app);
    }

    public static WebApplication BuildApplication(string[] args, Action<WebApplicationBuilder>? configureBuilder = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<ListingType>()));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ListingType>()));
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "SmartLost Listings API";
            document.Info.Version = "v1";
            document.Info.Description = "Public listings, owner-managed drafts, match decisions and recoveries.";
            document.Components ??= new();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            };
            return Task.CompletedTask;
        }).AddOperationTransformer((operation, context, _) =>
        {
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any())
            {
                operation.Security = [new() { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }];
            }
            return Task.CompletedTask;
        }));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            string? key = builder.Configuration["Jwt:SigningKey"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "AuthService",
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "AuthService",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = string.IsNullOrWhiteSpace(key) ? null : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddBuildingBlocksApi();
        builder.Services.AddListingsApplication();
        builder.Services.AddListingsPersistence(builder.Configuration);

        configureBuilder?.Invoke(builder);
        return builder.Build();
    }

    public static async Task RunApplicationAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
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
                await scope.ServiceProvider.GetRequiredService<ListingsDevelopmentSeeder>().SeedAsync(cancellationToken);
                _seedCompleted(app.Logger, null);
            }
        }
        else
        {
            app.UseWhen(context => context.Connection.LocalPort != 8082, branch => branch.UseHttpsRedirection());
        }

        if (seedOnly)
        {
            return;
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.RunAsync(cancellationToken);
    }
}
