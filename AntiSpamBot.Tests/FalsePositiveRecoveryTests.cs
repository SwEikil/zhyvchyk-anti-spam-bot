using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using AntiSpamBot.Moderation;
using AntiSpamBot.Services;
using Discord;
using Microsoft.Extensions.Options;
using Xunit;

namespace AntiSpamBot.Tests;

public sealed class FalsePositiveRecoveryTests
{
    [Fact]
    public async Task ExactStrikeReversal_PreservesNewerDetection_AndIsIdempotent()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateStrikeStore(directory);
            await store.RegisterDetectionAsync(1, 2, "older");
            var olderPunishment = DateTimeOffset.UtcNow.AddMinutes(-5);
            await store.SetLastPunishmentAsync(1, 2, olderPunishment, detectionId: "older");
            await store.RegisterDetectionAsync(1, 2, "newer");
            var newerPunishment = DateTimeOffset.UtcNow;
            await store.SetLastPunishmentAsync(1, 2, newerPunishment, detectionId: "newer");

            Assert.True(await store.ReverseDetectionAsync(1, 2, "older"));
            Assert.False(await store.ReverseDetectionAsync(1, 2, "older"));

            var state = await store.GetAsync(1, 2);
            Assert.NotNull(state);
            Assert.Equal(1, state.DetectionCount);
            Assert.DoesNotContain("older", state.DetectionIds);
            Assert.Contains("newer", state.DetectionIds);
            Assert.Equal(newerPunishment, state.LastPunishmentAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IncidentFalsePositiveClaim_AllowsOnlyOneRecovery()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = CreateIncidentStore(directory);
            await store.CreateAsync(new ModerationIncident
            {
                IncidentId = "incident-1",
                GuildId = 1,
                TargetUserId = 2,
                TriggerType = SpamTriggerType.MessageCountBurst,
                DetectedAt = DateTimeOffset.UtcNow
            });

            var claims = await Task.WhenAll(
                store.TryClaimFalsePositiveAsync(1, "incident-1", 100),
                store.TryClaimFalsePositiveAsync(1, "incident-1", 101));

            Assert.Single(claims, item => item.Result == IncidentClaimResult.Claimed);
            Assert.Single(claims, item => item.Result == IncidentClaimResult.AlreadyHandled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnauthorizedMember_DoesNotPassManagementAuthorization()
    {
        var settings = GuildSettings.CreateDefault();
        settings.SetupCompleted = true;

        var authorized = AccessControlService.CanConfigure(
            userId: 10,
            ownerId: 20,
            isAdministrator: false,
            roleIds: [],
            settings);

        Assert.False(authorized);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ExistingAuthorizedManagers_PassManagementAuthorization(
        bool owner,
        bool administrator,
        bool manager)
    {
        var settings = GuildSettings.CreateDefault();
        settings.SetupCompleted = true;
        settings.Access.ManagerRoleIds.Add(99);

        var authorized = AccessControlService.CanConfigure(
            userId: owner ? 20UL : 10UL,
            ownerId: 20,
            isAdministrator: administrator,
            roleIds: manager ? [99UL] : [],
            settings);

        Assert.True(authorized);
    }

    [Fact]
    public void NewerTimeout_DoesNotMatchOlderIncident()
    {
        var incidentTimeout = DateTimeOffset.UtcNow.AddMinutes(10);

        Assert.True(ModerationService.CanSafelyRemoveTimeout(incidentTimeout, incidentTimeout));
        Assert.False(ModerationService.CanSafelyRemoveTimeout(incidentTimeout.AddMinutes(30), incidentTimeout));
    }

    [Fact]
    public void RestoredMessageMentions_AllowOnlyOriginalAuthor()
    {
        var allowedMentions = ModerationService.BuildRestoredAllowedMentions(42);

        Assert.Null(allowedMentions.AllowedTypes);
        Assert.Equal([42UL], allowedMentions.UserIds);
        Assert.True(allowedMentions.RoleIds is null || allowedMentions.RoleIds.Count == 0);
    }

    private static JsonUserStrikeStore CreateStrikeStore(string directory) =>
        new(Options.Create(new BotOptions { DataDirectory = directory }));

    private static JsonModerationIncidentStore CreateIncidentStore(string directory) =>
        new(Options.Create(new BotOptions { DataDirectory = directory }));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"antispam-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
