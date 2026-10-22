using AdminService.Auditing;
using AdminService.Data;
using AdminService.Endpoints;
using AdminService.Errors;
using AdminService.Middleware;
using AdminService.Observability;
using AdminService.OpenApi;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Rosters;
using ClubReportHub.Shared.Messaging;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Health;
using ClubReportHub.Shared.Security;
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
builder.Services.AddCurrentAccountValidation(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminPolicies.AdminOnly, policy =>
        policy.RequireRole(AuthRoles.Admin));
    options.AddPolicy(AdminPolicies.StudentAffairsOnly, policy =>
        policy.RequireRole(AuthRoles.StudentAffairsAdmin));
    options.AddPolicy(AdminPolicies.BackofficeUser, policy =>
        policy.RequireRole(AuthRoles.Admin, AuthRoles.StudentAffairsAdmin));
    options.AddPolicy(AdminPolicies.AnyActor, policy =>
        policy.RequireRole(
            AuthRoles.Admin,
            AuthRoles.StudentAffairsAdmin,
            AuthRoles.ClubManager,
            AuthRoles.ClubMember));
});
builder.Services.AddClubReportTracing();
builder.Services.AddScoped<StudentExperienceSourceClient>();
builder.Services.AddHttpClient<SchoolAccountClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AuthService:BaseUrl"] ?? "http://auth-service:8080/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddClubAccessClient(builder.Configuration);
builder.Services.AddHttpClient<CurrentUserProfileClient>(client =>
{ client.BaseAddress = new Uri(builder.Configuration["Services:AuthService:BaseUrl"] ?? "http://auth-service:8080/"); client.Timeout = TimeSpan.FromSeconds(10); });
builder.Services.AddHttpClient<ClubExperienceAuthorityClient>(client =>
{ client.BaseAddress = new Uri(builder.Configuration["Services:ClubService:BaseUrl"] ?? "http://club-service:8080/"); client.Timeout = TimeSpan.FromSeconds(10); });
foreach (var service in new[] { "Activity", "Club" })
    builder.Services.AddHttpClient("Experience" + service, client =>
    {
        client.BaseAddress = new Uri(builder.Configuration[$"Services:{service}Service:BaseUrl"] ?? $"http://{service.ToLowerInvariant()}-service:8080/");
        client.Timeout = TimeSpan.FromSeconds(15);
    }).AddCorrelationIdForwarding();
builder.Services.AddHttpClient<ActivitySourceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:ActivityService:BaseUrl"] ?? "http://activity-service:8080/");
    client.Timeout = TimeSpan.FromSeconds(15);
}).AddCorrelationIdForwarding();
if (!builder.Environment.IsEnvironment("Test") && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddRedisStreamEventBus(builder.Configuration);
    builder.Services.AddTransactionalOutbox<AdminDbContext>();
    builder.Services.AddScoped<AdminExperienceEventHandler>();
    builder.Services.AddHostedService<ExperienceStreamConsumer<AdminExperienceEventHandler>>();
}
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

app.UseSecurityHeaders();
app.UseAuthentication();
app.UseMiddleware<StructuredRequestLoggingMiddleware>();
app.UseAuthorization();

app.MapStandardHealthChecks();
app.MapGet("/", () => Results.Ok(new
{
    service = "FPTU Xperience Admin Service",
    apiVersion = "v1",
    status = "running"
})).AllowAnonymous();
app.MapAdminEndpoints();
app.MapDeclarationEndpoints();
app.MapBenchmarkEndpoints();
app.MapBonusMatrixEndpoints();
app.MapQuestEndpoints();
app.MapAnomalyEndpoints();
app.MapSettingsEndpoints();
app.MapSemesterEndpoints();
app.MapSemesterRosterEndpoints();
app.MapAcademicCalendarEndpoints();
app.MapAcademicProfileEndpoints();
app.MapExperiencePolicyEndpoints();
app.MapWorkspaceCatalogEndpoints();
app.MapVerificationAssignmentEndpoints();
app.MapVerifiedContributionEndpoints();
app.MapManualAttendanceEndpoints();
app.MapPrivateEvidenceEndpoints();
app.MapClubExperienceStatisticsEndpoints();
app.MapStudentExperienceEndpoints();

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
