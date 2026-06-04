using System.Collections.Concurrent;
using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace AntiSpamBot.Services;

public interface IRaidLockdownService
{
    Task MaybeActivateAsync(SocketGuild guild, GuildSettings settings, ThreatLevel threatLevel, string reason, CancellationToken cancellationToken = default);
    Task ReleaseIfSafeAsync(SocketGuild guild, GuildSettings settings, ThreatLevel threatLevel, CancellationToken cancellationToken = default);
    Task RecoverAsync(SocketGuild guild, GuildSettings settings, ThreatLevel threatLevel, CancellationToken cancellationToken = default);
}

public sealed class RaidLockdownService(
    IRaidLockdownStore lockdownStore,
    ILocalModerationLogService localLogs,
    ILogger<RaidLockdownService> logger) : IRaidLockdownService
{
    private readonly ConcurrentDictionary<ulong, LockdownState> _active = new();

    public async Task MaybeActivateAsync(SocketGuild guild, GuildSettings settings, ThreatLevel threatLevel, string reason, CancellationToken cancellationToken = default)
    {
        if (!settings.RaidLockdown.Enabled || threatLevel < settings.RaidLockdown.MinimumThreatLevel)
        {
            return;
        }

        var until = DateTimeOffset.UtcNow.AddSeconds(settings.RaidLockdown.DurationSeconds);
        if (_active.TryGetValue(guild.Id, out var current) && current.Until > DateTimeOffset.UtcNow)
        {
            return;
        }

        var state = CaptureState(guild, settings, until);
        if (state.Channels.Count == 0)
        {
            logger.LogInformation("Skipping lockdown in guild {GuildId}: no manageable monitored channels.", guild.Id);
            return;
        }

        _active[guild.Id] = state;
        await lockdownStore.SaveAsync(state.ToRecord(guild.Id), cancellationToken);
        await localLogs.WriteAsync(guild.Id, settings, "raid_lockdown", new
        {
            threatLevel,
            reason,
            until
        }, cancellationToken);

        foreach (var channel in guild.TextChannels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (settings.Channels.IgnoredChannelIds.Contains(channel.Id))
                {
                    continue;
                }

                if (!guild.CurrentUser.GetPermissions(channel).ManageChannel)
                {
                    logger.LogInformation("Skipping lockdown for channel {ChannelId}: missing Manage Channel permission.", channel.Id);
                    continue;
                }

                if (settings.RaidLockdown.EnableSlowmode && SupportsSlowmode(channel))
                {
                    await channel.ModifyAsync(properties =>
                    {
                        properties.SlowModeInterval = settings.RaidLockdown.SlowmodeSeconds;
                    }, new RequestOptions { AuditLogReason = $"Anti-spam raid lockdown: {reason}" });
                }

                if (settings.RaidLockdown.EnableTemporaryChannelLock)
                {
                    await channel.AddPermissionOverwriteAsync(
                        guild.EveryoneRole,
                        OverwritePermissions.InheritAll.Modify(sendMessages: PermValue.Deny),
                        new RequestOptions { AuditLogReason = $"Anti-spam raid lockdown: {reason}" });
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to apply lockdown to channel {ChannelId}.", channel.Id);
            }
        }
    }

    public async Task ReleaseIfSafeAsync(SocketGuild guild, GuildSettings settings, ThreatLevel threatLevel, CancellationToken cancellationToken = default)
    {
        if (!_active.TryGetValue(guild.Id, out var state))
        {
            var stored = await lockdownStore.GetAsync(guild.Id, cancellationToken);
            if (stored is null)
            {
                return;
            }

            state = LockdownState.FromRecord(stored);
            _active[guild.Id] = state;
        }

        await ReleaseStateIfSafeAsync(guild, settings, threatLevel, state, cancellationToken);
    }

    public async Task RecoverAsync(SocketGuild guild, GuildSettings settings, ThreatLevel threatLevel, CancellationToken cancellationToken = default)
    {
        if (_active.ContainsKey(guild.Id))
        {
            return;
        }

        var stored = await lockdownStore.GetAsync(guild.Id, cancellationToken);
        if (stored is null)
        {
            return;
        }

        var state = LockdownState.FromRecord(stored);
        _active[guild.Id] = state;
        await ReleaseStateIfSafeAsync(guild, settings, threatLevel, state, cancellationToken);
    }

    private async Task ReleaseStateIfSafeAsync(SocketGuild guild, GuildSettings settings, ThreatLevel threatLevel, LockdownState state, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var minimumHold = TimeSpan.FromSeconds(Math.Max(0, settings.RaidLockdown.MinimumHoldSeconds));
        var heldLongEnough = now - state.StartedAt >= minimumHold;
        var expired = now >= state.Until;
        var threatDropped = settings.RaidLockdown.AutoReleaseWhenThreatDrops &&
            heldLongEnough &&
            threatLevel <= settings.RaidLockdown.ReleaseWhenAtOrBelow;

        if (!expired && !threatDropped)
        {
            return;
        }

        await RestoreAsync(guild, settings, state, expired ? "Anti-spam raid lockdown expired." : "Anti-spam threat level returned to normal.", cancellationToken);
    }

    private async Task RestoreAsync(SocketGuild guild, GuildSettings settings, LockdownState state, string auditReason, CancellationToken cancellationToken)
    {
        try
        {
            if (!_active.TryGetValue(guild.Id, out var active) || active != state)
            {
                return;
            }

            var failed = false;
            foreach (var (channelId, previous) in state.Channels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var channel = guild.GetTextChannel(channelId);
                if (channel is null)
                {
                    continue;
                }

                if (!guild.CurrentUser.GetPermissions(channel).ManageChannel)
                {
                    logger.LogWarning(
                        "Skipping lockdown restore for channel {ChannelId}: missing Manage Channel permission. Clearing stored lockdown state for this channel.",
                        channel.Id);
                    continue;
                }

                try
                {
                    if (previous.PreviousSlowmodeSeconds is not null)
                    {
                        await channel.ModifyAsync(properties =>
                        {
                            properties.SlowModeInterval = previous.PreviousSlowmodeSeconds.Value;
                        }, new RequestOptions { AuditLogReason = auditReason });
                    }

                    if (!state.LockedChannels)
                    {
                        continue;
                    }

                    if (previous.HadEveryoneOverwrite)
                    {
                        await channel.AddPermissionOverwriteAsync(
                            guild.EveryoneRole,
                            new OverwritePermissions(previous.EveryoneAllowValue, previous.EveryoneDenyValue),
                            new RequestOptions { AuditLogReason = auditReason });
                    }
                    else
                    {
                        await channel.RemovePermissionOverwriteAsync(
                            guild.EveryoneRole,
                            new RequestOptions { AuditLogReason = auditReason });
                    }
                }
                catch (HttpException exception) when (IsAccessDenied(exception))
                {
                    logger.LogWarning(
                        exception,
                        "Skipping lockdown restore for channel {ChannelId}: channel is no longer accessible. Clearing stored lockdown state for this channel.",
                        channel.Id);
                }
                catch (Exception exception)
                {
                    failed = true;
                    logger.LogWarning(exception, "Failed to restore lockdown for channel {ChannelId}.", channel.Id);
                }
            }

            if (failed)
            {
                return;
            }

            _active.TryRemove(guild.Id, out _);
            await lockdownStore.RemoveAsync(guild.Id, cancellationToken);
            await localLogs.WriteAsync(guild.Id, settings, "raid_lockdown_released", new
            {
                reason = auditReason,
                heldSeconds = (DateTimeOffset.UtcNow - state.StartedAt).TotalSeconds
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to restore raid lockdown in guild {GuildId}.", guild.Id);
        }
    }

    private static LockdownState CaptureState(SocketGuild guild, GuildSettings settings, DateTimeOffset until)
    {
        var state = new LockdownState(DateTimeOffset.UtcNow, until, settings.RaidLockdown.EnableTemporaryChannelLock);
        foreach (var channel in guild.TextChannels)
        {
            if (settings.Channels.IgnoredChannelIds.Contains(channel.Id) ||
                !guild.CurrentUser.GetPermissions(channel).ManageChannel)
            {
                continue;
            }

            var canApplySlowmode = settings.RaidLockdown.EnableSlowmode && SupportsSlowmode(channel);
            var canLockChannel = settings.RaidLockdown.EnableTemporaryChannelLock;
            if (!canApplySlowmode && !canLockChannel)
            {
                continue;
            }

            var overwrite = channel.GetPermissionOverwrite(guild.EveryoneRole);
            state.Channels[channel.Id] = new LockdownChannelState
            {
                PreviousSlowmodeSeconds = canApplySlowmode ? channel.SlowModeInterval : null,
                HadEveryoneOverwrite = overwrite is not null,
                EveryoneAllowValue = overwrite?.AllowValue ?? 0,
                EveryoneDenyValue = overwrite?.DenyValue ?? 0
            };
        }

        return state;
    }

    private static bool SupportsSlowmode(SocketTextChannel channel) => channel is not SocketNewsChannel;

    private static bool IsAccessDenied(HttpException exception) => (int?)exception.DiscordCode is 50001 or 50013;

    private sealed class LockdownState(DateTimeOffset startedAt, DateTimeOffset until, bool lockedChannels)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;
        public DateTimeOffset Until { get; } = until;
        public bool LockedChannels { get; } = lockedChannels;
        public Dictionary<ulong, LockdownChannelState> Channels { get; } = [];

        public LockdownRecord ToRecord(ulong guildId) => new()
        {
            GuildId = guildId,
            StartedAt = StartedAt,
            Until = Until,
            LockedChannels = LockedChannels,
            Channels = Channels.ToDictionary(
                item => item.Key,
                item => new LockdownChannelRecord
                {
                    PreviousSlowmodeSeconds = item.Value.PreviousSlowmodeSeconds,
                    HadEveryoneOverwrite = item.Value.HadEveryoneOverwrite,
                    EveryoneAllowValue = item.Value.EveryoneAllowValue,
                    EveryoneDenyValue = item.Value.EveryoneDenyValue
                })
        };

        public static LockdownState FromRecord(LockdownRecord record)
        {
            var state = new LockdownState(record.StartedAt, record.Until, record.LockedChannels);
            foreach (var (channelId, channel) in record.Channels)
            {
                state.Channels[channelId] = new LockdownChannelState
                {
                    PreviousSlowmodeSeconds = channel.PreviousSlowmodeSeconds,
                    HadEveryoneOverwrite = channel.HadEveryoneOverwrite,
                    EveryoneAllowValue = channel.EveryoneAllowValue,
                    EveryoneDenyValue = channel.EveryoneDenyValue
                };
            }

            return state;
        }
    }

    private sealed class LockdownChannelState
    {
        public int? PreviousSlowmodeSeconds { get; init; }
        public bool HadEveryoneOverwrite { get; init; }
        public ulong EveryoneAllowValue { get; init; }
        public ulong EveryoneDenyValue { get; init; }
    }
}
