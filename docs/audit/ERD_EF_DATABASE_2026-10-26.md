# ERD từ model EF — cập nhật sau triển khai 27/10/2026

Model hiện tại: **92 bảng, 61 FK, 169 index, 70 migration** trong 8 DbContext. Đây là model trong source, không phải dump production. Không tính bảng nội bộ Hangfire hoặc __EFMigrationsHistory.

[Báo cáo triển khai và kiểm chứng](TRIEN_KHAI_ERD_DATABASE_2026-10-27.md) · [Data dictionary đầy đủ](ERD_EF_DATABASE_2026-10-26.json).

PK/FK chỉ gắn khi EF khai báo. Các cột UserId/ClubId xuyên database có thể là logical reference; các đường nối chỉ là FK nội bộ. Sơ đồ chỉ hiển thị key và một số cột định danh/trạng thái; JSON có toàn bộ cột, SQL type/nullability/index/filter/INCLUDE/concurrency.

Report migration hiện tự tạo đủ 12 cột cũ; Receipt là bảng mới. TargetRevisionId vẫn đa loại, không được vẽ thành FK cố định tới revision.

## Auth — 9 bảng, 6 FK

```mermaid
erDiagram
    ApiOperationReceipt {
        bigint Id PK
        int ActorId
    }
    PublicMediaIntents {
        uniqueidentifier Id PK
        int ActorId
        int OwnerId
        uniqueidentifier Version
    }
    RefreshTokens {
        int Id PK
        int UserId FK
        nvarchar FamilyId
    }
    Roles {
        int Id PK
    }
    StudyScheduleBlocks {
        uniqueidentifier Id PK
        int UserId FK
        uniqueidentifier Version
    }
    StudyScheduleCells {
        uniqueidentifier BlockId PK,FK
        int Week PK
        nvarchar Day PK
        int SlotNumber PK
    }
    UserProfiles {
        int UserId PK,FK
        uniqueidentifier Version
    }
    UserRoles {
        int UserId PK,FK
        int RoleId PK,FK
    }
    Users {
        int Id PK
        nvarchar Email
        bit IsActive
    }
    Users ||--o{ RefreshTokens : "UserId / Cascade"
    Users ||--o{ StudyScheduleBlocks : "UserId / Restrict"
    StudyScheduleBlocks ||--o{ StudyScheduleCells : "BlockId / Cascade"
    Users ||--o| UserProfiles : "UserId / Restrict"
    Roles ||--o{ UserRoles : "RoleId / Cascade"
    Users ||--o{ UserRoles : "UserId / Cascade"
```

## Club — 12 bảng, 8 FK

```mermaid
erDiagram
    ApiOperationReceipt {
        bigint Id PK
        int ActorId
    }
    ClubCategories {
        int Id PK
    }
    ClubCreationApplications {
        int Id PK
        uniqueidentifier ConcurrencyToken
        int CreatedClubId
        int RequesterUserId
        int ReviewedByUserId
        nvarchar Status
    }
    ClubDisbandRequests {
        int Id PK
        int ClubId FK
        uniqueidentifier ConcurrencyToken
        int RequesterUserId
        int ReviewedByUserId
        nvarchar Status
    }
    ClubManagerAssignments {
        int Id PK
        int ClubId FK
        bit IsActive
        int ManagerUserId
    }
    ClubMemberships {
        int Id PK
        int ClubId FK
        int PreviousApplicationId FK
        uniqueidentifier ConcurrencyToken
        int DeletedByUserId
        nvarchar Email
        int InvitedByUserId
        uniqueidentifier InvitedRosterRevisionId
    }
    ClubOwnershipTransfers {
        int Id PK
        int ClubId FK
        uniqueidentifier ConcurrencyToken
        int CurrentOwnerUserId
        int NewOwnerUserId
        int ReviewedByUserId
        nvarchar Status
    }
    Clubs {
        int Id PK
        uniqueidentifier ConcurrencyToken
        int DeletedByUserId
        bit IsActive
    }
    MembershipActions {
        bigint Id PK
        int MembershipId FK
        int ActorUserId
        nvarchar Status
        uniqueidentifier Version
    }
    OutboxMessages {
        uniqueidentifier Id PK
        nvarchar ClaimedByInstanceId
        uniqueidentifier ConcurrencyToken
        nvarchar CorrelationId
        nvarchar Status
    }
    PlanRevisions {
        bigint Id PK
        int PlanId FK
        int ActorId
        uniqueidentifier Version
    }
    SemesterPlans {
        int Id PK
        int ClubId FK
        nvarchar SemesterId
        nvarchar Status
        uniqueidentifier Version
    }
    Clubs ||--o{ ClubDisbandRequests : "ClubId / Cascade"
    Clubs ||--o{ ClubManagerAssignments : "ClubId / Cascade"
    Clubs ||--o{ ClubMemberships : "ClubId / Cascade"
    ClubMemberships |o--o{ ClubMemberships : "PreviousApplicationId / Restrict"
    Clubs ||--o{ ClubOwnershipTransfers : "ClubId / Cascade"
    ClubMemberships ||--o{ MembershipActions : "MembershipId / Restrict"
    SemesterPlans ||--o{ PlanRevisions : "PlanId / Restrict"
    Clubs ||--o{ SemesterPlans : "ClubId / Restrict"
```

