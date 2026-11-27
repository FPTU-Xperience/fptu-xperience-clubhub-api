namespace ClubReportHub.Shared.Experience;

public sealed record RawFundingCandidate(int UserId, string SourceKey);
public sealed record RawFundingSource(string SourceKey, string Version, string Status, int ClubId, string CampusCode,
    string SemesterId, int CreatedBy, Guid? PolicyVersion, decimal? Budget, int? ActivityId,
    Guid? FundingReservationId, bool ClosingApproved, IReadOnlyList<RawFundingCandidate> Candidates);
public sealed record ReserveRawFundingRequest(string SourceKey, string ExpectedSourceVersion);
public sealed record RawFundingReceipt(Guid Id, int ClubId, string CampusCode, string SemesterId,
    string SourceKey, string SourceVersion, Guid PolicyVersion, decimal Budget, string Status);
