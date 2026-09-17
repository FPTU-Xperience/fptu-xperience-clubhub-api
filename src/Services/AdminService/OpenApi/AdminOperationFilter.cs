using AdminService.Errors;
using ClubReportHub.Shared.Tracing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AdminService.OpenApi;

public sealed class AdminOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = CorrelationIdConstants.HeaderName,
            In = ParameterLocation.Header,
            Required = false,
            Description = "Optional safe correlation identifier (max 128 characters).",
            Schema = new OpenApiSchema { Type = "string", MaxLength = 128 }
        });

        foreach (var statusCode in new[] { "400", "401", "403", "404", "409", "422", "502", "503", "500" })
        {
            operation.Responses.TryAdd(statusCode, new OpenApiResponse
            {
                Description = "Standard API error",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new()
                    {
                        Schema = context.SchemaGenerator.GenerateSchema(
                            typeof(ApiErrorEnvelope), context.SchemaRepository)
                    }
                }
            });
        }

        if (!context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    }] = []
                }
            ];
        }
    }
}