## Activity — 19 bảng, 20 FK

```mermaid
erDiagram
    Activities {
        int Id PK
        int ClubId
        int CreatedByUserId
        nvarchar SemesterId
        int SourceReportDetailId
        int SourceReportId
        nvarchar Status
        uniqueidentifier Version
    }
    ActivityActionCompletion {
        uniqueidentifier Id PK
        int ActionRuleId FK
        int ActivityId FK
        uniqueidentifier AssignmentId FK
        nvarchar Status
        int UserId
        int Version
    }
    ActivityActionRule {
        int Id PK
        int ActivityId FK
        nvarchar ClientId
    }
    ActivityApplication {
        int Id PK
        int ClubId
        nvarchar SemesterId
        nvarchar Status
        int Version
    }
    ActivityApplicationHistory {
        bigint Id PK
        int ApplicationId FK
        int ActorId
        nvarchar Status
        int Version
    }
    ActivityAttendances {
        int Id PK
        int ActivityId FK
        int SessionId FK
        int CheckedInByUserId
        uniqueidentifier ContributionId
        uniqueidentifier ManualRequestId
        nvarchar Status
        int UserId
    }
    ActivityExecution {
        int ActivityId PK,FK
        int ApplicationId FK
        int Version
    }
    ActivityExecutionAudit {
        bigint Id PK
        int ActivityId FK
        int ActorId
        int Version
    }
    ActivityParticipants {
        int Id PK
        int ActivityId FK
        int UserId
    }
    ActivityRecruitment {
        uniqueidentifier Id PK
        int ActivityId FK
        nvarchar Status
    }
    ActivityRecruitmentPosition {
        uniqueidentifier Id PK
        int ActionRuleId FK
        uniqueidentifier RecruitmentId FK
    }
    ActivityResource {
        uniqueidentifier Id PK
        int ActivityId FK
        int UploadedByUserId
        uniqueidentifier Version
    }
    ActivityResourceAudit {
        bigint Id PK
        uniqueidentifier ResourceId FK
        int ActivityId
        int ActorId
    }
    ActivityStaffAssignment {
        uniqueidentifier Id PK
        int ActionRuleId FK
        int ActivityId FK
        uniqueidentifier PositionId FK
        nvarchar Status
        int UserId
    }
    ApiOperationReceipt {
        bigint Id PK
        int ActorId
    }
    ExperienceInbox {
        uniqueidentifier EventId PK
    }
    OutboxMessages {
        uniqueidentifier Id PK
        nvarchar ClaimedByInstanceId
        uniqueidentifier ConcurrencyToken
        nvarchar CorrelationId
        nvarchar Status
    }
    SessionAudits {
        bigint Id PK
        int ActivityId
        int ActorId
        int SessionId
        uniqueidentifier Version
    }
    Sessions {
        int Id PK
        int ActivityId FK
        int CreatedByUserId
        nvarchar SemesterId
        uniqueidentifier SeriesId
        nvarchar Status
        uniqueidentifier Version
    }
    ActivityActionRule ||--o{ ActivityActionCompletion : "ActionRuleId / Restrict"
    ActivityExecution ||--o{ ActivityActionCompletion : "ActivityId / Restrict"
    ActivityStaffAssignment ||--o{ ActivityActionCompletion : "AssignmentId / Restrict"
    ActivityExecution ||--o{ ActivityActionRule : "ActivityId / Restrict"
    ActivityApplication ||--o{ ActivityApplicationHistory : "ApplicationId / Restrict"
    Activities ||--o{ ActivityAttendances : "ActivityId / Restrict"
    Sessions |o--o{ ActivityAttendances : "ActivityId,SessionId / Restrict"
    Activities ||--o| ActivityExecution : "ActivityId / Restrict"
    ActivityApplication ||--o| ActivityExecution : "ApplicationId / Restrict"
    ActivityExecution ||--o{ ActivityExecutionAudit : "ActivityId / Restrict"
    Activities ||--o{ ActivityParticipants : "ActivityId / Cascade"
    ActivityExecution ||--o| ActivityRecruitment : "ActivityId / Restrict"
    ActivityActionRule ||--o{ ActivityRecruitmentPosition : "ActionRuleId / Restrict"
    ActivityRecruitment ||--o{ ActivityRecruitmentPosition : "RecruitmentId / Restrict"
    Activities ||--o{ ActivityResource : "ActivityId / Restrict"
    ActivityResource ||--o{ ActivityResourceAudit : "ResourceId / Restrict"
    ActivityActionRule ||--o{ ActivityStaffAssignment : "ActionRuleId / Restrict"
    ActivityExecution ||--o{ ActivityStaffAssignment : "ActivityId / Restrict"
    ActivityRecruitmentPosition |o--o{ ActivityStaffAssignment : "PositionId / Restrict"
    Activities ||--o{ Sessions : "ActivityId / Restrict"
```

