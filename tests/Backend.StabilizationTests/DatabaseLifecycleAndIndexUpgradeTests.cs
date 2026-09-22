using System.Reflection;
using ActivityService.Data;
using ActivityService.Models;
using FinanceService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using NotificationService.Data;
using ReportService.Data;
using ReportService.Models;

namespace Backend.StabilizationTests;

public sealed class DatabaseLifecycleAndIndexUpgradeTests
{
    [Fact]
    public void ActivityDbContext_ModelConfiguresCoveringIndexes_WithIncludeAnnotations()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ActivityDbContext>()
            .UseSqlServer("Server=localhost;Database=TestActivityDb;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        using var context = new ActivityDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;

        // 1. ClubActivity: IX_Activities_StartTimeUtc -> INCLUDE (ClubId, Title, Status)
        var activityEntity = model.FindEntityType(typeof(ClubActivity));
        Assert.NotNull(activityEntity);
        var startTimeIndex = activityEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(ClubActivity.StartTimeUtc));
        Assert.NotNull(startTimeIndex);
        var startTimeIncludes = startTimeIndex.GetIncludeProperties();
        Assert.NotNull(startTimeIncludes);
        Assert.Contains(nameof(ClubActivity.ClubId), startTimeIncludes);
        Assert.Contains(nameof(ClubActivity.Title), startTimeIncludes);
        Assert.Contains(nameof(ClubActivity.Status), startTimeIncludes);

