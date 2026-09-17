using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Errors;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Tracing;
using Microsoft.Extensions.DependencyInjection;

namespace AdminService.IntegrationTests;

public sealed class AdminApiTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    [Fact]
    public void AdminService_DoesNotRegisterJwtTokenIssuer()
    {
        using var scope = factory.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<JwtTokenFactory>());
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsStandard401()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorAsync(response, ApiErrorCodes.Unauthenticated);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("expired")]
    public async Task ProtectedEndpoint_WithInvalidOrExpiredToken_Returns401(string token)
    {
        using var client = CreateClient(token);
        var response = await client.GetAsync("/api/v1/admin/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorAsync(response, ApiErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task GeneralMeEndpoint_NormalizesAuthenticatedActor()
    {
        using var client = CreateClient("admin");
        var response = await client.GetAsync("/api/v1/me");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("101", json.GetProperty("subjectId").GetString());
        Assert.Equal(101, json.GetProperty("userId").GetInt32());
        Assert.DoesNotContain("token", json.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminEndpoint_WithAdminRole_Succeeds()
    {
        using var client = CreateClient("admin");
        var response = await client.GetAsync("/api/v1/admin/me?role=STUDENT_AFFAIRS_ADMIN");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("101", json.GetProperty("subjectId").GetString());
    }

    [Fact]
    public async Task StudentAffairsEndpoint_WithStudentAffairsRole_Succeeds()
    {
        using var client = CreateClient("student-affairs");
        var response = await client.GetAsync("/api/v1/student-affairs/me");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("202", json.GetProperty("subjectId").GetString());
    }

    [Fact]
    public async Task AdminEndpoint_WithStudentAffairsRole_Returns403()
    {
        using var client = CreateClient("student-affairs");
        var response = await client.GetAsync("/api/v1/admin/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertErrorAsync(response, ApiErrorCodes.Forbidden);
    }

    [Fact]
    public async Task StudentAffairsEndpoint_WithAdminRole_Returns403()
    {
        using var client = CreateClient("admin");
        var response = await client.GetAsync("/api/v1/student-affairs/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertErrorAsync(response, ApiErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ForgedActorBody_FromStudentAffairs_IsDeniedWithoutMutation()
    {
        using var client = CreateClient("student-affairs");
        var before = factory.CountAuditEvents();
        var response = await client.PostAsJsonAsync("/__test/audited-operation", new
        {
            actorId = "101",
            userId = "101",
            actorRole = "ADMIN",
            role = "ADMIN",
            action = "ACCOUNT_UPDATED",
            resourceType = "ACCOUNT"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, factory.CountAuditEvents());
    }

    [Fact]
    public async Task ForgedActorQueryAndHeader_CannotElevatePrivilege()
    {
        using var client = CreateClient("student-affairs");
        client.DefaultRequestHeaders.Add("X-Actor-Role", "ADMIN");
        var response = await client.GetAsync("/api/v1/admin/me?role=ADMIN&actorId=101");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InvalidBody_ReturnsStandard400()
    {
        using var client = CreateClient("admin");
        var response = await client.PostAsJsonAsync("/__test/audited-operation", new
        {
            action = "",
            resourceType = "",
            outcome = "INVALID"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await AssertErrorAsync(response, ApiErrorCodes.Validation);
        Assert.NotEmpty(error.GetProperty("details").EnumerateArray());
    }

    [Fact]
    public async Task MissingResource_ReturnsStandard404()
    {
        using var client = CreateClient("admin");
        var response = await client.GetAsync($"/api/v1/admin/audit-events/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertErrorAsync(response, ApiErrorCodes.NotFound);
    }

    [Fact]
    public async Task UnexpectedException_ReturnsSanitized500()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/__test/unexpected-error");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(ApiErrorCodes.Internal, body);
        Assert.DoesNotContain("Sensitive test exception detail", body);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DependencyFailure_ReturnsStandard502()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/__test/dependency-failure");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        await AssertErrorAsync(response, ApiErrorCodes.DependencyFailure);
    }

    [Fact]
    public async Task MissingCorrelationId_GeneratesMatchingHeaderAndErrorValue()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/me");
        var error = await AssertErrorAsync(response, ApiErrorCodes.Unauthenticated);
        var header = response.Headers.GetValues(CorrelationIdConstants.HeaderName).Single();

        Assert.False(string.IsNullOrWhiteSpace(header));
        Assert.Equal(header, error.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task SuppliedCorrelationId_IsReturnedUnchanged()
    {
        using var client = CreateClient("admin");
        client.DefaultRequestHeaders.Add(CorrelationIdConstants.HeaderName, "test-correlation-id");
        var response = await client.GetAsync("/api/v1/admin/me");

        Assert.Equal(
            "test-correlation-id",
            response.Headers.GetValues(CorrelationIdConstants.HeaderName).Single());
    }

    [Theory]
    [InlineData("contains a space")]
    [InlineData("contains/slash")]
    public async Task UnsafeCorrelationId_IsReplaced(string unsafeValue)
    {
        using var client = CreateClient("admin");
        client.DefaultRequestHeaders.Add(CorrelationIdConstants.HeaderName, unsafeValue);
        var response = await client.GetAsync("/api/v1/admin/me");
        var returned = response.Headers.GetValues(CorrelationIdConstants.HeaderName).Single();

        Assert.NotEqual(unsafeValue, returned);
        Assert.Matches("^[A-Za-z0-9._-]{1,128}$", returned);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_WithAvailableDatabase_Return200(string path)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuditActor_AlwaysComesFromAuthenticatedPrincipal()
    {
        using var client = CreateClient("admin");
        client.DefaultRequestHeaders.Add(CorrelationIdConstants.HeaderName, "audit-correlation-id");
        var response = await client.PostAsJsonAsync("/__test/audited-operation", new
        {
            actorId = "forged-user-id",
            userId = "forged-user-id",
            actorRole = "STUDENT_AFFAIRS_ADMIN",
            role = "STUDENT_AFFAIRS_ADMIN",
            performedBy = "forged-user-id",
            action = "ACCOUNT_UPDATED",
            resourceType = "ACCOUNT",
            resourceId = "42"
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("101", json.GetProperty("actorSubjectId").GetString());
        Assert.Equal("audit-correlation-id", json.GetProperty("correlationId").GetString());
        Assert.Equal(TimeSpan.Zero, json.GetProperty("timestampUtc").GetDateTimeOffset().Offset);
        Assert.Equal("101", factory.GetLatestAuditActorSubjectId());
    }

    [Fact]
    public async Task Pagination_UsesDefaultsAndRejectsOver100()
    {
        using var client = CreateClient("admin");
        var valid = await client.GetAsync("/api/v1/admin/audit-events");
        var validJson = await valid.Content.ReadFromJsonAsync<JsonElement>();
        var invalid = await client.GetAsync("/api/v1/admin/audit-events?page=1&pageSize=101");

        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(1, validJson.GetProperty("pagination").GetProperty("page").GetInt32());
        Assert.Equal(20, validJson.GetProperty("pagination").GetProperty("pageSize").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await AssertErrorAsync(invalid, ApiErrorCodes.Validation);
    }

    [Fact]
    public async Task OpenApi_DeclaresV1BearerErrorsAndCorrelationHeader()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("v1", document.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal(
            "FPTU Xperience Admin API",
            document.GetProperty("info").GetProperty("title").GetString());
        Assert.True(document.GetProperty("components").GetProperty("securitySchemes")
            .TryGetProperty("Bearer", out _));
        var operation = document.GetProperty("paths")
            .GetProperty("/api/v1/admin/me")
            .GetProperty("get");
        Assert.NotEmpty(operation.GetProperty("security").EnumerateArray());
        Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("403", out _));
        Assert.Contains("ApiErrorEnvelope", operation.GetProperty("responses").ToString());
        Assert.Contains(CorrelationIdConstants.HeaderName, document.ToString());
    }

    private HttpClient CreateClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> AssertErrorAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = json.GetProperty("error");
        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("correlationId").GetString()));
        return error;
    }
}
