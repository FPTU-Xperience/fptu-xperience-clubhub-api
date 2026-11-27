using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ActivityService.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.StabilizationTests;

public sealed partial class ActivityResourceContractTests
{
    private static async Task<HttpResponseMessage> UploadFile(HttpClient client, string key, string visibility = "CLUB", string bytes = "%PDF-1.4\n%%EOF")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/activities/1/resources/upload");
        request.Headers.Add("Idempotency-Key", key);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(bytes)); file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "resource.pdf"); form.Add(new StringContent("DOCUMENT"), "kind");
        form.Add(new StringContent("Workshop handout"), "title"); form.Add(new StringContent(visibility), "visibility");
        request.Content = form; return await client.SendAsync(request);
    }

    [Fact]
    public async Task UploadPersistsPrivateBytesChecksLiveAuthorityAndDetachRemovesContent()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200); using var member = f.Client(100); using var visitor = f.Client(101);
        var response = await UploadFile(manager, "upload-one"); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await Json(response); var url = saved.GetProperty("url").GetString()!;
        Assert.Equal("DOCUMENT", saved.GetProperty("kind").GetString());
        Assert.Equal(saved.GetRawText(), (await Json(await UploadFile(manager, "upload-one"))).GetRawText());
        Assert.Equal("%PDF-1.4\n%%EOF", await member.GetStringAsync(url));
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(url.Replace("/1/", "/2/"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadFile(manager, "bad-signature", bytes: "<html>fake.pdf</html>")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadFile(manager, "active-pdf", bytes: "%PDF-1.4 /JavaScript")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadFile(visitor, "visitor-upload")).StatusCode);
        var id = saved.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Delete, $"/api/activities/1/resources/{id}", new { expectedVersion = saved.GetProperty("version").GetGuid(), reason = "Outdated document" }, "remove-file")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(url)).StatusCode);
        await f.Db(async db =>
        {
            Assert.Empty(await db.Set<ActivityResourceFile>().ToListAsync());
            Assert.Equal(2, await db.Set<ActivityResourceAudit>().CountAsync(x => x.ResourceId == id));
        });
    }

    [Fact]
    public async Task PublicFileStillRequiresVisibleActivityAndLiveAccount()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200); using var visitor = f.Client(101);
        var row = await Json(await UploadFile(manager, "public-file", "PUBLIC"));
        var url = row.GetProperty("url").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(url)).StatusCode);
        f.AuthorityAvailable = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await visitor.GetAsync(url)).StatusCode);
    }
}
