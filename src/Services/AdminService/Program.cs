using AdminService.Auditing;
using AdminService.Data;
using AdminService.Endpoints;
using AdminService.Errors;
using AdminService.Middleware;
using AdminService.Observability;
using AdminService.OpenApi;
using AdminService.Security;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Tracing;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
builder.Services.AddDbContext<AdminDbContext>(options => options.UseSqlServer(
    connectionString,
    sqlServer => sqlServer.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(10),
        errorNumbersToAdd: null)));

builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminPolicies.AdminOnly, policy =>
        policy.RequireRole(AuthRoles.Admin));
    options.AddPolicy(AdminPolicies.StudentAffairsOnly, policy =>
        policy.RequireRole(AuthRoles.StudentAffairsAdmin));
    options.AddPolicy(AdminPolicies.BackofficeUser, policy =>
        policy.RequireRole(AuthRoles.Admin, AuthRoles.StudentAffairsAdmin));
});
builder.Services.AddClubReportTracing();
builder.Services.AddScoped<ICurrentActor, HttpCurrentActor>();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
builder.Services.AddScoped<IAuditService, EfAuditService>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AdminDbContext>("admin-database", tags: ["ready"]);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FPTU Xperience Admin API",
        Version = "v1",
        Description = "System Admin and Student Affairs secure backend foundation."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT access token issued by the platform AuthService."
    });
    options.OperationFilter<AdminOperationFilter>();
});

var app = builder.Build();

app.UseCorrelationId();
app.UseMiddleware<ApiExceptionMiddleware>();
if (app.Environment.IsDevelopment()
    || app.Environment.IsEnvironment("Test")
    || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Admin API v1"));
}

app.UseAuthentication();
app.UseMiddleware<StructuredRequestLoggingMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();
app.MapGet("/", () => Results.Ok(new
{
    service = "FPTU Xperience Admin Service",
    apiVersion = "v1",
    status = "running"
})).AllowAnonymous();
app.MapAdminEndpoints();

if (app.Environment.IsEnvironment("Test"))
{
    app.MapAdminTestEndpoints();
    app.MapGet("/__test/unexpected-error", () =>
    {
        throw new InvalidOperationException("Sensitive test exception detail.");
    }).AllowAnonymous();
    app.MapGet("/__test/dependency-failure", () =>
    {
        throw new DependencyFailureException();
    }).AllowAnonymous();
}

if (app.Configuration.GetValue("Database:ApplyMigrationsAtStartup", true))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("AdminDatabaseStartup");
    await dbContext.ApplyMigrationsWithRetryAsync(logger);
}

await app.RunAsync();

public partial class Program;
