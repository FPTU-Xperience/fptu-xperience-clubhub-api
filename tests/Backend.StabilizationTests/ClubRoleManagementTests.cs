using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Models;
using ClubService.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.StabilizationTests;
public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    internal static async Task<WebApplication> CreateRolesAppAsync(string? connection=null)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});builder.WebHost.UseTestServer();builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Jwt:Issuer"]="ClubReportHub",["Jwt:Audience"]="ClubReportHub.Client",["Jwt:SigningKey"]=SigningKey});var name=Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ClubDbContext>(o=>{if(connection is null)o.UseInMemoryDatabase(name);else o.UseSqlServer(connection,sql=>sql.EnableRetryOnFailure());});builder.Services.AddClubReportJwtValidation(builder.Configuration,builder.Environment);
        var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapClubRoleEndpoints();
        await using(var scope=app.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<ClubDbContext>();if(connection is not null)await db.Database.MigrateAsync();var club=new Club{Code="ROLES",Name="Roles Club",CampusCode="HAN"};club.ManagerAssignments.Add(new ClubManagerAssignment{ManagerUserId=100,ManagerName="Owner"});foreach(var id in new[]{100,104,105,106})club.Memberships.Add(new ClubMembership{UserId=id,FullName=$"Student {id}",Email=$"{id}@example.test",Status=ClubMembershipStatuses.Approved});db.Add(club);await db.SaveChangesAsync();}await app.StartAsync();return app;
    }
    [Fact]
    public async Task Roles_ReadVsWriteAssignmentRevocationAndOwnerProtectionPersist()
    {
        await using var app=await CreateRolesAppAsync();using var owner=CreateAdminContractClient(app,100,AuthRoles.ClubMember);using var member=CreateAdminContractClient(app,104,AuthRoles.ClubMember);using var outsider=CreateAdminContractClient(app,999,AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.Forbidden,(await outsider.GetAsync("/api/clubs/1/roles")).StatusCode);var read=await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles");Assert.False(read.GetProperty("capabilities").GetProperty("canManageRoles").GetBoolean());Assert.DoesNotContain("email",read.GetRawText());
        var data=await owner.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles");var target=data.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("userId").GetInt32()==104);var id=target.GetProperty("memberId").GetInt32();
        var body=new{role="TREASURER",expectedVersion=target.GetProperty("version").GetGuid(),expectedClubVersion=data.GetProperty("version").GetGuid(),reason="Assign finance responsibility"};
        member.DefaultRequestHeaders.Add("Idempotency-Key","unauthorized");Assert.Equal(HttpStatusCode.Forbidden,(await member.PatchAsJsonAsync($"/api/clubs/1/roles/{id}",body)).StatusCode);
        owner.DefaultRequestHeaders.Add("Idempotency-Key","assign");var response=await owner.PatchAsJsonAsync($"/api/clubs/1/roles/{id}",body);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(HttpStatusCode.OK,(await owner.PatchAsJsonAsync($"/api/clubs/1/roles/{id}",body)).StatusCode);
        var saved=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("TREASURER",saved.GetProperty("role").GetString());Assert.NotEqual(body.expectedVersion,saved.GetProperty("version").GetGuid());
        owner.DefaultRequestHeaders.Remove("Idempotency-Key");owner.DefaultRequestHeaders.Add("Idempotency-Key","stale");Assert.Equal(HttpStatusCode.Conflict,(await owner.PatchAsJsonAsync($"/api/clubs/1/roles/{id}",body)).StatusCode);
        owner.DefaultRequestHeaders.Remove("Idempotency-Key");owner.DefaultRequestHeaders.Add("Idempotency-Key","revoke");var revoke=new{role="MEMBER",expectedVersion=saved.GetProperty("version").GetGuid(),expectedClubVersion=saved.GetProperty("clubVersion").GetGuid(),reason="Revoke finance responsibility"};Assert.Equal(HttpStatusCode.OK,(await owner.PatchAsJsonAsync($"/api/clubs/1/roles/{id}",revoke)).StatusCode);
        data=await owner.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles");var protectedOwner=data.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("userId").GetInt32()==100);Assert.False(protectedOwner.GetProperty("canRevokeTreasurer").GetBoolean());owner.DefaultRequestHeaders.Remove("Idempotency-Key");owner.DefaultRequestHeaders.Add("Idempotency-Key","demote-owner");Assert.Equal(HttpStatusCode.Conflict,(await owner.PatchAsJsonAsync($"/api/clubs/1/roles/{protectedOwner.GetProperty("memberId").GetInt32()}",new{role="MEMBER",expectedVersion=protectedOwner.GetProperty("version").GetGuid(),expectedClubVersion=data.GetProperty("version").GetGuid(),reason="Must not demote last owner"})).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ClubDbContext>();Assert.True((await db.ClubManagerAssignments.SingleAsync()).IsActive);Assert.Equal("MEMBER",(await db.ClubMemberships.SingleAsync(x=>x.UserId==104)).Role);Assert.Equal(2,await db.OutboxMessages.CountAsync(x=>x.EventType=="club.access.invalidated"));
        var assignment=await db.ClubManagerAssignments.SingleAsync();assignment.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Forbidden,(await owner.PatchAsJsonAsync($"/api/clubs/1/roles/{id}",body)).StatusCode);
    }
}
public sealed partial class ClubManagerSqlIntegrityTests
{
    [ClubManagerSqlFact]
    public async Task Roles_SqlConcurrentAssignmentsRespectVersionsAndTwoTreasurerLimit()
    {
        await using var database=await ManagerSqlDatabase.CreateAsync();await using var app=await CrossServiceWorkflowAuthorizationTests.CreateRolesAppAsync(database.ConnectionString);using var first=CrossServiceWorkflowAuthorizationTests.FormationClient(app,100,AuthRoles.ClubMember,"HAN");using var second=CrossServiceWorkflowAuthorizationTests.FormationClient(app,100,AuthRoles.ClubMember,"HAN");
        var data=await first.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles");var targets=data.GetProperty("items").EnumerateArray().Where(x=>x.GetProperty("userId").GetInt32()!=100).ToArray();
        object Input(JsonElement row,Guid clubVersion)=>new{role="TREASURER",expectedVersion=row.GetProperty("version").GetGuid(),expectedClubVersion=clubVersion,reason="Concurrent treasury assignment"};
        first.DefaultRequestHeaders.Add("Idempotency-Key","assign-a");second.DefaultRequestHeaders.Add("Idempotency-Key","assign-b");var results=await Task.WhenAll(first.PatchAsJsonAsync($"/api/clubs/1/roles/{targets[0].GetProperty("memberId").GetInt32()}",Input(targets[0],data.GetProperty("version").GetGuid())),second.PatchAsJsonAsync($"/api/clubs/1/roles/{targets[1].GetProperty("memberId").GetInt32()}",Input(targets[1],data.GetProperty("version").GetGuid())));Assert.Single(results,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Conflict);
        data=await first.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles");var next=data.GetProperty("items").EnumerateArray().First(x=>x.GetProperty("canAssignTreasurer").GetBoolean());first.DefaultRequestHeaders.Remove("Idempotency-Key");first.DefaultRequestHeaders.Add("Idempotency-Key","assign-next");Assert.Equal(HttpStatusCode.OK,(await first.PatchAsJsonAsync($"/api/clubs/1/roles/{next.GetProperty("memberId").GetInt32()}",Input(next,data.GetProperty("version").GetGuid()))).StatusCode);
        data=await first.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles");next=data.GetProperty("items").EnumerateArray().First(x=>x.GetProperty("role").GetString()=="MEMBER");first.DefaultRequestHeaders.Remove("Idempotency-Key");first.DefaultRequestHeaders.Add("Idempotency-Key","over-limit");Assert.Equal(HttpStatusCode.Conflict,(await first.PatchAsJsonAsync($"/api/clubs/1/roles/{next.GetProperty("memberId").GetInt32()}",Input(next,data.GetProperty("version").GetGuid()))).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ClubDbContext>();Assert.Equal(2,await db.ClubMemberships.CountAsync(x=>x.Role=="TREASURER"));
    }
}
