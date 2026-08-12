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
    IModerationIncidentStore incidentStore,
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
        if (!detection.HasActionableTrigger)
        {
            logger.LogWarning(
                "Ignored non-actionable anti-spam result for user {UserId} in guild {GuildId}: {TriggerType}.",
                user.Id,
                guild.Id,
                detection.TriggerType);
            return;
        }

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
                detection.StrictMonitoringApplied,
                detection.UserRiskScore,
                detection.UserRiskDetails,
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
                (existingStrike?.DetectionCount ?? 0) + 1,
                incidentId: null);
            return;
        }

        var incident = CreateIncident(guild.Id, user.Id, detection, existingStrike);
        await incidentStore.CreateAsync(incident, cancellationToken);

        var reportAttachments = settings.AdminReview.Enabled && settings.AdminReview.IncludeAttachments
            ? await DownloadReportAttachmentsAsync(detection, settings, cancellationToken)
            : new List<ReportAttachment>();
        try
        {
            var deletedMessageIds = await DeleteDetectedMessagesAsync(guild, detection, cancellationToken);
            var deletedCount = deletedMessageIds.Count;
            incident.DeletedMessageIds = deletedMessageIds;
            await incidentStore.UpdateAsync(incident, cancellationToken);

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
                    existingStrike?.DetectionCount ?? 1,
                    incident.IncidentId);
                return;
            }

            var strike = await strikeStore.RegisterDetectionAsync(guild.Id, user.Id, incident.IncidentId, cancellationToken);
            incident.StrikeRegistered = true;
            incident.ReviewDetectionCount = strike.DetectionCount;
            await incidentStore.UpdateAsync(incident, cancellationToken);
            var actionReason = AppendIncidentMarker(reason, incident);
            if (ShouldAutoBanMultiChannelDuplicate(settings, detection) &&
                settings.Punishment.BanAfterDetections > 0 &&
                strike.DetectionCount >= settings.Punishment.BanAfterDetections)
            {
                var appliedAt = DateTimeOffset.UtcNow;
                var autoBanPunishment = await BanUserAsync(guild, user.Id, user, settings, actionReason, cancellationToken);
                incident.PunishmentType = ModerationPunishmentTypes.PermanentBan;
                incident.PunishmentAppliedAt = appliedAt;
                await strikeStore.SetLastPunishmentAsync(
                    guild.Id,
                    user.Id,
                    DateTimeOffset.UtcNow,
                    cancellationToken,
                    incident.IncidentId);
                await incidentStore.UpdateAsync(incident, cancellationToken);
                await localLogs.WriteAsync(guild.Id, settings, "moderation_action", new
                {
                    incidentId = incident.IncidentId,
                    userId = user.Id,
                    user = user.Username,
                    detection.TriggerType,
                    detection.Reason,
                    detection.StrictMonitoringApplied,
                    detection.UserRiskScore,
                    detection.UserRiskDetails,
                    deletedCount,
                    punishment = autoBanPunishment,
                    escalation = "auto_ban_multi_channel_duplicate",
                    detectionCount = strike.DetectionCount,
                    channels = detection.AffectedChannelIds
                }, cancellationToken);
                await SendLogAsync(guild, user, settings, detection, deletedCount, autoBanPunishment, reason, reportAttachments, strike.DetectionCount, incident.IncidentId);
                return;
            }

            var forceStrictTimeout = ShouldApplyStrictMonitoringTimeout(settings, detection);
            var punishment = await ApplyPunishmentAsync(guild, user, settings, strike, actionReason, forceStrictTimeout, incident.IncidentId, cancellationToken);
            incident.PunishmentType = punishment.Type;
            incident.PunishmentAppliedAt = punishment.AppliedAt;
            incident.PunishmentExpiresAt = punishment.ExpiresAt;

            await strikeStore.SetLastPunishmentAsync(
                guild.Id,
                user.Id,
                DateTimeOffset.UtcNow,
                cancellationToken,
                incident.IncidentId);
            await incidentStore.UpdateAsync(incident, cancellationToken);
            await localLogs.WriteAsync(guild.Id, settings, "moderation_action", new
            {
                incidentId = incident.IncidentId,
                userId = user.Id,
                user = user.Username,
                detection.TriggerType,
                detection.Reason,
                detection.StrictMonitoringApplied,
                detection.UserRiskScore,
                detection.UserRiskDetails,
                deletedCount,
                punishment = punishment.Description,
                channels = detection.AffectedChannelIds
            }, cancellationToken);
            await SendLogAsync(guild, user, settings, detection, deletedCount, punishment.Description, reason, reportAttachments, strike.DetectionCount, incident.IncidentId);
        }
        finally
        {
            foreach (var attachment in reportAttachments)
            {
                await attachment.DisposeAsync();
            }
        }
    }

    private static ModerationIncident CreateIncident(
        ulong guildId,
        ulong userId,
        SpamDetectionResult detection,
        UserStrikeState? existingStrike)
    {
        var incidentId = Guid.NewGuid().ToString("N");
        return new ModerationIncident
        {
            IncidentId = incidentId,
            GuildId = guildId,
            TargetUserId = userId,
            TriggerType = detection.TriggerType,
            DetectedAt = DateTimeOffset.UtcNow,
            Messages = detection.Messages
                .GroupBy(item => item.MessageId)
                .Select(group => group.First())
                .Select(item => new ModerationIncidentMessage
                {
                    ChannelId = item.ChannelId,
                    MessageId = item.MessageId,
                    RawContent = item.RawContent,
                    Attachments = item.Attachments.ToList()
                })
                .ToList(),
            ReviewDetectionCount = (existingStrike?.DetectionCount ?? 0) + 1,
            PunishmentReasonMarker = $"anti-spam incident:{incidentId}"
        };
    }

    private static string AppendIncidentMarker(string reason, ModerationIncident incident)
    {
        var suffix = $" [{incident.PunishmentReasonMarker}]";
        var maxReasonLength = Math.Max(0, 512 - suffix.Length);
        var prefix = reason.Length <= maxReasonLength ? reason : reason[..maxReasonLength];
        return prefix + suffix;
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
        if (parts.Length != 3)
        {
            await component.RespondAsync(localizer.Get(settings, "moderation_action_invalid"), ephemeral: true);
            return true;
        }

        var responsePlan = InteractionResponseFlow.ModerationComponent;
        await InteractionResponseFlow.ExecuteAsync(component, responsePlan.Acknowledge);

        if (parts[1].Equals("falsepositive", StringComparison.Ordinal))
        {
            await HandleFalsePositiveAsync(component, actor, settings, parts[2], responsePlan, cancellationToken);
            return true;
        }

        var incident = await incidentStore.GetAsync(actor.Guild.Id, parts[2], cancellationToken);
        var hasLegacyTarget = ulong.TryParse(parts[2], out var legacyTargetUserId);
        var targetUserId = incident?.TargetUserId ?? legacyTargetUserId;
        if (incident is null && !hasLegacyTarget)
        {
            await InteractionResponseFlow.ExecuteAsync(
                component,
                responsePlan.Complete,
                localizer.Get(settings, "moderation_incident_not_found"));
            return true;
        }

        if (incident is not null &&
            !incident.Status.Equals(ModerationIncidentStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            await InteractionResponseFlow.ExecuteAsync(
                component,
                responsePlan.Complete,
                localizer.Get(settings, "false_positive_already_handled"));
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
            await InteractionResponseFlow.ExecuteAsync(
                component,
                responsePlan.Complete,
                localizer.Get(settings, "moderation_action_failed"));
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

        await InteractionResponseFlow.ExecuteAsync(
            component,
            responsePlan.Complete,
            localizer.Format(settings, "moderation_action_applied", new Dictionary<string, string>
            {
                ["result"] = SanitizeForReport(result),
                ["userId"] = targetUserId.ToString()
            }));
        return true;
    }

    private async Task HandleFalsePositiveAsync(
        SocketMessageComponent component,
        SocketGuildUser actor,
        GuildSettings settings,
        string incidentId,
        DeferredInteractionResponsePlan responsePlan,
        CancellationToken cancellationToken)
    {
        var claim = await incidentStore.TryClaimFalsePositiveAsync(
            actor.Guild.Id,
            incidentId,
            actor.Id,
            cancellationToken);
        if (claim.Result == IncidentClaimResult.NotFound || claim.Incident is null)
        {
            await InteractionResponseFlow.ExecuteAsync(
                component,
                responsePlan.Complete,
                localizer.Get(settings, "moderation_incident_not_found"));
            return;
        }

        if (claim.Result == IncidentClaimResult.AlreadyHandled)
        {
            await InteractionResponseFlow.ExecuteAsync(
                component,
                responsePlan.Complete,
                localizer.Get(settings, "false_positive_already_handled"));
            return;
        }

        var incident = claim.Incident;
        if (incident.StrikeRegistered)
        {
            if (await strikeStore.ReverseDetectionAsync(
                    incident.GuildId,
                    incident.TargetUserId,
                    incident.IncidentId,
                    cancellationToken))
            {
                incident.RevertedParts.Add("strike");
            }
            else
            {
                incident.UnrevertedParts.Add("strike_not_active");
            }
        }

        await TryUndoPunishmentAsync(actor.Guild, incident, cancellationToken);
        await RestoreDeletedMessagesAsync(actor.Guild, incident, settings, cancellationToken);

        incident.Status = ModerationIncidentStatuses.FalsePositive;
        await incidentStore.UpdateAsync(incident, cancellationToken);

        var restored = incident.RestoredMessageIds.Count;
        var revertedText = incident.RevertedParts.Count == 0
            ? localizer.Get(settings, "false_positive_none")
            : string.Join(", ", incident.RevertedParts.Select(part => LocalizeRecoveryPart(settings, part)));
        var failedText = incident.UnrevertedParts.Count == 0
            ? localizer.Get(settings, "false_positive_none")
            : string.Join(", ", incident.UnrevertedParts.Select(part => LocalizeRecoveryPart(settings, part)));
        var response = localizer.Format(settings, "false_positive_result", new Dictionary<string, string>
        {
            ["incidentId"] = incident.IncidentId,
            ["reverted"] = revertedText,
            ["restored"] = restored.ToString(),
            ["failed"] = failedText
        });

        await localLogs.WriteAsync(actor.Guild.Id, settings, "false_positive_recovery", new
        {
            incidentId = incident.IncidentId,
            originalTrigger = incident.TriggerType,
            userId = incident.TargetUserId,
            moderatorId = actor.Id,
            moderator = actor.Username,
            reverted = incident.RevertedParts,
            messageRestored = restored > 0,
            restoredCount = restored,
            couldNotRevert = incident.UnrevertedParts,
            logText = localizer.Format(settings, "false_positive_log_text", new Dictionary<string, string>
            {
                ["incidentId"] = incident.IncidentId,
                ["userId"] = incident.TargetUserId.ToString(),
                ["moderatorId"] = actor.Id.ToString()
            }),
            summary = response
        }, cancellationToken);

        await InteractionResponseFlow.ExecuteAsync(component, responsePlan.Complete, response);
    }

    public Task RemoveTimeoutAsync(SocketGuildUser user, string reason, CancellationToken cancellationToken = default) =>
        user.RemoveTimeOutAsync(new RequestOptions
        {
            AuditLogReason = string.IsNullOrWhiteSpace(reason) ? "Manual anti-spam unmute" : reason
        });

    private async Task TryUndoPunishmentAsync(
        SocketGuild guild,
        ModerationIncident incident,
        CancellationToken cancellationToken)
    {
        if (incident.PunishmentType.Equals(ModerationPunishmentTypes.None, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (incident.PunishmentType.Equals(ModerationPunishmentTypes.Timeout, StringComparison.OrdinalIgnoreCase))
        {
            var target = guild.GetUser(incident.TargetUserId);
            if (target is null)
            {
                incident.UnrevertedParts.Add("timeout_user_missing");
                return;
            }

            var currentTimeout = target.TimedOutUntil;
            if (currentTimeout is null || currentTimeout <= DateTimeOffset.UtcNow)
            {
                incident.RevertedParts.Add("timeout_inactive");
                return;
            }

            if (!CanSafelyRemoveTimeout(currentTimeout, incident.PunishmentExpiresAt))
            {
                incident.UnrevertedParts.Add("timeout_changed");
                return;
            }

            try
            {
                await target.RemoveTimeOutAsync(new RequestOptions
                {
                    AuditLogReason = $"False-positive recovery for anti-spam incident {incident.IncidentId}"
                });
                incident.RevertedParts.Add("timeout");
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to remove timeout for incident {IncidentId}.", incident.IncidentId);
                incident.UnrevertedParts.Add("timeout_failed");
            }

            return;
        }

        if (incident.PunishmentType is not (ModerationPunishmentTypes.TempBan or ModerationPunishmentTypes.PermanentBan))
        {
            incident.UnrevertedParts.Add("punishment_unknown");
            return;
        }

        try
        {
            var currentBan = await guild.GetBanAsync(incident.TargetUserId);
            if (currentBan is null)
            {
                if (incident.PunishmentType.Equals(ModerationPunishmentTypes.TempBan, StringComparison.OrdinalIgnoreCase))
                {
                    await tempBanStore.RemoveIfIncidentAsync(
                        incident.GuildId,
                        incident.TargetUserId,
                        incident.IncidentId,
                        cancellationToken);
                }

                incident.RevertedParts.Add("ban_inactive");
                return;
            }

            if (string.IsNullOrWhiteSpace(incident.PunishmentReasonMarker) ||
                currentBan.Reason?.Contains(incident.PunishmentReasonMarker, StringComparison.Ordinal) != true)
            {
                incident.UnrevertedParts.Add("ban_changed");
                return;
            }

            if (incident.PunishmentType.Equals(ModerationPunishmentTypes.TempBan, StringComparison.OrdinalIgnoreCase))
            {
                var tempBan = await tempBanStore.GetAsync(incident.GuildId, incident.TargetUserId, cancellationToken);
                if (tempBan is null || !tempBan.IncidentId.Equals(incident.IncidentId, StringComparison.Ordinal))
                {
                    incident.UnrevertedParts.Add("ban_changed");
                    return;
                }
            }

            await guild.RemoveBanAsync(incident.TargetUserId, new RequestOptions
            {
                AuditLogReason = $"False-positive recovery for anti-spam incident {incident.IncidentId}"
            });
            if (incident.PunishmentType.Equals(ModerationPunishmentTypes.TempBan, StringComparison.OrdinalIgnoreCase))
            {
                await tempBanStore.RemoveIfIncidentAsync(
                    incident.GuildId,
                    incident.TargetUserId,
                    incident.IncidentId,
                    cancellationToken);
            }

            incident.RevertedParts.Add("ban");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to remove ban for incident {IncidentId}.", incident.IncidentId);
            incident.UnrevertedParts.Add("ban_failed");
        }
    }

    internal static bool CanSafelyRemoveTimeout(DateTimeOffset? currentTimeout, DateTimeOffset? incidentTimeout) =>
        currentTimeout is not null &&
        incidentTimeout is not null &&
        currentTimeout.Value.Equals(incidentTimeout.Value);

    private async Task RestoreDeletedMessagesAsync(
        SocketGuild guild,
        ModerationIncident incident,
        GuildSettings settings,
        CancellationToken cancellationToken)
    {
        foreach (var message in incident.Messages.Where(item => incident.DeletedMessageIds.Contains(item.MessageId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (incident.RestoredMessageIds.ContainsKey(message.MessageId))
            {
                continue;
            }

            var channel = guild.GetTextChannel(message.ChannelId);
            if (channel is null)
            {
                incident.UnrevertedParts.Add($"message_channel_missing:{message.ChannelId}");
                continue;
            }

            var text = BuildRestoredMessageText(settings, incident.TargetUserId, message.RawContent);
            var attachments = settings.AdminReview.IncludeAttachments
                ? await DownloadReportAttachmentsAsync(message.Attachments, settings, cancellationToken)
                : [];
            try
            {
                IUserMessage restored;
                var allowedMentions = BuildRestoredAllowedMentions(incident.TargetUserId);
                if (attachments.Count > 0)
                {
                    var files = attachments.Select(item => new FileAttachment(item.Stream, item.FileName)).ToArray();
                    restored = await channel.SendFilesAsync(
                        files,
                        text: text,
                        allowedMentions: allowedMentions,
                        flags: MessageFlags.SuppressEmbeds);
                }
                else
                {
                    restored = await channel.SendMessageAsync(
                        text,
                        allowedMentions: allowedMentions,
                        flags: MessageFlags.SuppressEmbeds);
                }

                incident.RestoredMessageIds[message.MessageId] = restored.Id;
                incident.RevertedParts.Add($"message:{message.MessageId}");
                await incidentStore.UpdateAsync(incident, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to restore message {MessageId} for incident {IncidentId}.",
                    message.MessageId,
                    incident.IncidentId);
                incident.UnrevertedParts.Add($"message_failed:{message.MessageId}");
            }
            finally
            {
                foreach (var attachment in attachments)
                {
                    await attachment.DisposeAsync();
                }
            }
        }
    }

    internal string BuildRestoredMessageText(GuildSettings settings, ulong userId, string rawContent)
    {
        var content = string.IsNullOrWhiteSpace(rawContent) ? localizer.Get(settings, "empty") : rawContent;
        var prefix = localizer.Format(settings, "false_positive_restored_message", new Dictionary<string, string>
        {
            ["user"] = $"<@{userId}>",
            ["message"] = ""
        }).TrimEnd();
        var available = Math.Max(0, 2000 - prefix.Length - 1);
        if (content.Length > available)
        {
            content = available >= 3 ? content[..(available - 3)] + "..." : "";
        }

        return localizer.Format(settings, "false_positive_restored_message", new Dictionary<string, string>
        {
            ["user"] = $"<@{userId}>",
            ["message"] = content
        });
    }

    internal static AllowedMentions BuildRestoredAllowedMentions(ulong userId) =>
        new(null)
        {
            UserIds = new List<ulong> { userId }
        };

    private string LocalizeRecoveryPart(GuildSettings settings, string part)
    {
        var key = part.Split(':', 2)[0];
        return localizer.Get(settings, $"false_positive_part_{key}");
    }

    private async Task<HashSet<ulong>> DeleteDetectedMessagesAsync(SocketGuild guild, SpamDetectionResult detection, CancellationToken cancellationToken)
    {
        var deletedMessageIds = new HashSet<ulong>();
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
                deletedMessageIds.Add(tracked.MessageId);
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

        return deletedMessageIds;
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

    private async Task<PunishmentOutcome> ApplyPunishmentAsync(
        SocketGuild guild,
        SocketGuildUser user,
        GuildSettings settings,
        UserStrikeState strike,
        string reason,
        bool forceTimeout,
        string incidentId,
        CancellationToken cancellationToken)
    {
        if (settings.Punishment.EnableBan &&
            settings.Punishment.BanAfterDetections > 0 &&
            strike.DetectionCount >= settings.Punishment.BanAfterDetections)
        {
            if (settings.Punishment.EnablePermanentBan)
            {
                var banAppliedAt = DateTimeOffset.UtcNow;
                var banDescription = await BanUserAsync(guild, user.Id, user, settings, reason, cancellationToken);
                return new PunishmentOutcome(ModerationPunishmentTypes.PermanentBan, banDescription, banAppliedAt, null);
            }

            var tempBanDuration = GetTempBanDuration(settings, strike.DetectionCount);
            var tempBanAppliedAt = DateTimeOffset.UtcNow;
            var tempBanDescription = await TempBanUserAsync(
                guild,
                user.Id,
                user,
                settings,
                tempBanDuration,
                reason,
                cancellationToken,
                incidentId);
            return new PunishmentOutcome(
                ModerationPunishmentTypes.TempBan,
                tempBanDescription,
                tempBanAppliedAt,
                tempBanAppliedAt.Add(tempBanDuration));
        }

        if (!settings.Punishment.EnableTimeout && !forceTimeout)
        {
            return new PunishmentOutcome(
                ModerationPunishmentTypes.None,
                localizer.Get(settings, "punishment_none"),
                null,
                null);
        }

        var duration = GetTimeoutDuration(settings, strike.DetectionCount);
        var appliedAt = DateTimeOffset.UtcNow;
        var normalizedDuration = NormalizeTimeoutDuration(duration);
        var description = await TimeoutUserAsync(user, normalizedDuration, reason);
        return new PunishmentOutcome(
            ModerationPunishmentTypes.Timeout,
            description,
            appliedAt,
            user.TimedOutUntil ?? DateTimeOffset.UtcNow.Add(normalizedDuration));
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
        CancellationToken cancellationToken,
        string incidentId = "")
    {
        var until = DateTimeOffset.UtcNow.Add(duration);
        var record = new TempBanRecord
        {
            GuildId = guild.Id,
            UserId = userId,
            BannedAt = DateTimeOffset.UtcNow,
            ExpiresAt = until,
            Status = TempBanStatuses.Pending,
            Reason = reason,
            IncidentId = incidentId
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

        var clamped = NormalizeTimeoutDuration(duration);
        await user.SetTimeOutAsync(clamped, new RequestOptions
        {
            AuditLogReason = reason
        });

        return $"timeout for {FormatDuration(clamped)}";
    }

    private static TimeSpan NormalizeTimeoutDuration(TimeSpan duration) =>
        TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 1, TimeSpan.FromDays(MaxDiscordTimeoutDays).TotalSeconds));

    private async Task SendLogAsync(
        SocketGuild guild,
        SocketGuildUser user,
        GuildSettings settings,
        SpamDetectionResult detection,
        int deletedCount,
        string punishment,
        string reason,
        IReadOnlyList<ReportAttachment> reportAttachments,
        int reviewDetectionCount,
        string? incidentId)
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
        var components = BuildReviewComponents(settings, user.Id, incidentId, Math.Max(1, reviewDetectionCount));
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
        var localizedReason = localizer.Format(settings, detection.ReasonKey, values);
        if (!detection.StrictMonitoringApplied)
        {
            return localizedReason;
        }

        localizedReason = localizer.Format(settings, "reason_strict_monitoring", new Dictionary<string, string>
        {
            ["reason"] = localizedReason,
            ["score"] = detection.UserRiskScore.ToString(),
            ["details"] = string.IsNullOrWhiteSpace(detection.UserRiskDetails) ? localizer.Get(settings, "unknown") : detection.UserRiskDetails
        });

        return settings.UserRisk.StrictMonitoringTimeoutOnConfirmedSpam
            ? localizer.Format(settings, "reason_strict_timeout", new Dictionary<string, string> { ["reason"] = localizedReason })
            : localizedReason;
    }

    public static bool ShouldApplyStrictMonitoringTimeout(GuildSettings settings, SpamDetectionResult detection) =>
        detection.HasActionableTrigger &&
        detection.StrictMonitoringApplied &&
        settings.UserRisk.StrictMonitoringEnabled &&
        settings.UserRisk.StrictMonitoringTimeoutOnConfirmedSpam &&
        !settings.DryRun.Enabled;

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

    private MessageComponent? BuildReviewComponents(GuildSettings settings, ulong userId, string? incidentId, int detectionCount)
    {
        if (!settings.AdminReview.Enabled || !settings.AdminReview.ActionButtonsEnabled)
        {
            return null;
        }

        var nextLabel = $"Apply: {BuildReviewActionLabel(settings, detectionCount)}";
        var escalateLabel = $"Escalate: {BuildReviewActionLabel(settings, detectionCount + 1)}";

        var target = string.IsNullOrWhiteSpace(incidentId) ? userId.ToString() : incidentId;
        var builder = new ComponentBuilder()
            .WithButton(TrimButtonLabel(nextLabel), $"{ReviewPrefix}:next:{target}", ButtonStyle.Primary, row: 0)
            .WithButton(TrimButtonLabel(escalateLabel), $"{ReviewPrefix}:escalate:{target}", ButtonStyle.Secondary, row: 0);
        if (!string.IsNullOrWhiteSpace(incidentId))
        {
            builder.WithButton(
                TrimButtonLabel(localizer.Get(settings, "false_positive_button")),
                $"{ReviewPrefix}:falsepositive:{incidentId}",
                ButtonStyle.Success,
                row: 1);
        }

        return builder.Build();
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
        CancellationToken cancellationToken) =>
        await DownloadReportAttachmentsAsync(
            detection.Messages.SelectMany(item => item.Attachments),
            settings,
            cancellationToken);

    private async Task<List<ReportAttachment>> DownloadReportAttachmentsAsync(
        IEnumerable<TrackedAttachment> sourceAttachments,
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
        foreach (var attachment in sourceAttachments
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

    private sealed record PunishmentOutcome(
        string Type,
        string Description,
        DateTimeOffset? AppliedAt,
        DateTimeOffset? ExpiresAt);
}
