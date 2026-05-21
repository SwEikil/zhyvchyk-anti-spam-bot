using System.Collections.Concurrent;
using AntiSpamBot.Configuration;
using Discord;
using Discord.WebSocket;

namespace AntiSpamBot.Services;

public interface IAntiNukeService
{
    Task ObserveAsync(SocketAuditLogEntry entry, SocketGuild guild, GuildSettings settings, CancellationToken cancellationToken = default);
}

public sealed class AntiNukeService(
    IThreatService threatService,
    IRaidLockdownService lockdownService,
    ILocalModerationLogService localLogs) : IAntiNukeService
{
    private static readonly HashSet<ActionType> DestructiveActions =
    [
        ActionType.ChannelDeleted,
        ActionType.RoleDeleted,
        ActionType.Ban,
        ActionType.Kick,
        ActionType.WebhookDeleted,
        ActionType.OverwriteDeleted,
        ActionType.MessageBulkDeleted
    ];

    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, List<DateTimeOffset>>> _actions = new();
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _lastTriggerAt = new();

    public async Task ObserveAsync(SocketAuditLogEntry entry, SocketGuild guild, GuildSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.AntiNuke.Enabled ||
            !DestructiveActions.Contains(entry.Action) ||
            IsTrustedActor(entry, guild, settings))
        {
            return;
        }

        var guildActions = _actions.GetOrAdd(guild.Id, _ => new ConcurrentDictionary<ulong, List<DateTimeOffset>>());
        var userActions = guildActions.GetOrAdd(entry.User.Id, _ => []);
        var count = 0;

        lock (userActions)
        {
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-settings.AntiNuke.WindowSeconds);
            userActions.RemoveAll(item => item < cutoff);
            userActions.Add(DateTimeOffset.UtcNow);
            count = userActions.Count;
        }

        await localLogs.WriteAsync(guild.Id, settings, "anti_nuke_observed", new
        {
            actorId = entry.User.Id,
            actor = entry.User.Username,
            action = entry.Action.ToString(),
            count
        }, cancellationToken);

        if (count < settings.AntiNuke.MaxDestructiveActions)
        {
            return;
        }

        if (_lastTriggerAt.TryGetValue(guild.Id, out var lastTrigger) &&
            DateTimeOffset.UtcNow - lastTrigger < TimeSpan.FromSeconds(Math.Max(10, settings.AntiNuke.WindowSeconds)))
        {
            return;
        }

        _lastTriggerAt[guild.Id] = DateTimeOffset.UtcNow;

        threatService.ObserveRaidSignal(guild.Id, settings.ThreatLevels.CriticalScore, settings);
        await localLogs.WriteAsync(guild.Id, settings, "anti_nuke_triggered", new
        {
            actorId = entry.User.Id,
            actor = entry.User.Username,
            action = entry.Action.ToString(),
            count
        }, cancellationToken);

        if (settings.AntiNuke.TriggerLockdown)
        {
            await lockdownService.MaybeActivateAsync(guild, settings, ThreatLevel.Critical, $"anti-nuke threshold by {entry.User.Username}", cancellationToken);
        }
    }

    private static bool IsTrustedActor(SocketAuditLogEntry entry, SocketGuild guild, GuildSettings settings)
    {
        if (entry.User.Id == guild.CurrentUser.Id ||
            entry.User.Id == guild.OwnerId ||
            settings.AntiNuke.TrustedUserIds.Contains(entry.User.Id))
        {
            return true;
        }

        var user = guild.GetUser(entry.User.Id);
        if (user is null)
        {
            return false;
        }

        if (settings.AntiNuke.IgnoreBotManagers &&
            user.Roles.Any(role => settings.Access.ManagerRoleIds.Contains(role.Id)))
        {
            return true;
        }

        return user.Roles.Any(role => settings.AntiNuke.TrustedRoleIds.Contains(role.Id));
    }
}
