using System.Threading.RateLimiting;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Cors;
using ClubReportHub.Shared.Health;
using ClubReportHub.Shared.RateLimiting;
using ClubReportHub.Shared.Security;
using ClubReportHub.Shared.Tracing;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("yarp.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"yarp.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

builder.Services.ConfigureTrustedForwardedHeaders(builder.Configuration);
builder.Services.AddClubReportJwt(builder.Configuration, builder.Environment);

var authServiceUrl = builder.Configuration.GetValue<string>("Services:AuthServiceUrl");
if (!string.IsNullOrWhiteSpace(authServiceUrl) && Uri.TryCreate(authServiceUrl, UriKind.Absolute, out var authUri))
{
    builder.Services.AddRemoteUserSecurityStampValidator(authUri);
}

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

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = RateLimitingExtensions.CreateRateLimitRejectedHandler();

    // 1. Auth Login Policy (Google login, dev login)
    var authLoginLimit = builder.Configuration.GetValue("RateLimiting:AuthLogin:PermitLimit", 60);
    var authLoginWindow = builder.Configuration.GetValue("RateLimiting:AuthLogin:WindowSeconds", 60);
    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitingExtensions.ResolveClientKey(httpContext),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = authLoginLimit,
                Window = TimeSpan.FromSeconds(authLoginWindow),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // 2. Auth Refresh Policy
    var authRefreshLimit = builder.Configuration.GetValue("RateLimiting:AuthRefresh:PermitLimit", 30);
    var authRefreshWindow = builder.Configuration.GetValue("RateLimiting:AuthRefresh:WindowSeconds", 60);
    options.AddPolicy("auth-refresh", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitingExtensions.ResolveClientKey(httpContext),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = authRefreshLimit,
                Window = TimeSpan.FromSeconds(authRefreshWindow),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // 3. Upload Policy
    var uploadLimit = builder.Configuration.GetValue("RateLimiting:Upload:PermitLimit", 30);
    var uploadWindow = builder.Configuration.GetValue("RateLimiting:Upload:WindowSeconds", 60);
    options.AddPolicy("upload", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitingExtensions.ResolveClientKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = uploadLimit,
                Window = TimeSpan.FromSeconds(uploadWindow),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // 4. Export Policy
    var exportLimit = builder.Configuration.GetValue("RateLimiting:Export:PermitLimit", 20);
    var exportWindow = builder.Configuration.GetValue("RateLimiting:Export:WindowSeconds", 60);
    options.AddPolicy("export", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitingExtensions.ResolveClientKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = exportLimit,
                Window = TimeSpan.FromSeconds(exportWindow),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // 5. General API Policy
    var generalLimit = builder.Configuration.GetValue("RateLimiting:GeneralApi:PermitLimit", 300);
    var generalWindow = builder.Configuration.GetValue("RateLimiting:GeneralApi:WindowSeconds", 60);
    options.AddPolicy("general-api", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitingExtensions.ResolveClientKey(httpContext),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = generalLimit,
                Window = TimeSpan.FromSeconds(generalWindow),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // 6. Global Limiter (exempts health checks and root status)
    var globalLimit = builder.Configuration.GetValue("RateLimiting:Global:PermitLimit", 600);
    var globalWindow = builder.Configuration.GetValue("RateLimiting:Global:WindowSeconds", 60);
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) ||
            httpContext.Request.Path == "/")
        {
            return RateLimitPartition.GetNoLimiter("no-limiter");
        }

        return RateLimitPartition.GetSlidingWindowLimiter(
            RateLimitingExtensions.ResolveClientKey(httpContext),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = globalLimit,
                Window = TimeSpan.FromSeconds(globalWindow),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        builderContext.AddRequestHeaderRemove("X-Combined-Report-Workflow");
    });
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCorrelationId();
app.UseForwardedHeaders();
app.UseSecurityHeaders();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapStandardHealthChecks();
app.MapGet("/", () => Results.Ok(new { service = "YARP API Gateway", status = "running" }));

app.MapReverseProxy();

await app.RunAsync();