## Admin — 31 bảng, 18 FK

```mermaid
erDiagram
    AcademicCalendarRevisions {
        uniqueidentifier Id PK
        uniqueidentifier CalendarId FK
        int ActorId
        uniqueidentifier Version
    }
    AcademicCalendars {
        uniqueidentifier Id PK
        uniqueidentifier SemesterId FK
        int UpdatedByUserId
        uniqueidentifier Version
    }
    ApiOperationReceipt {
        bigint Id PK
        int ActorId
    }
    ApprovedExperiencePolicies {
        uniqueidentifier Id PK
        uniqueidentifier BenchmarkId FK
        nvarchar SemesterId
    }
    AuditRecords {
        uniqueidentifier Id PK
        nvarchar ActorSubjectId
        nvarchar CorrelationId
        nvarchar ResourceId
    }
    BonusMatrixNodes {
        uniqueidentifier Id PK
    }
    ContributionDecisions {
        uniqueidentifier Id PK
        uniqueidentifier ContributionId FK
        uniqueidentifier RevisionId FK
        uniqueidentifier AssignmentId
        int ReviewerUserId
        uniqueidentifier TargetRevisionId
    }
    ContributionEvidence {
        uniqueidentifier Id PK
        int OwnerUserId
    }
    ContributionRevisions {
        uniqueidentifier Id PK
        uniqueidentifier ContributionId FK
        int ActorId
        nvarchar Status
    }
    Contributions {
        uniqueidentifier Id PK
        int ActivityId
        int AttendanceId
        int ClubId
        uniqueidentifier CurrentRevisionId
        nvarchar SemesterId
        int SessionId
        nvarchar SourceId
    }
    ContributionUpgradeRequests {
        uniqueidentifier Id PK
        uniqueidentifier ContributionId FK
        nvarchar Status
        uniqueidentifier Version
    }
    ExperienceInbox {
        uniqueidentifier EventId PK
    }
    ExperienceLedgerAllocations {
        bigint Id PK
        bigint EffectId FK
    }
    ExperienceLedgerEffects {
        bigint Id PK
        uniqueidentifier ContributionId FK
        uniqueidentifier RevisionId FK
        uniqueidentifier EventId
        nvarchar SemesterId
        int UserId
        uniqueidentifier VerificationId
        int VerifiedByUserId
    }
    InvalidatedExperienceSources {
        bigint Id PK
        int ActivityId
        int ActorId
        int SessionId
    }
    ManualAttendanceRequests {
        uniqueidentifier Id PK
        int ActivityId
        int ClubId
        uniqueidentifier ContributionId
        int SessionId
        nvarchar Status
        int UserId
        uniqueidentifier Version
    }
    OutboxMessages {
        uniqueidentifier Id PK
        nvarchar ClaimedByInstanceId
        uniqueidentifier ConcurrencyToken
        nvarchar CorrelationId
        nvarchar Status
    }
    PlatformSettings {
        uniqueidentifier Id PK
    }
    QuestCompletionRecords {
        uniqueidentifier Id PK
        uniqueidentifier ParticipantId FK
    }
    QuestParticipants {
        uniqueidentifier Id PK
        uniqueidentifier QuestId FK
        nvarchar Status
        int StudentUserId
        uniqueidentifier Version
    }
    Quests {
        uniqueidentifier Id PK
        int CreatedByUserId
        nvarchar Status
        uniqueidentifier Version
    }
    SelfDeclarations {
        uniqueidentifier Id PK
        int ClubId
        int ReviewedByUserId
        nvarchar Status
        int StudentId
    }
    SemesterBenchmarkConfigs {
        uniqueidentifier Id PK
        int ApprovedByUserId
        int CreatedByUserId
        bit IsActive
        int LockedByUserId
        uniqueidentifier Version
    }
    SemesterRoster {
        uniqueidentifier Id PK
        uniqueidentifier SemesterId FK
        uniqueidentifier RevisionId
        uniqueidentifier Version
    }
    SemesterRosterImport {
        uniqueidentifier Id PK
        uniqueidentifier SemesterId FK
        int ActorId
    }
    SemesterRosterRevision {
        uniqueidentifier Id PK
        uniqueidentifier ImportId FK
        uniqueidentifier RosterId FK
        uniqueidentifier SourceRevisionId FK
        int ActorId
        uniqueidentifier Version
    }
    SemesterRosterRow {
        bigint Id PK
        uniqueidentifier RevisionId FK
        nvarchar Email
        int UserId
    }
    Semesters {
        uniqueidentifier Id PK
        bit IsActive
    }
    VerificationAssignments {
        uniqueidentifier Id PK
        int ClubId
        int UserId
        uniqueidentifier Version
    }
    XpAnomalies {
        uniqueidentifier Id PK
        nvarchar Status
        int StudentUserId
    }
    XpLedgerEntries {
        uniqueidentifier Id PK
        uniqueidentifier RelatedAnomalyId
        int StudentUserId
    }
    AcademicCalendars ||--o{ AcademicCalendarRevisions : "CalendarId / Restrict"
    Semesters ||--o{ AcademicCalendars : "SemesterId / Restrict"
    SemesterBenchmarkConfigs ||--o{ ApprovedExperiencePolicies : "BenchmarkId / Restrict"
    Contributions ||--o{ ContributionDecisions : "ContributionId / Restrict"
    ContributionRevisions ||--o{ ContributionDecisions : "ContributionId,RevisionId / Restrict"
    Contributions ||--o{ ContributionRevisions : "ContributionId / Restrict"
    Contributions ||--o{ ContributionUpgradeRequests : "ContributionId / Restrict"
    ExperienceLedgerEffects ||--o{ ExperienceLedgerAllocations : "EffectId / Restrict"
    Contributions ||--o{ ExperienceLedgerEffects : "ContributionId / Restrict"
    ContributionRevisions ||--o{ ExperienceLedgerEffects : "ContributionId,RevisionId / Restrict"
    QuestParticipants ||--o{ QuestCompletionRecords : "ParticipantId / Restrict"
    Quests ||--o{ QuestParticipants : "QuestId / Cascade"
    Semesters ||--o{ SemesterRoster : "SemesterId / Restrict"
    Semesters ||--o{ SemesterRosterImport : "SemesterId / Restrict"
    SemesterRosterImport |o--o{ SemesterRosterRevision : "ImportId / Restrict"
    SemesterRoster ||--o{ SemesterRosterRevision : "RosterId / Restrict"
    SemesterRosterRevision |o--o{ SemesterRosterRevision : "SourceRevisionId / Restrict"
    SemesterRosterRevision ||--o{ SemesterRosterRow : "RevisionId / Restrict"
```

