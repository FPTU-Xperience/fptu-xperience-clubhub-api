using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Cors;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Errors;
using ClubReportHub.Shared.Health;
using ClubReportHub.Shared.Messaging;
using ClubReportHub.Shared.Security;
using ClubReportHub.Shared.Tracing;
using ExportService.Data;
using ExportService.Endpoints;
using ExportService.Services;
using Hangfire;
using Hangfire.Common;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ExportDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddClubReportJwt(builder.Configuration, builder.Environment);
builder.Services.AddClubReportTracing();
builder.Services.AddRedisStreamEventBus(builder.Configuration);
builder.Services.AddTransactionalOutbox<ExportDbContext>();
builder.Services.AddSingleton<ExportFileGenerator>();
builder.Services.AddScoped<ExportGenerationJob>();
builder.Services.AddScoped<ExportRetentionJob>();
builder.Services.AddHttpClient("ReportService", client =>
{
    var baseUrl = builder.Configuration["Services:ReportService:BaseUrl"] ?? "http://localhost:5103";
    client.BaseAddress = new Uri(baseUrl);
}).AddCorrelationIdForwarding().AddStandardResilienceHandler();
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new SqlServerStorageOptions
        {
            PrepareSchemaIfNecessary = true,
            QueuePollInterval = TimeSpan.FromSeconds(2)
        }));
var exportWorkerCount = Math.Clamp(builder.Configuration.GetValue<int>("Hangfire:WorkerCount", 2), 1, 8);
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["exports", "default"];
    options.WorkerCount = exportWorkerCount;
});
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
    .AddDbContextCheck<ExportDbContext>("export-db", tags: ["ready"])
    .AddRedisHealthCheck();

var app = builder.Build();

app.UseCorrelationId();

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
app.UseSecurityHeaders();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapStandardHealthChecks();
app.MapGlobalErrorEndpoint();
app.MapGet("/", () => Results.Ok(new { service = "Export Service", status = "running" }));

app.MapExportEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
    await db.ApplyMigrationsWithRetryAsync(logger);
    await ExportSchemaUpgrader.ApplyAsync(db);
}

var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobs.AddOrUpdate(
    "expired-export-cleanup",
    Job.FromExpression<ExportRetentionJob>(
        job => job.CleanupExpiredAsync(CancellationToken.None)),
    Cron.Hourly(),
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.Utc
    });

app.Run();
