using System.Data;
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
    private sealed record BookingRequest(Guid SessionVersion, int? VenueId, string? VenueType, EquipmentRequest[]? Equipment);
    private sealed record BookingDecision(Guid Version, int? VenueId, EquipmentRequest[]? Equipment, bool Cancel);
    private sealed record VenueInput(string Code, string Name, string Type, string? Building, string? Floor,
        int Capacity, bool IsBookable, int? PlanX, int? PlanY);
    private sealed record EquipmentInput(string Code, string Name, int Quantity, bool IsBookable);
    private sealed record CutoffInput(int Day, TimeOnly Time);
    private sealed record LayoutInput(string? Building, string? Floor, string? ImageUrl, int? PlanX, int? PlanY);
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

    private static async Task<IResult> ForSession(int sessionId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var session = await LoadSession(sessionId, db, ct);
        var scope = await Authorize(session.Activity.ClubId, http, ct);
        Manager(scope);
        var campus = scope.Club.CampusCode;
        var rows = await db.Set<ActivityBooking>().AsNoTracking().Include(x => x.Equipment)
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
        Require(type?.Length is not > 40 && lines.Length <= 100 && lines.All(x => x is { Quantity: > 0 })
            && !(input.VenueId.HasValue && !string.IsNullOrEmpty(type))
            && lines.Select(x => x.EquipmentId).Distinct().Count() == lines.Length
            && (input.VenueId.HasValue || !string.IsNullOrEmpty(type) || lines.Length > 0),
            400, "INVALID_BOOKING", "Provide a venue preference or equipment quantities.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.Entry(session).ReloadAsync(ct);
        await db.Entry(session.Activity).ReloadAsync(ct);
        Require(session.Version == input.SessionVersion, 409, "STALE_VERSION", "Reload the current session.");
        Require(Bookable(session, http), 409, "SESSION_NOT_BOOKABLE", "Select a future onsite session.");
        Require(!await db.Set<ActivityBooking>().AnyAsync(x => x.SessionId == sessionId && (x.Status == "PENDING" || x.Status == "CONFIRMED")
            && x.StartsAtUtc == session.StartTimeUtc && x.EndsAtUtc == session.EndTimeUtc, ct),
            409, "BOOKING_EXISTS", "The session already has a booking.");
        if (input.VenueId.HasValue)
            Require(await db.Set<CampusVenue>().AnyAsync(x => x.Id == input.VenueId && x.CampusCode == campus && x.IsBookable, ct),
                400, "INVALID_VENUE", "Select a bookable venue in this campus.");
        var ids = lines.Select(x => x.EquipmentId).ToArray();
        Require(await db.Set<CampusEquipment>().CountAsync(x => ids.Contains(x.Id) && x.CampusCode == campus && x.IsBookable, ct) == ids.Length,
            400, "INVALID_EQUIPMENT", "Select bookable equipment in this campus.");
        var row = new ActivityBooking
        {
            Session = session, CampusCode = campus, RequestedVenueId = input.VenueId, RequestedVenueType = type,
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
        var isBuildingPin = building.Length > 0 && floor.Length == 0;
        Require(building.Length <= 80 && floor.Length <= 40 && (floor.Length == 0 || building.Length > 0)
            && (isBuildingPin
                ? image is null && (input.PlanX is null && input.PlanY is null || input.PlanX is >= 0 and <= 100 && input.PlanY is >= 0 and <= 100)
                : input.PlanX is null && input.PlanY is null && (image is null || image.Length <= 2048
                    && Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0)),
            400, "INVALID_LAYOUT", "Provide a valid HTTPS plan image or both building coordinates.");
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

    private static async Task<IResult> SaveVenue(string campus, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        await CampusAccess(campus, http, true, ct);
        var input = await Body<VenueInput>(http, ct);
        Require(Fits(input.Code, 40) && Fits(input.Name, 160) && Fits(input.Type, 40)
            && input.Building?.Length is not > 80 && input.Floor?.Length is not > 40 && input.Capacity >= 0
            && (input.PlanX is null && input.PlanY is null || input.PlanX is >= 0 and <= 100
                && input.PlanY is >= 0 and <= 100 && Fits(input.Building, 80) && Fits(input.Floor, 40)),
            400, "INVALID_VENUE", "Check venue code, name, type and capacity.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var code = input.Code.Trim().ToUpperInvariant();
        var row = await db.Set<CampusVenue>().SingleOrDefaultAsync(x => x.CampusCode == campus && x.Code == code, ct);
        if (row is null)
        {
            row = new CampusVenue { CampusCode = campus, Code = code };
            db.Add(row);
        }
        row.Name = input.Name.Trim();
        row.VenueType = input.Type.Trim();
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
        var rows = await db.Set<ActivityBooking>().AsNoTracking().Include(x => x.Equipment)
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
        var reserved = await VenueConflicts(db, row, now).Select(x => x.ConfirmedVenueId).Distinct().ToListAsync(ct);
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

    private static async Task<IResult> Decide(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var campus = await db.Set<ActivityBooking>().Where(x => x.Id == id).Select(x => x.CampusCode).SingleOrDefaultAsync(ct);
        Require(campus is not null, 404, "BOOKING_NOT_FOUND", "Booking not found.");
        await CampusAccess(campus!, http, true, ct);
        var input = await Body<BookingDecision>(http, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await db.Set<ActivityBooking>().Include(x => x.Equipment).Include(x => x.Session)
            .ThenInclude(x => x.Activity).SingleAsync(x => x.Id == id, ct);
        var now = Now(http);
        Require(row.Version == input.Version && EffectiveStatus(row, now) is "PENDING" or "CONFIRMED",
            409, "STALE_VERSION", "Reload the booking.");
        Require(Bookable(row.Session, http) && SameWindow(row),
            409, "SESSION_CHANGED", "This session no longer accepts booking changes.");
        if (!input.Cancel)
        {
            Require(!input.VenueId.HasValue || row.RequestedVenueId.HasValue || !string.IsNullOrEmpty(row.RequestedVenueType),
                400, "VENUE_NOT_REQUESTED", "This request is for equipment only.");
            if (input.VenueId.HasValue)
            {
                var venue = await db.Set<CampusVenue>().SingleOrDefaultAsync(x => x.Id == input.VenueId && x.CampusCode == campus
                    && (x.IsBookable || x.Id == row.ConfirmedVenueId), ct);
                Require(venue is not null, 400, "INVALID_VENUE", "Select a bookable venue in this campus.");
                Require(!await VenueConflicts(db, row, now).AnyAsync(x => x.ConfirmedVenueId == input.VenueId, ct),
                    409, "VENUE_UNAVAILABLE", "The venue already has a booking in this time window.");
                row.OriginalLocation ??= row.Session.Location;
                row.Session.Location = venue!.Name;
            }
            Require(!row.ConfirmedVenueId.HasValue || input.VenueId.HasValue, 400, "VENUE_REQUIRED", "Cancel to release the confirmed venue.");
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
            row.ConfirmedVenueId = input.VenueId;
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
        var row = await db.Set<ActivityBooking>().Include(x => x.Equipment)
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
