using AntiSpamBot.Configuration;
using AntiSpamBot.Localization;
using AntiSpamBot.Models;
using AntiSpamBot.Services;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AntiSpamBot.Moderation;

public interface IModerationService
{
    Task ApplyAsync(
        SocketGuild guild,
        SocketGuildUser user,
        SpamDetectionResult detection,
        GuildSettings settings,
        CancellationToken cancellationToken = default);

    Task RemoveTimeoutAsync(SocketGuildUser user, string reason, CancellationToken cancellationToken = default);
}

public sealed class ModerationService(
    IUserStrikeStore strikeStore,
    ITempBanStore tempBanStore,
    ITextLocalizer localizer,
    ILocalModerationLogService localLogs,
    ILogger<ModerationService> logger) : IModerationService
{
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _deleteAttempts = new();

    public async Task ApplyAsync(
        SocketGuild guild,
        SocketGuildUser user,
        SpamDetectionResult detection,
        GuildSettings settings,
        CancellationToken cancellationToken = default)
    {
        var existingStrike = await strikeStore.GetAsync(guild.Id, user.Id, cancellationToken);
        var isPunishmentCooldownActive = existingStrike is not null &&
            settings.Punishment.CooldownSeconds > 0 &&
            DateTimeOffset.UtcNow - existingStrike.LastPunishmentAt < TimeSpan.FromSeconds(settings.Punishment.CooldownSeconds);

        var localizedDetectionReason = LocalizeDetectionReason(settings, detection);
        var reason = BuildReason(settings, localizedDetectionReason, detection);
        if (settings.DryRun.Enabled)
        {
            await localLogs.WriteAsync(guild.Id, settings, "dry_run_detection", new
            {
                userId = user.Id,
                user = user.Username,
                detection.TriggerType,
                detection.Reason,
                messageIds = detection.Messages.Select(item => item.MessageId).ToArray(),
                channels = detection.AffectedChannelIds
            }, cancellationToken);
            await SendLogAsync(guild, user, settings, detection, 0, localizer.Get(settings, "punishment_dry_run"), reason);
            return;
        }

        var deletedCount = await DeleteDetectedMessagesAsync(guild, detection, cancellationToken);

        if (isPunishmentCooldownActive)
        {
            // A single spam burst can generate several gateway events. The cooldown prevents
            // duplicate punishments, but new spam messages still have to be removed.
            await SendLogAsync(guild, user, settings, detection, deletedCount, localizer.Get(settings, "punishment_cooldown"), reason);
            return;
        }

        var strike = await strikeStore.RegisterDetectionAsync(guild.Id, user.Id, cancellationToken);
        var punishment = await ApplyPunishmentAsync(guild, user, settings, strike, reason, cancellationToken);

        await strikeStore.SetLastPunishmentAsync(guild.Id, user.Id, DateTimeOffset.UtcNow, cancellationToken);
        await localLogs.WriteAsync(guild.Id, settings, "moderation_action", new
        {
            userId = user.Id,
            user = user.Username,
            detection.TriggerType,
            detection.Reason,
            deletedCount,
            punishment,
            channels = detection.AffectedChannelIds
        }, cancellationToken);
        await SendLogAsync(guild, user, settings, detection, deletedCount, punishment, reason);
    }

    public Task RemoveTimeoutAsync(SocketGuildUser user, string reason, CancellationToken cancellationToken = default) =>
        user.RemoveTimeOutAsync(new RequestOptions
        {
            AuditLogReason = string.IsNullOrWhiteSpace(reason) ? "Manual anti-spam unmute" : reason
        });

    private async Task<int> DeleteDetectedMessagesAsync(SocketGuild guild, SpamDetectionResult detection, CancellationToken cancellationToken)
    {
        var deletedCount = 0;
        CleanupDeleteAttempts();

        foreach (var tracked in detection.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_deleteAttempts.TryAdd(tracked.MessageId, DateTimeOffset.UtcNow))
            {
                continue;
            }

            try
            {
                var channel = guild.GetTextChannel(tracked.ChannelId);
                if (channel is null)
                {
                    continue;
                }

                await channel.DeleteMessageAsync(tracked.MessageId);
                deletedCount++;
            }
            catch (HttpException exception) when ((int?)exception.DiscordCode == 10008)
            {
                logger.LogDebug("Spam message {MessageId} was already deleted in guild {GuildId}.", tracked.MessageId, guild.Id);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to delete spam message {MessageId} in guild {GuildId}.",
                    tracked.MessageId,
                    guild.Id);
            }
        }

        return deletedCount;
    }

    private void CleanupDeleteAttempts()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
        foreach (var (messageId, timestamp) in _deleteAttempts)
        {
            if (timestamp < cutoff)
            {
                _deleteAttempts.TryRemove(messageId, out _);
            }
        }
    }

    private async Task<string> ApplyPunishmentAsync(
        SocketGuild guild,
        SocketGuildUser user,
        GuildSettings settings,
        UserStrikeState strike,
        string reason,
        CancellationToken cancellationToken)
    {
        if (settings.Punishment.EnableBan &&
            settings.Punishment.BanAfterDetections > 0 &&
            strike.DetectionCount >= settings.Punishment.BanAfterDetections)
        {
            if (settings.Punishment.EnablePermanentBan)
            {
                await TryDmUserAsync(user, BuildBanDm(settings, guild, reason, null));
                await guild.AddBanAsync(user, Math.Clamp(settings.Punishment.BanDeleteMessageDays, 0, 7), reason);
                return localizer.Get(settings, "punishment_permanent_ban");
            }

            var tempBanDuration = GetTempBanDuration(settings, strike.DetectionCount);
            var until = DateTimeOffset.UtcNow.Add(tempBanDuration);
            var record = new TempBanRecord
            {
                GuildId = guild.Id,
                UserId = user.Id,
                BannedAt = DateTimeOffset.UtcNow,
                ExpiresAt = until,
                Status = TempBanStatuses.Pending,
                Reason = reason
            };
            await tempBanStore.AddAsync(record, cancellationToken);
            await TryDmUserAsync(user, BuildBanDm(settings, guild, reason, until));
            try
            {
                await guild.AddBanAsync(user, Math.Clamp(settings.Punishment.BanDeleteMessageDays, 0, 7), reason);
            }
            catch
            {
                await tempBanStore.RemoveAsync(guild.Id, user.Id, cancellationToken);
                throw;
            }

            record.Status = TempBanStatuses.Active;
            await tempBanStore.UpdateAsync(record, cancellationToken);
            return localizer.Format(settings, "punishment_tempban", new Dictionary<string, string>
            {
                ["duration"] = FormatDuration(tempBanDuration),
                ["until"] = until.ToString("u")
            });
        }

        if (!settings.Punishment.EnableTimeout)
        {
            return localizer.Get(settings, "punishment_none");
        }

        var duration = GetTimeoutDuration(settings, strike.DetectionCount);
        await user.SetTimeOutAsync(duration, new RequestOptions
        {
            AuditLogReason = reason
        });

        return localizer.Format(settings, "punishment_timeout", new Dictionary<string, string>
        {
            ["duration"] = FormatDuration(duration)
        });
    }

    private static TimeSpan GetTimeoutDuration(GuildSettings settings, int detectionCount)
    {
        var durations = settings.Punishment.TimeoutDurationsSeconds is { Count: > 0 }
            ? settings.Punishment.TimeoutDurationsSeconds.Where(seconds => seconds > 0).ToArray()
            : settings.Punishment.TimeoutDurationsMinutes
                .Where(minutes => minutes > 0)
                .Select(minutes => minutes * 60.0)
                .DefaultIfEmpty(600.0)
                .ToArray();

        var index = Math.Clamp(detectionCount - 1, 0, durations.Length - 1);
        return TimeSpan.FromSeconds(Math.Clamp(durations[index], 1, 2419200));
    }

    private static TimeSpan GetTempBanDuration(GuildSettings settings, int detectionCount)
    {
        var durations = settings.Punishment.TempBanDurationsSeconds
            .Where(seconds => seconds > 0)
            .DefaultIfEmpty(3600.0)
            .ToArray();

        var banStep = Math.Max(1, detectionCount - Math.Max(1, settings.Punishment.BanAfterDetections) + 1);
        var index = Math.Clamp(banStep - 1, 0, durations.Length - 1);
        return TimeSpan.FromSeconds(Math.Clamp(durations[index], 1, 2419200));
    }

    private async Task SendLogAsync(
        SocketGuild guild,
        SocketGuildUser user,
        GuildSettings settings,
        SpamDetectionResult detection,
        int deletedCount,
        string punishment,
        string reason)
    {
        var logChannelId = settings.Channels.LogChannelId ?? settings.Channels.NotificationChannelId;
        if (logChannelId is null)
        {
            logger.LogInformation(
                "Spam action in guild {GuildId} had no configured log channel. User {UserId}, reason {Reason}.",
                guild.Id,
                user.Id,
                reason);
            return;
        }

        var channel = guild.GetTextChannel(logChannelId.Value);
        if (channel is null)
        {
            return;
        }

        var channels = string.Join(", ", detection.AffectedChannelIds.Select(id => $"<#{id}>"));
        var sample = detection.Messages.FirstOrDefault()?.RawContent ?? "";
        var timestamps = string.Join("\n", detection.Messages.Select(item => $"{item.Timestamp:u} <#{item.ChannelId}>"));
        var ping = BuildAdminPing(settings, user, detection, deletedCount, punishment, reason, channels);

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get(settings, "spam_log_title"))
            .WithColor(Color.Red)
            .AddField(localizer.Get(settings, "log_field_user"), $"{user.Mention} `{user.Username}` (`{user.Id}`)", false)
            .AddField(localizer.Get(settings, "log_field_reason"), reason, false)
            .AddField(localizer.Get(settings, "log_field_channels"), string.IsNullOrWhiteSpace(channels) ? localizer.Get(settings, "unknown") : channels, false)
            .AddField(localizer.Get(settings, "log_field_deleted"), deletedCount.ToString(), true)
            .AddField(localizer.Get(settings, "log_field_punishment"), punishment, true)
            .AddField(localizer.Get(settings, "log_field_original"), TrimForEmbed(sample, settings), false)
            .AddField(localizer.Get(settings, "log_field_timestamps"), TrimForEmbed(timestamps, settings), false)
            .WithFooter(localizer.Format(settings, "spam_log_footer", new Dictionary<string, string>
            {
                ["trigger"] = detection.TriggerType.ToString(),
                ["similarity"] = detection.SimilarityScore.ToString("0.00")
            }))
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(
            string.IsNullOrWhiteSpace(ping) ? null : ping,
            embed: embed);
    }

    private string LocalizeDetectionReason(GuildSettings settings, SpamDetectionResult detection)
    {
        if (string.IsNullOrWhiteSpace(detection.ReasonKey))
        {
            return detection.Reason;
        }

        var values = detection.ReasonValues is { Count: > 0 }
            ? detection.ReasonValues
            : new Dictionary<string, string> { ["reason"] = detection.Reason };
        return localizer.Format(settings, detection.ReasonKey, values);
    }

    private static string BuildReason(GuildSettings settings, string localizedReason, SpamDetectionResult detection) =>
        settings.Punishment.ReasonTemplate
            .Replace("{reason}", localizedReason, StringComparison.OrdinalIgnoreCase)
            .Replace("{trigger}", detection.TriggerType.ToString(), StringComparison.OrdinalIgnoreCase);

    private static string BuildAdminPing(
        GuildSettings settings,
        SocketGuildUser user,
        SpamDetectionResult detection,
        int deletedCount,
        string punishment,
        string reason,
        string channels) =>
        settings.Notifications.AdminPingMessage
            .Replace("{user}", user.Mention, StringComparison.OrdinalIgnoreCase)
            .Replace("{username}", user.Username, StringComparison.OrdinalIgnoreCase)
            .Replace("{userId}", user.Id.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{reason}", reason, StringComparison.OrdinalIgnoreCase)
            .Replace("{channels}", channels, StringComparison.OrdinalIgnoreCase)
            .Replace("{deletedCount}", deletedCount.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{punishment}", punishment, StringComparison.OrdinalIgnoreCase);

    private static async Task TryDmUserAsync(SocketGuildUser user, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            await user.SendMessageAsync(message);
        }
        catch
        {
            // Users often disable DMs from server members; moderation should still continue.
        }
    }

    private string BuildBanDm(GuildSettings settings, SocketGuild guild, string reason, DateTimeOffset? until)
    {
        if (!settings.Punishment.DmUserBeforeBan)
        {
            return "";
        }

        var defaultTemplate = "You were temporarily banned from {guild} until {until}. Reason: {reason}";
        var template = string.IsNullOrWhiteSpace(settings.Punishment.BanDmTemplate) ||
            settings.Punishment.BanDmTemplate.Equals(defaultTemplate, StringComparison.Ordinal)
            ? localizer.Get(settings, "ban_dm_template")
            : settings.Punishment.BanDmTemplate;
        var untilText = until?.ToString("u") ?? localizer.Get(settings, "permanent");

        return template
            .Replace("{guild}", guild.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{reason}", reason, StringComparison.OrdinalIgnoreCase)
            .Replace("{until}", untilText, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
        {
            return $"{duration.TotalSeconds:0.#}s";
        }

        if (duration.TotalMinutes < 60)
        {
            return $"{duration.TotalMinutes:0.#}m";
        }

        if (duration.TotalHours < 24)
        {
            return $"{duration.TotalHours:0.#}h";
        }

        return $"{duration.TotalDays:0.#}d";
    }

    private string TrimForEmbed(string value, GuildSettings settings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return localizer.Get(settings, "empty");
        }

        return value.Length <= 1000 ? value : value[..997] + "...";
    }
}