        // 2. ActivityParticipant: IX_ActivityParticipants_UserId -> INCLUDE (ActivityId, AttendanceStatus)
        var participantEntity = model.FindEntityType(typeof(ActivityParticipant));
        Assert.NotNull(participantEntity);
        var participantUserIndex = participantEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(ActivityParticipant.UserId));
        Assert.NotNull(participantUserIndex);
        var participantIncludes = participantUserIndex.GetIncludeProperties();
        Assert.NotNull(participantIncludes);
        Assert.Contains(nameof(ActivityParticipant.ActivityId), participantIncludes);
        Assert.Contains(nameof(ActivityParticipant.AttendanceStatus), participantIncludes);

        // 3. ActivityAttendance: IX_ActivityAttendances_UserId_AttendanceDate -> INCLUDE (ActivityId, Status)
        var attendanceEntity = model.FindEntityType(typeof(ActivityAttendance));
        Assert.NotNull(attendanceEntity);
        var attendanceDateIndex = attendanceEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 2 &&
                                 i.Properties.Any(p => p.Name == nameof(ActivityAttendance.UserId)) &&
                                 i.Properties.Any(p => p.Name == nameof(ActivityAttendance.AttendanceDate)));
        Assert.NotNull(attendanceDateIndex);
        var attendanceIncludes = attendanceDateIndex.GetIncludeProperties();
        Assert.NotNull(attendanceIncludes);
        Assert.Contains(nameof(ActivityAttendance.ActivityId), attendanceIncludes);
        Assert.Contains(nameof(ActivityAttendance.Status), attendanceIncludes);
    }

    [Fact]
    public void ReportDbContext_ModelConfiguresCoveringIndexes_AndCompositeIndexes()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlServer("Server=localhost;Database=TestReportDb;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        using var context = new ReportDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;

        var reportEntity = model.FindEntityType(typeof(Report));
        Assert.NotNull(reportEntity);

        // IX_Reports_UpdatedAtUtc -> INCLUDE (ClubId, Period, Status)
        var updatedAtIndex = reportEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Report.UpdatedAtUtc)));
        Assert.NotNull(updatedAtIndex);
        var updatedIncludes = updatedAtIndex.GetIncludeProperties();
        Assert.NotNull(updatedIncludes);
        Assert.Contains(nameof(Report.ClubId), updatedIncludes);
        Assert.Contains(nameof(Report.Period), updatedIncludes);
        Assert.Contains(nameof(Report.Status), updatedIncludes);

        // Composite index: CreatedByUserId + Status
        var createdByIndex = reportEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 2 &&
                                 i.Properties.Any(p => p.Name == nameof(Report.CreatedByUserId)) &&
                                 i.Properties.Any(p => p.Name == nameof(Report.Status)));
        Assert.NotNull(createdByIndex);
    }

    [Fact]
    public void ActivitySchemaUpgrader_SqlContainsNonCoveringIndexInspectionAndDrop_BeforeRecreate()
    {
        // Verify via inspection that the SQL script in ActivitySchemaUpgrader drops non-covering indexes
        var upgraderType = typeof(ActivitySchemaUpgrader);
        var method = upgraderType.GetMethod("ApplyAsync", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        // Read source file or inspect the constant in method body
        var assembly = upgraderType.Assembly;
        var filePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Services", "ActivityService", "Data", "ActivitySchemaUpgrader.cs");

        var sqlContent = File.ReadAllText(filePath);
        // PERF-F01 check: must check sys.index_columns.is_included_column = 1
        Assert.Contains("is_included_column = 1", sqlContent);
        Assert.Contains("DROP INDEX [IX_ActivityAttendances_UserId_AttendanceDate]", sqlContent);
        Assert.Contains("DROP INDEX [IX_ActivityParticipants_UserId]", sqlContent);
        Assert.Contains("DROP INDEX [IX_Activities_StartTimeUtc]", sqlContent);
        Assert.Contains("INCLUDE ([ActivityId], [Status])", sqlContent);
    }

    [Fact]
    public void ReportSchemaUpgrader_SqlContainsNonCoveringIndexInspectionAndDrop_BeforeRecreate()
    {
        // POT-F08 check: must check sys.index_columns.is_included_column = 1 for IX_Reports_UpdatedAtUtc
        var filePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Services", "ReportService", "Data", "ReportSchemaUpgrader.cs");

        var sqlContent = File.ReadAllText(filePath);
        Assert.Contains("is_included_column = 1", sqlContent);
        Assert.Contains("DROP INDEX [IX_Reports_UpdatedAtUtc]", sqlContent);
        Assert.Contains("INCLUDE ([ClubId], [Period], [Status])", sqlContent);
    }

    [Fact]
    public void MicroserviceMigrations_AllTargetDatabasesHaveRegisteredMigrationsAndSnapshots()
    {
        // ActivityService
        var activityAssembly = typeof(ActivityDbContext).Assembly;
        var activityMigrations = activityAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Migration)) && t.GetCustomAttribute<MigrationAttribute>() != null)
            .ToList();
        Assert.NotEmpty(activityMigrations);
        Assert.Contains(activityMigrations, m => m.Name.Contains("InitialActivityBaseline"));

        var activitySnapshot = activityAssembly.GetTypes()
            .FirstOrDefault(t => t.IsSubclassOf(typeof(ModelSnapshot)));
        Assert.NotNull(activitySnapshot);

        // FinanceService
        var financeAssembly = typeof(FinanceDbContext).Assembly;
        var financeMigrations = financeAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Migration)) && t.GetCustomAttribute<MigrationAttribute>() != null)
            .ToList();
        Assert.NotEmpty(financeMigrations);
        Assert.Contains(financeMigrations, m => m.Name.Contains("InitialFinanceBaseline"));

        var financeSnapshot = financeAssembly.GetTypes()
            .FirstOrDefault(t => t.IsSubclassOf(typeof(ModelSnapshot)));
        Assert.NotNull(financeSnapshot);

        // NotificationService
        var notificationAssembly = typeof(NotificationDbContext).Assembly;
        var notificationMigrations = notificationAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Migration)) && t.GetCustomAttribute<MigrationAttribute>() != null)
            .ToList();
        Assert.NotEmpty(notificationMigrations);
        Assert.Contains(notificationMigrations, m => m.Name.Contains("InitialNotificationBaseline"));

        var notificationSnapshot = notificationAssembly.GetTypes()
            .FirstOrDefault(t => t.IsSubclassOf(typeof(ModelSnapshot)));
        Assert.NotNull(notificationSnapshot);
    }
}
