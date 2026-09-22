using ActivityService.Data;
using ActivityService.Models;
using AuthService.Data;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Models;
using ExportService.Data;
using FinanceService.Data;
using FinanceService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NotificationService.Data;
using NotificationService.Models;
using ReportService.Data;
using ReportService.Models;
using StackExchange.Redis;
using ReportEntity = ReportService.Models.Report;

namespace DemoDataSeeder;

public sealed class DemoDatasetSeeder(DemoSeederOptions options)
{
    private static readonly string[] AllDefinedRoles =
    [
        AuthRoles.Admin,
        AuthRoles.SystemAdmin,
        AuthRoles.StudentAffairsAdmin,
        AuthRoles.ClubManager,
        AuthRoles.Treasurer,
        AuthRoles.ClubMember
    ];

    private static readonly HashSet<string> DemoActorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        AuthRoles.Admin,
        AuthRoles.ClubManager,
        AuthRoles.ClubMember
    };

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var catalog = DemoCatalog.Build(options.ReferenceDate, options.IdentityOverrides);
        var definitionErrors = catalog.ValidateDefinition();
        if (definitionErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "The demo catalog is invalid:\n- " + string.Join("\n- ", definitionErrors));
        }

        await using var authDb = CreateAuthDb();
        await using var clubDb = CreateClubDb();
        await using var activityDb = CreateActivityDb();
        await using var reportDb = CreateReportDb();
        await using var financeDb = CreateFinanceDb();
        await using var notificationDb = CreateNotificationDb();
        await using var exportDb = CreateExportDb();

        Console.WriteLine($"Demo reference date: {catalog.ReferenceDate:yyyy-MM-dd}");
        await WaitForServiceHealthAsync(cancellationToken);
        Console.WriteLine("Applying schema upgraders and waiting for all service schemas...");
        await ActivityService.Data.ActivitySchemaUpgrader.ApplyAsync(activityDb, cancellationToken);
        await ReportService.Data.ReportSchemaUpgrader.ApplyAsync(reportDb, cancellationToken);
        await FinanceService.Data.FinanceSchemaUpgrader.ApplyAsync(financeDb, cancellationToken);

        await WaitForSchemasAsync(
            authDb,
            clubDb,
            activityDb,
            reportDb,
            financeDb,
            notificationDb,
            exportDb,
            cancellationToken);

        if (options.ValidateOnly)
        {
            Console.WriteLine("Validation-only mode: no data was changed.");
            await VerifyAsync(catalog, authDb, clubDb, activityDb, reportDb, financeDb, notificationDb, cancellationToken);
            return;
        }

        if (options.ResetAll)
        {
            Console.WriteLine("ResetAll=true: clearing data from the seven demo databases...");
            await ResetAllAsync(authDb, clubDb, activityDb, reportDb, financeDb, notificationDb, exportDb, cancellationToken);
            await TrimIntegrationEventStreamAsync(cancellationToken);
            await notificationDb.ProcessedEvents.ExecuteDeleteAsync(cancellationToken);
            await notificationDb.Notifications.ExecuteDeleteAsync(cancellationToken);
            notificationDb.ChangeTracker.Clear();
            Console.WriteLine("Existing demo data was cleared. Role definitions were retained.");
        }

        await SeedCatalogAsync(
            catalog,
            authDb,
            clubDb,
            activityDb,
            reportDb,
            financeDb,
            notificationDb,
            cancellationToken);
    }

    internal async Task SeedCatalogAsync(
        DemoCatalog catalog,
        AuthDbContext authDb,
        ClubDbContext clubDb,
        ActivityDbContext activityDb,
        ReportDbContext reportDb,
        FinanceDbContext financeDb,
        NotificationDbContext notificationDb,
        CancellationToken cancellationToken = default)
    {
        var definitionErrors = catalog.ValidateDefinition();
        if (definitionErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "The demo catalog is invalid:\n- " + string.Join("\n- ", definitionErrors));
        }

        Console.WriteLine("Seeding users and the three actor groups...");
        var users = await SeedUsersAsync(catalog, authDb, cancellationToken);

        Console.WriteLine("Seeding clubs, manager assignments, memberships, and applications...");
        var clubs = await SeedClubsAsync(catalog, users, clubDb, cancellationToken);

        Console.WriteLine("Seeding reports, details, feedback, and audit history...");
        var reports = await SeedReportsAsync(catalog, users, clubs, reportDb, cancellationToken);

        Console.WriteLine("Seeding activities, participants, and attendance history...");
        var activities = await SeedActivitiesAsync(catalog, users, clubs, reports, activityDb, reportDb, cancellationToken);

        Console.WriteLine("Seeding budget proposals, settlements, and finance transactions...");
        await SeedFinanceAsync(catalog, users, clubs, reports, activities, financeDb, reportDb, cancellationToken);

        Console.WriteLine("Seeding notifications for admin, club managers, and students...");
        await SeedNotificationsAsync(catalog, users, notificationDb, cancellationToken);

        Console.WriteLine("Running cross-service integrity checks...");
        await VerifyAsync(catalog, authDb, clubDb, activityDb, reportDb, financeDb, notificationDb, cancellationToken);
    }

    private AuthDbContext CreateAuthDb() => new(
        new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer(options.AuthConnectionString, ConfigureSqlServer)
            .Options);

    private ClubDbContext CreateClubDb() => new(
        new DbContextOptionsBuilder<ClubDbContext>()
            .UseSqlServer(options.ClubConnectionString, ConfigureSqlServer)
            .Options);

    private ActivityDbContext CreateActivityDb() => new(
        new DbContextOptionsBuilder<ActivityDbContext>()
            .UseSqlServer(options.ActivityConnectionString, ConfigureSqlServer)
            .Options);

    private ReportDbContext CreateReportDb() => new(
        new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlServer(options.ReportConnectionString, ConfigureSqlServer)
            .Options);

    private FinanceDbContext CreateFinanceDb() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(options.FinanceConnectionString, ConfigureSqlServer)
            .Options);

    private NotificationDbContext CreateNotificationDb() => new(
        new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer(options.NotificationConnectionString, ConfigureSqlServer)
            .Options);

    private ExportDbContext CreateExportDb() => new(
        new DbContextOptionsBuilder<ExportDbContext>()
            .UseSqlServer(options.ExportConnectionString, ConfigureSqlServer)
            .Options);

    private static void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sql) =>
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);

    private async Task WaitForServiceHealthAsync(CancellationToken cancellationToken)
    {
        var configuredUrls = Environment.GetEnvironmentVariable("DemoData__ServiceHealthUrls");
        if (string.IsNullOrWhiteSpace(configuredUrls))
        {
            Console.WriteLine("No service health URLs configured; continuing with database schema checks.");
            return;
        }

        var healthUrls = configuredUrls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => new Uri(value, UriKind.Absolute))
            .ToArray();
        var deadline = DateTimeOffset.UtcNow.Add(options.DatabaseWaitTimeout);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        Exception? lastError = null;

        Console.WriteLine($"Waiting for {healthUrls.Length} application services to finish startup...");
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var healthUrl in healthUrls)
                {
                    using var response = await client.GetAsync(healthUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();
                }

                Console.WriteLine("All application services are healthy.");
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new TimeoutException(
            $"Application services were not healthy after {options.DatabaseWaitTimeout.TotalSeconds:0} seconds.",
            lastError);
    }

    private async Task WaitForSchemasAsync(
        AuthDbContext authDb,
        ClubDbContext clubDb,
        ActivityDbContext activityDb,
        ReportDbContext reportDb,
        FinanceDbContext financeDb,
        NotificationDbContext notificationDb,
        ExportDbContext exportDb,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(options.DatabaseWaitTimeout);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await authDb.Users.AsNoTracking().Take(1).CountAsync(cancellationToken);
                await clubDb.Clubs.AsNoTracking().Take(1).CountAsync(cancellationToken);
                await activityDb.Activities.AsNoTracking().Take(1).CountAsync(cancellationToken);
                await reportDb.Reports.AsNoTracking().Take(1).CountAsync(cancellationToken);
                await financeDb.BudgetProposals.AsNoTracking().Take(1).CountAsync(cancellationToken);
                await notificationDb.Notifications.AsNoTracking().Take(1).CountAsync(cancellationToken);
                await exportDb.ExportRequests.AsNoTracking().Take(1).CountAsync(cancellationToken);
                Console.WriteLine("All database schemas are ready.");
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastError = exception;
                authDb.ChangeTracker.Clear();
                clubDb.ChangeTracker.Clear();
                activityDb.ChangeTracker.Clear();
                reportDb.ChangeTracker.Clear();
                financeDb.ChangeTracker.Clear();
                notificationDb.ChangeTracker.Clear();
                exportDb.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new TimeoutException(
            $"Service schemas were not ready after {options.DatabaseWaitTimeout.TotalSeconds:0} seconds.",
            lastError);
    }

    private static async Task ResetAllAsync(
        AuthDbContext authDb,
        ClubDbContext clubDb,
        ActivityDbContext activityDb,
        ReportDbContext reportDb,
        FinanceDbContext financeDb,
        NotificationDbContext notificationDb,
        ExportDbContext exportDb,
        CancellationToken cancellationToken)
    {
        await exportDb.ExportFiles.ExecuteDeleteAsync(cancellationToken);
        await exportDb.ExportRequests.ExecuteDeleteAsync(cancellationToken);

        await activityDb.ActivityAttendances.ExecuteDeleteAsync(cancellationToken);
        await activityDb.ActivityParticipants.ExecuteDeleteAsync(cancellationToken);
        await activityDb.Activities.ExecuteDeleteAsync(cancellationToken);

        await financeDb.Settlements.ExecuteDeleteAsync(cancellationToken);
        await financeDb.FinanceTransactions.ExecuteDeleteAsync(cancellationToken);
        await financeDb.OutboxMessages.ExecuteDeleteAsync(cancellationToken);
        await financeDb.BudgetProposals.ExecuteDeleteAsync(cancellationToken);

        await reportDb.ReportAttachments.ExecuteDeleteAsync(cancellationToken);
        await reportDb.ReportUploadedFiles.ExecuteDeleteAsync(cancellationToken);
        await reportDb.ReportFeedback.ExecuteDeleteAsync(cancellationToken);
        await reportDb.ReportDetails.ExecuteDeleteAsync(cancellationToken);
        await reportDb.AuditLogs.ExecuteDeleteAsync(cancellationToken);
        await reportDb.OutboxMessages.ExecuteDeleteAsync(cancellationToken);
        await reportDb.Reports.ExecuteDeleteAsync(cancellationToken);
        await reportDb.ReportingDeadlines.ExecuteDeleteAsync(cancellationToken);

        await clubDb.ClubOwnershipTransfers.ExecuteDeleteAsync(cancellationToken);
        await clubDb.ClubDisbandRequests.ExecuteDeleteAsync(cancellationToken);
        await clubDb.ClubCreationApplications.ExecuteDeleteAsync(cancellationToken);
        await clubDb.ClubManagerAssignments.ExecuteDeleteAsync(cancellationToken);
        await clubDb.ClubMemberships.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await clubDb.Clubs.ExecuteDeleteAsync(cancellationToken);

        await authDb.RefreshTokens.ExecuteDeleteAsync(cancellationToken);
        await authDb.UserRoles.ExecuteDeleteAsync(cancellationToken);
        await authDb.Users.ExecuteDeleteAsync(cancellationToken);

        ClearTrackers(authDb, clubDb, activityDb, reportDb, financeDb, notificationDb, exportDb);
    }

    private static async Task TrimIntegrationEventStreamAsync(CancellationToken cancellationToken)
    {
        var connectionString = Environment.GetEnvironmentVariable("Redis__ConnectionString");
        var streamName = Environment.GetEnvironmentVariable("Redis__StreamName");
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(streamName))
        {
            Console.WriteLine("Redis stream trimming skipped (Redis is not configured).");
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
            var database = connection.GetDatabase();
            var removedEntries = await database.StreamTrimAsync(
                streamName,
                maxLength: 0,
                useApproximateMaxLength: false);
            Console.WriteLine($"Trimmed {removedEntries} stale integration events from Redis stream '{streamName}'.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not trim Redis stream: {ex.Message}");
        }
    }

    private static void ClearTrackers(params DbContext[] contexts)
    {
        foreach (var context in contexts)
        {
            context.ChangeTracker.Clear();
        }
    }

    private async Task<Dictionary<string, User>> SeedUsersAsync(
        DemoCatalog catalog,
        AuthDbContext db,
        CancellationToken cancellationToken)
    {
        foreach (var roleName in AllDefinedRoles)
        {
            if (!await db.Roles.AnyAsync(x => x.Name == roleName, cancellationToken))
            {
                db.Roles.Add(new AuthService.Models.Role { Name = roleName });
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        var roles = await db.Roles.ToDictionaryAsync(x => x.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var spec in catalog.Users)
        {
            var user = await db.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .SingleOrDefaultAsync(x => x.Username == spec.Username, cancellationToken);

            if (user is null)
            {
                user = new User
                {
                    Username = spec.Username,
                    CreatedAtUtc = At(catalog.ReferenceDate.AddDays(-250), 2)
                };
                db.Users.Add(user);
            }

            user.FullName = spec.FullName;
            user.Email = spec.Username;
            user.IsActive = true;
            user.IsLocked = false;

            var extraRoles = user.UserRoles
                .Where(x => !string.Equals(x.Role.Name, spec.Role, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (extraRoles.Length > 0)
            {
                db.UserRoles.RemoveRange(extraRoles);
            }

            if (!user.UserRoles.Any(x => string.Equals(x.Role.Name, spec.Role, StringComparison.OrdinalIgnoreCase)))
            {
                user.UserRoles.Add(new UserRole { User = user, Role = roles[spec.Role] });
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return await db.Users
            .Where(x => catalog.Users.Select(spec => spec.Username).Contains(x.Username))
            .ToDictionaryAsync(
                user => catalog.Users.Single(spec => spec.Username == user.Username).Key,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);
    }

    private static async Task<Dictionary<string, Club>> SeedClubsAsync(
        DemoCatalog catalog,
        IReadOnlyDictionary<string, User> users,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        foreach (var spec in catalog.Clubs)
        {
            var club = await db.Clubs
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.Code == spec.Code, cancellationToken);
            if (club is null)
            {
                club = new Club { Code = spec.Code, CreatedAtUtc = At(catalog.ReferenceDate.AddDays(-230), 2) };
                db.Clubs.Add(club);
            }

            club.Name = spec.Name;
            club.Category = spec.Category;
            club.Description = spec.Description;
            club.ContactEmail = spec.ContactEmail;
            club.ContactPhone = spec.ContactPhone;
            club.LogoUrl = null;
            club.IsActive = true;
            club.DeletedAtUtc = null;
            club.DeletedByUserId = null;
        }
        await db.SaveChangesAsync(cancellationToken);

        var clubCodes = catalog.Clubs.Select(x => x.Code).ToArray();
        var clubs = await db.Clubs
            .Where(x => clubCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var spec in catalog.Clubs)
        {
            var club = clubs[spec.Code];
            var manager = users[spec.ManagerUserKey];
            var activeAssignments = await db.ClubManagerAssignments
                .Where(x => x.ClubId == club.Id && x.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var oldAssignment in activeAssignments.Where(x => x.ManagerUserId != manager.Id))
            {
                oldAssignment.IsActive = false;
                oldAssignment.EndedAtUtc = At(catalog.ReferenceDate.AddDays(-229), 2);
            }

            var assignment = activeAssignments.FirstOrDefault(x => x.ManagerUserId == manager.Id);
            if (assignment is null)
            {
                assignment = new ClubManagerAssignment
                {
                    ClubId = club.Id,
                    ManagerUserId = manager.Id,
                    AssignedAtUtc = At(catalog.ReferenceDate.AddDays(-225), 2),
                    IsActive = true
                };
                db.ClubManagerAssignments.Add(assignment);
            }
            assignment.ManagerName = manager.FullName;
            assignment.EndedAtUtc = null;
            assignment.IsActive = true;
        }

        foreach (var spec in catalog.Memberships)
        {
            var club = clubs[spec.ClubCode];
            var user = users[spec.UserKey];
            var profile = catalog.Users.Single(x => x.Key == spec.UserKey);
            var membership = await db.ClubMemberships
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.ClubId == club.Id && x.UserId == user.Id, cancellationToken);

            if (membership is null)
            {
                membership = new ClubMembership { ClubId = club.Id, UserId = user.Id };
                db.ClubMemberships.Add(membership);
            }

            membership.FullName = user.FullName;
            membership.DateOfBirth = profile.DateOfBirth;
            membership.Gender = profile.Gender;
            membership.Email = user.Email;
            membership.PhoneNumber = profile.PhoneNumber;
            membership.Address = profile.Address;
            membership.Role = spec.ClubRole;
            membership.TreasurerSlot = spec.ClubRole == ClubMemberRoles.Treasurer ? 1 : null;
            membership.Status = spec.Status;
            membership.RequestMessage = spec.RequestMessage;
            membership.PersonalInfo = profile.PersonalInfo;
            membership.Goals = profile.Goals;
            membership.Reason = spec.RequestMessage;
            membership.Hobbies = profile.Hobbies;
            membership.Skills = profile.Skills;
            membership.Expectations = profile.Expectations;
            membership.Contributions = profile.Contributions;
            membership.AdditionalInfoJson = $"{{\"source\":\"DEMO_DATASET\",\"account\":\"{user.Username}\"}}";
            membership.AcceptedClubRules = true;
            membership.CommittedToParticipate = true;
            membership.ReviewNote = spec.ReviewNote;
            membership.RequestedAtUtc = At(catalog.ReferenceDate.AddDays(-spec.RequestedDaysBeforeReference), 3);
            membership.IsDeleted = false;
            membership.DeletedAtUtc = null;
            membership.DeletedByUserId = null;

            if (spec.Status == ClubMembershipStatuses.Pending)
            {
                membership.ReviewedAtUtc = null;
                membership.ReviewedByUserId = null;
            }
            else
            {
                var managerKey = catalog.Clubs.Single(x => x.Code == spec.ClubCode).ManagerUserKey;
                membership.ReviewedAtUtc = spec.UserKey == managerKey
                    ? membership.RequestedAtUtc
                    : membership.RequestedAtUtc.AddDays(2);
                membership.ReviewedByUserId = spec.UserKey == managerKey
                    ? users["admin"].Id
                    : users[managerKey].Id;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await SeedClubApplicationsAsync(catalog, users, clubs, db, cancellationToken);
        return clubs;
    }

    private static async Task SeedClubApplicationsAsync(
        DemoCatalog catalog,
        IReadOnlyDictionary<string, User> users,
        IReadOnlyDictionary<string, Club> clubs,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        foreach (var spec in catalog.ClubApplications)
        {
            var requester = users[spec.RequesterUserKey];
            var application = await db.ClubCreationApplications
                .SingleOrDefaultAsync(
                    x => x.Code == spec.Code && x.RequesterUserId == requester.Id,
                    cancellationToken);

            if (application is null)
            {
                application = new ClubCreationApplication
                {
                    Code = spec.Code,
                    RequesterUserId = requester.Id
                };
                db.ClubCreationApplications.Add(application);
            }

            application.RequesterName = requester.FullName;
            application.Name = spec.Name;
            application.Category = spec.Category;
            application.Description = $"Đề án thành lập {spec.Name}, hướng tới môi trường sinh hoạt định kỳ và sản phẩm đầu ra có thể đo lường.";
            application.Purpose = "Tạo môi trường để sinh viên cùng học, thực hành kỹ năng và đóng góp cho cộng đồng trường.";
            application.Reason = "Nhu cầu tham gia chủ đề tăng và hiện chưa có đủ hoạt động chuyên sâu phục vụ nhóm sinh viên quan tâm.";
            application.LogoUrl = null;
            application.ContactEmail = spec.Code.ToLowerInvariant().Replace("fpt-", string.Empty) + "@fpt.edu.vn";
            application.ContactPhone = catalog.Users.Single(x => x.Key == spec.RequesterUserKey).PhoneNumber;
            application.FounderRole = "Trưởng ban sáng lập";
            application.FounderOrganization = "Đại học FPT - Cơ sở Hòa Lạc";
            application.FoundingMemberCount = 7;
            application.FoundingMembersJson = $"[{{\"userId\":{requester.Id},\"fullName\":\"{requester.FullName}\",\"committed\":true}}]";
            application.FoundingMembersCommitted = true;
            application.MainActivities = "Sinh hoạt chuyên môn hai tuần một lần; workshop theo tháng; một sự kiện cộng đồng hoặc cuộc thi mỗi học kỳ.";
            application.ActivityFrequency = "Hai tuần một lần";
            application.ExpectedLocation = "Khuôn viên Đại học FPT Hòa Lạc";
            application.ExpectedSchedule = "18:00-20:00 thứ Tư hoặc sáng Chủ nhật";
            application.MajorEvents = "Ngày hội giới thiệu CLB, workshop mở và dự án học kỳ có báo cáo tổng kết.";
            application.VenueSupport = ClubResourceOptions.SupportNeeded;
            application.FundingSupport = ClubFundingOptions.Combined;
            application.EquipmentNeeds = "Phòng sinh hoạt, máy chiếu và tủ lưu trữ vật tư dùng chung.";
            application.AdvisorNeeded = true;
            application.CommittedToRules = true;
            application.CommittedToResponsibility = true;
            application.CommittedToReporting = true;
            application.Status = spec.Status;
            application.ReviewNote = spec.ReviewNote;
            application.ReviewConditions = spec.Status == ClubApplicationStatuses.Approved
                ? "Báo cáo hoạt động và tài chính theo từng học kỳ."
                : null;
            application.ReviewerSignature = spec.Status == ClubApplicationStatuses.Submitted ? null : users["admin"].FullName;
            application.CreatedClubId = spec.CreatedClubCode is null ? null : clubs[spec.CreatedClubCode].Id;
            application.SubmittedAtUtc = At(catalog.ReferenceDate.AddDays(spec.Status == ClubApplicationStatuses.Approved ? -235 : -9), 3);
            application.ReviewedAtUtc = spec.Status == ClubApplicationStatuses.Submitted
                ? null
                : application.SubmittedAtUtc.AddDays(3);
            application.ReviewedByUserId = spec.Status == ClubApplicationStatuses.Submitted
                ? null
                : users["admin"].Id;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, ReportEntity>> SeedReportsAsync(
        DemoCatalog catalog,
        IReadOnlyDictionary<string, User> users,
        IReadOnlyDictionary<string, Club> clubs,
        ReportDbContext db,
        CancellationToken cancellationToken)
    {
        var deadlineSpecs = new[]
        {
            new ReportingDeadline
            {
                Period = $"SUMMER{catalog.ReferenceDate.Year}",
                DueDate = new DateOnly(catalog.ReferenceDate.Year, 8, 15),
                IsActive = false
            },
            new ReportingDeadline
            {
                Period = $"FALL{catalog.ReferenceDate.Year}",
                DueDate = new DateOnly(catalog.ReferenceDate.Year, 11, 30),
                IsActive = true
            },
            new ReportingDeadline
            {
                Period = $"SPRING{catalog.ReferenceDate.Year + 1}",
                DueDate = new DateOnly(catalog.ReferenceDate.Year + 1, 4, 30),
                IsActive = true
            }
        };

        foreach (var spec in deadlineSpecs)
        {
            var deadline = await db.ReportingDeadlines
                .SingleOrDefaultAsync(x => x.Period == spec.Period, cancellationToken);
            if (deadline is null)
            {
                db.ReportingDeadlines.Add(spec);
            }
            else
            {
                deadline.DueDate = spec.DueDate;
                deadline.IsActive = spec.IsActive;
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        foreach (var spec in catalog.Reports)
        {
            var report = await db.Reports
                .Include(x => x.Details)
                .Include(x => x.Feedback)
                .SingleOrDefaultAsync(x => x.SeedKey == spec.Key, cancellationToken);

            if (report is null)
            {
                report = new ReportEntity { SeedKey = spec.Key };
                db.Reports.Add(report);
            }
            else
            {
                db.ReportDetails.RemoveRange(report.Details);
                db.ReportFeedback.RemoveRange(report.Feedback);
            }

            var club = clubs[spec.ClubCode];
            report.ClubId = club.Id;
            report.ClubName = club.Name;
            report.Period = spec.Period;
            report.ReportType = spec.ReportType;
            report.Tag = spec.Tag;
            report.Status = spec.Status;
            report.CreatedByUserId = users[spec.CreatedByUserKey].Id;
            report.DueDate = spec.DueDate;
            report.CreatedAtUtc = spec.CreatedAtUtc;
            report.UpdatedAtUtc = spec.ReviewedAtUtc ?? spec.SubmittedAtUtc ?? spec.CreatedAtUtc;
            report.SubmittedAtUtc = spec.SubmittedAtUtc;
            report.ReviewedAtUtc = spec.ReviewedAtUtc;
            report.ReviewedByUserId = spec.ReviewedByUserKey is null ? null : users[spec.ReviewedByUserKey].Id;
            report.Version = Math.Max(1, 1 + spec.Feedback.Count + (spec.SubmittedAtUtc.HasValue ? 1 : 0));
            report.ContentSource = ReportContentSources.StructuredForm;
            report.ExecutiveSummary = spec.ExecutiveSummary;
            report.Achievements = spec.Achievements;
            report.Challenges = spec.Challenges;
            report.Recommendations = spec.Recommendations;
            report.NextPeriodPlan = spec.NextPeriodPlan;
            report.BudgetProposalId = null;
            report.BudgetRequestedAmount = null;
            report.BudgetApprovedAmount = null;
            report.BudgetDescription = null;
            report.FinanceSubmittedAtUtc = null;
            report.PublishedActivityId = null;

            report.Details = spec.Details.Select(detail => new ReportDetail
            {
                Report = report,
                ActivityName = detail.ActivityName,
                ActivityDate = detail.ActivityDate,
                Description = detail.Description,
                ParticipantCount = detail.ParticipantCount,
                Outcome = detail.Outcome,
                ActivityType = detail.ActivityType,
                Location = detail.Location,
                PartnerUnit = detail.PartnerUnit,
                Objective = detail.Objective,
                TargetParticipantCount = detail.TargetParticipantCount,
                BudgetSpent = detail.BudgetSpent,
                EvidenceUrl = $"https://demo.clubreporthub.vn/evidence/{spec.Key.ToLowerInvariant()}/{detail.SortOrder}",
                SortOrder = detail.SortOrder
            }).ToList();

            report.Feedback = spec.Feedback.Select(feedback => new ReportFeedback
            {
                Report = report,
                ReviewerUserId = users[feedback.ReviewerUserKey].Id,
                ReviewerName = users[feedback.ReviewerUserKey].FullName,
                Decision = feedback.Decision,
                Message = feedback.Message,
                CreatedAtUtc = spec.CreatedAtUtc.AddDays(feedback.DaysAfterCreation)
            }).ToList();

            await db.SaveChangesAsync(cancellationToken);

            var oldAuditLogs = await db.AuditLogs
                .Where(x => x.ReportId == report.Id)
                .ToListAsync(cancellationToken);
            db.AuditLogs.RemoveRange(oldAuditLogs);
            db.AuditLogs.Add(new AuditLog
            {
                ReportId = report.Id,
                Action = "Created",
                ActorUserId = report.CreatedByUserId,
                Description = $"Tạo báo cáo demo {report.Tag} cho kỳ {report.Period}.",
                CreatedAtUtc = report.CreatedAtUtc
            });

            if (report.SubmittedAtUtc.HasValue)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    ReportId = report.Id,
                    Action = report.Status == ReportStatuses.AwaitingFinance ? "SubmittedToFinance" : "Submitted",
                    ActorUserId = report.CreatedByUserId,
                    Description = report.Status == ReportStatuses.AwaitingFinance
                        ? "Gửi báo cáo sự kiện tương lai sang bước lập và duyệt ngân sách."
                        : "Gửi báo cáo vào quy trình xét duyệt.",
                    CreatedAtUtc = report.SubmittedAtUtc.Value
                });
            }

            foreach (var feedback in report.Feedback)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    ReportId = report.Id,
                    Action = feedback.Decision,
                    ActorUserId = feedback.ReviewerUserId,
                    Description = feedback.Message,
                    CreatedAtUtc = feedback.CreatedAtUtc
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var keys = catalog.Reports.Select(x => x.Key).ToArray();
        var seededReports = await db.Reports
            .Include(x => x.Details)
            .Where(x => x.SeedKey != null && keys.Contains(x.SeedKey))
            .ToListAsync(cancellationToken);
        return seededReports.ToDictionary(x => x.SeedKey!, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, ClubActivity>> SeedActivitiesAsync(
        DemoCatalog catalog,
        IReadOnlyDictionary<string, User> users,
        IReadOnlyDictionary<string, Club> clubs,
        IReadOnlyDictionary<string, ReportEntity> reports,
        ActivityDbContext db,
        ReportDbContext reportDb,
        CancellationToken cancellationToken)
    {
        var seededActivities = new Dictionary<string, ClubActivity>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in catalog.Activities)
        {
            var club = clubs[spec.ClubCode];
            int? sourceReportId = null;
            int? sourceReportDetailId = null;
            if (spec.SourceReportKey is not null)
            {
                var sourceReport = reports[spec.SourceReportKey];
                sourceReportId = sourceReport.Id;
                sourceReportDetailId = sourceReport.Details
                    .Single(x => x.SortOrder == spec.SourceReportDetailSortOrder).Id;
            }

            ClubActivity? activity;
            if (sourceReportId.HasValue)
            {
                activity = await db.Activities
                    .Include(x => x.Participants)
                    .Include(x => x.Attendances)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync(x => x.SourceReportId == sourceReportId, cancellationToken);
            }
            else
            {
                activity = await db.Activities
                    .Include(x => x.Participants)
                    .Include(x => x.Attendances)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync(
                        x => x.ClubId == club.Id && x.Title == spec.Title,
                        cancellationToken);
            }

            if (activity is null)
            {
                activity = new ClubActivity();
                db.Activities.Add(activity);
            }
            else
            {
                db.ActivityAttendances.RemoveRange(activity.Attendances);
                db.ActivityParticipants.RemoveRange(activity.Participants);
            }

            activity.SourceReportId = sourceReportId;
            activity.SourceReportDetailId = sourceReportDetailId;
            activity.ClubId = club.Id;
            activity.ClubName = club.Name;
            activity.Title = spec.Title;
            activity.Description = spec.Description;
            activity.StartTimeUtc = spec.StartTimeUtc;
            activity.EndTimeUtc = spec.EndTimeUtc;
            activity.MeetingDaysCsv = string.Join(',', spec.MeetingDays.OrderBy(x => x));
            activity.Location = spec.Location;
            activity.Status = spec.Status;
            activity.CreatedByUserId = users[spec.CreatedByUserKey].Id;
            activity.CreatedAtUtc = sourceReportId.HasValue
                ? reports[spec.SourceReportKey!].ReviewedAtUtc ?? spec.StartTimeUtc.AddDays(-14)
                : spec.StartTimeUtc.AddDays(-30);
            activity.UpdatedAtUtc = spec.Status == ActivityStatuses.Completed
                ? spec.EndTimeUtc.AddHours(2)
                : activity.CreatedAtUtc;

            var attendanceByUser = spec.Attendances
                .GroupBy(x => x.UserKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);

            activity.Participants = spec.ParticipantUserKeys.Select(userKey =>
            {
                var userAttendances = attendanceByUser.GetValueOrDefault(userKey, []);
                var attendanceStatus = spec.Status == ActivityStatuses.Completed
                    ? userAttendances.Any(x => x.Status is AttendanceStatuses.Present or AttendanceStatuses.Late)
                        ? AttendanceStatuses.Attended
                        : AttendanceStatuses.Absent
                    : AttendanceStatuses.Registered;

                return new ActivityParticipant
                {
                    Activity = activity,
                    UserId = users[userKey].Id,
                    FullName = users[userKey].FullName,
                    AttendanceStatus = attendanceStatus,
                    RegisteredAtUtc = activity.CreatedAtUtc.AddDays(1)
                };
            }).ToList();

            var managerKey = catalog.Clubs.Single(x => x.Code == spec.ClubCode).ManagerUserKey;
            activity.Attendances = spec.Attendances.Select(attendance =>
            {
                var marked = attendance.Status != AttendanceStatuses.NotMarked;
                return new ActivityAttendance
                {
                    Activity = activity,
                    UserId = users[attendance.UserKey].Id,
                    FullName = users[attendance.UserKey].FullName,
                    AttendanceDate = attendance.Date,
                    Status = attendance.Status,
                    Note = attendance.Note,
                    CheckedInAtUtc = marked ? At(attendance.Date, 8) : null,
                    CheckedInByUserId = marked ? users[managerKey].Id : null,
                    CreatedAtUtc = At(attendance.Date, 7, 30),
                    UpdatedAtUtc = marked ? At(attendance.Date, 8) : null
                };
            }).ToList();

            await db.SaveChangesAsync(cancellationToken);
            seededActivities[spec.Key] = activity;

            if (sourceReportId.HasValue)
            {
                var report = await reportDb.Reports.SingleAsync(x => x.Id == sourceReportId.Value, cancellationToken);
                report.PublishedActivityId = activity.Id;
                report.UpdatedAtUtc = activity.CreatedAtUtc;
                var oldPublishAudits = await reportDb.AuditLogs
                    .Where(x => x.ReportId == report.Id && x.Action == "ActivityPublished")
                    .ToListAsync(cancellationToken);
                reportDb.AuditLogs.RemoveRange(oldPublishAudits);
                reportDb.AuditLogs.Add(new AuditLog
                {
                    ReportId = report.Id,
                    Action = "ActivityPublished",
                    ActorUserId = activity.CreatedByUserId,
                    Description = $"Xuất bản hoạt động '{activity.Title}' từ báo cáo đã phê duyệt.",
                    CreatedAtUtc = activity.CreatedAtUtc
                });
                await reportDb.SaveChangesAsync(cancellationToken);
            }
        }

        return seededActivities;
    }

    private static async Task SeedFinanceAsync(
        DemoCatalog catalog,
        IReadOnlyDictionary<string, User> users,
        IReadOnlyDictionary<string, Club> clubs,
        IReadOnlyDictionary<string, ReportEntity> reports,
        IReadOnlyDictionary<string, ClubActivity> activities,
        FinanceDbContext db,
        ReportDbContext reportDb,
        CancellationToken cancellationToken)
    {
        var proposalByKey = new Dictionary<string, BudgetProposal>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in catalog.BudgetProposals)
        {
            var club = clubs[spec.ClubCode];
            int? sourceReportId = spec.SourceReportKey is null
                ? null
                : reports[spec.SourceReportKey].Id;
            BudgetProposal? proposal;

            if (sourceReportId.HasValue)
            {
                proposal = await db.BudgetProposals
                    .Include(x => x.Settlements)
                    .SingleOrDefaultAsync(x => x.SourceReportId == sourceReportId, cancellationToken);
            }
            else
            {
                proposal = await db.BudgetProposals
                    .Include(x => x.Settlements)
                    .SingleOrDefaultAsync(
                        x => x.ClubId == club.Id && x.Title == spec.Title,
                        cancellationToken);
            }

            if (proposal is null)
            {
                proposal = new BudgetProposal();
                db.BudgetProposals.Add(proposal);
            }
            else
            {
                db.Settlements.RemoveRange(proposal.Settlements);
            }

            proposal.ClubId = club.Id;
            proposal.ClubName = club.Name;
            proposal.ActivityId = spec.ActivityKey is null ? null : activities[spec.ActivityKey].Id;
            proposal.SourceReportId = sourceReportId;
            proposal.Title = spec.Title;
            proposal.Description = spec.Description;
            proposal.RequestedAmount = spec.RequestedAmount;
            proposal.ApprovedAmount = spec.ApprovedAmount;
            proposal.Status = spec.Status;
            proposal.ProposedByUserId = users[spec.ProposedByUserKey].Id;
            proposal.ProposedAtUtc = spec.ProposedAtUtc;
            proposal.ManagerReviewedByUserId = spec.ManagerReviewedByUserKey is null
                ? null
                : users[spec.ManagerReviewedByUserKey].Id;
            proposal.ManagerReviewedAtUtc = spec.ManagerReviewedAtUtc;
            proposal.ManagerReviewNote = spec.ManagerReviewNote;
            proposal.ReviewedByUserId = spec.ReviewedByUserKey is null
                ? null
                : users[spec.ReviewedByUserKey].Id;
            proposal.ReviewedAtUtc = spec.ReviewedAtUtc;
            proposal.ReviewNote = spec.ReviewNote;
            proposal.Version = spec.Status switch
            {
                FinanceStatuses.Submitted => 1,
                FinanceStatuses.ManagerApproved => 2,
                _ => 3
            };

            if (spec.Settlement is not null)
            {
                proposal.Settlements =
                [
                    new Settlement
                    {
                        BudgetProposal = proposal,
                        TotalSpent = spec.Settlement.TotalSpent,
                        ReceiptUrl = spec.Settlement.ReceiptUrl,
                        Status = spec.Settlement.Status,
                        SubmittedAtUtc = spec.Settlement.SubmittedAtUtc,
                        ReviewedByUserId = spec.Settlement.ReviewedByUserKey is null
                            ? null
                            : users[spec.Settlement.ReviewedByUserKey].Id,
                        ReviewedAtUtc = spec.Settlement.ReviewedAtUtc,
                        ReviewNote = spec.Settlement.ReviewNote
                    }
                ];
            }

            await db.SaveChangesAsync(cancellationToken);
            proposalByKey[spec.Key] = proposal;

            if (sourceReportId.HasValue)
            {
                var report = await reportDb.Reports.SingleAsync(x => x.Id == sourceReportId.Value, cancellationToken);
                report.BudgetProposalId = proposal.Id;
                report.BudgetRequestedAmount = proposal.RequestedAmount;
                report.BudgetApprovedAmount = proposal.ApprovedAmount;
                report.BudgetDescription = proposal.Description;
                report.FinanceSubmittedAtUtc = proposal.ProposedAtUtc;
                report.UpdatedAtUtc = new[]
                    {
                        report.UpdatedAtUtc,
                        proposal.ProposedAtUtc,
                        proposal.ManagerReviewedAtUtc ?? proposal.ProposedAtUtc,
                        proposal.ReviewedAtUtc ?? proposal.ProposedAtUtc
                    }
                    .Max();

                var oldBudgetAudits = await reportDb.AuditLogs
                    .Where(x => x.ReportId == report.Id
                                && (x.Action == "BudgetLinked" || x.Action == "BudgetManagerReviewed"))
                    .ToListAsync(cancellationToken);
                reportDb.AuditLogs.RemoveRange(oldBudgetAudits);
                reportDb.AuditLogs.Add(new AuditLog
                {
                    ReportId = report.Id,
                    Action = "BudgetLinked",
                    ActorUserId = proposal.ProposedByUserId,
                    Description = $"Liên kết đề xuất ngân sách {proposal.RequestedAmount:N0} đồng với báo cáo sự kiện.",
                    CreatedAtUtc = proposal.ProposedAtUtc
                });
                if (proposal.ManagerReviewedByUserId.HasValue && proposal.ManagerReviewedAtUtc.HasValue)
                {
                    reportDb.AuditLogs.Add(new AuditLog
                    {
                        ReportId = report.Id,
                        Action = "BudgetManagerReviewed",
                        ActorUserId = proposal.ManagerReviewedByUserId.Value,
                        Description = proposal.ManagerReviewNote ?? "Quản lý CLB đã kiểm tra đề xuất ngân sách.",
                        CreatedAtUtc = proposal.ManagerReviewedAtUtc.Value
                    });
                }
                await reportDb.SaveChangesAsync(cancellationToken);
            }
        }

        var proposalIds = proposalByKey.Values.Select(x => x.Id).ToArray();
        var oldTransactions = await db.FinanceTransactions
            .Where(x => x.ReferenceId.HasValue && proposalIds.Contains(x.ReferenceId.Value))
            .ToListAsync(cancellationToken);
        db.FinanceTransactions.RemoveRange(oldTransactions);

        foreach (var spec in catalog.BudgetProposals)
        {
            var proposal = proposalByKey[spec.Key];
            if (proposal.ApprovedAmount.HasValue && proposal.ReviewedAtUtc.HasValue)
            {
                db.FinanceTransactions.Add(new FinanceTransaction
                {
                    ClubId = proposal.ClubId,
                    Amount = proposal.ApprovedAmount.Value,
                    Type = TransactionTypes.BudgetApproved,
                    Description = proposal.Title,
                    ReferenceId = proposal.Id,
                    TransactionDateUtc = proposal.ReviewedAtUtc.Value
                });
            }

            if (spec.Settlement is null)
            {
                continue;
            }

            db.FinanceTransactions.Add(new FinanceTransaction
            {
                ClubId = proposal.ClubId,
                Amount = spec.Settlement.TotalSpent,
                Type = TransactionTypes.SettlementSubmitted,
                Description = $"Đã nộp quyết toán cho {proposal.Title}",
                ReferenceId = proposal.Id,
                TransactionDateUtc = spec.Settlement.SubmittedAtUtc
            });

            if (spec.Settlement.Status == FinanceStatuses.Approved && spec.Settlement.ReviewedAtUtc.HasValue)
            {
                db.FinanceTransactions.Add(new FinanceTransaction
                {
                    ClubId = proposal.ClubId,
                    Amount = spec.Settlement.TotalSpent,
                    Type = TransactionTypes.SettlementApproved,
                    Description = $"Đã phê duyệt quyết toán cho {proposal.Title}",
                    ReferenceId = proposal.Id,
                    TransactionDateUtc = spec.Settlement.ReviewedAtUtc.Value
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedNotificationsAsync(
        DemoCatalog catalog,
        IReadOnlyDictionary<string, User> users,
        NotificationDbContext db,
        CancellationToken cancellationToken)
    {
        var titles = catalog.Notifications.Select(x => x.Title).ToArray();
        var existing = await db.Notifications
            .Where(x => titles.Contains(x.Title))
            .ToListAsync(cancellationToken);
        db.Notifications.RemoveRange(existing);

        db.Notifications.AddRange(catalog.Notifications.Select(spec => new Notification
        {
            RecipientUserId = spec.RecipientUserKey is null ? null : users[spec.RecipientUserKey].Id,
            RecipientRole = spec.RecipientRole,
            EventType = spec.EventType,
            Title = spec.Title,
            Message = spec.Message,
            IsRead = spec.IsRead,
            CreatedAtUtc = spec.CreatedAtUtc
        }));

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task VerifyAsync(
        DemoCatalog catalog,
        AuthDbContext authDb,
        ClubDbContext clubDb,
        ActivityDbContext activityDb,
        ReportDbContext reportDb,
        FinanceDbContext financeDb,
        NotificationDbContext notificationDb,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var usernames = catalog.Users.Select(x => x.Username).ToArray();
        var seedKeys = catalog.Reports.Select(x => x.Key).ToArray();
        var clubCodes = catalog.Clubs.Select(x => x.Code).ToArray();

        var users = await authDb.Users
            .AsNoTracking()
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .Where(x => usernames.Contains(x.Username))
            .ToListAsync(cancellationToken);
        if (users.Count != catalog.Users.Count)
            errors.Add($"Expected {catalog.Users.Count} seeded users, found {users.Count}.");

        foreach (var spec in catalog.Users)
        {
            var user = users.SingleOrDefault(x => x.Username == spec.Username);
            var roleNames = user?.UserRoles.Select(x => x.Role.Name).ToArray() ?? [];
            if (user is null || !user.IsActive || roleNames.Length != 1 || roleNames[0] != spec.Role)
                errors.Add($"User '{spec.Username}' is missing, inactive, or has incorrect roles.");
        }

        if (options.ResetAll)
        {
            var unexpectedActiveUsers = await authDb.Users
                .AsNoTracking()
                .Where(x => x.IsActive && !usernames.Contains(x.Username))
                .CountAsync(cancellationToken);
            if (unexpectedActiveUsers > 0)
                errors.Add($"Reset mode expected no extra active users, found {unexpectedActiveUsers}.");

            var outOfScopeRoleAssignments = await authDb.UserRoles
                .AsNoTracking()
                .Include(x => x.Role)
                .CountAsync(x => !DemoActorRoles.Contains(x.Role.Name), cancellationToken);
            if (outOfScopeRoleAssignments > 0)
                errors.Add($"Found {outOfScopeRoleAssignments} assignments outside ADMIN, CLUB_MANAGER, and CLUB_MEMBER.");
        }

        var clubs = await clubDb.Clubs
            .AsNoTracking()
            .Where(x => clubCodes.Contains(x.Code))
            .ToListAsync(cancellationToken);
        if (clubs.Count != catalog.Clubs.Count)
            errors.Add($"Expected {catalog.Clubs.Count} clubs, found {clubs.Count}.");

        var clubIds = clubs.Select(x => x.Id).ToArray();
        var managerAssignments = await clubDb.ClubManagerAssignments
            .AsNoTracking()
            .CountAsync(x => clubIds.Contains(x.ClubId) && x.IsActive, cancellationToken);
        if (managerAssignments != catalog.Clubs.Count)
            errors.Add($"Expected one active manager per club, found {managerAssignments} active assignments.");

        var memberships = await clubDb.ClubMemberships
            .AsNoTracking()
            .CountAsync(x => clubIds.Contains(x.ClubId), cancellationToken);
        if (memberships < catalog.Memberships.Count)
            errors.Add($"Expected at least {catalog.Memberships.Count} memberships, found {memberships}.");

        var activities = await activityDb.Activities
            .AsNoTracking()
            .Include(x => x.Participants)
            .Include(x => x.Attendances)
            .AsSplitQuery()
            .Where(x => clubIds.Contains(x.ClubId))
            .ToListAsync(cancellationToken);
        var seededActivityTitles = catalog.Activities.Select(x => x.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seededActivities = activities.Where(x => seededActivityTitles.Contains(x.Title)).ToList();
        if (seededActivities.Count != catalog.Activities.Count)
            errors.Add($"Expected {catalog.Activities.Count} activities, found {seededActivities.Count}.");

        var expectedAttendanceCount = catalog.Activities.Sum(x => x.Attendances.Count);
        var attendanceCount = seededActivities.Sum(x => x.Attendances.Count);
        if (attendanceCount != expectedAttendanceCount)
            errors.Add($"Expected {expectedAttendanceCount} attendance rows, found {attendanceCount}.");

        var reports = await reportDb.Reports
            .AsNoTracking()
            .Include(x => x.Details)
            .Where(x => x.SeedKey != null && seedKeys.Contains(x.SeedKey))
            .ToListAsync(cancellationToken);
        if (reports.Count != catalog.Reports.Count)
            errors.Add($"Expected {catalog.Reports.Count} reports, found {reports.Count}.");
        if (reports.Any(x => string.IsNullOrWhiteSpace(x.ClubName)
                             || string.IsNullOrWhiteSpace(x.ExecutiveSummary)
                             || x.Details.Count == 0))
            errors.Add("One or more seeded reports are incomplete.");

        var reportIds = reports.Select(x => x.Id).ToHashSet();
        if (seededActivities.Any(x => x.SourceReportId.HasValue && !reportIds.Contains(x.SourceReportId.Value)))
            errors.Add("An activity points to a report outside the seeded dataset.");

        var proposals = await financeDb.BudgetProposals
            .AsNoTracking()
            .Include(x => x.Settlements)
            .Where(x => clubIds.Contains(x.ClubId))
            .ToListAsync(cancellationToken);
        var proposalTitles = catalog.BudgetProposals.Select(x => x.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seededProposals = proposals.Where(x => proposalTitles.Contains(x.Title)).ToList();
        if (seededProposals.Count != catalog.BudgetProposals.Count)
            errors.Add($"Expected {catalog.BudgetProposals.Count} budget proposals, found {seededProposals.Count}.");
        if (seededProposals.Any(x => x.SourceReportId.HasValue && !reportIds.Contains(x.SourceReportId.Value)))
            errors.Add("A budget proposal points to a report outside the seeded dataset.");

        var notificationTitles = catalog.Notifications.Select(x => x.Title).ToArray();
        var notificationCount = await notificationDb.Notifications
            .AsNoTracking()
            .CountAsync(x => notificationTitles.Contains(x.Title), cancellationToken);
        if (notificationCount != catalog.Notifications.Count)
            errors.Add($"Expected {catalog.Notifications.Count} notifications, found {notificationCount}.");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Demo data verification failed:\n- " + string.Join("\n- ", errors));
        }

        var statusSummary = reports
            .GroupBy(x => x.Status)
            .OrderBy(x => x.Key)
            .Select(x => $"{x.Key}={x.Count()}");
        Console.WriteLine(
            $"DEMO DATA READY | users={users.Count}, clubs={clubs.Count}, memberships={memberships}, " +
            $"activities={seededActivities.Count}, attendance={attendanceCount}, reports={reports.Count} " +
            $"({string.Join(", ", statusSummary)}), budgets={seededProposals.Count}, " +
            $"settlements={seededProposals.Sum(x => x.Settlements.Count)}, notifications={notificationCount}");
    }

    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0) =>
        new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(7))
            .ToUniversalTime();
}
