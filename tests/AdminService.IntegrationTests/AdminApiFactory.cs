using AdminService.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AdminService.IntegrationTests;

public sealed class AdminApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AdminDbContext>>();
            services.RemoveAll<AdminDbContext>();
            _connection.Open();
            services.AddDbContext<AdminDbContext>(options => options.UseSqlite(_connection));

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AdminDbContext>().Database.EnsureCreated();
        return host;
    }

    public int CountAuditEvents()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AdminDbContext>().AuditRecords.Count();
    }

    public string? GetLatestAuditActorSubjectId()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AdminDbContext>()
            .AuditRecords
            .OrderByDescending(record => record.TimestampUtc)
            .Select(record => record.ActorSubjectId)
            .FirstOrDefault();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
