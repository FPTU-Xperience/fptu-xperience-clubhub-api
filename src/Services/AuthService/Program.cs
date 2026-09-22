using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Extensions;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Errors;
using ClubReportHub.Shared.Health;
using ClubReportHub.Shared.Security;
using ClubReportHub.Shared.Tracing;

var builder = WebApplication.CreateBuilder(args);

// Add all services
builder.Services.AddAuthServices(builder.Configuration, builder.Environment);

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
app.UseForwardedHeaders();
app.UseSecurityHeaders();
app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ============================================================================
// Health & Info Endpoints
// ============================================================================

app.MapStandardHealthChecks();
app.MapGlobalErrorEndpoint();
app.MapGet("/", () => Results.Ok(new { service = "Auth Service", status = "running" }));

// ============================================================================
// Map Endpoints
// ============================================================================

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapRoleEndpoints();

// ============================================================================
// Database Initialization
// ============================================================================

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
    await db.ApplyMigrationsWithRetryAsync(logger);

    await AuthSeeder.SeedAsync(db, app.Configuration, app.Environment);
}

app.Run();
