-- Read-only preflight for existing databases before applying the 26/10 migrations.
-- Run each section in its named database; new installations do not need data cleanup.
-- Any returned rows require explicit reconciliation before deployment. This script changes no data.

USE [ClubReportHub_Club];
GO
SELECT [RequesterUserId], COUNT_BIG(*) AS [SubmittedCount]
FROM [dbo].[ClubCreationApplications]
WHERE [Status] = N'Submitted'
GROUP BY [RequesterUserId]
HAVING COUNT_BIG(*) > 1;
GO

USE [ClubReportHub_Report];
GO
IF COL_LENGTH(N'dbo.Reports', N'BudgetProposalId') IS NOT NULL
    EXEC(N'SELECT [BudgetProposalId], COUNT_BIG(*) AS [ReportCount]
           FROM [dbo].[Reports] WHERE [BudgetProposalId] IS NOT NULL
           GROUP BY [BudgetProposalId] HAVING COUNT_BIG(*) > 1;');

SELECT a.[Id] AS [AttachmentId], a.[ReportId], a.[ReportDetailId]
FROM [dbo].[ReportAttachments] a
LEFT JOIN [dbo].[ReportDetails] d
  ON d.[Id] = a.[ReportDetailId] AND d.[ReportId] = a.[ReportId]
WHERE a.[ReportDetailId] IS NOT NULL AND d.[Id] IS NULL;
-- The Report migration detaches these invalid detail links; file/attachment rows are retained.
GO

USE [ClubReportHub_Activity];
GO
SELECT a.[Id] AS [AttendanceId], a.[ActivityId], a.[SessionId]
FROM [dbo].[ActivityAttendances] a
LEFT JOIN [dbo].[Sessions] s
  ON s.[Id] = a.[SessionId] AND s.[ActivityId] = a.[ActivityId]
WHERE a.[SessionId] IS NOT NULL AND s.[Id] IS NULL;
GO

USE [ClubReportHub_Admin];
GO
SELECT e.[Id] AS [EffectId], e.[ContributionId], e.[RevisionId]
FROM [dbo].[ExperienceLedgerEffects] e
LEFT JOIN [dbo].[ContributionRevisions] r
  ON r.[Id] = e.[RevisionId] AND r.[ContributionId] = e.[ContributionId]
WHERE r.[Id] IS NULL;

SELECT d.[Id] AS [DecisionId], d.[ContributionId], d.[RevisionId]
FROM [dbo].[ContributionDecisions] d
LEFT JOIN [dbo].[ContributionRevisions] r
  ON r.[Id] = d.[RevisionId] AND r.[ContributionId] = d.[ContributionId]
WHERE r.[Id] IS NULL;
-- TargetRevisionId intentionally remains polymorphic; it is not a fixed revision FK.
GO
