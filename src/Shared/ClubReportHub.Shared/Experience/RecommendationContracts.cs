namespace ClubReportHub.Shared.Experience;

public sealed record RecommendationCalendar(Guid SemesterKey, string CampusCode, Guid Version,
    string Block10StartDate, string Block3StartDate);
public sealed record RecommendationExportQuery(int AfterUserId, int Limit, IReadOnlyList<RecommendationCalendar> Calendars);
public sealed record RecommendationBusyInterval(DateTimeOffset Start, DateTimeOffset End);
public sealed record RecommendationStudentSource(int UserId, string CampusCode, string AccountVersion,
    Guid PreferenceVersion, IReadOnlyList<string> Interests, string? DeclaredMajor,
    string ScheduleVersion, string ScheduleAvailability, IReadOnlyList<RecommendationBusyInterval> BusyIntervals);
public sealed record RecommendationStudentPage(IReadOnlyList<RecommendationStudentSource> Items, int NextUserId);
public sealed record RecommendationSession(int Id, DateTimeOffset Start, DateTimeOffset End, bool Required);
public sealed record RecommendationActivitySource(DateTimeOffset RegistrationDeadline, string Version,
    IReadOnlyList<RecommendationSession> Sessions);
