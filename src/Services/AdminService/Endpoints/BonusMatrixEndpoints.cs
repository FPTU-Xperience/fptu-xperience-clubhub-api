using AdminService.Contracts;
using AdminService.Data;
using AdminService.Security;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class BonusMatrixEndpoints
{
    private static readonly (int Position, string Label, int Multiplier)[] DefaultNodes =
    [
        (0, "Ngưỡng cơ bản", 100),
        (50, "Ngưỡng trung bình", 80),
        (100, "Ngưỡng cao", 60),
        (150, "Ngưỡng xuất sắc", 40)
    ];

    public static IEndpointRouteBuilder MapBonusMatrixEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Support both /api/bonus-matrix and /api/v1/bonus-matrix
        MapGroup(endpoints, "/api/bonus-matrix");
        MapGroup(endpoints, "/api/v1/bonus-matrix");

        return endpoints;
    }

    private static void MapGroup(IEndpointRouteBuilder endpoints, string prefix)
    {
        var group = endpoints.MapGroup(prefix)
            .WithTags("Bonus Matrix");

        group.MapGet("/", GetAllNodesAsync)
            .WithName($"GetAllBonusMatrixNodes_{prefix.Replace("/", "_")}")
            .AllowAnonymous()
            .Produces<BonusMatrixSuccessResponse<IReadOnlyList<BonusMatrixNodeResponse>>>();

        group.MapPost("/", CreateNodeAsync)
            .WithName($"CreateBonusMatrixNode_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<BonusMatrixSuccessResponse<BonusMatrixNodeResponse>>(StatusCodes.Status201Created)
            .Produces<BonusMatrixErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapPut("/{id}", UpdateNodeAsync)
            .WithName($"UpdateBonusMatrixNode_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<BonusMatrixSuccessResponse<BonusMatrixNodeResponse>>()
            .Produces<BonusMatrixErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<BonusMatrixErrorResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}", DeleteNodeAsync)
            .WithName($"DeleteBonusMatrixNode_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<BonusMatrixMessageResponse>()
            .Produces<BonusMatrixErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<BonusMatrixErrorResponse>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetAllNodesAsync(
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultNodesAsync(dbContext, cancellationToken);

        var nodes = await dbContext.BonusMatrixNodes
            .AsNoTracking()
            .OrderBy(x => x.Position)
            .ToListAsync(cancellationToken);

        var responses = nodes.Select(BonusMatrixNodeResponse.From).ToList();
        return Results.Json(new BonusMatrixSuccessResponse<IReadOnlyList<BonusMatrixNodeResponse>>(true, responses));
    }

    private static async Task<IResult> CreateNodeAsync(
        CreateBonusMatrixNodeRequest request,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultNodesAsync(dbContext, cancellationToken);

        if (!request.Position.HasValue || !IsValidStep(request.Position.Value))
        {
            return ErrorResult("INVALID_POSITION", "Position phải là số nguyên trong khoảng 0–200 và chia hết cho 5.");
        }

        var position = request.Position.Value;
        if (await dbContext.BonusMatrixNodes.AnyAsync(x => x.Position == position, cancellationToken))
        {
            return ErrorResult("INVALID_POSITION", $"Position {position} đã tồn tại trong hệ thống.");
        }

        var label = request.Label?.Trim();
        if (!string.IsNullOrEmpty(label) && label.Length > 60)
        {
            return ErrorResult("INVALID_LABEL", "Label không được vượt quá 60 ký tự.");
        }

        if (string.IsNullOrEmpty(label))
        {
            label = $"Ngưỡng {position}";
        }

        if (!request.Multiplier.HasValue || !IsValidStep(request.Multiplier.Value))
        {
            return ErrorResult("INVALID_MULTIPLIER", "Multiplier phải là số nguyên trong khoảng 0–200 và chia hết cho 5.");
        }

        var node = new BonusMatrixNode
        {
            Position = position,
            Label = label,
            Multiplier = request.Multiplier.Value,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.BonusMatrixNodes.Add(node);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Json(
            new BonusMatrixSuccessResponse<BonusMatrixNodeResponse>(true, BonusMatrixNodeResponse.From(node)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateNodeAsync(
        string id,
        UpdateBonusMatrixNodeRequest request,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultNodesAsync(dbContext, cancellationToken);
        if (!Guid.TryParse(id, out var parsedId))
        {
            return ErrorResult("NOT_FOUND", "Không tìm thấy ngưỡng với ID được cung cấp.", StatusCodes.Status404NotFound);
        }

        var node = await dbContext.BonusMatrixNodes
            .SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);

        if (node is null)
        {
            return ErrorResult("NOT_FOUND", "Không tìm thấy ngưỡng với ID được cung cấp.", StatusCodes.Status404NotFound);
        }

        if (request.Position.HasValue)
        {
            if (!IsValidStep(request.Position.Value))
            {
                return ErrorResult("INVALID_POSITION", "Position phải là số nguyên trong khoảng 0–200 và chia hết cho 5.");
            }

            if (await dbContext.BonusMatrixNodes.AnyAsync(x => x.Id != parsedId && x.Position == request.Position.Value, cancellationToken))
            {
                return ErrorResult("INVALID_POSITION", $"Position {request.Position.Value} đã tồn tại trong hệ thống.");
            }

            node.Position = request.Position.Value;
        }

        if (request.Label is not null)
        {
            var trimmed = request.Label.Trim();
            if (trimmed.Length > 60)
            {
                return ErrorResult("INVALID_LABEL", "Label không được vượt quá 60 ký tự.");
            }

            node.Label = string.IsNullOrEmpty(trimmed) ? $"Ngưỡng {node.Position}" : trimmed;
        }

        if (request.Multiplier.HasValue)
        {
            if (!IsValidStep(request.Multiplier.Value))
            {
                return ErrorResult("INVALID_MULTIPLIER", "Multiplier phải là số nguyên trong khoảng 0–200 và chia hết cho 5.");
            }

            node.Multiplier = request.Multiplier.Value;
        }

        node.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Json(new BonusMatrixSuccessResponse<BonusMatrixNodeResponse>(true, BonusMatrixNodeResponse.From(node)));
    }

    private static async Task<IResult> DeleteNodeAsync(
        string id,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultNodesAsync(dbContext, cancellationToken);

        if (!Guid.TryParse(id, out var parsedId))
        {
            return ErrorResult("NOT_FOUND", "Không tìm thấy ngưỡng với ID được cung cấp.", StatusCodes.Status404NotFound);
        }

        var node = await dbContext.BonusMatrixNodes
            .SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);

        if (node is null)
        {
            return ErrorResult("NOT_FOUND", "Không tìm thấy ngưỡng với ID được cung cấp.", StatusCodes.Status404NotFound);
        }

        var count = await dbContext.BonusMatrixNodes.CountAsync(cancellationToken);
        if (count <= 1)
        {
            return ErrorResult("CANNOT_DELETE_LAST_NODE", "Không thể xóa. Phải có ít nhất 1 ngưỡng trong hệ thống.");
        }

        dbContext.BonusMatrixNodes.Remove(node);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Json(new BonusMatrixMessageResponse(true, "Đã xóa ngưỡng thành công."));
    }

    private static async Task EnsureDefaultNodesAsync(AdminDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await dbContext.BonusMatrixNodes.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            var seededNodes = DefaultNodes.Select(def => new BonusMatrixNode
            {
                Position = def.Position,
                Label = def.Label,
                Multiplier = def.Multiplier,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }).ToList();

            dbContext.BonusMatrixNodes.AddRange(seededNodes);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsValidStep(int value) =>
        value is >= 0 and <= 200 && value % 5 == 0;

    private static IResult ErrorResult(string code, string message, int statusCode = StatusCodes.Status400BadRequest) =>
        Results.Json(
            new BonusMatrixErrorResponse(false, new BonusMatrixErrorDetails(code, message)),
            statusCode: statusCode);
}
