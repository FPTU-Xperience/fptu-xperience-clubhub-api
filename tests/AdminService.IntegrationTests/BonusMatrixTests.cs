using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Contracts;
using Xunit;

namespace AdminService.IntegrationTests;

public sealed class BonusMatrixTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetAllNodes_Anonymous_Returns200WithDefaultTiers()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/bonus-matrix");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<BonusMatrixSuccessResponse<List<BonusMatrixNodeResponse>>>(JsonOptions);
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotEmpty(result.Data);

        // Verify sorted by position ascending
        for (var i = 0; i < result.Data.Count - 1; i++)
        {
            Assert.True(result.Data[i].Position <= result.Data[i + 1].Position);
        }

        // Also verify /api/v1/bonus-matrix works identically
        var v1Response = await client.GetAsync("/api/v1/bonus-matrix");
        Assert.Equal(HttpStatusCode.OK, v1Response.StatusCode);
    }

    [Fact]
    public async Task CreateNode_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/bonus-matrix", new CreateBonusMatrixNodeRequest(65, "Thử nghiệm", 55));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateNode_StudentForbidden_Returns403()
    {
        using var client = CreateClient("student-1");

        var response = await client.PostAsJsonAsync("/api/bonus-matrix", new CreateBonusMatrixNodeRequest(65, "Thử nghiệm", 55));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateNode_Valid_Returns201Created()
    {
        using var client = CreateClient("admin");

        var request = new CreateBonusMatrixNodeRequest(75, "Ngưỡng khá", 70);
        var response = await client.PostAsJsonAsync("/api/bonus-matrix", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<BonusMatrixSuccessResponse<BonusMatrixNodeResponse>>(JsonOptions);
        Assert.NotNull(created);
        Assert.True(created.Success);
        Assert.Equal(75, created.Data.Position);
        Assert.Equal("Ngưỡng khá", created.Data.Label);
        Assert.Equal(70, created.Data.Multiplier);
        Assert.False(string.IsNullOrWhiteSpace(created.Data.Id));
    }

    [Fact]
    public async Task CreateNode_DefaultLabel_WhenEmpty()
    {
        using var client = CreateClient("student-affairs");

        var request = new CreateBonusMatrixNodeRequest(35, null, 85);
        var response = await client.PostAsJsonAsync("/api/bonus-matrix", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<BonusMatrixSuccessResponse<BonusMatrixNodeResponse>>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("Ngưỡng 35", created.Data.Label);
    }

    [Theory]
    [InlineData(-5, 50, "INVALID_POSITION")]
    [InlineData(205, 50, "INVALID_POSITION")]
    [InlineData(12, 50, "INVALID_POSITION")]
    [InlineData(60, -5, "INVALID_MULTIPLIER")]
    [InlineData(60, 205, "INVALID_MULTIPLIER")]
    [InlineData(60, 33, "INVALID_MULTIPLIER")]
    public async Task CreateNode_InvalidPositionOrMultiplier_Returns400(int position, int multiplier, string expectedCode)
    {
        using var client = CreateClient("admin");

        var request = new CreateBonusMatrixNodeRequest(position, "Test", multiplier);
        var response = await client.PostAsJsonAsync("/api/bonus-matrix", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<BonusMatrixErrorResponse>(JsonOptions);
        Assert.NotNull(error);
        Assert.False(error.Success);
        Assert.Equal(expectedCode, error.Error.Code);
    }

    [Fact]
    public async Task CreateNode_DuplicatePosition_Returns400_INVALID_POSITION()
    {
        using var client = CreateClient("admin");

        // 0 already exists in default seeds
        var request = new CreateBonusMatrixNodeRequest(0, "Duplicate", 100);
        var response = await client.PostAsJsonAsync("/api/bonus-matrix", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<BonusMatrixErrorResponse>(JsonOptions);
        Assert.NotNull(error);
        Assert.Equal("INVALID_POSITION", error.Error.Code);
    }

    [Fact]
    public async Task CreateNode_LabelTooLong_Returns400_INVALID_LABEL()
    {
        using var client = CreateClient("admin");

        var longLabel = new string('A', 61);
        var request = new CreateBonusMatrixNodeRequest(45, longLabel, 80);
        var response = await client.PostAsJsonAsync("/api/bonus-matrix", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<BonusMatrixErrorResponse>(JsonOptions);
        Assert.NotNull(error);
        Assert.Equal("INVALID_LABEL", error.Error.Code);
    }

    [Fact]
    public async Task UpdateNode_ValidAndInvalid_Flow()
    {
        using var client = CreateClient("admin");

        // 1. Create a node to update
        var createResponse = await client.PostAsJsonAsync(
            "/api/bonus-matrix",
            new CreateBonusMatrixNodeRequest(85, "Ngưỡng ban đầu", 65));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<BonusMatrixSuccessResponse<BonusMatrixNodeResponse>>(JsonOptions);
        Assert.NotNull(created);
        var id = created.Data.Id;

        // 2. Update with valid values
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/bonus-matrix/{id}",
            new UpdateBonusMatrixNodeRequest(90, "Ngưỡng cập nhật", 60));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<BonusMatrixSuccessResponse<BonusMatrixNodeResponse>>(JsonOptions);
        Assert.NotNull(updated);
        Assert.True(updated.Success);
        Assert.Equal(90, updated.Data.Position);
        Assert.Equal("Ngưỡng cập nhật", updated.Data.Label);
        Assert.Equal(60, updated.Data.Multiplier);

        // 3. Update with invalid step
        var invalidResponse = await client.PutAsJsonAsync(
            $"/api/bonus-matrix/{id}",
            new UpdateBonusMatrixNodeRequest(91, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        var error = await invalidResponse.Content.ReadFromJsonAsync<BonusMatrixErrorResponse>(JsonOptions);
        Assert.NotNull(error);
        Assert.Equal("INVALID_POSITION", error.Error.Code);

        // 4. Update non-existent ID -> 404 NOT_FOUND
        var notFoundResponse = await client.PutAsJsonAsync(
            $"/api/bonus-matrix/{Guid.NewGuid()}",
            new UpdateBonusMatrixNodeRequest(95, "Không tồn tại", 55));
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        var notFoundError = await notFoundResponse.Content.ReadFromJsonAsync<BonusMatrixErrorResponse>(JsonOptions);
        Assert.NotNull(notFoundError);
        Assert.Equal("NOT_FOUND", notFoundError.Error.Code);
    }

    [Fact]
    public async Task DeleteNode_CannotDeleteLastNode_Enforced()
    {
        using var client = CreateClient("admin");

        // Get current list of nodes
        var listResponse = await client.GetAsync("/api/bonus-matrix");
        var listResult = await listResponse.Content.ReadFromJsonAsync<BonusMatrixSuccessResponse<List<BonusMatrixNodeResponse>>>(JsonOptions);
        Assert.NotNull(listResult);
        var nodes = listResult.Data;

        // Delete down until only 1 remains
        while (nodes.Count > 1)
        {
            var target = nodes.Last();
            var delResponse = await client.DeleteAsync($"/api/bonus-matrix/{target.Id}");
            Assert.Equal(HttpStatusCode.OK, delResponse.StatusCode);

            var delMessage = await delResponse.Content.ReadFromJsonAsync<BonusMatrixMessageResponse>(JsonOptions);
            Assert.NotNull(delMessage);
            Assert.True(delMessage.Success);
            Assert.Equal("Đã xóa ngưỡng thành công.", delMessage.Message);

            nodes.RemoveAt(nodes.Count - 1);
        }

        Assert.Single(nodes);
        var lastNode = nodes[0];

        // Attempting to delete the last node must fail with CANNOT_DELETE_LAST_NODE
        var failResponse = await client.DeleteAsync($"/api/bonus-matrix/{lastNode.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, failResponse.StatusCode);

        var failError = await failResponse.Content.ReadFromJsonAsync<BonusMatrixErrorResponse>(JsonOptions);
        Assert.NotNull(failError);
        Assert.False(failError.Success);
        Assert.Equal("CANNOT_DELETE_LAST_NODE", failError.Error.Code);
        Assert.Equal("Không thể xóa. Phải có ít nhất 1 ngưỡng trong hệ thống.", failError.Error.Message);

        // Deleting non-existent node returns 404 NOT_FOUND
        var notFoundResp = await client.DeleteAsync($"/api/bonus-matrix/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        var notFoundErr = await notFoundResp.Content.ReadFromJsonAsync<BonusMatrixErrorResponse>(JsonOptions);
        Assert.NotNull(notFoundErr);
        Assert.Equal("NOT_FOUND", notFoundErr.Error.Code);
    }

    private HttpClient CreateClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