## Report — 8 bảng, 5 FK

```mermaid
erDiagram
    AuditLogs {
        int Id PK
        int ActorUserId
        int ReportId
    }
    OutboxMessages {
        uniqueidentifier Id PK
        nvarchar ClaimedByInstanceId
        uniqueidentifier ConcurrencyToken
        nvarchar CorrelationId
        nvarchar Status
    }
    ReportAttachments {
        int Id PK
        int ReportDetailId FK
        int ReportId FK
    }
    ReportDetails {
        int Id PK
        int ReportId FK
    }
    ReportFeedback {
        int Id PK
        int ReportId FK
        int ReviewerUserId
    }
    ReportingDeadlines {
        int Id PK
        bit IsActive
    }
    Reports {
        int Id PK
        int BudgetProposalId
        int ClubId
        int CreatedByUserId
        int PublishedActivityId
        int ReviewedByUserId
        nvarchar Status
        int Version
    }
    ReportUploadedFiles {
        int Id PK
        int ReportId FK
        bit IsActive
        int UploadedByUserId
    }
    Reports ||--o{ ReportAttachments : "ReportId / Cascade"
    ReportDetails |o--o{ ReportAttachments : "ReportId,ReportDetailId / NoAction"
    Reports ||--o{ ReportDetails : "ReportId / Cascade"
    Reports ||--o{ ReportFeedback : "ReportId / Cascade"
    Reports ||--o| ReportUploadedFiles : "ReportId / Cascade"
```

