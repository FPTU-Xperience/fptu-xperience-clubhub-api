# Gemini CLI & Antigravity Guide for ClubHub Backend API

This project is the backend API for **FPTU-Xperience ClubHub** (ASP.NET Core / .NET Minimal APIs & Clean Architecture).

## Project Overview
- **Path**: `c:\Users\ADMIN\Downloads\DOAN\fptu-xperience-clubhub-api`
- **Solution**: Microservices under `src/Services/`
  - `AuthService`: Authentication, Google Sign-in, JWT, Role management
  - `ClubService`: Club directory, applications, memberships, roles
  - `ActivityService`: Club events, attendance tracking, rosters
  - `ReportService`: Financial reports, KPI, file uploads, Hangfire jobs
  - `NotificationService`: Real-time notifications, Redis stream consumers
  - `Shared/ClubReportHub.Shared`: Common models, events, security & messaging

## Common Commands
- **Build**: `dotnet build`
- **Run Tests**: `dotnet test`
- **Run Seeder**: `dotnet run --project src/Tools/DemoDataSeeder/DemoDataSeeder.csproj`

## Code Intelligence & Token Optimization
Always use the pre-built indexes before reading large files:
- **CodeGraph**: Run `codegraph explore "<query>"` for single-call AST traversal with line numbers.
- **GitNexus**: Run `gitnexus context "<symbol>"` for 360-degree symbol views, or `gitnexus impact "<symbol>"` for blast-radius analysis before modifying functions/classes.

## Task Delegation to Gemini (agy CLI)

Antigravity CLI (`agy`) is available through `scripts/run-gemini-task.ps1`.
The wrapper resolves the repository root, defaults to the configured Gemini model,
keeps the active context by default, and applies a two-minute command timeout.

### Canonical usage

Continue the current Gemini context:

```powershell
powershell -File ./scripts/run-gemini-task.ps1 "<goal>. Scope: <exact files or directory>. Stop immediately when <done condition>."
```

Start a completely separate context only for an unrelated task:

```powershell
powershell -File ./scripts/run-gemini-task.ps1 "<new goal>. Scope: <exact files or directory>. Stop immediately when <done condition>." -NewSession
```

The default model is `gemini-3.8-flash-high`. Override it only when needed:

```powershell
powershell -File ./scripts/run-gemini-task.ps1 "<task>" -Model "<model>"
```

### Delegation boundary

Codex runs these directly: `git status`, `git diff`, directory/file audits,
configuration reads, `dotnet build`, `dotnet test`, and `dotnet run`.

Use Gemini only for bounded, heavy work:

- multi-file code generation such as DTOs, repositories, or services;
- broad refactors across a defined module or several services;
- iterative compiler-error fixes after Codex has identified the scope.

Every prompt sent to Gemini must include:

1. The concrete goal.
2. The exact target files or directories.
3. A stop condition and what must remain untouched.

Do not include credentials, tokens, or other secrets. Gemini must preserve
unrelated user changes and stop after the stated condition. Codex reviews all
Gemini edits, runs the relevant verification commands, and performs the final
architecture/security decision.
