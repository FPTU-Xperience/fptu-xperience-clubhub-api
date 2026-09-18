namespace Backend.StabilizationTests;

public sealed class SolutionIntegrityTests
{
    [Fact]
    public void SolutionContainsRealAdminProjectsAndNoMissingLegacyTestProject()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "ClubReportHub.sln"));

        Assert.DoesNotContain("tests\\ClubReportHub.Tests\\ClubReportHub.Tests.csproj", solution);
        Assert.Contains("src\\Services\\AdminService\\AdminService.csproj", solution);
        Assert.Contains(
            "tests\\AdminService.IntegrationTests\\AdminService.IntegrationTests.csproj",
            solution);
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
