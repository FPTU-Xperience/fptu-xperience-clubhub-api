using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    private const string BookingApi = "/api/activity-bookings";
    private const string HanoiBooking = BookingApi + "/campuses/HAN";

    [Fact]
    public Task ActivityBooking_ScopesRequestsAndReturnsIndependentVenueAndEquipmentResults() =>
        VerifyBookingWorkflow(null);

    [Fact]
    public void ActivityBooking_ModelMatchesSqlServerMigration()
    {
        using var db = new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>()
            .UseSqlServer("Server=localhost;Database=ActivityBookingModelTest;Trusted_Connection=True").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task ActivityBooking_ImportsFloorRoomsAndPreservesExistingBookingIds()
    {
        await using var app = await CreateManagerSessionAppAsync(register: false, booking: true);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var reviewer = CreateAdminContractClient(app, 102, AuthRoles.StudentAffairsAdmin);
        using var outsider = CreateAdminContractClient(app, 106, AuthRoles.StudentAffairsAdmin);
        const string planPath = HanoiBooking + "/layout";
        const string catalogPath = HanoiBooking + "/catalog";
        const string bookingPath = BookingApi + "/sessions/1";
        const string planArea = "10,60 18.25,60 18.25,66 10,66";
        var rooms = new[]
        {
            new { code = "031", name = "Phòng 031", type = "Phòng", capacity = 25, isBookable = true, planX = 12, planY = 63, planArea }
        };
        var plan = new { building = "A", floor = "G", imageUrl = "https://assets.example.invalid/ag.jpg", rooms };

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync(planPath, plan)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PutAsJsonAsync(planPath, plan)).StatusCode);
        await BookingJson(await reviewer.PutAsJsonAsync(planPath, plan));

        var catalog = await manager.GetFromJsonAsync<JsonElement>(catalogPath);
        var room = Assert.Single(catalog.GetProperty("venues").EnumerateArray());
        var venueId = room.GetProperty("id").GetInt32();
        Assert.Equal("031", room.GetProperty("code").GetString());
        Assert.Equal("G", room.GetProperty("floor").GetString());
        Assert.Equal(planArea, room.GetProperty("planArea").GetString());
        var state = await manager.GetFromJsonAsync<JsonElement>(bookingPath);
        var booking = await BookingJson(await manager.PostAsJsonAsync(bookingPath,
            new { sessionVersion = state.GetProperty("sessionVersion").GetGuid(), venueId }));
        Assert.Equal(venueId, booking.GetProperty("requestedVenueId").GetInt32());

        await BookingJson(await reviewer.PutAsJsonAsync(planPath, new
        {
            plan.building,
            plan.floor,
            plan.imageUrl,
            rooms = rooms.Select(x => new { x.code, name = "New import name", x.type, capacity = 0, isBookable = false, planX = 15, x.planY })
        }));
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.PutAsJsonAsync(planPath,
            new { building = "B", plan.floor, plan.imageUrl, rooms })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.PutAsJsonAsync(HanoiBooking + "/venues", new
        {
            code = "031",
            name = "Phòng 031",
            type = "Phòng",
            building = "B",
            floor = "G",
            capacity = 0,
            planX = 15,
            planY = 45,
            createOnly = true
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.PutAsJsonAsync(planPath, new
        {
            plan.building,
            plan.floor,
            plan.imageUrl,
            rooms = rooms.Select(x => new { x.code, x.name, x.type, x.planX })
        })).StatusCode);

        catalog = await manager.GetFromJsonAsync<JsonElement>(catalogPath);
        room = Assert.Single(catalog.GetProperty("venues").EnumerateArray());
        Assert.Single(catalog.GetProperty("layouts").EnumerateArray());
        Assert.Equal(venueId, room.GetProperty("id").GetInt32());
        Assert.Equal("Phòng 031", room.GetProperty("name").GetString());
        Assert.Equal(25, room.GetProperty("capacity").GetInt32());
        Assert.Equal(15, room.GetProperty("planX").GetInt32());
        Assert.Equal(planArea, room.GetProperty("planArea").GetString());
        Assert.True(room.GetProperty("isBookable").GetBoolean());
    }

    internal static async Task VerifyBookingWorkflow(string? connection)
    {
        await using var app = await CreateManagerSessionAppAsync(connection, register: false, booking: true);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var reviewer = CreateAdminContractClient(app, 102, AuthRoles.StudentAffairsAdmin);
        using var outsider = CreateAdminContractClient(app, 106, AuthRoles.StudentAffairsAdmin);
        using var member = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        const string firstPath = BookingApi + "/sessions/1";
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(firstPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(HanoiBooking + "/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(HanoiBooking + "/queue?weekOf=2026-10-12")).StatusCode);

        await BookingJson(await reviewer.PutAsJsonAsync(HanoiBooking + "/cutoff", new { day = 5, time = "17:00:00" }));
        var venue = await BookingJson(await reviewer.PutAsJsonAsync(HanoiBooking + "/venues", new
        {
            code = "R101",
            name = "Seminar Room",
            type = "Seminar",
            building = "A",
            floor = "1",
            capacity = 30,
            isBookable = true,
            planX = 25,
            planY = 50
        }));
        var venueId = venue.GetProperty("id").GetInt32();
        await BookingJson(await reviewer.PutAsJsonAsync(HanoiBooking + "/layout", new
        {
            building = "A",
            floor = "1",
            imageUrl = "https://assets.example.invalid/a1.png"
        }));
        var hall = await BookingJson(await reviewer.PutAsJsonAsync(HanoiBooking + "/venues", new
        {
            code = "HT-A",
            name = "Hall A",
            type = "Auditorium",
            building = "A",
            floor = "1",
            capacity = 100,
            isBookable = true,
            planArea = "10,10 30,10 30,20 10,20",
            planAreasByFloor = new Dictionary<string, string> { ["2"] = "10,10 30,10 30,20 10,20" }
        }));
        var hallId = hall.GetProperty("id").GetInt32();
        Assert.True(hall.GetProperty("planAreasByFloor").TryGetProperty("2", out _));
        var venueIds = new[] { venueId, hallId };
        var device = await BookingJson(await reviewer.PutAsJsonAsync(HanoiBooking + "/equipment",
            new { code = "MIC", name = "Microphone", quantity = 3, isBookable = true }));
        var equipmentId = device.GetProperty("id").GetInt32();
        var catalog = await manager.GetFromJsonAsync<JsonElement>(HanoiBooking + "/catalog");
        Assert.Equal(1, catalog.GetProperty("layouts").GetArrayLength());

        int secondId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var first = await db.Sessions.SingleAsync();
            first.Location = "Original venue";
            var second = new ActivitySession
            {
                ActivityId = first.ActivityId,
                SemesterId = first.SemesterId,
                Location = "Second venue",
                StartTimeUtc = first.StartTimeUtc.AddMinutes(10),
                EndTimeUtc = first.EndTimeUtc.AddMinutes(10),
                OpensAt = first.OpensAt.AddMinutes(10),
                ClosesAt = first.ClosesAt.AddMinutes(10),
                Latitude = first.Latitude,
                Longitude = first.Longitude,
                RadiusMeters = first.RadiusMeters,
                ScoringSnapshotJson = first.ScoringSnapshotJson
            };
            db.Sessions.Add(second);
            await db.SaveChangesAsync();
            secondId = second.Id;
        }
        var secondPath = $"{BookingApi}/sessions/{secondId}";
        var firstState = await manager.GetFromJsonAsync<JsonElement>(firstPath);
        var cutoff = firstState.GetProperty("cutoffAtUtc").GetDateTimeOffset();
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 17, 0, 0, TimeSpan.FromHours(7)), cutoff);
        Assert.True(firstState.GetProperty("isLate").GetBoolean());
        var firstRequest = new
        {
            sessionVersion = firstState.GetProperty("sessionVersion").GetGuid(),
            venueIds,
            equipment = new[] { new { equipmentId, quantity = 3 } }
        };
        var firstBooking = await BookingJson(await manager.PostAsJsonAsync(firstPath, firstRequest));
        Assert.Equal(venueIds, firstBooking.GetProperty("requestedVenueIds").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync(firstPath, firstRequest)).StatusCode);
        var secondState = await manager.GetFromJsonAsync<JsonElement>(secondPath);
        var secondBooking = await BookingJson(await manager.PostAsJsonAsync(secondPath, new
        {
            sessionVersion = secondState.GetProperty("sessionVersion").GetGuid(),
            venueId,
            equipment = new[] { new { equipmentId, quantity = 2 } }
        }));
        var queue = await reviewer.GetFromJsonAsync<JsonElement>(HanoiBooking + "/queue?weekOf=2026-10-12");
        Assert.Equal(2, queue.GetArrayLength());
        string Decision(JsonElement booking) => $"{BookingApi}/{booking.GetProperty("id").GetInt32()}/decision";
        object Allocation(JsonElement booking, int? room, int quantity) => new
        {
            version = booking.GetProperty("version").GetGuid(),
            venueId = room,
            equipment = new[] { new { equipmentId, quantity } }
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PutAsJsonAsync(Decision(firstBooking), Allocation(firstBooking, venueId, 2))).StatusCode);
        var decision = new
        {
            version = firstBooking.GetProperty("version").GetGuid(),
            venueIds,
            equipment = new[] { new { equipmentId, quantity = 2 } }
        };
        var firstDecision = reviewer.PutAsJsonAsync(Decision(firstBooking), decision);
        JsonElement confirmed;
        if (connection is null) confirmed = await BookingJson(await firstDecision);
        else
        {
            var decisions = await Task.WhenAll(firstDecision, reviewer.PutAsJsonAsync(Decision(firstBooking), decision));
            Assert.Single(decisions, response => response.StatusCode == HttpStatusCode.Conflict);
            confirmed = await BookingJson(Assert.Single(decisions, response => response.StatusCode == HttpStatusCode.OK));
        }
        Assert.Equal("CONFIRMED", confirmed.GetProperty("venueStatus").GetString());
        Assert.Equal(1, confirmed.GetProperty("equipment")[0].GetProperty("missingQuantity").GetInt32());
        var availability = await reviewer.GetFromJsonAsync<JsonElement>($"{BookingApi}/{secondBooking.GetProperty("id").GetInt32()}/availability");
        Assert.Equal(venueIds, availability.GetProperty("reservedVenueIds").EnumerateArray().Select(x => x.GetInt32()).Order().ToArray());
        Assert.Equal(1, availability.GetProperty("availableEquipment").GetProperty(equipmentId.ToString()).GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.PutAsJsonAsync(Decision(secondBooking), Allocation(secondBooking, venueId, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.PutAsJsonAsync(Decision(secondBooking), Allocation(secondBooking, hallId, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.PutAsJsonAsync(Decision(secondBooking), Allocation(secondBooking, null, 2))).StatusCode);
        var partial = await BookingJson(await reviewer.PutAsJsonAsync(Decision(secondBooking), Allocation(secondBooking, null, 1)));
        Assert.Equal("UNAVAILABLE", partial.GetProperty("venueStatus").GetString());
        Assert.Equal(1, partial.GetProperty("equipment")[0].GetProperty("confirmedQuantity").GetInt32());

        var updatedSession = await manager.GetFromJsonAsync<JsonElement>(firstPath);
        Assert.Equal("Seminar Room · Hall A", updatedSession.GetProperty("location").GetString());
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "edit-confirmed-booking-session");
        var locked = await manager.PatchAsJsonAsync("/api/activities/1/sessions/1",
            new UpdateActivitySessionRequest(updatedSession.GetProperty("sessionVersion").GetGuid(), Location: "New venue"));
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        var cancelled = await BookingJson(await manager.PostAsJsonAsync($"{firstPath}/{confirmed.GetProperty("id").GetInt32()}/cancel",
            new { version = confirmed.GetProperty("version").GetGuid() }));
        Assert.Equal("CANCELLED", cancelled.GetProperty("status").GetString());
        var freed = await reviewer.GetFromJsonAsync<JsonElement>($"{BookingApi}/{secondBooking.GetProperty("id").GetInt32()}/availability");
        Assert.Empty(freed.GetProperty("reservedVenueIds").EnumerateArray());
        var restored = await manager.GetFromJsonAsync<JsonElement>(firstPath);
        Assert.Equal("Original venue", restored.GetProperty("location").GetString());

        var newBooking = await BookingJson(await manager.PostAsJsonAsync(firstPath, new
        {
            sessionVersion = restored.GetProperty("sessionVersion").GetGuid(),
            equipment = new[] { new { equipmentId, quantity = 3 } }
        }));
        var liveSecond = await manager.GetFromJsonAsync<JsonElement>(secondPath);
        manager.DefaultRequestHeaders.Remove("Idempotency-Key");
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "cancel-booked-session");
        await BookingJson(await manager.PostAsJsonAsync($"/api/activities/1/sessions/{secondId}/cancel",
            new SessionDecisionRequest(liveSecond.GetProperty("sessionVersion").GetGuid(), "Session cancelled.")));
        var released = await BookingJson(await reviewer.PutAsJsonAsync(Decision(newBooking), Allocation(newBooking, null, 3)));
        Assert.Equal("NOT_REQUESTED", released.GetProperty("venueStatus").GetString());
        Assert.Equal(0, released.GetProperty("equipment")[0].GetProperty("missingQuantity").GetInt32());
        var cancelledSession = await manager.GetFromJsonAsync<JsonElement>(secondPath);
        Assert.Equal("CANCELLED", cancelledSession.GetProperty("bookings")[0].GetProperty("status").GetString());
    }

    private static async Task<JsonElement> BookingJson(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}

public sealed partial class ClubManagerSqlIntegrityTests
{
    [ClubManagerSqlFact]
    public async Task ActivityBooking_SqlServerMigrationSupportsAllocationAndCancellation()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await CrossServiceWorkflowAuthorizationTests.VerifyBookingWorkflow(database.ConnectionString);
    }
}
