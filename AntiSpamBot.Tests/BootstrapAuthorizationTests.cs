using AntiSpamBot.Configuration;
using AntiSpamBot.Services;
using Xunit;

namespace AntiSpamBot.Tests;

public sealed class BootstrapAuthorizationTests
{
    [Fact]
    public void ServerOwner_CanPerformInitialSetup()
    {
        var settings = GuildSettings.CreateDefault();

        var allowed = AccessControlService.CanConfigure(
            userId: 10,
            ownerId: 10,
            isAdministrator: false,
            roleIds: [],
            settings);

        Assert.True(allowed);
    }

    [Fact]
    public void DiscordAdministrator_CanPerformInitialSetupOnAdminBootstrapVariant()
    {
        var settings = GuildSettings.CreateDefault();

        Assert.False(settings.SetupCompleted);
        Assert.False(settings.Access.OwnerOnlyBootstrap);
        Assert.True(AccessControlService.CanConfigure(
            userId: 20,
            ownerId: 10,
            isAdministrator: true,
            roleIds: [],
            settings));
    }
}
