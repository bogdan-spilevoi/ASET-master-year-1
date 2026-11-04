using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartLost.ListingsService.Api;
using SmartLost.ListingsService.Infrastructure.Persistence;

namespace SmartLost.ListingsService.UnitTests;

internal sealed class ListingsApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _environment;
    private readonly bool _seed;
    private readonly bool _seedOnly;
    private readonly TimeProvider? _clock;
    private readonly DbCommandInterceptor? _interceptor;

    internal ListingsApiFactory(string environment = "Development", bool seed = true,
        TimeProvider? clock = null, DbCommandInterceptor? interceptor = null, bool seedOnly = false)
    {
        _environment = environment;
        _seed = seed;
        _seedOnly = seedOnly;
        _clock = clock;
        _interceptor = interceptor;
        _connection.Open();
        using ListingsDbContext context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    internal ListingsDbContext CreateDbContext()
    {
        return new ListingsDbContext(new DbContextOptionsBuilder<ListingsDbContext>().UseSqlite(_connection).Options);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(Settings()));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(Settings()));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ListingsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ListingsDbContext>>();
            services.AddDbContext<ListingsDbContext>(options =>
            {
                options.UseSqlite(_connection);
                if (_interceptor is not null)
                {
                    options.AddInterceptors(_interceptor);
                }
            });
            if (_clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_clock);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }

    private Dictionary<string, string?> Settings()
    {
        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:ListingsDatabase"] = "Host=unused;Database=unused",
            ["SeedData:Enabled"] = _seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SeedData:Only"] = _seedOnly.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