## Finance — 5 bảng, 2 FK

```mermaid
erDiagram
    BudgetProposals {
        int Id PK
        int ActivityId
        int ClubId
        int ManagerReviewedByUserId
        int ProposedByUserId
        int ReviewedByUserId
        int SourceReportId
        nvarchar Status
    }
    FinanceTransactions {
        int Id PK
        int ClubId
        int ReferenceId
    }
    ManualFinanceAdjustments {
        int Id PK
        int FinanceTransactionId FK
        int CreatedByUserId
    }
    OutboxMessages {
        uniqueidentifier Id PK
        nvarchar ClaimedByInstanceId
        uniqueidentifier ConcurrencyToken
        nvarchar CorrelationId
        nvarchar Status
    }
    Settlements {
        int Id PK
        int BudgetProposalId FK
        int ReviewedByUserId
        nvarchar Status
    }
    FinanceTransactions ||--o| ManualFinanceAdjustments : "FinanceTransactionId / Restrict"
    BudgetProposals ||--o{ Settlements : "BudgetProposalId / Cascade"
```

## Export — 5 bảng, 1 FK

```mermaid
erDiagram
    ApiOperationReceipt {
        bigint Id PK
        int ActorId
    }
    ClubExperienceReports {
        uniqueidentifier Id PK
        int ClubId
        nvarchar SemesterId
        nvarchar Status
        uniqueidentifier Version
    }
    ExportFiles {
        int Id PK
        int ExportRequestId FK
    }
    ExportRequests {
        int Id PK
        int ClubId
        int ReportId
        int RequestedByUserId
        nvarchar Status
    }
    OutboxMessages {
        uniqueidentifier Id PK
        nvarchar ClaimedByInstanceId
        uniqueidentifier ConcurrencyToken
        nvarchar CorrelationId
        nvarchar Status
    }
    ExportRequests ||--o| ExportFiles : "ExportRequestId / Cascade"
```

## Notification — 3 bảng, 1 FK

```mermaid
erDiagram
    NotificationReadReceipts {
        int NotificationId PK,FK
        int UserId PK
    }
    Notifications {
        int Id PK
        bit IsRead
        nvarchar RecipientRole
        int RecipientUserId
    }
    ProcessedEvents {
        uniqueidentifier EventId PK
    }
    Notifications ||--o{ NotificationReadReceipts : "NotificationId / Cascade"
```
