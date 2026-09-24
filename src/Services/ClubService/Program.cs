using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Cors;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Errors;
using ClubReportHub.Shared.Health;
using ClubReportHub.Shared.Messaging;
using ClubReportHub.Shared.Security;
using ClubReportHub.Shared.Tracing;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<ClubDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Authentication & Authorization
builder.Services.AddClubReportJwt(builder.Configuration, builder.Environment);
builder.Services.AddClubReportTracing();

// Event Bus (Redis Streams)
builder.Services.AddRedisStreamEventBus(builder.Configuration);
builder.Services.AddTransactionalOutbox<ClubDbContext>();

// HTTP Client for Activity Service
builder.Services.AddHttpClient<ActivityStatisticsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:ActivityService:BaseUrl"] ?? "http://localhost:5106/");
    client.Timeout = TimeSpan.FromSeconds(15);
}).AddCorrelationIdForwarding().AddStandardResilienceHandler();

// API Documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
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

// Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ClubDbContext>("club-db", tags: ["ready"])
    .AddRedisHealthCheck();

var app = builder.Build();

// ============================================================================
// Pipeline Configuration
// ============================================================================

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

// CORS
app.UseCors("frontend");

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Health Check
app.MapStandardHealthChecks();

// Error Endpoint
app.MapGlobalErrorEndpoint();

// Root Endpoint
app.MapGet("/", () => Results.Ok(new { service = "Club Service", status = "running" }));

// ============================================================================
// Map Endpoints
// ============================================================================

app.MapClubEndpoints();             // Club CRUD operations
app.MapApplicationEndpoints();      // Club creation applications
app.MapMembershipEndpoints();       // Membership requests & management
app.MapManagerEndpoints();          // Manager assignments
app.MapMemberManagementEndpoints();  // Member listing & roster
app.MapDisbandEndpoints();          // Club disband workflow
app.MapTransferEndpoints();         // Ownership transfer workflow

// ============================================================================
// Database Initialization
// ============================================================================

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
    await db.ApplyMigrationsWithRetryAsync(logger);
    await ClubSchemaUpgrader.ApplyAsync(db);
    await ClubSeeder.SeedAsync(db);
}

app.Run();
