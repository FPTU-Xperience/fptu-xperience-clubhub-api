using AuthService.Models;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Xunit;

namespace ClubReportHub.Tests;

public class ActorAccountPolicyTests
{
    [Theory]
    [InlineData(AuthRoles.Admin)]
    [InlineData(AuthRoles.ClubManager)]
    [InlineData(AuthRoles.ClubMember)]
    public void IsAllowedGoogleActorRole_StandardRoles_ReturnsTrue(string roleName)
    {
        var result = ActorAccountPolicy.IsAllowedGoogleActorRole(roleName);

        Assert.True(result);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("club_manager")]
    [InlineData("club_member")]
    [InlineData("ADMIN")]
    [InlineData("Club_Manager")]
    public void IsAllowedGoogleActorRole_CaseInsensitiveMatching_ReturnsTrue(string roleName)
    {
        var result = ActorAccountPolicy.IsAllowedGoogleActorRole(roleName);

        Assert.True(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GUEST")]
    [InlineData("SUPER_ADMIN")]
    [InlineData("STUDENT")]
    [InlineData("FACULTY")]
    public void IsAllowedGoogleActorRole_InvalidOrEmptyRoles_ReturnsFalse(string? roleName)
    {
        var result = ActorAccountPolicy.IsAllowedGoogleActorRole(roleName);

        Assert.False(result);
    }

    [Theory]
    [InlineData(AuthRoles.Admin)]
    [InlineData(AuthRoles.ClubManager)]
    [InlineData(AuthRoles.ClubMember)]
    public void HasValidActorConfiguration_SingleAllowedRole_ReturnsTrue(string roleName)
    {
        var user = new User
        {
            Id = 1,
            Username = "actor.user",
            Email = "actor@fpt.edu.vn",
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = roleName } }
            }
        };

        var result = ActorAccountPolicy.HasValidActorConfiguration(user);

        Assert.True(result);
    }

    [Fact]
    public void HasValidActorConfiguration_NoRoles_ReturnsFalse()
    {
        var user = new User
        {
            Id = 1,
            Username = "noroles.user",
            Email = "noroles@fpt.edu.vn",
            UserRoles = new List<UserRole>()
        };

        var result = ActorAccountPolicy.HasValidActorConfiguration(user);

        Assert.False(result);
    }

    [Fact]
    public void HasValidActorConfiguration_MultipleDistinctRoles_ReturnsFalse()
    {
        var user = new User
        {
            Id = 1,
            Username = "multirole.user",
            Email = "multi@fpt.edu.vn",
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = AuthRoles.Admin } },
                new() { Role = new Role { Name = AuthRoles.ClubManager } }
            }
        };

        var result = ActorAccountPolicy.HasValidActorConfiguration(user);

        Assert.False(result);
    }

    [Fact]
    public void HasValidActorConfiguration_DuplicateSameAllowedRole_ReturnsTrue()
    {
        // Edge case: duplicate entries of the same role should collapse via Distinct to 1 role
        var user = new User
        {
            Id = 1,
            Username = "duprole.user",
            Email = "dup@fpt.edu.vn",
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = AuthRoles.Admin } },
                new() { Role = new Role { Name = "admin" } }
            }
        };

        var result = ActorAccountPolicy.HasValidActorConfiguration(user);

        Assert.True(result);
    }

    [Fact]
    public void HasValidActorConfiguration_DisallowedRole_ReturnsFalse()
    {
        var user = new User
        {
            Id = 1,
            Username = "disallowed.user",
            Email = "disallowed@fpt.edu.vn",
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = "GUEST" } }
            }
        };

        var result = ActorAccountPolicy.HasValidActorConfiguration(user);

        Assert.False(result);
    }

    [Fact]
    public void HasValidActorConfiguration_AllowedAndDisallowedRole_ReturnsFalse()
    {
        var user = new User
        {
            Id = 1,
            Username = "mixed.user",
            Email = "mixed@fpt.edu.vn",
            UserRoles = new List<UserRole>
            {
                new() { Role = new Role { Name = AuthRoles.Admin } },
                new() { Role = new Role { Name = "GUEST" } }
            }
        };

        var result = ActorAccountPolicy.HasValidActorConfiguration(user);

        Assert.False(result);
    }
}
