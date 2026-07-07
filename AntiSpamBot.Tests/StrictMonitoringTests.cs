using AntiSpamBot.AntiSpam;
using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using AntiSpamBot.Moderation;
using AntiSpamBot.Services;
using Xunit;

namespace AntiSpamBot.Tests;

public sealed class StrictMonitoringTests
{
    [Fact]
    public void StrictMonitoringUser_UsesLowerSpamThresholds()
    {
        var settings = GuildSettings.CreateDefault();
        var risk = new UserRiskEvaluation(
            Score: 39,
            Reason: "fresh account",
            IsFreshAccount: true,
            IsRecentJoin: false,
            StrictMonitoringApplies: true,
            StrictMonitoringScoreBonus: 0);

        var thresholds = AntiSpamService.CalculateEffectiveThresholds(
            settings.Detection,
            settings.UserRisk,
            ThreatLevel.Normal,
            risk);

        Assert.Equal(settings.UserRisk.StrictMonitoringMinimumSpamCount, thresholds.MinimumSpamCount);
        Assert.True(thresholds.MinimumSpamCount < settings.Detection.MinimumSpamCount);
        Assert.True(thresholds.MaxMessagesBeforePunishment < settings.Detection.MaxMessagesBeforePunishment);
    }

    [Fact]
    public void TrustedOlderUser_UsesNormalThresholds()
    {
        var settings = GuildSettings.CreateDefault();

        var thresholds = AntiSpamService.CalculateEffectiveThresholds(
            settings.Detection,
            settings.UserRisk,
            ThreatLevel.Normal,
            UserRiskEvaluation.None);

        Assert.Equal(settings.Detection.MinimumSpamCount, thresholds.MinimumSpamCount);
        Assert.Equal(settings.Detection.MaxMessagesBeforePunishment, thresholds.MaxMessagesBeforePunishment);
    }

    [Fact]
    public void DryRun_DisablesStrictMonitoringTimeoutApplication()
    {
        var settings = GuildSettings.CreateDefault();
        settings.DryRun.Enabled = true;
        var detection = new SpamDetectionResult
        {
            IsSpam = true,
            StrictMonitoringApplied = true,
            UserRiskScore = 69,
            UserRiskDetails = "fresh account, recent join"
        };

        var shouldTimeout = ModerationService.ShouldApplyStrictMonitoringTimeout(settings, detection);

        Assert.False(shouldTimeout);
    }

    [Fact]
    public void StrictMonitoringTimeout_IsSelectedForConfirmedStrictSpam()
    {
        var settings = GuildSettings.CreateDefault();
        settings.Punishment.EnableTimeout = false;
        var detection = new SpamDetectionResult
        {
            IsSpam = true,
            StrictMonitoringApplied = true,
            UserRiskScore = 69,
            UserRiskDetails = "fresh account, recent join"
        };

        var shouldTimeout = ModerationService.ShouldApplyStrictMonitoringTimeout(settings, detection);

        Assert.True(shouldTimeout);
    }

    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public void FalsePositiveProtections_ExemptUsersBeforePunishment(
        bool ignoredUser,
        bool whitelistedUser,
        bool ignoredRole,
        bool administrator,
        bool managerRole)
    {
        var settings = GuildSettings.CreateDefault();
        var userId = 42UL;
        var roleId = 99UL;
        if (ignoredUser)
        {
            settings.FalsePositiveProtection.IgnoredUserIds.Add(userId);
        }

        if (whitelistedUser)
        {
            settings.FalsePositiveProtection.WhitelistedUserIds.Add(userId);
        }

        if (ignoredRole)
        {
            settings.FalsePositiveProtection.IgnoredRoleIds.Add(roleId);
        }

        if (managerRole)
        {
            settings.Access.ManagerRoleIds.Add(roleId);
        }

        var exempt = AccessControlService.IsExemptFromModeration(
            userId,
            administrator,
            ignoredRole || managerRole ? new[] { roleId } : Array.Empty<ulong>(),
            settings);

        Assert.True(exempt);
    }
}
