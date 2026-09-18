using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Tracing;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("yarp.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"yarp.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
builder.Services.AddClubReportJwt(builder.Configuration, builder.Environment);
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (!builder.Environment.IsProduction())
        {
            allowedOrigins = allowedOrigins
                .Concat([
                    "http://localhost:3000",
                    "http://localhost:3001",
                    "http://localhost:5173",
                    "http://127.0.0.1:3000",
                    "http://127.0.0.1:5173"
                ])
                .ToArray();
        }

        policy.WithOrigins(allowedOrigins
                  .Where(origin => !string.IsNullOrWhiteSpace(origin))
                  .Select(origin => origin.Trim().TrimEnd('/'))
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCorrelationId();
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { service = "YARP API Gateway", status = "running" }));

app.MapReverseProxy();

await app.RunAsync();
