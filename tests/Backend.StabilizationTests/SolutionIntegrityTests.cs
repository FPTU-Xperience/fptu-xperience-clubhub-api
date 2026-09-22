namespace Backend.StabilizationTests;

public sealed class SolutionIntegrityTests
{
    [Fact]
    public void SolutionContainsRealAdminProjectsAndAllTestProjects()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "ClubReportHub.sln"));

        // TEST-F03: every test project must be part of the solution so that
        // `dotnet test ClubReportHub.sln` (and therefore CI) executes it.
        Assert.True(
            solution.Contains("tests\\ClubReportHub.Tests\\ClubReportHub.Tests.csproj") ||
            solution.Contains("tests/ClubReportHub.Tests/ClubReportHub.Tests.csproj"),
            "ClubReportHub.Tests.csproj must be part of ClubReportHub.sln");
        Assert.True(
            solution.Contains("tests\\Backend.StabilizationTests\\Backend.StabilizationTests.csproj") ||
            solution.Contains("tests/Backend.StabilizationTests/Backend.StabilizationTests.csproj"),
            "Backend.StabilizationTests.csproj must be part of ClubReportHub.sln");
        Assert.True(
            solution.Contains("src\\Services\\AdminService\\AdminService.csproj") ||
            solution.Contains("src/Services/AdminService/AdminService.csproj"),
            "AdminService.csproj must be part of ClubReportHub.sln");
        Assert.True(
            solution.Contains("tests\\AdminService.IntegrationTests\\AdminService.IntegrationTests.csproj") ||
            solution.Contains("tests/AdminService.IntegrationTests/AdminService.IntegrationTests.csproj"),
            "AdminService.IntegrationTests.csproj must be part of ClubReportHub.sln");
        Assert.True(File.Exists(Path.Combine(
            root,
            "src",
            "Services",
            "AdminService",
            "AdminService.csproj")));
        Assert.True(File.Exists(Path.Combine(
            root,
            "tests",
            "AdminService.IntegrationTests",
            "AdminService.IntegrationTests.csproj")));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ClubReportHub.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate ClubReportHub.sln.");
    }
}
