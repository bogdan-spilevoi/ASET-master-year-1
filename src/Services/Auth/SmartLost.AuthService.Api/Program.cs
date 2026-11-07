using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartLost.AuthService.Api;
using SmartLost.AuthService.Application;
using SmartLost.AuthService.Infrastructure;
using SmartLost.AuthService.Infrastructure.Authentication;
using SmartLost.AuthService.Infrastructure.Seeding;
using SmartLost.BuildingBlocks.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddApiMapping();
builder.Services.AddOpenApi();
builder.Services.AddBuildingBlocksApi();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, settings) =>
    {
        JwtOptions jwtOptions = settings.Value;
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

WebApplication app = builder.Build();

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
        options.SwaggerEndpoint("../openapi/v1.json", "SmartLost AuthService v1");
        options.DocumentTitle = "SmartLost AuthService API";
    });

    if (seedOnly || app.Configuration.GetValue<bool>("SeedData:Enabled"))
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthDevelopmentSeeder>().SeedAsync();
    }
}

if (seedOnly)
{
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
