using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

public static class ActivityBookingEndpoints
{
    private sealed record EquipmentRequest(int EquipmentId, int Quantity);
    private sealed record BookingRequest(Guid SessionVersion, int? VenueId, string? VenueType, EquipmentRequest[]? Equipment, int[]? VenueIds = null);
    private sealed record BookingDecision(Guid Version, int? VenueId, EquipmentRequest[]? Equipment, bool Cancel, int[]? VenueIds = null);
    private sealed record VenueInput(string Code, string Name, string Type, string? Building, string? Floor,
        int Capacity, bool IsBookable, int? PlanX, int? PlanY, bool CreateOnly = false, string? PlanArea = null,
        Dictionary<string, string>? PlanAreasByFloor = null);
    private sealed record EquipmentInput(string Code, string Name, int Quantity, bool IsBookable);
    private sealed record CutoffInput(int Day, TimeOnly Time);
    private sealed record MappedRoomInput(string Code, string Name, string Type, int Capacity, bool IsBookable, int? PlanX, int? PlanY,
        string? PlanArea = null, Dictionary<string, string>? PlanAreasByFloor = null);
    private sealed record LayoutInput(string? Building, string? Floor, string? ImageUrl, int? PlanX, int? PlanY,
        string? UploadedImageUrl = null, MappedRoomInput[]? Rooms = null, bool ReplaceGeometry = false);
    private sealed record CancelInput(Guid Version);

