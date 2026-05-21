using AntiSpamBot.Configuration;
using Discord.WebSocket;

namespace AntiSpamBot.Services;

public interface IAccessControlService
{
    bool CanConfigure(SocketGuildUser user, GuildSettings settings);
    bool IsExemptFromModeration(SocketGuildUser user, GuildSettings settings);
}

public sealed class AccessControlService : IAccessControlService
{
    public bool CanConfigure(SocketGuildUser user, GuildSettings settings)
    {
        if (user.Id == user.Guild.OwnerId)
        {
            return true;
        }

        if (settings.Access.OwnerOnlyBootstrap && !settings.SetupCompleted)
        {
            return false;
        }

        if (settings.Access.AllowDiscordAdministratorsAfterSetup && user.GuildPermissions.Administrator)
        {
            return true;
        }

        return user.Roles.Any(role => settings.Access.ManagerRoleIds.Contains(role.Id));
    }

    public bool IsExemptFromModeration(SocketGuildUser user, GuildSettings settings)
    {
        if (settings.FalsePositiveProtection.WhitelistedUserIds.Contains(user.Id) ||
            settings.FalsePositiveProtection.IgnoredUserIds.Contains(user.Id))
        {
            return true;
        }

        if (settings.FalsePositiveProtection.IgnoreAdministrators && user.GuildPermissions.Administrator)
        {
            return true;
        }

        return user.Roles.Any(role => settings.FalsePositiveProtection.IgnoredRoleIds.Contains(role.Id));
    }
}
