using ClubReportHub.Shared.Security;
using ClubReportHub.Shared.Tracing;

namespace ActivityService.Extensions;

public static class ApplicationPipelineExtensions
{
    public static WebApplication UseActivityServicePipeline(
        this WebApplication app)
    {
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

        return app;
    }
}
