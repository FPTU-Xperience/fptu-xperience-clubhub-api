using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Cors;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Errors;
using ClubReportHub.Shared.Health;
using ClubReportHub.Shared.Messaging;
using ClubReportHub.Shared.Security;
using ClubReportHub.Shared.Tracing;
using Microsoft.EntityFrameworkCore;
using NotificationService.Consumers;
using NotificationService.Data;
using NotificationService.Endpoints;
using NotificationService.Models;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddClubReportTracing();
builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.Configure<RedisStreamOptions>(
    builder.Configuration.GetSection(RedisStreamOptions.SectionName));
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisStreamOptions>>().Value;
    var config = ConfigurationOptions.Parse(options.ConnectionString);
    config.AbortOnConnectFail = false;
    config.ConnectRetry = options.MaxRetries > 0 ? options.MaxRetries : 3;
    config.ConnectTimeout = options.ConnectTimeoutMs;
    config.SyncTimeout = options.SyncTimeoutMs;
    config.KeepAlive = options.KeepAliveSeconds;
    config.ClientName = "NotificationService";
    return ConnectionMultiplexer.Connect(config);
});
builder.Services.AddHostedService<RedisStreamNotificationConsumer>();
builder.Services.AddClubAccessClient(builder.Configuration);
builder.Services.AddClubReportJwt(builder.Configuration, builder.Environment);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        CorsOriginConfiguration.ApplyFrontendCorsPolicy(
            policy,
            builder.Configuration,
            builder.Environment);
    });
});
builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotificationDbContext>("notification-db", tags: ["ready"])
    .AddRedisHealthCheck();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
}

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCorrelationId();
app.UseSecurityHeaders();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapStandardHealthChecks();
app.MapGlobalErrorEndpoint();
app.MapGet("/", () => Results.Ok(new { service = "Notification Service", status = "running" }));

app.MapNotificationEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
    await db.ApplyMigrationsWithRetryAsync(logger);
    await NotificationSeeder.SeedAsync(db);
}

app.Run();
