using AdminService.Auditing;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Errors;
using AdminService.Security;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class QuestEndpoints
{
    private static readonly (string Title, string Description, string Category, string Kind, string Scope, int RewardXp, int Target, string Status, string Icon)[] DefaultQuests =
    [
        (
            "Chiến dịch Xanh: Một giờ vì Trái Đất",
            "Tham gia chuỗi hoạt động thu gom rác thải, trồng cây xanh và lan tỏa lối sống bền vững trong khuôn viên trường.",
            "Community",
            "Chiến dịch",
            "Toàn bộ sinh viên",
            40,
            200,
            "published",
            "leaf"
        ),
        (
            "Workshop AI & Khởi nghiệp Đổi mới sáng tạo",
            "Tham dự chuỗi hội thảo chuyên sâu về Trí tuệ nhân tạo thế hệ mới và xây dựng đề án khởi nghiệp công nghệ.",
            "Entrepreneurship",
            "Cá nhân",
            "Toàn bộ sinh viên",
            50,
            120,
            "published",
            "sparkles"
        ),
        (
            "Thử thách Đọc sách & Nghiên cứu Khoa học",
            "Đọc và tóm tắt ít nhất 3 bài báo khoa học hoặc sách chuyên ngành, chia sẻ bài cảm nhận với cộng đồng.",
            "Research",
            "Cá nhân",
            "Tân sinh viên",
            30,
            80,
            "published",
            "target"
        ),
        (
            "Giải đấu Thể thao & Rèn luyện Thể chất",
            "Đăng ký thi đấu giao hữu các môn thể thao tập thể nhằm nâng cao thể lực và thắt chặt tinh thần đồng đội.",
            "CultureSports",
            "Theo nhóm",
            "Toàn bộ sinh viên",
            45,
            150,
            "draft",
            "sparkles"
        )
    ];

    public static IEndpointRouteBuilder MapQuestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapGroup(endpoints, "/api/v1/quests");
        MapGroup(endpoints, "/api/quests");

        return endpoints;
    }

    private static void MapGroup(IEndpointRouteBuilder endpoints, string prefix)
    {
        var group = endpoints.MapGroup(prefix)
            .WithTags("Quests and Campaigns");

        group.MapGet("/", GetQuestsAsync)
            .WithName($"GetQuests_{prefix.Replace("/", "_")}")
            .AllowAnonymous()
            .Produces<IReadOnlyList<QuestResponse>>();

        group.MapGet("/me", GetMyQuestsAsync)
            .WithName($"GetMyQuests_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.AnyActor)
            .Produces<IReadOnlyList<QuestParticipantResponse>>();

        group.MapGet("/{id}", GetQuestByIdAsync)
            .WithName($"GetQuestById_{prefix.Replace("/", "_")}")
            .AllowAnonymous()
            .Produces<QuestResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateQuestAsync)
            .WithName($"CreateQuest_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<QuestResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/{id}", UpdateQuestAsync)
            .WithName($"UpdateQuest_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<QuestResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/{id}/status", UpdateQuestStatusAsync)
            .WithName($"UpdateQuestStatus_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<QuestResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}", DeleteQuestAsync)
            .WithName($"DeleteQuest_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/join", JoinQuestAsync)
            .WithName($"JoinQuest_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.AnyActor)
            .Produces<QuestParticipantResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/complete", CompleteQuestAsync)
            .WithName($"CompleteQuest_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.AnyActor)
            .Produces<QuestParticipantResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetQuestsAsync(
        string? semester,
        string? period,
        string? season,
        string? status,
        string? category,
        string? search,
        string? campusCode,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await EnsureDefaultQuestsAsync(dbContext, cancellationToken);

        var targetSemester = semester ?? period ?? season;

        var query = dbContext.Quests
            .AsNoTracking()
            .Include(x => x.Participants)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(targetSemester))
        {
            var sem = targetSemester.Trim();
            query = query.Where(x => x.SemesterCode == sem);
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var st = status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status.ToLower() == st);
        }

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var cat = category.Trim();
            query = query.Where(x => x.Category.Contains(cat));
        }

        if (!string.IsNullOrWhiteSpace(campusCode) && !campusCode.Equals(CampusCodes.Global, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.CampusCode == CampusCodes.Global || x.CampusCode == campusCode);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Title.Contains(term) || x.Description.Contains(term));
        }

        var quests = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var responses = quests.Select(q =>
        {
            var joined = q.Participants.Count;
            var completed = q.Participants.Count(p => p.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
            return QuestResponse.From(q, joined, completed);
        }).ToList();

        return Results.Ok(responses);
    }

    private static async Task<IResult> GetQuestByIdAsync(
        string id,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId))
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var quest = await dbContext.Quests
            .AsNoTracking()
            .Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);

        if (quest is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var joined = quest.Participants.Count;
        var completed = quest.Participants.Count(p => p.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
        return Results.Ok(QuestResponse.From(quest, joined, completed));
    }

    private static async Task<IResult> CreateQuestAsync(
        CreateQuestRequest request,
        AdminDbContext dbContext,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        var title = (request.Title ?? request.Name)?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return Results.BadRequest(new { message = "Tên nhiệm vụ không được để trống." });
        }

        var description = request.Description?.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            return Results.BadRequest(new { message = "Mô tả và điều kiện hoàn thành không được để trống." });
        }

        DateTimeOffset? deadline = null;
        var deadlineInput = request.Deadline ?? request.DueDate;
        if (!string.IsNullOrWhiteSpace(deadlineInput) && DateTimeOffset.TryParse(deadlineInput, out var parsedDeadline))
        {
            deadline = parsedDeadline;
        }

        var quest = new Quest
        {
            Title = title,
            Description = description,
            Category = request.Category?.Trim() ?? request.Tag?.Trim() ?? "Community",
            Kind = request.Kind?.Trim() ?? request.ReportType?.Trim() ?? "Cá nhân",
            Scope = request.Scope?.Trim() ?? "Toàn bộ sinh viên",
            RewardXp = request.RewardXp ?? request.Reward ?? 30,
            TargetParticipants = request.TargetParticipants ?? request.Target ?? 100,
            SemesterCode = request.SemesterCode?.Trim() ?? request.Period?.Trim() ?? "FALL2026",
            CampusCode = request.CampusCode?.Trim() ?? currentActor.CampusCode ?? "GLOBAL",
            Icon = request.Icon?.Trim() ?? "sparkles",
            DeadlineUtc = deadline,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "published" : request.Status.Trim().ToLowerInvariant(),
            CreatedByUserId = currentActor.UserId,
            CreatedByName = currentActor.Email ?? currentActor.SubjectId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.Quests.Add(quest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Json(QuestResponse.From(quest, 0, 0), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateQuestAsync(
        string id,
        UpdateQuestRequest request,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId))
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var quest = await dbContext.Quests
            .Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);

        if (quest is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var title = (request.Title ?? request.Name)?.Trim();
        if (!string.IsNullOrWhiteSpace(title))
        {
            quest.Title = title;
        }

        if (request.Description is not null)
        {
            quest.Description = request.Description.Trim();
        }

        var category = request.Category ?? request.Tag;
        if (!string.IsNullOrWhiteSpace(category))
        {
            quest.Category = category.Trim();
        }

        var kind = request.Kind ?? request.ReportType;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            quest.Kind = kind.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            quest.Scope = request.Scope.Trim();
        }

        if (request.RewardXp.HasValue || request.Reward.HasValue)
        {
            quest.RewardXp = request.RewardXp ?? request.Reward!.Value;
        }

        if (request.TargetParticipants.HasValue || request.Target.HasValue)
        {
            quest.TargetParticipants = request.TargetParticipants ?? request.Target!.Value;
        }

        var semester = request.SemesterCode ?? request.Period;
        if (!string.IsNullOrWhiteSpace(semester))
        {
            quest.SemesterCode = semester.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.CampusCode))
        {
            quest.CampusCode = request.CampusCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Icon))
        {
            quest.Icon = request.Icon.Trim();
        }

        var deadlineInput = request.Deadline ?? request.DueDate;
        if (!string.IsNullOrWhiteSpace(deadlineInput) && DateTimeOffset.TryParse(deadlineInput, out var parsedDeadline))
        {
            quest.DeadlineUtc = parsedDeadline;
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            quest.Status = request.Status.Trim().ToLowerInvariant();
        }

        quest.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var joined = quest.Participants.Count;
        var completed = quest.Participants.Count(p => p.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
        return Results.Ok(QuestResponse.From(quest, joined, completed));
    }

    private static async Task<IResult> UpdateQuestStatusAsync(
        string id,
        UpdateQuestStatusRequest request,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId))
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var quest = await dbContext.Quests
            .Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);

        if (quest is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var validStatuses = new[] { "draft", "published", "paused", "completed" };
        var next = request.Status?.Trim().ToLowerInvariant() ?? "";
        if (!validStatuses.Contains(next))
        {
            return Results.BadRequest(new { message = "Trạng thái không hợp lệ. Cho phép: draft, published, paused, completed." });
        }

        quest.Status = next;
        quest.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var joined = quest.Participants.Count;
        var completed = quest.Participants.Count(p => p.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
        return Results.Ok(QuestResponse.From(quest, joined, completed));
    }

    private static async Task<IResult> DeleteQuestAsync(
        string id,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId))
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var quest = await dbContext.Quests
            .Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);

        if (quest is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        // Only permit deleting if not completed by any participants
        if (quest.Participants.Any(p => p.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)))
        {
            return Results.BadRequest(new { message = "Không thể xóa nhiệm vụ đã có sinh viên hoàn thành ghi nhận điểm." });
        }

        dbContext.Quests.Remove(quest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> JoinQuestAsync(
        string id,
        JoinQuestRequest? request,
        AdminDbContext dbContext,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId))
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var quest = await dbContext.Quests.SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);
        if (quest is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        if (quest.Status.Equals("paused", StringComparison.OrdinalIgnoreCase) || quest.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "Nhiệm vụ đang tạm dừng hoặc đã kết thúc, không thể tham gia." });
        }

        var studentUserId = currentActor.UserId ?? 0;
        if (studentUserId <= 0)
        {
            return Results.BadRequest(new { message = "Yêu cầu tài khoản sinh viên hợp lệ để tham gia." });
        }

        var existing = await dbContext.QuestParticipants
            .SingleOrDefaultAsync(x => x.QuestId == parsedId && x.StudentUserId == studentUserId, cancellationToken);

        if (existing is not null)
        {
            return Results.Ok(QuestParticipantResponse.From(existing));
        }

        var studentName = currentActor.Email?.Split('@')[0] ?? currentActor.SubjectId ?? "Sinh viên";
        var participant = new QuestParticipant
        {
            QuestId = parsedId,
            StudentUserId = studentUserId,
            StudentName = studentName,
            StudentEmail = currentActor.Email ?? string.Empty,
            CampusCode = currentActor.CampusCode ?? "HAN",
            Status = "Joined",
            JoinedAtUtc = DateTimeOffset.UtcNow,
            Note = request?.Note?.Trim()
        };

        dbContext.QuestParticipants.Add(participant);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Json(QuestParticipantResponse.From(participant), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> CompleteQuestAsync(
        string id,
        CompleteQuestRequest? request,
        AdminDbContext dbContext,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId))
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var quest = await dbContext.Quests.SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);
        if (quest is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var targetUserId = request?.StudentUserId ?? currentActor.UserId ?? 0;
        if (targetUserId <= 0)
        {
            return Results.BadRequest(new { message = "Yêu cầu mã sinh viên hợp lệ." });
        }

        var participant = await dbContext.QuestParticipants
            .SingleOrDefaultAsync(x => x.QuestId == parsedId && x.StudentUserId == targetUserId, cancellationToken);

        var verifierName = currentActor.Email ?? currentActor.SubjectId ?? "Cán bộ CTSV";
        var studentName = currentActor.Email?.Split('@')[0] ?? currentActor.SubjectId ?? "Sinh viên";

        if (participant is null)
        {
            // Auto join and complete
            participant = new QuestParticipant
            {
                QuestId = parsedId,
                StudentUserId = targetUserId,
                StudentName = studentName,
                StudentEmail = currentActor.Email ?? string.Empty,
                CampusCode = currentActor.CampusCode ?? "HAN",
                Status = "Completed",
                JoinedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                VerifiedByName = verifierName,
                Note = request?.Note?.Trim()
            };
            dbContext.QuestParticipants.Add(participant);
        }
        else
        {
            participant.Status = "Completed";
            participant.CompletedAtUtc = DateTimeOffset.UtcNow;
            participant.VerifiedByName = verifierName;
            if (!string.IsNullOrWhiteSpace(request?.Note))
            {
                participant.Note = request.Note.Trim();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(QuestParticipantResponse.From(participant));
    }

    private static async Task<IResult> GetMyQuestsAsync(
        AdminDbContext dbContext,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        var studentUserId = currentActor.UserId ?? 0;
        if (studentUserId <= 0)
        {
            return Results.Ok(Array.Empty<QuestParticipantResponse>());
        }

        var myQuests = await dbContext.QuestParticipants
            .AsNoTracking()
            .Where(x => x.StudentUserId == studentUserId)
            .OrderByDescending(x => x.JoinedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(myQuests.Select(QuestParticipantResponse.From).ToList());
    }

    private static async Task EnsureDefaultQuestsAsync(AdminDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await dbContext.Quests.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            var seeded = DefaultQuests.Select(def => new Quest
            {
                Title = def.Title,
                Description = def.Description,
                Category = def.Category,
                Kind = def.Kind,
                Scope = def.Scope,
                RewardXp = def.RewardXp,
                TargetParticipants = def.Target,
                SemesterCode = "FALL2026",
                CampusCode = "GLOBAL",
                Icon = def.Icon,
                DeadlineUtc = now.AddMonths(3),
                Status = def.Status,
                CreatedByName = "Cán bộ CTSV",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }).ToList();

            dbContext.Quests.AddRange(seeded);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
