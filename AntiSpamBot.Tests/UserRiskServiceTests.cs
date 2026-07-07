using AntiSpamBot.Configuration;
using AntiSpamBot.Services;
using Xunit;

namespace AntiSpamBot.Tests;

public sealed class UserRiskServiceTests
{
    private static readonly DateTimeOffset Now = new(2034, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AccountYoungerThanConfiguredDays_IsRisky()
    {
        var settings = new UserRiskSettings();

        var result = UserRiskService.Evaluate(
            Now.AddDays(-30),
            Now.AddDays(-120),
            messageHasLinks: false,
            settings,
            Now);

        Assert.True(result.IsFreshAccount);
        Assert.False(result.IsRecentJoin);
        Assert.True(result.StrictMonitoringApplies);
        Assert.Contains("fresh account", result.Reason);
    }

    [Fact]
    public void RecentlyJoinedMember_IsRisky()
    {
        var settings = new UserRiskSettings();

        var result = UserRiskService.Evaluate(
            Now.AddDays(-400),
            Now.AddDays(-10),
            messageHasLinks: false,
            settings,
            Now);

        Assert.False(result.IsFreshAccount);
        Assert.True(result.IsRecentJoin);
        Assert.True(result.StrictMonitoringApplies);
        Assert.Contains("recent join", result.Reason);
    }

    [Fact]
    public void FreshAccountAndRecentJoin_AddStrictMonitoringBonus()
    {
        var settings = new UserRiskSettings();

        var freshOnly = UserRiskService.Evaluate(
            Now.AddDays(-10),
            Now.AddDays(-120),
            messageHasLinks: false,
            settings,
            Now);
        var both = UserRiskService.Evaluate(
            Now.AddDays(-10),
            Now.AddDays(-5),
            messageHasLinks: false,
            settings,
            Now);

        Assert.True(both.IsFreshAccount);
        Assert.True(both.IsRecentJoin);
        Assert.True(both.Score > freshOnly.Score);
        Assert.Equal(settings.StrictMonitoringScoreBonus, both.StrictMonitoringScoreBonus);
        Assert.Contains("strict monitoring user-risk rule", both.Reason);
    }

    [Fact]
    public void OldAccountAndOldJoin_AreNotStrict()
    {
        var settings = new UserRiskSettings();

        var result = UserRiskService.Evaluate(
            Now.AddDays(-400),
            Now.AddDays(-300),
            messageHasLinks: false,
            settings,
            Now);

        Assert.False(result.IsFreshAccount);
        Assert.False(result.IsRecentJoin);
        Assert.False(result.StrictMonitoringApplies);
        Assert.Equal(0, result.Score);
        Assert.Equal("", result.Reason);
    }
}