    public static void MapActivityBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = SafeGroup(app, "/api/activity-bookings");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (DbUpdateConcurrencyException)
            {
                return ApiOperations.Error(409, "STALE_VERSION", "Reload the booking or session.");
            }
        });
        group.MapGet("/sessions/{sessionId:int}", ForSession);
        group.MapPost("/sessions/{sessionId:int}", Submit);
        group.MapGet("/campuses/{campus}/catalog", Catalog);
        group.MapPut("/campuses/{campus}/venues", SaveVenue);
        group.MapPut("/campuses/{campus}/equipment", SaveEquipment);
        group.MapPut("/campuses/{campus}/cutoff", SaveCutoff);
        group.MapPut("/campuses/{campus}/layout", SaveLayout);
        group.MapGet("/campuses/{campus}/queue", Queue);
        group.MapGet("/{id:int}/availability", Availability);
        group.MapPut("/{id:int}/decision", Decide);
        group.MapPost("/sessions/{sessionId:int}/{id:int}/cancel", CancelByManager);
    }

    private static async Task<ActivitySession> LoadSession(int id, ActivityDbContext db, CancellationToken ct)
    {
        var session = await db.Sessions.Include(x => x.Activity).SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(session is not null, 404, "SESSION_NOT_FOUND", "Session not found.");
        return session!;
    }

    private static bool Bookable(ActivitySession session, HttpContext http) =>
        session.Mode == "ONSITE" && session.Status == "SCHEDULED" && session.StartTimeUtc > Now(http)
        && Mutable(session.Activity, http);

    private static async Task CampusAccess(string campus, HttpContext http, bool review, CancellationToken ct)
    {
        Require(CampusCodes.FiveCampuses.Contains(campus), 400, "INVALID_CAMPUS", "Use a physical campus code.");
        var actor = http.User.GetUserId();
        var account = (await http.RequestServices.GetRequiredService<CurrentUserProfileClient>()
            .GetAsync([actor], ct)).GetValueOrDefault(actor);
        Require(account is { IsActive: true, IsLocked: false }, 403, "ACCOUNT_UNAVAILABLE", "An active account is required.");
        Require(http.User.IsInRole(AuthRoles.Admin) ||
            account!.CampusCode == campus && (!review || http.User.IsInRole(AuthRoles.StudentAffairsAdmin)),
            403, "CAMPUS_SCOPE_DENIED", "Access to this campus is required.");
    }

    private static bool SameWindow(ActivityBooking row) => row.Session.Mode == "ONSITE"
        && row.StartsAtUtc == row.Session.StartTimeUtc && row.EndsAtUtc == row.Session.EndTimeUtc;

    private static string EffectiveStatus(ActivityBooking row, DateTimeOffset now)
    {
        if (row.Status == "CANCELLED" || row.Session.Status == "CANCELLED"
            || row.Session.Activity.Status == ActivityStatuses.Cancelled) return "CANCELLED";
        if (row.Status == "SESSION_CHANGED") return row.Status;
        if (!SameWindow(row)) return "SESSION_CHANGED";
        if (row.Status == "PENDING" && (row.StartsAtUtc <= now || row.Session.Status != "SCHEDULED"
            || row.Session.Activity.Status != ActivityStatuses.Scheduled)) return "EXPIRED";
        if (row.Status == "CONFIRMED" && row.StartsAtUtc > now && row.Session.Status != "SCHEDULED") return "CANCELLED";
        return row.Status;
    }

    private static object View(ActivityBooking booking, DateTimeOffset now)
    {
        var status = EffectiveStatus(booking, now);
        return new
        {
            booking.Id, booking.SessionId, booking.CampusCode, booking.RequestedVenueId, booking.RequestedVenueType,
            booking.ConfirmedVenueId, booking.StartsAtUtc, booking.EndsAtUtc, status, booking.Version,
            requestedVenueIds = VenueIds(booking.RequestedVenueId, booking.AdditionalVenues.Where(x => !x.IsConfirmed).Select(x => x.VenueId)),
            confirmedVenueIds = VenueIds(booking.ConfirmedVenueId, booking.AdditionalVenues.Where(x => x.IsConfirmed).Select(x => x.VenueId)),
            booking.RequestedAtUtc, booking.ProcessedAtUtc, booking.CutoffAtUtc,
            activityId = booking.Session.ActivityId,
            activityTitle = booking.Session.Activity.Title,
            clubName = booking.Session.Activity.ClubName,
            isLate = booking.RequestedAtUtc > booking.CutoffAtUtc,
            canManage = booking.StartsAtUtc > now && (status is "PENDING" or "CONFIRMED"),
            venueStatus = status is "CANCELLED" or "EXPIRED" or "SESSION_CHANGED" ? status
                : booking.ConfirmedVenueId.HasValue ? "CONFIRMED"
                : booking.RequestedVenueId.HasValue || !string.IsNullOrWhiteSpace(booking.RequestedVenueType)
                    ? status == "PENDING" ? "PENDING" : "UNAVAILABLE"
                    : "NOT_REQUESTED",
            equipment = booking.Equipment.Select(x => new
            {
                x.EquipmentId, x.RequestedQuantity, x.ConfirmedQuantity,
                missingQuantity = status == "CONFIRMED" ? x.RequestedQuantity - x.ConfirmedQuantity : null
            })
        };
    }

    private static int[] VenueIds(int? primary, IEnumerable<int> additional) =>
        primary.HasValue ? [primary.Value, .. additional] : [];

    private static int[] RequestedIds(int? legacy, int[]? ids)
    {
        var result = ids ?? (legacy.HasValue ? [legacy.Value] : []);
        Require((ids is null || !legacy.HasValue) && result.Length <= 200
            && result.All(id => id > 0) && result.Distinct().Count() == result.Length,
            400, "INVALID_VENUE", "Select distinct rooms.");
        return result;
    }

    private static DateOnly Monday(DateTimeOffset instant)
    {
        var day = DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(7)).DateTime);
        return day.AddDays(-((int)day.DayOfWeek + 6) % 7);
    }

    private static async Task<DateTimeOffset?> Cutoff(ActivityDbContext db, string campus, DateTimeOffset start, CancellationToken ct)
    {
        var policy = await db.Set<CampusBookingPolicy>().FindAsync([campus], ct);
        return policy is null ? null : new DateTimeOffset(
            Monday(start).AddDays(policy.CutoffDay - 8).ToDateTime(policy.CutoffTime), TimeSpan.FromHours(7));
    }

    internal static string RoomLocation(string[] names)
    {
        var location = string.Join(" · ", names);
        return location.Length <= 200 ? location : $"{names[0]} + {names.Length - 1} phòng";
    }

    internal static async Task AddApplicationBooking(ActivityDbContext db, ClubActivity activity,
        IReadOnlyList<ActivitySession> sessions, HttpContext http, CancellationToken ct)
    {
        var session = sessions.SingleOrDefault(x => x.Mode == "ONSITE" && x.StartTimeUtc == activity.StartTimeUtc
            && x.EndTimeUtc == activity.EndTimeUtc && x.StartTimeUtc > Now(http));
        if (session is null) return;

        var application = await (from execution in db.Set<ActivityExecution>()
            join proposal in db.Set<ActivityApplication>() on execution.ApplicationId equals proposal.Id
            where execution.ActivityId == activity.Id && proposal.Status == "APPROVED"
            select proposal).AsNoTracking().SingleOrDefaultAsync(ct);
        if (application is null) return;

        var content = JsonSerializer.Deserialize<ActivityApplicationInput>(application.ContentJson, Json);
        if (content?.VenueIds is not { Length: > 0 } ids
            || content.StartTimeUtc != session.StartTimeUtc || content.EndTimeUtc != session.EndTimeUtc) return;

        var submittedAt = await db.Set<ActivityApplicationHistory>().Where(x => x.ApplicationId == application.Id && x.Status == "SUBMITTED")
            .OrderByDescending(x => x.Version).Select(x => (DateTimeOffset?)x.CreatedAt).FirstOrDefaultAsync(ct);
        db.Add(new ActivityBooking
        {
            Session = session,
            CampusCode = application.CampusCode,
            RequestedVenueId = ids[0],
            AdditionalVenues = ids.Skip(1).Select(id => new ActivityBookingVenue { VenueId = id }).ToList(),
            StartsAtUtc = session.StartTimeUtc,
            EndsAtUtc = session.EndTimeUtc,
            CutoffAtUtc = await Cutoff(db, application.CampusCode, session.StartTimeUtc, ct),
            RequestedByUserId = application.CreatedBy,
            RequestedAtUtc = submittedAt ?? application.UpdatedAt
        });
    }

    private static async Task<IResult> ForSession(int sessionId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var session = await LoadSession(sessionId, db, ct);
        var scope = await Authorize(session.Activity.ClubId, http, ct);
        Manager(scope);
        var campus = scope.Club.CampusCode;
        var rows = await db.Set<ActivityBooking>().AsNoTracking().Include(x => x.Equipment).Include(x => x.AdditionalVenues)
            .Include(x => x.Session).ThenInclude(x => x.Activity)
            .Where(x => x.SessionId == sessionId).OrderByDescending(x => x.Id).ToListAsync(ct);
        var now = Now(http);
        var cutoff = await Cutoff(db, campus, session.StartTimeUtc, ct);
        return Results.Ok(new
        {
            campusCode = campus,
            sessionVersion = session.Version,
            session.StartTimeUtc, session.EndTimeUtc, session.Location,
            canSubmit = CampusCodes.FiveCampuses.Contains(campus) && Bookable(session, http)
                && !rows.Any(x => EffectiveStatus(x, now) is "PENDING" or "CONFIRMED"),
            cutoffAtUtc = cutoff,
            isLate = now > cutoff,
            bookings = rows.Select(x => View(x, now))
        });
    }

    private static async Task<IResult> Submit(int sessionId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var session = await LoadSession(sessionId, db, ct);
        var scope = await Authorize(session.Activity.ClubId, http, ct);
        Manager(scope);
        var campus = scope.Club.CampusCode;
        Require(CampusCodes.FiveCampuses.Contains(campus), 400, "INVALID_CAMPUS", "The club must belong to a physical campus.");
        var input = await Body<BookingRequest>(http, ct);
        var type = input.VenueType?.Trim();
        var lines = input.Equipment ?? [];
        var venueIds = RequestedIds(input.VenueId, input.VenueIds);
        Require(type?.Length is not > 40 && lines.Length <= 100 && lines.All(x => x is { Quantity: > 0 })
            && !(venueIds.Length > 0 && !string.IsNullOrEmpty(type))
            && lines.Select(x => x.EquipmentId).Distinct().Count() == lines.Length
            && (venueIds.Length > 0 || !string.IsNullOrEmpty(type) || lines.Length > 0),
            400, "INVALID_BOOKING", "Provide a venue preference or equipment quantities.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.Entry(session).ReloadAsync(ct);
        await db.Entry(session.Activity).ReloadAsync(ct);
        Require(session.Version == input.SessionVersion, 409, "STALE_VERSION", "Reload the current session.");
        Require(Bookable(session, http), 409, "SESSION_NOT_BOOKABLE", "Select a future onsite session.");
        Require(!await db.Set<ActivityBooking>().AnyAsync(x => x.SessionId == sessionId && (x.Status == "PENDING" || x.Status == "CONFIRMED")
            && x.StartsAtUtc == session.StartTimeUtc && x.EndsAtUtc == session.EndTimeUtc, ct),
            409, "BOOKING_EXISTS", "The session already has a booking.");
        if (venueIds.Length > 0)
            Require(await db.Set<CampusVenue>().CountAsync(x => venueIds.Contains(x.Id) && x.CampusCode == campus && x.IsBookable, ct) == venueIds.Length,
                400, "INVALID_VENUE", "Select bookable rooms in this campus.");
        var ids = lines.Select(x => x.EquipmentId).ToArray();
        Require(await db.Set<CampusEquipment>().CountAsync(x => ids.Contains(x.Id) && x.CampusCode == campus && x.IsBookable, ct) == ids.Length,
            400, "INVALID_EQUIPMENT", "Select bookable equipment in this campus.");
        var row = new ActivityBooking
        {
            Session = session, CampusCode = campus, RequestedVenueId = venueIds.Length > 0 ? venueIds[0] : null, RequestedVenueType = type,
            AdditionalVenues = venueIds.Skip(1).Select(venueId => new ActivityBookingVenue { VenueId = venueId }).ToList(),
            StartsAtUtc = session.StartTimeUtc, EndsAtUtc = session.EndTimeUtc,
            CutoffAtUtc = await Cutoff(db, campus, session.StartTimeUtc, ct),
            RequestedByUserId = http.User.GetUserId(), RequestedAtUtc = Now(http),
            Equipment = lines.Select(x => new ActivityBookingEquipment { EquipmentId = x.EquipmentId, RequestedQuantity = x.Quantity }).ToList()
        };
        db.Add(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(View(row, Now(http)));
    }

    private static async Task<IResult> Catalog(string campus, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        await CampusAccess(campus, http, false, ct);
        var venues = await db.Set<CampusVenue>().Where(x => x.CampusCode == campus).OrderBy(x => x.Code).ToListAsync(ct);
        var equipment = await db.Set<CampusEquipment>().Where(x => x.CampusCode == campus).OrderBy(x => x.Code).ToListAsync(ct);
        var policy = await db.Set<CampusBookingPolicy>().FindAsync([campus], ct);
        var layouts = await db.Set<CampusBookingLayout>().AsNoTracking().Where(x => x.CampusCode == campus)
            .OrderBy(x => x.Building).ThenBy(x => x.Floor).ToListAsync(ct);
        return Results.Ok(new { venues, equipment, policy, layouts });
    }

    private static async Task<IResult> SaveLayout(string campus, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        await CampusAccess(campus, http, true, ct);
        var input = await Body<LayoutInput>(http, ct);
        var building = input.Building?.Trim() ?? "";
        var floor = input.Floor?.Trim() ?? "";
        var image = string.IsNullOrWhiteSpace(input.ImageUrl) ? null : input.ImageUrl.Trim();
        var uploaded = input.UploadedImageUrl?.Trim();
        var rooms = input.Rooms ?? [];
        var isBuildingPin = building.Length > 0 && floor.Length == 0;
        Require(building.Length <= 80 && floor.Length <= 40 && (floor.Length == 0 || building.Length > 0)
            && (isBuildingPin
                ? image is null && (input.PlanX is null && input.PlanY is null || input.PlanX is >= 0 and <= 100 && input.PlanY is >= 0 and <= 100)
                : input.PlanX is null && input.PlanY is null && (image is null || image.Length <= 2048
                    && Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0)),
            400, "INVALID_LAYOUT", "Provide a valid HTTPS plan image or both building coordinates.");
        Require(uploaded is null || !isBuildingPin && image is null && uploaded.Length > 0,
            400, "INVALID_LAYOUT", "Choose a single plan image source.");
        Require(rooms.Length <= 200 && (rooms.Length == 0 || floor.Length > 0)
            && rooms.All(x => x is not null && Fits(x.Code, 40) && Fits(x.Name, 160) && Fits(x.Type, 40)
                && x.Capacity >= 0 && x.PlanX is >= 0 and <= 100 && x.PlanY is >= 0 and <= 100 && ValidPlanArea(x.PlanArea)
                && ValidSharedFloors(x.PlanAreasByFloor, floor))
            && rooms.Select(x => x.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == rooms.Length,
            400, "INVALID_ROOM_MAPPING", "Provide distinct room codes, names, types and valid coordinates for this floor.");

        if (uploaded is not null)
        {
            var media = http.RequestServices.GetRequiredService<MediaAttachmentClient>();
            var verified = await media.VerifyAsync(uploaded, null, "campus-plan", http.User.GetUserId(), http, ct);
            Require(verified.StatusCode == 200, verified.StatusCode, verified.Code ?? "MEDIA_ATTACHMENT_REJECTED",
                "The uploaded floor plan could not be attached.");
            image = verified.PublicUrl;
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await db.Set<CampusBookingLayout>().FindAsync([campus, building, floor], ct);
        if (row is null)
        {
            row = new CampusBookingLayout { CampusCode = campus, Building = building, Floor = floor };
            db.Add(row);
        }
        row.ImageUrl = image;
        row.PlanX = input.PlanX;
        row.PlanY = input.PlanY;
        if (rooms.Length > 0)
        {
            var codes = rooms.Select(x => x.Code.Trim().ToUpperInvariant()).ToArray();
            var existing = await db.Set<CampusVenue>().Where(x => x.CampusCode == campus && codes.Contains(x.Code))
                .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
            for (var index = 0; index < rooms.Length; index++)
            {
                var room = rooms[index];
                var code = codes[index];
                if (!existing.TryGetValue(code, out var venue))
                {
                    venue = new CampusVenue
                    {
                        CampusCode = campus, Code = code, Name = room.Name.Trim(), VenueType = room.Type.Trim(),
                        Building = row.Building, Floor = row.Floor, Capacity = room.Capacity, IsBookable = room.IsBookable
                    };
                    db.Add(venue);
                }
                else
                {
                    var sameBuilding = string.Equals(venue.Building, row.Building, StringComparison.OrdinalIgnoreCase);
                    var shared = venue.PlanAreasByFloor;
                    if (sameBuilding && shared?.ContainsKey(row.Floor) == true && !string.IsNullOrEmpty(room.PlanArea))
                    {
                        shared[row.Floor] = room.PlanArea;
                        venue.SharedFloorAreas = JsonSerializer.Serialize(shared);
                        continue;
                    }
                    Require(sameBuilding && string.Equals(venue.Floor, row.Floor, StringComparison.OrdinalIgnoreCase),
                        409, "ROOM_CODE_IN_USE", $"Room code {code} belongs to a different location.");
                    venue.Building = row.Building;
                    venue.Floor = row.Floor;
                }
                venue.PlanX = room.PlanX;
                venue.PlanY = room.PlanY;
                if (room.PlanArea is not null) venue.PlanArea = room.PlanArea.Length == 0 ? null : room.PlanArea;
                if (room.PlanAreasByFloor is not null) venue.SharedFloorAreas = JsonSerializer.Serialize(room.PlanAreasByFloor);
            }
        }
        if (input.ReplaceGeometry && floor.Length > 0)
        {
            var included = rooms.Select(room => room.Code.Trim().ToUpperInvariant()).ToArray();
            var oldVenues = await db.Set<CampusVenue>().Where(x => x.CampusCode == campus && x.Building == building
                && !included.Contains(x.Code)).ToListAsync(ct);
            foreach (var venue in oldVenues)
            {
                if (venue.Floor == floor)
                {
                    venue.PlanX = null;
                    venue.PlanY = null;
                    venue.PlanArea = null;
                }
                var shared = venue.PlanAreasByFloor;
                if (shared?.Remove(floor) == true) venue.SharedFloorAreas = JsonSerializer.Serialize(shared);
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(row);
    }

    private static async Task<IResult> SaveCutoff(string campus, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        await CampusAccess(campus, http, true, ct);
        var input = await Body<CutoffInput>(http, ct);
        Require(input.Day is >= 1 and <= 7, 400, "INVALID_CUTOFF", "Choose a weekday from Monday to Sunday.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await db.Set<CampusBookingPolicy>().FindAsync([campus], ct);
        if (row is null)
        {
            row = new CampusBookingPolicy { CampusCode = campus };
            db.Add(row);
        }
        row.CutoffDay = input.Day;
        row.CutoffTime = input.Time;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(row);
    }

    private static bool Fits(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max;
    private static bool ValidSharedFloors(Dictionary<string, string>? floors, string? primaryFloor) =>
        floors is null || floors.Count <= 10 && (floors.Count == 0 || Fits(primaryFloor, 40))
        && floors.All(pair => Fits(pair.Key, 40) && pair.Key == pair.Key.Trim()
            && !string.Equals(pair.Key, primaryFloor, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(pair.Value) && ValidPlanArea(pair.Value));

    // SVG coordinates are stored in the image's 0–100 coordinate space.
    private static bool ValidPlanArea(string? area)
    {
        if (string.IsNullOrEmpty(area)) return true;
        const string coordinate = @"[0-9]{1,3}(?:\.[0-9]{1,2})?";
        if (area.Length > 256 || !Regex.IsMatch(area, $@"\A{coordinate},{coordinate}(?: {coordinate},{coordinate}){{2,11}}\z"))
            return false;
        var points = area.Split(' ').Select(point => point.Split(',')
            .Select(value => decimal.Parse(value, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        if (points.Any(point => point[0] > 100 || point[1] > 100) ||
            points.Select(point => (point[0], point[1])).Distinct().Count() != points.Length)
            return false;
        return points.Select((point, index) =>
        {
            var next = points[(index + 1) % points.Length];
            return point[0] * next[1] - point[1] * next[0];
        }).Sum() != 0;
    }

    private static async Task<IResult> SaveVenue(string campus, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        await CampusAccess(campus, http, true, ct);
        var input = await Body<VenueInput>(http, ct);
        Require(Fits(input.Code, 40) && Fits(input.Name, 160) && Fits(input.Type, 40)
            && input.Building?.Length is not > 80 && input.Floor?.Length is not > 40 && input.Capacity >= 0
            && ValidPlanArea(input.PlanArea)
            && ValidSharedFloors(input.PlanAreasByFloor, input.Floor?.Trim())
            && (string.IsNullOrEmpty(input.PlanArea) && input.PlanAreasByFloor?.Count is not > 0
                || Fits(input.Building, 80) && Fits(input.Floor, 40))
            && (input.PlanX is null && input.PlanY is null || input.PlanX is >= 0 and <= 100
                && input.PlanY is >= 0 and <= 100 && Fits(input.Building, 80) && Fits(input.Floor, 40)),
            400, "INVALID_VENUE", "Check venue code, name, type and capacity.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var code = input.Code.Trim().ToUpperInvariant();
        var row = await db.Set<CampusVenue>().SingleOrDefaultAsync(x => x.CampusCode == campus && x.Code == code, ct);
        Require(!input.CreateOnly || row is null, 409, "ROOM_CODE_IN_USE", $"Room code {code} already exists.");
        if (row is null)
        {
            row = new CampusVenue { CampusCode = campus, Code = code };
            db.Add(row);
        }
        row.Name = input.Name.Trim();
        row.VenueType = input.Type.Trim();
        var building = input.Building?.Trim();
        var floor = input.Floor?.Trim();
        if (input.PlanArea is not null || row.Building != building || row.Floor != floor)
            row.PlanArea = string.IsNullOrEmpty(input.PlanArea) ? null : input.PlanArea;
        if (input.PlanAreasByFloor is not null || row.Building != building || row.Floor != floor)
            row.SharedFloorAreas = input.PlanAreasByFloor is null ? null : JsonSerializer.Serialize(input.PlanAreasByFloor);
        row.Building = input.Building?.Trim();
        row.Floor = input.Floor?.Trim();
        row.Capacity = input.Capacity;
        row.PlanX = input.PlanX;
        row.PlanY = input.PlanY;
        row.IsBookable = input.IsBookable;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(row);
    }

    private static async Task<IResult> SaveEquipment(string campus, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        await CampusAccess(campus, http, true, ct);
        var input = await Body<EquipmentInput>(http, ct);
        Require(Fits(input.Code, 40) && Fits(input.Name, 160) && input.Quantity >= 0,
            400, "INVALID_EQUIPMENT", "Check equipment code, name and quantity.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var code = input.Code.Trim().ToUpperInvariant();
        var row = await db.Set<CampusEquipment>().SingleOrDefaultAsync(x => x.CampusCode == campus && x.Code == code, ct);
        if (row is null)
        {
            row = new CampusEquipment { CampusCode = campus, Code = code };
            db.Add(row);
        }
        else
        {
            var now = Now(http);
            var allocations = await Allocations(db, row.Id, 0, now, DateTimeOffset.MaxValue, now, ct);
            Require(Peak(allocations, now, DateTimeOffset.MaxValue) <= input.Quantity,
                409, "EQUIPMENT_ALREADY_ALLOCATED", "Quantity is below an existing allocation.");
        }
        row.Name = input.Name.Trim();
        row.Quantity = input.Quantity;
        row.IsBookable = input.IsBookable;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(row);
    }

    private static async Task<IResult> Queue(string campus, DateOnly weekOf, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        await CampusAccess(campus, http, true, ct);
        Require(weekOf.DayOfWeek == DayOfWeek.Monday && weekOf.Year is >= 2000 and <= 9998,
            400, "INVALID_WEEK", "Provide the Monday of the event week.");
        var start = new DateTimeOffset(weekOf.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
        var end = start.AddDays(7);
        var rows = await db.Set<ActivityBooking>().AsNoTracking().Include(x => x.Equipment).Include(x => x.AdditionalVenues)
            .Include(x => x.Session).ThenInclude(x => x.Activity)
            .Where(x => x.CampusCode == campus && x.StartsAtUtc >= start && x.StartsAtUtc < end)
            .OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var now = Now(http);
        return Results.Ok(rows.Select(x => View(x, now)));
    }

    private sealed record Allocation(DateTimeOffset Start, DateTimeOffset End, int Quantity);

    private static Task<List<Allocation>> Allocations(ActivityDbContext db, int equipmentId, int exceptId,
        DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, CancellationToken ct) =>
        (from line in db.Set<ActivityBookingEquipment>()
         join booking in db.Set<ActivityBooking>() on line.BookingId equals booking.Id
         where line.EquipmentId == equipmentId && line.ConfirmedQuantity > 0 && booking.Id != exceptId
             && booking.Status == "CONFIRMED" && booking.Session.Status != "CANCELLED"
             && (booking.Session.Status == "SCHEDULED" || booking.StartsAtUtc <= now)
             && booking.Session.Activity.Status == ActivityStatuses.Scheduled
             && booking.StartsAtUtc < end && booking.EndsAtUtc > start
         select new Allocation(booking.StartsAtUtc, booking.EndsAtUtc, line.ConfirmedQuantity!.Value)).ToListAsync(ct);

    private static long Peak(IEnumerable<Allocation> allocations, DateTimeOffset start, DateTimeOffset end)
    {
        var changes = allocations.SelectMany(x => new[]
        {
            (At: x.Start < start ? start : x.Start, Delta: (long)x.Quantity),
            (At: x.End > end ? end : x.End, Delta: -(long)x.Quantity)
        }).OrderBy(x => x.At).ThenBy(x => x.Delta);
        long active = 0, peak = 0;
        foreach (var change in changes)
        {
            active += change.Delta;
            peak = Math.Max(peak, active);
        }
        return peak;
    }

    private static async Task<IResult> Availability(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var row = await db.Set<ActivityBooking>().AsNoTracking().Include(x => x.Equipment)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(row is not null, 404, "BOOKING_NOT_FOUND", "Booking not found.");
        await CampusAccess(row!.CampusCode, http, true, ct);
        var now = Now(http);
        var reserved = await ReservedRooms(db, row, now).Distinct().ToListAsync(ct);
        var equipmentIds = row.Equipment.Select(x => x.EquipmentId).ToArray();
        var items = await db.Set<CampusEquipment>().AsNoTracking().Where(x => equipmentIds.Contains(x.Id)).ToListAsync(ct);
        var quantities = new Dictionary<int, int>();
        foreach (var item in items)
        {
            var allocations = await Allocations(db, item.Id, id, row.StartsAtUtc, row.EndsAtUtc, now, ct);
            quantities[item.Id] = item.IsBookable
                ? (int)Math.Max(0, item.Quantity - Peak(allocations, row.StartsAtUtc, row.EndsAtUtc))
                : row.Equipment.Single(x => x.EquipmentId == item.Id).ConfirmedQuantity ?? 0;
        }
        return Results.Ok(new { reservedVenueIds = reserved, availableEquipment = quantities });
    }

    private static IQueryable<ActivityBooking> VenueConflicts(ActivityDbContext db, ActivityBooking row, DateTimeOffset now) =>
        db.Set<ActivityBooking>().Where(x => x.Id != row.Id && x.CampusCode == row.CampusCode
            && x.Status == "CONFIRMED" && x.Session.Status != "CANCELLED"
            && (x.Session.Status == "SCHEDULED" || x.StartsAtUtc <= now)
            && x.Session.Activity.Status == ActivityStatuses.Scheduled && x.ConfirmedVenueId != null
            && x.StartsAtUtc < row.EndsAtUtc && x.EndsAtUtc > row.StartsAtUtc);

    private static IQueryable<int> ReservedRooms(ActivityDbContext db, ActivityBooking row, DateTimeOffset now)
    {
        var conflicts = VenueConflicts(db, row, now);
        return conflicts.Select(x => x.ConfirmedVenueId!.Value)
            .Concat(conflicts.SelectMany(x => x.AdditionalVenues.Where(venue => venue.IsConfirmed).Select(venue => venue.VenueId)));
    }

    private static async Task<IResult> Decide(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var campus = await db.Set<ActivityBooking>().Where(x => x.Id == id).Select(x => x.CampusCode).SingleOrDefaultAsync(ct);
        Require(campus is not null, 404, "BOOKING_NOT_FOUND", "Booking not found.");
        await CampusAccess(campus!, http, true, ct);
        var input = await Body<BookingDecision>(http, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await db.Set<ActivityBooking>().Include(x => x.Equipment).Include(x => x.AdditionalVenues).Include(x => x.Session)
            .ThenInclude(x => x.Activity).SingleAsync(x => x.Id == id, ct);
        var now = Now(http);
        Require(row.Version == input.Version && EffectiveStatus(row, now) is "PENDING" or "CONFIRMED",
            409, "STALE_VERSION", "Reload the booking.");
        Require(Bookable(row.Session, http) && SameWindow(row),
            409, "SESSION_CHANGED", "This session no longer accepts booking changes.");
        if (!input.Cancel)
        {
            var venueIds = RequestedIds(input.VenueId, input.VenueIds);
            Require(venueIds.Length == 0 || row.RequestedVenueId.HasValue || !string.IsNullOrEmpty(row.RequestedVenueType),
                400, "VENUE_NOT_REQUESTED", "This request is for equipment only.");
            if (venueIds.Length > 0)
            {
                var current = VenueIds(row.ConfirmedVenueId, row.AdditionalVenues.Where(venue => venue.IsConfirmed).Select(venue => venue.VenueId));
                var venues = await db.Set<CampusVenue>().Where(x => venueIds.Contains(x.Id) && x.CampusCode == campus
                    && (x.IsBookable || current.Contains(x.Id))).ToDictionaryAsync(x => x.Id, ct);
                Require(venues.Count == venueIds.Length, 400, "INVALID_VENUE", "Select bookable rooms in this campus.");
                Require(!await ReservedRooms(db, row, now).AnyAsync(room => venueIds.Contains(room), ct),
                    409, "VENUE_UNAVAILABLE", "At least one room already has a booking in this time window.");
                row.OriginalLocation ??= row.Session.Location;
                row.Session.Location = RoomLocation(venueIds.Select(venueId => venues[venueId].Name).ToArray());
            }
            Require(!row.ConfirmedVenueId.HasValue || venueIds.Length > 0, 400, "VENUE_REQUIRED", "Cancel to release the confirmed venue.");
            var quantities = input.Equipment ?? [];
            Require(quantities.All(x => x is { Quantity: >= 0 }) && quantities.Length == row.Equipment.Count
                && quantities.Select(x => x.EquipmentId).Distinct().Count() == quantities.Length
                && quantities.All(x => row.Equipment.Any(line => line.EquipmentId == x.EquipmentId && x.Quantity <= line.RequestedQuantity)),
                400, "INVALID_ALLOCATION", "Provide a quantity for each requested item, including zero.");
            foreach (var line in row.Equipment.OrderBy(x => x.EquipmentId))
            {
                var quantity = quantities.Single(x => x.EquipmentId == line.EquipmentId).Quantity;
                if (quantity > 0)
                {
                    var item = await db.Set<CampusEquipment>().SingleAsync(x => x.Id == line.EquipmentId, ct);
                    var allocations = await Allocations(db, item.Id, id, row.StartsAtUtc, row.EndsAtUtc, now, ct);
                    Require((item.IsBookable || quantity <= line.ConfirmedQuantity)
                        && Peak(allocations, row.StartsAtUtc, row.EndsAtUtc) + quantity <= item.Quantity,
                        409, "EQUIPMENT_UNAVAILABLE", "Reduce the allocated quantity for this time window.");
                }
                line.ConfirmedQuantity = quantity;
            }
            row.ConfirmedVenueId = venueIds.Length > 0 ? venueIds[0] : null;
            var additional = venueIds.Skip(1).ToHashSet();
            row.AdditionalVenues.RemoveAll(venue => venue.IsConfirmed && !additional.Contains(venue.VenueId));
            additional.ExceptWith(row.AdditionalVenues.Where(venue => venue.IsConfirmed).Select(venue => venue.VenueId));
            row.AdditionalVenues.AddRange(additional.Select(venueId => new ActivityBookingVenue { VenueId = venueId, IsConfirmed = true }));
        }
        if (input.Cancel) Cancel(row);
        else row.Status = "CONFIRMED";
        Stamp(row, http, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(View(row, now));
    }

    private static async Task<IResult> CancelByManager(int sessionId, int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var session = await LoadSession(sessionId, db, ct);
        Manager(await Authorize(session.Activity.ClubId, http, ct));
        var input = await Body<CancelInput>(http, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.Entry(session).ReloadAsync(ct);
        await db.Entry(session.Activity).ReloadAsync(ct);
        var row = await db.Set<ActivityBooking>().Include(x => x.Equipment).Include(x => x.AdditionalVenues)
            .SingleOrDefaultAsync(x => x.Id == id && x.SessionId == sessionId, ct);
        var now = Now(http);
        Require(row is not null, 404, "BOOKING_NOT_FOUND", "Booking not found.");
        Require(row!.Version == input.Version && Bookable(session, http) && SameWindow(row)
            && row.Status is "PENDING" or "CONFIRMED", 409, "STALE_VERSION", "Reload the current booking.");
        Cancel(row);
        Stamp(row, http, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(View(row, now));
    }

    private static void Cancel(ActivityBooking row)
    {
        if (row.OriginalLocation is not null) row.Session.Location = row.OriginalLocation;
        row.Status = "CANCELLED";
    }

    private static void Stamp(ActivityBooking row, HttpContext http, DateTimeOffset now)
    {
        row.Session.Version = Guid.NewGuid();
        row.Session.UpdatedAtUtc = now;
        row.ProcessedByUserId = http.User.GetUserId();
        row.ProcessedAtUtc = now;
        row.Version = Guid.NewGuid();
    }
}
