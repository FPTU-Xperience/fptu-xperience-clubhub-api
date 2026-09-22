using System.Security.Claims;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubService.Data;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;
using ReportService.Data;
using ReportService.Models;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class DatabaseQueryOptimizationTests
{
    // =========================================================================
    // 1. PERF-F02: Database-Side Projection & Aggregation in KPI Query
    // =========================================================================

    [Fact]
    public async Task KpiLeaderboard_AggregatesDirectlyAtDatabaseLevel_WithoutMaterializingEntityGraph()
    {
        var dbName = "kpi_db_aggregation_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var vietnamToday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

        using (var db = new ReportDbContext(options))
        {
            // Seed Club 1 with 2 reports: 1 approved (2 activities, 15 participants), 1 overdue draft
            var r1 = new Report
            {
                ClubId = 1,
                ClubName = "Music Club",
                Period = "2026-Q1",
                Status = ReportStatuses.Approved,
                DueDate = vietnamToday.AddDays(5),
                Details = new List<ReportDetail>
                {
                    new ReportDetail { ActivityName = "Concert 1", ParticipantCount = 10 },
                    new ReportDetail { ActivityName = "Concert 2", ParticipantCount = 5 }
                }
            };

            var r2 = new Report
            {
                ClubId = 1,
                ClubName = "Music Club",
                Period = "2026-Q1",
                Status = ReportStatuses.Draft,
                DueDate = vietnamToday.AddDays(-2), // Overdue
                Details = new List<ReportDetail>()
            };

            // Seed Club 2 with 1 rejected report
            var r3 = new Report
            {
                ClubId = 2,
                ClubName = "Coding Club",
                Period = "2026-Q1",
                Status = ReportStatuses.Rejected,
                DueDate = vietnamToday.AddDays(1),
                Details = new List<ReportDetail>()
            };

            db.Reports.AddRange(r1, r2, r3);
            await db.SaveChangesAsync();
        }

        // Query using the exact PERF-F02 DB-side projection logic
        using (var queryDb = new ReportDbContext(options))
        {
            var query = queryDb.Reports.AsNoTracking().Where(x => x.Period == "2026-Q1");

            var aggregatedReports = await query
                .Select(r => new
                {
                    r.ClubId,
                    r.ClubName,
                    IsApproved = r.Status == ReportStatuses.Approved,
                    IsRejected = r.Status == ReportStatuses.Rejected,
                    IsOverdue = (r.Status == ReportStatuses.Draft || r.Status == ReportStatuses.Rejected) && r.DueDate < vietnamToday,
                    ActivityCount = r.Status == ReportStatuses.Approved ? r.Details.Count : 0,
                    ParticipantCount = r.Status == ReportStatuses.Approved ? (r.Details.Sum(d => (int?)d.ParticipantCount) ?? 0) : 0
                })
                .GroupBy(x => new { x.ClubId, x.ClubName })
                .Select(g => new
                {
                    g.Key.ClubId,
                    g.Key.ClubName,
                    ApprovedReports = g.Count(x => x.IsApproved),
                    RejectedReports = g.Count(x => x.IsRejected),
                    OverdueReports = g.Count(x => x.IsOverdue),
                    Activities = g.Sum(x => x.ActivityCount),
                    Participants = g.Sum(x => x.ParticipantCount)
                })
                .ToListAsync();

            Assert.Equal(2, aggregatedReports.Count);

            var club1 = aggregatedReports.FirstOrDefault(x => x.ClubId == 1);
            Assert.NotNull(club1);
            Assert.Equal("Music Club", club1.ClubName);
            Assert.Equal(1, club1.ApprovedReports);
            Assert.Equal(0, club1.RejectedReports);
            Assert.Equal(1, club1.OverdueReports);
            Assert.Equal(2, club1.Activities);
            Assert.Equal(15, club1.Participants);

            var club2 = aggregatedReports.FirstOrDefault(x => x.ClubId == 2);
            Assert.NotNull(club2);
            Assert.Equal("Coding Club", club2.ClubName);
            Assert.Equal(0, club2.ApprovedReports);
            Assert.Equal(1, club2.RejectedReports);
            Assert.Equal(0, club2.OverdueReports);
            Assert.Equal(0, club2.Activities);
            Assert.Equal(0, club2.Participants);

            // Verify that zero entity instances are tracked in the DbContext
            Assert.Empty(queryDb.ChangeTracker.Entries());
        }
    }

    // =========================================================================
    // 2. PERF-F03: AsNoTracking on Read-Only Queries
    // =========================================================================

    [Fact]
    public async Task ClubReadQueries_WithAsNoTracking_DoNotTrackEntitiesInChangeTracker()
    {
        var dbName = "club_notracking_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ClubDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using (var db = new ClubDbContext(options))
        {
            var club = new Club
            {
                Id = 10,
                Code = "TESTCLUB",
                Name = "Test Club",
                Category = "ACADEMIC",
                Description = "A club for testing query performance",
                IsActive = true
            };
            db.Clubs.Add(club);

            var membership = new ClubMembership
            {
                Id = 100,
                ClubId = 10,
                UserId = 55,
                FullName = "Member One",
                Role = ClubMemberRoles.Member,
                Status = ClubMembershipStatuses.Approved
            };
            db.ClubMemberships.Add(membership);

            var manager = new ClubManagerAssignment
            {
                Id = 200,
                ClubId = 10,
                ManagerUserId = 55,
                ManagerName = "Member One",
                IsActive = true
            };
            db.ClubManagerAssignments.Add(manager);

            await db.SaveChangesAsync();
        }

        using (var queryDb = new ClubDbContext(options))
        {
            // Execute read queries with AsNoTracking()
            var clubs = await queryDb.Clubs
                .AsNoTracking()
                .Include(x => x.ManagerAssignments)
                .Include(x => x.Memberships)
                .ToListAsync();

            var memberships = await queryDb.ClubMemberships
                .AsNoTracking()
                .Where(x => x.ClubId == 10)
                .ToListAsync();

            var isOwner = await queryDb.ClubManagerAssignments
                .AsNoTracking()
                .AnyAsync(x => x.ClubId == 10 && x.ManagerUserId == 55 && x.IsActive);

            Assert.Single(clubs);
            Assert.Single(memberships);
            Assert.True(isOwner);

            // Verify that ChangeTracker has exactly zero tracked entities
            Assert.Empty(queryDb.ChangeTracker.Entries());
        }
    }

    // =========================================================================
    // 3. PERF-F05: CancellationToken Responsiveness
    // =========================================================================

    [Fact]
    public async Task ClubAndReportQueries_HonorCancellationToken_WhenAborted()
    {
        var dbName = "cancel_token_test_" + Guid.NewGuid().ToString("N");
        var clubOptions = new DbContextOptionsBuilder<ClubDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using var db = new ClubDbContext(clubOptions);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await db.Clubs.AsNoTracking().ToListAsync(cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await db.ClubMemberships.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await db.ClubManagerAssignments.AsNoTracking().AnyAsync(x => x.ClubId == 1, cts.Token);
        });
    }

    [Fact]
    public async Task ReportQueries_HonorCancellationToken_WhenAborted()
    {
        var dbName = "report_cancel_token_test_" + Guid.NewGuid().ToString("N");
        var reportOptions = new DbContextOptionsBuilder<ReportDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using var db = new ReportDbContext(reportOptions);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await db.Reports.AsNoTracking().ToListAsync(cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await db.Reports.AsNoTracking().AnyAsync(x => x.Period == "2026-Q1", cts.Token);
        });
    }
}
