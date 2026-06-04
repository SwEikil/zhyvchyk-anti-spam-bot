using AntiSpamBot.Configuration;
using AntiSpamBot.Localization;
using AntiSpamBot.Models;
using AntiSpamBot.Services;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace AntiSpamBot.Moderation;

public interface IModerationService
{
    Task ApplyAsync(
        SocketGuild guild,
        SocketGuildUser user,
        SpamDetectionResult detection,
        GuildSettings settings,
        CancellationToken cancellationToken = default);

    Task<bool> HandleComponentAsync(SocketMessageComponent component, GuildSettings settings, CancellationToken cancellationToken = default);
    Task RemoveTimeoutAsync(SocketGuildUser user, string reason, CancellationToken cancellationToken = default);
}

public sealed class ModerationService(
    IUserStrikeStore strikeStore,
    ITempBanStore tempBanStore,
    IAccessControlService accessControl,
    ITextLocalizer localizer,
    ILocalModerationLogService localLogs,
    ILogger<ModerationService> logger) : IModerationService
{
    private const string ReviewPrefix = "mod";
    private const int MaxDiscordTimeoutDays = 28;
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private static readonly Regex MentionRegex = new(@"<@!?\d+>|<@&\d+>|<#\d+>|@everyone|@here", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://\S+|www\.\S+|(?<![@\w])(?:https?://|www\.)?[\p{L}\p{N}](?:[\p{L}\p{N}-]{0,61}[\p{L}\p{N}])?(?:\.[\p{L}\p{N}](?:[\p{L}\p{N}-]{0,61}[\p{L}\p{N}])?)+(?:/[^\s<]*)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
            await SendLogAsync(
                guild,
                user,
                settings,
                detection,
                0,
                localizer.Get(settings, "punishment_dry_run"),
                reason,
                Array.Empty<ReportAttachment>(),
                (existingStrike?.DetectionCount ?? 0) + 1);
            return;
        }

        var reportAttachments = settings.AdminReview.Enabled && settings.AdminReview.IncludeAttachments
            ? await DownloadReportAttachmentsAsync(detection, settings, cancellationToken)
            : new List<ReportAttachment>();
        try
        {
            var deletedCount = await DeleteDetectedMessagesAsync(guild, detection, cancellationToken);

            if (isPunishmentCooldownActive)
            {
                // A single spam burst can generate several gateway events. The cooldown prevents
                // duplicate punishments, but new spam messages still have to be removed.
                await SendLogAsync(
                    guild,
                    user,
                    settings,
                    detection,
                    deletedCount,
                    localizer.Get(settings, "punishment_cooldown"),
                    reason,
                    reportAttachments,
                    existingStrike?.DetectionCount ?? 1);
                return;
            }

            var strike = await strikeStore.RegisterDetectionAsync(guild.Id, user.Id, cancellationToken);
            if (ShouldAutoBanMultiChannelDuplicate(settings, detection) &&
                settings.Punishment.BanAfterDetections > 0 &&
                strike.DetectionCount >= settings.Punishment.BanAfterDetections)
            {
                var autoBanPunishment = await BanUserAsync(guild, user.Id, user, settings, reason, cancellationToken);
                await strikeStore.SetLastPunishmentAsync(guild.Id, user.Id, DateTimeOffset.UtcNow, cancellationToken);
                await localLogs.WriteAsync(guild.Id, settings, "moderation_action", new
                {
                    userId = user.Id,
                    user = user.Username,
                    detection.TriggerType,
                    detection.Reason,
                    deletedCount,
                    punishment = autoBanPunishment,
                    escalation = "auto_ban_multi_channel_duplicate",
                    detectionCount = strike.DetectionCount,
                    channels = detection.AffectedChannelIds
                }, cancellationToken);
                await SendLogAsync(guild, user, settings, detection, deletedCount, autoBanPunishment, reason, reportAttachments, strike.DetectionCount);
                return;
            }

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
            await SendLogAsync(guild, user, settings, detection, deletedCount, punishment, reason, reportAttachments, strike.DetectionCount);
        }
        finally
        {
            foreach (var attachment in reportAttachments)
            {
                await attachment.DisposeAsync();
            }
        }
    }

    public async Task<bool> HandleComponentAsync(SocketMessageComponent component, GuildSettings settings, CancellationToken cancellationToken = default)
    {
        if (!component.Data.CustomId.StartsWith(ReviewPrefix + ":", StringComparison.Ordinal))
        {
            return false;
        }

        if (component.User is not SocketGuildUser actor || !accessControl.CanConfigure(actor, settings))
        {
            await component.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return true;
        }

        var parts = component.Data.CustomId.Split(':');
        if (parts.Length != 3 || !ulong.TryParse(parts[2], out var targetUserId))
        {
            await component.RespondAsync("Invalid moderation action.", ephemeral: true);
            return true;
        }

        var reason = $"Manual anti-spam review action by {actor.Username}";
        var target = actor.Guild.GetUser(targetUserId);
        var currentStrike = await strikeStore.GetAsync(actor.Guild.Id, targetUserId, cancellationToken);
        var detectionCount = Math.Max(1, currentStrike?.DetectionCount ?? 1);
        var result = parts[1] switch
        {
            "next" => await ApplyManualReviewPunishmentAsync(actor.Guild, targetUserId, target, settings, detectionCount, reason, cancellationToken),
            "escalate" => await ApplyManualReviewPunishmentAsync(actor.Guild, targetUserId, target, settings, detectionCount + 1, reason, cancellationToken),
            "ban" => await BanUserAsync(actor.Guild, targetUserId, target, settings, reason, cancellationToken),
            "mute" => await TimeoutUserAsync(target, TimeSpan.FromDays(MaxDiscordTimeoutDays), reason),
            "tempban" => await TempBanUserAsync(actor.Guild, targetUserId, target, settings, GetTempBanDuration(settings, detectionCount), reason, cancellationToken),
            "tempmute" => await TimeoutUserAsync(target, GetTimeoutDuration(settings, detectionCount), reason),
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(result))
        {
            await component.RespondAsync("Could not apply moderation action. The user may no longer be in the server.", ephemeral: true);
            return true;
        }

        await localLogs.WriteAsync(actor.Guild.Id, settings, "manual_review_action", new
        {
            actorId = actor.Id,
            actor = actor.Username,
            targetUserId,
            action = parts[1],
            result
        }, cancellationToken);
        await strikeStore.SetLastPunishmentAsync(actor.Guild.Id, targetUserId, DateTimeOffset.UtcNow, cancellationToken);

        await component.RespondAsync($"Applied action: `{SanitizeForReport(result)}` to user `{targetUserId}`.", ephemeral: true);
        return true;
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
                return await BanUserAsync(guild, user.Id, user, settings, reason, cancellationToken);
            }

            var tempBanDuration = GetTempBanDuration(settings, strike.DetectionCount);
            return await TempBanUserAsync(guild, user.Id, user, settings, tempBanDuration, reason, cancellationToken);
        }

        if (!settings.Punishment.EnableTimeout)
        {
            return localizer.Get(settings, "punishment_none");
        }

        var duration = GetTimeoutDuration(settings, strike.DetectionCount);
        return await TimeoutUserAsync(user, duration, reason);
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

    private async Task<string> BanUserAsync(
        SocketGuild guild,
        ulong userId,
        SocketGuildUser? user,
        GuildSettings settings,
        string reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (user is not null)
        {
            await TryDmUserAsync(user, BuildBanDm(settings, guild, reason, null));
        }

        await guild.AddBanAsync(userId, Math.Clamp(settings.Punishment.BanDeleteMessageDays, 0, 7), reason);
        return localizer.Get(settings, "punishment_permanent_ban");
    }

    private async Task<string> TempBanUserAsync(
        SocketGuild guild,
        ulong userId,
        SocketGuildUser? user,
        GuildSettings settings,
        TimeSpan duration,
        string reason,
        CancellationToken cancellationToken)
    {
        var until = DateTimeOffset.UtcNow.Add(duration);
        var record = new TempBanRecord
        {
            GuildId = guild.Id,
            UserId = userId,
            BannedAt = DateTimeOffset.UtcNow,
            ExpiresAt = until,
            Status = TempBanStatuses.Pending,
            Reason = reason
        };
        await tempBanStore.AddAsync(record, cancellationToken);
        if (user is not null)
        {
            await TryDmUserAsync(user, BuildBanDm(settings, guild, reason, until));
        }

        try
        {
            await guild.AddBanAsync(userId, Math.Clamp(settings.Punishment.BanDeleteMessageDays, 0, 7), reason);
        }
        catch
        {
            await tempBanStore.RemoveAsync(guild.Id, userId, cancellationToken);
            throw;
        }

        record.Status = TempBanStatuses.Active;
        await tempBanStore.UpdateAsync(record, cancellationToken);
        return localizer.Format(settings, "punishment_tempban", new Dictionary<string, string>
        {
            ["duration"] = FormatDuration(duration),
            ["until"] = until.ToString("u")
        });
    }

    private async Task<string> TimeoutUserAsync(SocketGuildUser? user, TimeSpan duration, string reason)
    {
        if (user is null)
        {
            return "";
        }

        var clamped = TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 1, TimeSpan.FromDays(MaxDiscordTimeoutDays).TotalSeconds));
        await user.SetTimeOutAsync(clamped, new RequestOptions
        {
            AuditLogReason = reason
        });

        return $"timeout for {FormatDuration(clamped)}";
    }

    private async Task SendLogAsync(
        SocketGuild guild,
        SocketGuildUser user,
        GuildSettings settings,
        SpamDetectionResult detection,
        int deletedCount,
        string punishment,
        string reason,
        IReadOnlyList<ReportAttachment> reportAttachments)
    {
        await SendLogAsync(
            guild,
            user,
            settings,
            detection,
            deletedCount,
            punishment,
            reason,
            reportAttachments,
            Math.Max(1, (await strikeStore.GetAsync(guild.Id, user.Id))?.DetectionCount ?? 1));
    }

    private async Task SendLogAsync(
        SocketGuild guild,
        SocketGuildUser user,
        GuildSettings settings,
        SpamDetectionResult detection,
        int deletedCount,
        string punishment,
        string reason,
        IReadOnlyList<ReportAttachment> reportAttachments,
        int reviewDetectionCount)
    {
        var logChannelId = settings.Channels.NotificationChannelId ?? settings.Channels.LogChannelId;
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
        var attachments = BuildAttachmentSummary(detection, settings);
        var timestamps = string.Join("\n", detection.Messages.Select(item => $"{item.Timestamp:u} <#{item.ChannelId}>"));
        var ping = BuildAdminPing(settings, user, detection, deletedCount, punishment, reason, channels);

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get(settings, "spam_log_title"))
            .WithColor(Color.Red)
            .AddField(localizer.Get(settings, "log_field_user"), $"`{SanitizeForReport(user.Username)}` (`{user.Id}`)", false)
            .AddField(localizer.Get(settings, "log_field_reason"), SanitizeForReport(reason), false)
            .AddField(localizer.Get(settings, "log_field_channels"), string.IsNullOrWhiteSpace(channels) ? localizer.Get(settings, "unknown") : channels, false)
            .AddField(localizer.Get(settings, "log_field_deleted"), deletedCount.ToString(), true)
            .AddField(localizer.Get(settings, "log_field_punishment"), SanitizeForReport(punishment), true)
            .AddField(localizer.Get(settings, "log_field_timestamps"), TrimForEmbed(timestamps, settings), false)
            .WithFooter(localizer.Format(settings, "spam_log_footer", new Dictionary<string, string>
            {
                ["trigger"] = detection.TriggerType.ToString(),
                ["similarity"] = detection.SimilarityScore.ToString("0.00")
            }))
            .WithCurrentTimestamp()
            ;

        if (settings.AdminReview.IncludeDeletedMessageQuote)
        {
            embed.AddField(localizer.Get(settings, "log_field_original"), TrimForEmbed(SanitizeForReport(sample), settings), false);
        }

        if (!string.IsNullOrWhiteSpace(attachments))
        {
            embed.AddField(localizer.Get(settings, "log_field_attachments"), TrimForEmbed(attachments, settings), false);
        }

        var builtEmbed = embed.Build();
        var components = BuildReviewComponents(settings, user.Id, Math.Max(1, reviewDetectionCount));
        var text = string.IsNullOrWhiteSpace(ping) ? null : ping;
        var allowedMentions = BuildAllowedMentions(settings);
        try
        {
            if (reportAttachments.Count > 0)
            {
                var files = reportAttachments
                    .Select(item => new FileAttachment(item.Stream, item.FileName))
                    .ToArray();
                foreach (var attachment in reportAttachments)
                {
                    attachment.Stream.Position = 0;
                }

                await channel.SendFilesAsync(files, text: text, embed: builtEmbed, allowedMentions: allowedMentions, components: components);
                return;
            }

            await channel.SendMessageAsync(
                text,
                embed: builtEmbed,
                allowedMentions: allowedMentions,
                components: components);
        }
        catch (HttpException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to send moderation log in guild {GuildId} channel {ChannelId}. Check bot View Channel, Send Messages, Embed Links, and Attach Files permissions.",
                guild.Id,
                channel.Id);
        }
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
        string channels)
    {
        var roleMention = settings.Notifications.AdminPingRoleId is { } roleId ? $"<@&{roleId}>" : "";
        var template = settings.Notifications.AdminPingMessage ?? "";
        if (!string.IsNullOrWhiteSpace(roleMention))
        {
            template = template
                .Replace("@here", "{role}", StringComparison.OrdinalIgnoreCase)
                .Replace("@everyone", "{role}", StringComparison.OrdinalIgnoreCase);
            if (!template.Contains("{role}", StringComparison.OrdinalIgnoreCase))
            {
                template = "{role} " + template;
            }
        }
        else
        {
            template = template
                .Replace("{role}", "", StringComparison.OrdinalIgnoreCase)
                .Replace("@here", "", StringComparison.OrdinalIgnoreCase)
                .Replace("@everyone", "", StringComparison.OrdinalIgnoreCase);
        }

        return template
            .Replace("{role}", roleMention, StringComparison.OrdinalIgnoreCase)
            .Replace("{user}", $"`{SanitizeForReport(user.Username)}`", StringComparison.OrdinalIgnoreCase)
            .Replace("{username}", SanitizeForReport(user.Username), StringComparison.OrdinalIgnoreCase)
            .Replace("{userId}", user.Id.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{reason}", SanitizeForReport(reason), StringComparison.OrdinalIgnoreCase)
            .Replace("{channels}", channels, StringComparison.OrdinalIgnoreCase)
            .Replace("{deletedCount}", deletedCount.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{punishment}", SanitizeForReport(punishment), StringComparison.OrdinalIgnoreCase);
    }

    private static AllowedMentions BuildAllowedMentions(GuildSettings settings)
    {
        if (settings.Notifications.AdminPingRoleId is not { } roleId)
        {
            return AllowedMentions.None;
        }

        return new AllowedMentions(null)
        {
            RoleIds = new List<ulong> { roleId }
        };
    }

    private async Task<string> ApplyManualReviewPunishmentAsync(
        SocketGuild guild,
        ulong userId,
        SocketGuildUser? user,
        GuildSettings settings,
        int detectionCount,
        string reason,
        CancellationToken cancellationToken)
    {
        if (settings.Punishment.EnableBan &&
            settings.Punishment.BanAfterDetections > 0 &&
            detectionCount >= settings.Punishment.BanAfterDetections)
        {
            if (settings.Punishment.EnablePermanentBan)
            {
                return await BanUserAsync(guild, userId, user, settings, reason, cancellationToken);
            }

            return await TempBanUserAsync(guild, userId, user, settings, GetTempBanDuration(settings, detectionCount), reason, cancellationToken);
        }

        if (!settings.Punishment.EnableTimeout)
        {
            return localizer.Get(settings, "punishment_none");
        }

        return await TimeoutUserAsync(user, GetTimeoutDuration(settings, detectionCount), reason);
    }

    private static MessageComponent? BuildReviewComponents(GuildSettings settings, ulong userId, int detectionCount)
    {
        if (!settings.AdminReview.Enabled || !settings.AdminReview.ActionButtonsEnabled)
        {
            return null;
        }

        var nextLabel = $"Apply: {BuildReviewActionLabel(settings, detectionCount)}";
        var escalateLabel = $"Escalate: {BuildReviewActionLabel(settings, detectionCount + 1)}";

        return new ComponentBuilder()
            .WithButton(TrimButtonLabel(nextLabel), $"{ReviewPrefix}:next:{userId}", ButtonStyle.Primary, row: 0)
            .WithButton(TrimButtonLabel(escalateLabel), $"{ReviewPrefix}:escalate:{userId}", ButtonStyle.Secondary, row: 0)
            .Build();
    }

    private static string BuildReviewActionLabel(GuildSettings settings, int detectionCount)
    {
        if (settings.Punishment.EnableBan &&
            settings.Punishment.BanAfterDetections > 0 &&
            detectionCount >= settings.Punishment.BanAfterDetections)
        {
            return settings.Punishment.EnablePermanentBan
                ? "ban"
                : $"temp ban {FormatDuration(GetTempBanDuration(settings, detectionCount))}";
        }

        if (!settings.Punishment.EnableTimeout)
        {
            return "log only";
        }

        return $"mute {FormatDuration(GetTimeoutDuration(settings, detectionCount))}";
    }

    private static string TrimButtonLabel(string label) => label.Length <= 80 ? label : label[..80];

    private static bool ShouldAutoBanMultiChannelDuplicate(GuildSettings settings, SpamDetectionResult detection)
    {
        if (!settings.Escalation.AutoBanMultiChannelDuplicate ||
            detection.AffectedChannelIds.Distinct().Count() < Math.Max(2, settings.Escalation.MultiChannelMinimumChannels))
        {
            return false;
        }

        if (detection.TriggerType is not (
            SpamTriggerType.SimilarMultiChannelMessages or
            SpamTriggerType.RepeatedSuspiciousLinks or
            SpamTriggerType.RepeatedAttachments))
        {
            return false;
        }

        var messages = detection.Messages.OrderBy(item => item.Timestamp).ToArray();
        if (messages.Length == 0)
        {
            return false;
        }

        var span = messages[^1].Timestamp - messages[0].Timestamp;
        return span <= TimeSpan.FromSeconds(Math.Max(1, settings.Escalation.MultiChannelWindowSeconds)) &&
            detection.SimilarityScore >= Math.Clamp(settings.Escalation.SimilarityThreshold, 0.5, 1.0);
    }

    private async Task<List<ReportAttachment>> DownloadReportAttachmentsAsync(
        SpamDetectionResult detection,
        GuildSettings settings,
        CancellationToken cancellationToken)
    {
        var maxCount = Math.Clamp(settings.AdminReview.MaxReuploadedImages, 0, 10);
        if (maxCount == 0)
        {
            return [];
        }

        var maxBytes = Math.Clamp(settings.AdminReview.MaxAttachmentBytes, 1, 25 * 1024 * 1024);
        var result = new List<ReportAttachment>();
        foreach (var attachment in detection.Messages
            .SelectMany(item => item.Attachments)
            .Where(item => item.IsImage && item.Size <= maxBytes)
            .GroupBy(item => item.Url)
            .Select(group => group.First())
            .Take(maxCount))
        {
            try
            {
                using var response = await Http.GetAsync(attachment.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode ||
                    response.Content.Headers.ContentLength is long contentLength &&
                    contentLength > maxBytes)
                {
                    continue;
                }

                MemoryStream? stream = new();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[8192];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    if (stream.Length + read > maxBytes)
                    {
                        await stream.DisposeAsync();
                        stream = null;
                        break;
                    }

                    stream.Write(buffer, 0, read);
                }

                if (stream is null)
                {
                    continue;
                }

                stream.Position = 0;
                result.Add(new ReportAttachment(stream, SafeFileName(attachment.Filename)));
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Failed to download spam report attachment {AttachmentUrl}.", attachment.Url);
            }
        }

        return result;
    }

    private static string BuildAttachmentSummary(SpamDetectionResult detection, GuildSettings settings)
    {
        if (!settings.AdminReview.IncludeAttachments)
        {
            return "";
        }

        var attachments = detection.Messages
            .SelectMany(item => item.Attachments)
            .GroupBy(item => item.Url)
            .Select(group => group.First())
            .Take(10)
            .Select(item =>
            {
                var type = string.IsNullOrWhiteSpace(item.ContentType) ? "unknown" : item.ContentType;
                return $"`{SanitizeForReport(item.Filename)}` ({SanitizeForReport(type)}, {item.Size} bytes)";
            });

        return string.Join("\n", attachments);
    }

    private static string SanitizeForReport(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var sanitized = MentionRegex.Replace(value, match => EscapeMention(match.Value));
        return UrlRegex.Replace(sanitized, match => BreakLink(match.Value));
    }

    private static string EscapeMention(string value)
    {
        if (value.StartsWith("<@", StringComparison.Ordinal))
        {
            return value.Replace("@", "\\@", StringComparison.Ordinal);
        }

        if (value.StartsWith("<#", StringComparison.Ordinal))
        {
            return value.Replace("#", "\\#", StringComparison.Ordinal);
        }

        return "\\" + value;
    }

    private static string BreakLink(string value) =>
        value
            .Replace("https://", "https[:]//", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "http[:]//", StringComparison.OrdinalIgnoreCase)
            .Replace(".", "[.]", StringComparison.Ordinal);

    private static string SafeFileName(string value)
    {
        var fileName = Path.GetFileName(string.IsNullOrWhiteSpace(value) ? "attachment" : value);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName.Length <= 80 ? fileName : fileName[^80..];
    }

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

    private sealed class ReportAttachment(MemoryStream stream, string fileName) : IAsyncDisposable
    {
        public MemoryStream Stream { get; } = stream;
        public string FileName { get; } = fileName;

        public ValueTask DisposeAsync() => Stream.DisposeAsync();
    }
}
