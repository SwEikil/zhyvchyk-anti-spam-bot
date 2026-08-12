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
        => CanConfigure(
            user.Id,
            user.Guild.OwnerId,
            user.GuildPermissions.Administrator,
            user.Roles.Select(role => role.Id),
            settings);

    public static bool CanConfigure(
        ulong userId,
        ulong ownerId,
        bool isAdministrator,
        IEnumerable<ulong> roleIds,
        GuildSettings settings)
    {
        if (userId == ownerId)
        {
            return true;
        }

        if (settings.Access.OwnerOnlyBootstrap && !settings.SetupCompleted)
        {
            return false;
        }

        if (settings.Access.AllowDiscordAdministratorsAfterSetup && isAdministrator)
        {
            return true;
        }

        return roleIds.Any(settings.Access.ManagerRoleIds.Contains);
    }

    public bool IsExemptFromModeration(SocketGuildUser user, GuildSettings settings)
    {
        return IsExemptFromModeration(
            user.Id,
            user.GuildPermissions.Administrator,
            user.Roles.Select(role => role.Id),
            settings);
    }

    public static bool IsExemptFromModeration(
        ulong userId,
        bool isAdministrator,
        IEnumerable<ulong> roleIds,
        GuildSettings settings)
    {
        if (settings.FalsePositiveProtection.WhitelistedUserIds.Contains(userId) ||
            settings.FalsePositiveProtection.IgnoredUserIds.Contains(userId))
        {
            return true;
        }

        if (settings.FalsePositiveProtection.IgnoreAdministrators && isAdministrator)
        {
            return true;
        }

        return roleIds.Any(roleId =>
            settings.FalsePositiveProtection.IgnoredRoleIds.Contains(roleId) ||
            settings.Access.ManagerRoleIds.Contains(roleId));
    }
}
