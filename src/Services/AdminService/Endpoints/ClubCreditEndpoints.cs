using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AdminService.Data;
using AdminService.Security;
using AdminService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Rosters;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RewardDraftInput(string Label, string Description, string Kind, decimal Price,
    Dictionary<string, string>? Palette = null, string? AssetUrl = null, IReadOnlyList<Guid>? GrantIds = null, Guid? ExpectedVersion = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RewardPublishInput(Guid ExpectedVersion, DateTimeOffset? OpensAt = null, DateTimeOffset? ClosesAt = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RewardRetireInput(Guid ExpectedVersion, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RewardRedeemInput(Guid ExpectedRewardVersion, Guid ExpectedWalletVersion);

public static class ClubCreditEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static void MapClubCreditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clubs/{clubId:int}").AddEndpointFilter<RosterExceptionFilter>().WithTags("Club credit and rewards");
        group.MapGet("/wallet/me", Wallet).RequireAuthorization(AdminPolicies.AnyActor);
        group.MapGet("/points/me", Wallet).RequireAuthorization(AdminPolicies.AnyActor);
        group.MapGet("/wallet/me/graph", Graph).RequireAuthorization(AdminPolicies.AnyActor);
        group.MapGet("/points/me/ledger", Ledger).RequireAuthorization(AdminPolicies.AnyActor);
        group.MapGet("/rewards", Catalog).AllowAnonymous();
        group.MapPost("/rewards", Create).RequireAuthorization(AdminPolicies.AnyActor);
        group.MapPatch("/rewards/{rewardId:guid}", Update).RequireAuthorization(AdminPolicies.AnyActor);
        group.MapPost("/rewards/{rewardId:guid}/publish", Publish).RequireAuthorization(AdminPolicies.AnyActor);
        group.MapPost("/rewards/{rewardId:guid}/retire", Retire).RequireAuthorization(AdminPolicies.AnyActor);
        group.MapPost("/rewards/{rewardId:guid}/redeem", Redeem).RequireAuthorization(AdminPolicies.AnyActor);
        group.MapGet("/rewards/redemptions/me", Receipts).RequireAuthorization(AdminPolicies.AnyActor);
        app.MapGet("/api/users/me/cosmetics", Inventory).RequireAuthorization(AdminPolicies.AnyActor).AddEndpointFilter<RosterExceptionFilter>();
    }

    private static DateTimeOffset Now(HttpContext http) => http.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
    private static async Task<bool> Active(HttpContext http, CancellationToken ct)
    {
        var account = (await http.RequestServices.GetRequiredService<SchoolAccountClient>().UsersAsync([http.User.GetUserId()], ct)).SingleOrDefault();
        return account is { IsActive: true, IsLocked: false };
    }
    private static async Task<bool> Manage(int clubId, HttpContext http, CancellationToken ct)
    {
        if (!await Active(http, ct)) return false;
        if (http.User.IsInRole(AuthRoles.Admin)) return true;
        return (await http.RequestServices.GetRequiredService<ClubAccessClient>().GetMyAccessAsync(http.GetBearerToken(), ct, true)).Any(x => x.ClubId == clubId && x.CanManage);
    }
    private static Task<ClubExperienceAuthority?> Club(int clubId, int? userId, HttpContext http, CancellationToken ct) =>
        http.RequestServices.GetRequiredService<ClubExperienceAuthorityClient>().GetAsync(clubId, userId, ct);
    private static object BalanceBody(int clubId, int userId, ClubCreditLedger.Balance state) => new
    {
        clubId,
        userId,
        unit = "CLUB_CREDIT",
        scope = "LIFETIME",
        policyVersion = ClubCreditLedger.Policy,
        earnedTotal = state.Earned,
        revokedTotal = state.Revoked,
        spentTotal = state.Spent,
        balance = state.Available,
        version = state.Version,
        sourceRevision = state.SourceRevision,
        hasDebt = state.Available < 0,
        conversion = "VERIFIED_CLUB_XP_1_TO_1",
        experiencePointsAreNotSpent = true
    };
    private static Task<IResult> Consistent(AdminDbContext db, Func<Task<IResult>> action, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var result = await action(); if (transaction is not null) await transaction.CommitAsync(ct); return result;
        });
    private static async Task<IResult> Wallet(int clubId, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await Active(http, ct)) return Results.Forbid();
        if (await Club(clubId, http.User.GetUserId(), http, ct) is null) return Results.NotFound();
        return await Consistent(db, async () =>
        {
            var userId = http.User.GetUserId(); var effects = await ClubCreditLedger.Source(db, userId, clubId).ToListAsync(ct);
            var wallet = await db.Set<ClubCreditWallet>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.ClubId == clubId, ct);
            if (!await ClubCreditLedger.Reconciles(db, userId, clubId, wallet?.Spent ?? 0, ct))
                return ApiOperations.Error(503, "WALLET_RECONCILIATION_REQUIRED", "Wallet debit history does not match the current account.");
            return Results.Ok(BalanceBody(clubId, userId, ClubCreditLedger.Calculate(userId, clubId, effects, wallet)));
        }, ct);
    }
    private static async Task<IResult> Graph(int clubId, int? days, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await Active(http, ct)) return Results.Forbid();
        if (await Club(clubId, http.User.GetUserId(), http, ct) is null) return Results.NotFound();
        if (days is < 1 or > 365) return ApiOperations.Error(400, "INVALID_GRAPH_WINDOW", "Use 1–365 days.");
        return await Consistent(db, async () =>
        {
            var userId = http.User.GetUserId(); var now = Now(http);
            var today = DateOnly.FromDateTime(now.UtcDateTime.AddHours(7)); var from = today.AddDays(1 - (days ?? 30));
            var effects = await ClubCreditLedger.Source(db, userId, clubId).ToListAsync(ct);
            var debits = await db.Set<ClubRewardRedemption>().AsNoTracking().Where(x => x.UserId == userId && x.ClubId == clubId).ToListAsync(ct);
            var wallet = await db.Set<ClubCreditWallet>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.ClubId == clubId, ct);
            var state = ClubCreditLedger.Calculate(userId, clubId, effects, wallet);
            if (debits.Sum(x => x.Amount) != state.Spent) return ApiOperations.Error(503, "WALLET_RECONCILIATION_REQUIRED", "Wallet debit history does not match the current account.");
            static DateOnly Day(DateTimeOffset value) => DateOnly.FromDateTime(value.UtcDateTime.AddHours(7));
            var openingCredit = effects.Where(x => Day(x.RecordedAt) < from).Sum(x => x.Amount);
            var openingSpent = debits.Where(x => Day(x.CreatedAt) < from).Sum(x => x.Amount);
            var credit = openingCredit; var spent = openingSpent; var points = new List<object>();
            for (var date = from; date <= today; date = date.AddDays(1))
            {
                credit += effects.Where(x => Day(x.RecordedAt) == date).Sum(x => x.Amount);
                spent += debits.Where(x => Day(x.CreatedAt) == date).Sum(x => x.Amount);
                points.Add(new { date = date.ToString("yyyy-MM-dd"), earned = credit, spent, balance = credit - spent });
            }
            return Results.Ok(new
            {
                clubId,
                userId,
                unit = "CLUB_CREDIT",
                policyVersion = ClubCreditLedger.Policy,
                version = state.Version,
                sourceRevision = state.SourceRevision,
                timezone = "Asia/Ho_Chi_Minh",
                asOf = now,
                from = from.ToString("yyyy-MM-dd"),
                to = today.ToString("yyyy-MM-dd"),
                openingBalance = openingCredit - openingSpent,
                points
            });
        }, ct);
    }
    private static async Task<IResult> Ledger(int clubId, int? page, int? pageSize, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await Active(http, ct)) return Results.Forbid();
        if (await Club(clubId, http.User.GetUserId(), http, ct) is null) return Results.NotFound();
        var p = Math.Max(1, page ?? 1); var size = Math.Clamp(pageSize ?? 20, 1, 100); var userId = http.User.GetUserId();
        return await Consistent(db, async () =>
        {
            var effects = await ClubCreditLedger.Source(db, userId, clubId).ToListAsync(ct);
            var debits = await db.Set<ClubRewardRedemption>().AsNoTracking().Where(x => x.UserId == userId && x.ClubId == clubId).ToListAsync(ct);
            var entries = effects.Select(x => new Movement("XP:" + x.Id, x.Amount < 0 ? "REVERSAL" : "CREDIT", x.Amount, "VERIFIED_XP_EFFECT", x.Id.ToString(), x.RecordedAt, x.SemesterId))
                .Concat(debits.Select(x => new Movement("REDEEM:" + x.Id, "DEBIT", -x.Amount, "REWARD_REDEMPTION", x.Id.ToString(), x.CreatedAt, null)))
                .OrderByDescending(x => x.At).ThenByDescending(x => x.Id).ToArray();
            return Results.Ok(new
            {
                clubId,
                userId,
                unit = "CLUB_CREDIT",
                policyVersion = ClubCreditLedger.Policy,
                items = entries.Skip((int)Math.Min((long)(p - 1) * size, int.MaxValue)).Take(size),
                page = p,
                pageSize = size,
                totalItems = entries.Length,
                totalPages = (int)Math.Ceiling((decimal)entries.Length / size)
            });
        }, ct);
    }
    private sealed record Movement(string Id, string Type, decimal Amount, string SourceType, string SourceId, DateTimeOffset At, string? SemesterId);

    private static string Availability(ClubReward row, DateTimeOffset now) => row.Status != "PUBLISHED" ? row.Status
        : row.OpensAt > now ? "SCHEDULED" : row.ClosesAt <= now ? "EXPIRED" : "OPEN";
    private static Guid[] Grants(ClubReward row) => JsonSerializer.Deserialize<Guid[]>(row.GrantIdsJson, Json) ?? [];
    private static object Definition(ClubCosmeticDefinition row) => new { cosmeticId = row.Id, issuerClubId = row.ClubId, row.Kind, row.Label, definition = JsonSerializer.Deserialize<JsonElement>(row.DefinitionJson, Json) };
    private static object Reward(ClubReward row, IReadOnlyList<ClubCosmeticDefinition> definitions, DateTimeOffset now) => new
    {
        row.Id,
        row.ClubId,
        row.Label,
        row.Description,
        row.Kind,
        row.Price,
        row.Status,
        row.Version,
        row.OpensAt,
        row.ClosesAt,
        availability = Availability(row, now),
        grants = definitions.Where(x => Grants(row).Contains(x.Id)).Select(Definition),
        partialOwnershipPolicy = "FULL_BUNDLE_PRICE_FOR_MISSING_GRANTS",
        timezone = "Asia/Ho_Chi_Minh"
    };
    private static async Task<IResult> Catalog(int clubId, bool? manage, int? page, int? pageSize, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var authenticated = http.User.Identity?.IsAuthenticated == true;
        var club = await Club(clubId, authenticated ? http.User.GetUserId() : null, http, ct);
        if (club is not { IsActive: true }) return Results.NotFound();
        var active = authenticated && await Active(http, ct);
        var manager = active && await Manage(clubId, http, ct);
        if (manage == true && !manager) return Results.Forbid();
        var query = db.Set<ClubReward>().AsNoTracking().Where(x => x.ClubId == clubId);
        if (manage != true) query = query.Where(x => x.Status == "PUBLISHED");
        var p = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 20, 1, 100); var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((int)Math.Min((long)(p - 1) * size, int.MaxValue)).Take(size).ToArrayAsync(ct);
        var ids = rows.SelectMany(Grants).Distinct().ToArray();
        var definitions = await db.Set<ClubCosmeticDefinition>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(ct);
        var now = Now(http);
        return Results.Ok(new
        {
            clubId,
            asOf = now,
            capabilities = new { canManage = manager, canRedeem = active && club.IsApprovedMember },
            items = rows.Select(row => Reward(row, definitions, now)),
            page = p,
            pageSize = size,
            totalItems = total,
            totalPages = (int)Math.Ceiling((decimal)total / size)
        });
    }

    private static async Task<(Guid[]? Grants, IResult? Error)> Prepare(int clubId, RewardDraftInput input, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Label) || input.Label.Trim().Length > 80 || input.Description is null || input.Description.Length > 2000
            || input.Kind is not ("COLOR" or "SHAPE" or "FRAME" or "BUNDLE") || input.Price is < 0 or > 100000 || input.Price != decimal.Truncate(input.Price))
            return (null, ApiOperations.Error(400, "INVALID_REWARD", "Use a label up to 80 characters, description up to 2000 and integer price 0–100000."));
        if (input.Kind == "BUNDLE")
        {
            if (input.GrantIds is not { Count: 2 } || input.GrantIds.Distinct().Count() != 2 || input.Palette is not null || input.AssetUrl is not null)
                return (null, ApiOperations.Error(400, "INVALID_BUNDLE", "Select one existing color and one existing shape from this club."));
            var existing = await db.Set<ClubCosmeticDefinition>().Where(x => x.ClubId == clubId && input.GrantIds.Contains(x.Id)).ToArrayAsync(ct);
            if (existing.Length != 2 || !existing.Any(x => x.Kind == "COLOR") || !existing.Any(x => x.Kind == "SHAPE"))
                return (null, ApiOperations.Error(400, "INVALID_BUNDLE", "Bundle components must be this issuer's immutable color and shape definitions."));
            return (input.GrantIds.ToArray(), null);
        }
        if (input.GrantIds is { Count: > 0 }) return (null, ApiOperations.Error(400, "INVALID_GRANTS", "Server creates the cosmetic identity."));
        object definition;
        if (input.Kind == "COLOR")
        {
            string[] keys = ["background", "secondary", "accent", "ink"];
            if (input.AssetUrl is not null || input.Palette is null || input.Palette.Count != 4
                || keys.Any(key => !input.Palette.TryGetValue(key, out var value) || !Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$")))
                return (null, ApiOperations.Error(400, "INVALID_PALETTE", "Use four solid HEX colors: background, secondary, accent and ink."));
            definition = input.Palette;
        }
        else
        {
            if (input.Palette is not null || string.IsNullOrWhiteSpace(input.AssetUrl))
                return (null, ApiOperations.Error(400, "REWARD_ASSET_REQUIRED", "Upload a club reward image first."));
            var verified = await http.RequestServices.GetRequiredService<MediaAttachmentClient>().VerifyAsync(input.AssetUrl, null, "club-reward", clubId, http, ct);
            if (verified.StatusCode != 200) return (null, ApiOperations.Error(verified.StatusCode, verified.Code ?? "INVALID_REWARD_ASSET", "The reward image has not been verified for this club."));
            definition = new { assetUrl = verified.PublicUrl };
        }
        var item = new ClubCosmeticDefinition { ClubId = clubId, Kind = input.Kind, Label = input.Label.Trim(), DefinitionJson = JsonSerializer.Serialize(definition, Json), CreatedAt = Now(http) };
        db.Add(item); return ([item.Id], null);
    }
    private static void Audit(AdminDbContext db, ClubReward row, HttpContext http, string action, string reason = "") =>
        db.Add(new ClubRewardAudit
        {
            RewardId = row.Id,
            ActorId = http.User.GetUserId(),
            Action = action,
            Reason = reason,
            SnapshotJson = JsonSerializer.Serialize(row, Json),
            CreatedAt = Now(http)
        });
    private static async Task<IResult> Create(int clubId, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await Manage(clubId, http, ct)) return Results.Forbid();
        if (await Club(clubId, null, http, ct) is not { IsActive: true }) return Results.NotFound();
        var input = await BoundedJsonRequest.ReadAsync<RewardDraftInput>(http, 16384, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            if (!await Manage(clubId, http, ct)) return Results.Forbid();
            var prepared = await Prepare(clubId, input, db, http, ct); if (prepared.Error is not null) return prepared.Error;
            var row = new ClubReward
            {
                ClubId = clubId,
                Label = input.Label.Trim(),
                Description = input.Description.Trim(),
                Kind = input.Kind,
                Price = input.Price,
                GrantIdsJson = JsonSerializer.Serialize(prepared.Grants, Json),
                CreatedBy = http.User.GetUserId(),
                CreatedAt = Now(http)
            };
            db.Add(row); Audit(db, row, http, "CREATE"); await db.SaveChangesAsync(ct);
            var definitions = await db.Set<ClubCosmeticDefinition>().Where(x => prepared.Grants!.Contains(x.Id)).ToArrayAsync(ct);
            return Results.Created($"/api/clubs/{clubId}/rewards/{row.Id}", Reward(row, definitions, Now(http)));
        }, ct);
    }
    private static async Task<IResult> Update(int clubId, Guid rewardId, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await Manage(clubId, http, ct)) return Results.Forbid();
        var input = await BoundedJsonRequest.ReadAsync<RewardDraftInput>(http, 16384, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            if (!await Manage(clubId, http, ct)) return Results.Forbid();
            var row = await db.Set<ClubReward>().SingleOrDefaultAsync(x => x.Id == rewardId && x.ClubId == clubId, ct);
            if (row is null) return Results.NotFound();
            if (row.Status != "DRAFT" || row.Version != input.ExpectedVersion) return ApiOperations.Error(409, "REWARD_IMMUTABLE_OR_STALE", "Only the current draft can be changed. Published grants are immutable.");
            var prepared = await Prepare(clubId, input, db, http, ct); if (prepared.Error is not null) return prepared.Error;
            row.Label = input.Label.Trim(); row.Description = input.Description.Trim(); row.Price = input.Price; row.Kind = input.Kind;
            row.GrantIdsJson = JsonSerializer.Serialize(prepared.Grants, Json); row.Version = Guid.NewGuid(); Audit(db, row, http, "EDIT");
            await db.SaveChangesAsync(ct); var definitions = await db.Set<ClubCosmeticDefinition>().Where(x => prepared.Grants!.Contains(x.Id)).ToArrayAsync(ct);
            return Results.Ok(Reward(row, definitions, Now(http)));
        }, ct);
    }
    private static async Task<IResult> Publish(int clubId, Guid rewardId, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await Manage(clubId, http, ct)) return Results.Forbid();
        var input = await BoundedJsonRequest.ReadAsync<RewardPublishInput>(http, 8192, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            if (!await Manage(clubId, http, ct)) return Results.Forbid();
            var row = await db.Set<ClubReward>().SingleOrDefaultAsync(x => x.Id == rewardId && x.ClubId == clubId, ct);
            if (row is null) return Results.NotFound(); var now = Now(http); var opens = input.OpensAt ?? now;
            if (row.Status != "DRAFT" || row.Version != input.ExpectedVersion) return ApiOperations.Error(409, "STALE_REWARD", "Reload the current unpublished draft.");
            if (input.ClosesAt is { } closes && (closes <= opens || closes <= now)) return ApiOperations.Error(400, "INVALID_REWARD_WINDOW", "Closing time must follow opening time and the current time.");
            row.Status = "PUBLISHED"; row.OpensAt = opens; row.ClosesAt = input.ClosesAt; row.Version = Guid.NewGuid(); Audit(db, row, http, "PUBLISH");
            await db.SaveChangesAsync(ct); var ids = Grants(row); var definitions = await db.Set<ClubCosmeticDefinition>().Where(x => ids.Contains(x.Id)).ToArrayAsync(ct);
            return Results.Ok(Reward(row, definitions, now));
        }, ct);
    }
    private static async Task<IResult> Retire(int clubId, Guid rewardId, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await Manage(clubId, http, ct)) return Results.Forbid();
        var input = await BoundedJsonRequest.ReadAsync<RewardRetireInput>(http, 8192, ct);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000) return ApiOperations.Error(400, "REASON_REQUIRED", "Provide a retirement reason up to 1000 characters.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            if (!await Manage(clubId, http, ct)) return Results.Forbid();
            var row = await db.Set<ClubReward>().SingleOrDefaultAsync(x => x.Id == rewardId && x.ClubId == clubId, ct);
            if (row is null) return Results.NotFound();
            if (row.Version != input.ExpectedVersion || row.Status == "RETIRED") return ApiOperations.Error(409, "STALE_REWARD", "Reload the current reward.");
            row.Status = "RETIRED"; row.Version = Guid.NewGuid(); Audit(db, row, http, "RETIRE", input.Reason.Trim()); await db.SaveChangesAsync(ct);
            return Results.Ok(new { row.Id, row.ClubId, row.Status, row.Version });
        }, ct);
    }
    private static object Receipt(ClubRewardRedemption row) => new
    {
        receiptId = row.Id,
        row.UserId,
        row.ClubId,
        row.RewardId,
        row.RewardLabel,
        amount = row.Amount,
        grants = JsonSerializer.Deserialize<Guid[]>(row.GrantIdsJson, Json),
        balance = row.BalanceAfter,
        row.WalletVersion,
        row.SourceRevision,
        row.PolicyVersion,
        row.CreatedAt,
        unit = "CLUB_CREDIT"
    };
    private static async Task<IResult> Redeem(int clubId, Guid rewardId, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await Active(http, ct)) return Results.Forbid();
        var userId = http.User.GetUserId();
        if (await Club(clubId, userId, http, ct) is not { IsActive: true, IsApprovedMember: true }) return Results.Forbid();
        var input = await BoundedJsonRequest.ReadAsync<RewardRedeemInput>(http, 4096, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            if (!await Active(http, ct) || await Club(clubId, userId, http, ct) is not { IsActive: true, IsApprovedMember: true }) return Results.Forbid();
            var reward = await db.Set<ClubReward>().SingleOrDefaultAsync(x => x.Id == rewardId && x.ClubId == clubId, ct);
            if (reward is null) return Results.NotFound();
            var previous = await db.Set<ClubRewardRedemption>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.RewardId == rewardId, ct);
            if (previous is not null) return Results.Ok(Receipt(previous));
            if (reward.Version != input.ExpectedRewardVersion || Availability(reward, Now(http)) != "OPEN") return ApiOperations.Error(409, "REWARD_NOT_OPEN_OR_STALE", "Reload an open reward before redeeming.");
            var wallet = await db.Set<ClubCreditWallet>().SingleOrDefaultAsync(x => x.UserId == userId && x.ClubId == clubId, ct);
            var effects = await ClubCreditLedger.Source(db, userId, clubId).ToListAsync(ct); var before = ClubCreditLedger.Calculate(userId, clubId, effects, wallet);
            if (!await ClubCreditLedger.Reconciles(db, userId, clubId, before.Spent, ct)) return ApiOperations.Error(503, "WALLET_RECONCILIATION_REQUIRED", "Wallet debit history must be reconciled before spending.");
            if (before.Version != input.ExpectedWalletVersion) return ApiOperations.Error(409, "STALE_WALLET", "Credit or spending changed. Reload the wallet.");
            var ids = Grants(reward);
            var owned = await db.Set<ClubCosmeticOwnership>().Where(x => x.UserId == userId && ids.Contains(x.CosmeticId)).Select(x => x.CosmeticId).ToArrayAsync(ct);
            var missing = ids.Except(owned).ToArray(); var charge = missing.Length == 0 ? 0 : reward.Price;
            if (ids.Length == 0 || await db.Set<ClubCosmeticDefinition>().CountAsync(x => ids.Contains(x.Id) && x.ClubId == clubId, ct) != ids.Length)
                return ApiOperations.Error(409, "INVALID_REWARD_GRANTS", "The reward catalog cannot grant these items.");
            if (before.Available < 0 || before.Available < charge) return ApiOperations.Error(409, "INSUFFICIENT_CREDIT", "Available club credit is insufficient; debt must be repaid before further redemption.");
            if (wallet is null) { wallet = new ClubCreditWallet { UserId = userId, ClubId = clubId }; db.Add(wallet); }
            wallet.Spent += charge; wallet.Version = Guid.NewGuid(); var after = ClubCreditLedger.Calculate(userId, clubId, effects, wallet);
            var receipt = new ClubRewardRedemption
            {
                RewardId = rewardId,
                UserId = userId,
                ClubId = clubId,
                Amount = charge,
                GrantIdsJson = JsonSerializer.Serialize(ids, Json),
                RewardLabel = reward.Label,
                RewardVersion = reward.Version,
                SourceRevision = before.SourceRevision,
                WalletVersion = after.Version,
                BalanceAfter = after.Available,
                CreatedAt = Now(http)
            };
            db.Add(receipt); db.AddRange(missing.Select(id => new ClubCosmeticOwnership { UserId = userId, CosmeticId = id, ReceiptId = receipt.Id, AcquiredAt = receipt.CreatedAt }));
            await db.SaveChangesAsync(ct); return Results.Ok(Receipt(receipt));
        }, ct);
    }
    private static async Task<IResult> Receipts(int clubId, int? page, int? pageSize, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store"; if (!await Active(http, ct)) return Results.Forbid();
        var query = db.Set<ClubRewardRedemption>().AsNoTracking().Where(x => x.UserId == http.User.GetUserId() && x.ClubId == clubId);
        var total = await query.CountAsync(ct); var p = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((int)Math.Min((long)(p - 1) * size, int.MaxValue)).Take(size).ToArrayAsync(ct);
        return Results.Ok(new { userId = http.User.GetUserId(), clubId, items = rows.Select(Receipt), page = p, pageSize = size, totalItems = total, totalPages = (int)Math.Ceiling((decimal)total / size) });
    }
    private static async Task<IResult> Inventory(int? page, int? pageSize, AdminDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store"; if (!await Active(http, ct)) return Results.Forbid();
        var query = db.Set<ClubCosmeticOwnership>().AsNoTracking().Where(x => x.UserId == http.User.GetUserId()); var total = await query.CountAsync(ct);
        var p = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 100, 1, 100);
        var rows = await query.OrderBy(x => x.CosmeticId).Skip((int)Math.Min((long)(p - 1) * size, int.MaxValue)).Take(size).ToArrayAsync(ct); var ids = rows.Select(x => x.CosmeticId).ToArray();
        var definitions = await db.Set<ClubCosmeticDefinition>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(ct);
        return Results.Ok(new
        {
            userId = http.User.GetUserId(),
            items = rows.Select(x => new { cosmetic = Definition(definitions.Single(d => d.Id == x.CosmeticId)), sourceReceiptId = x.ReceiptId, x.AcquiredAt }),
            page = p,
            pageSize = size,
            totalItems = total,
            totalPages = (int)Math.Ceiling((decimal)total / size)
        });
    }
}
