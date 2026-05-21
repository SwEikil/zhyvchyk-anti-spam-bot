using System.Collections.Concurrent;
using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using AntiSpamBot.Services;
using AntiSpamBot.Utilities;
using Discord.WebSocket;

namespace AntiSpamBot.AntiSpam;

public interface IAntiSpamService
{
    SpamDetectionResult Inspect(SocketUserMessage message, ulong guildId, GuildSettings settings);
    void Cleanup(TimeSpan maxAge);
}

public sealed class AntiSpamService(
    IMessageNormalizer normalizer,
    IMessageSimilarity similarity,
    IScamLinkDetector scamLinkDetector,
    IUserRiskService userRiskService,
    IThreatService threatService) : IAntiSpamService
{
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, UserMessageWindow>> _guildWindows = new();

    public SpamDetectionResult Inspect(SocketUserMessage message, ulong guildId, GuildSettings settings)
    {
        if (!settings.Detection.Enabled)
        {
            return SpamDetectionResult.Clean;
        }

        var tracked = normalizer.Normalize(message, guildId, settings.Detection);
        var guild = _guildWindows.GetOrAdd(guildId, _ => new ConcurrentDictionary<ulong, UserMessageWindow>());
        var userWindow = guild.GetOrAdd(message.Author.Id, _ => new UserMessageWindow());

        // Each user gets a short rolling window. Detection is intentionally local to the sender,
        // which keeps memory bounded and avoids comparing unrelated users on large servers.
        lock (userWindow.SyncRoot)
        {
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-settings.Detection.TimeWindowSeconds);
            userWindow.Messages.RemoveAll(item => item.Timestamp < cutoff);
            userWindow.Messages.Add(tracked);

            var recent = userWindow.Messages
                .Where(item => item.Timestamp >= cutoff)
                .OrderBy(item => item.Timestamp)
                .ToArray();

            return Detect(recent, tracked, message.Author as SocketGuildUser, settings);
        }
    }

    public void Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(maxAge);

        foreach (var guild in _guildWindows)
        {
            foreach (var user in guild.Value)
            {
                lock (user.Value.SyncRoot)
                {
                    user.Value.Messages.RemoveAll(item => item.Timestamp < cutoff);
                }

                if (user.Value.Messages.Count == 0)
                {
                    guild.Value.TryRemove(user.Key, out _);
                }
            }

            if (guild.Value.IsEmpty)
            {
                _guildWindows.TryRemove(guild.Key, out _);
            }
        }
    }

    private SpamDetectionResult Detect(IReadOnlyList<TrackedMessage> recent, TrackedMessage current, SocketGuildUser? user, GuildSettings settings)
    {
        var detection = settings.Detection;
        var threatLevel = threatService.GetCurrentLevel(current.GuildId, settings);
        var minimumSpamCount = threatLevel >= ThreatLevel.UnderAttack
            ? Math.Max(2, detection.MinimumSpamCount - 1)
            : detection.MinimumSpamCount;
        var maxMessagesBeforePunishment = threatLevel >= ThreatLevel.UnderAttack
            ? Math.Max(2, detection.MaxMessagesBeforePunishment - 1)
            : detection.MaxMessagesBeforePunishment;
        var similarityThreshold = threatLevel >= ThreatLevel.UnderAttack
            ? Math.Max(0.72, detection.SimilarityThreshold - 0.05)
            : detection.SimilarityThreshold;
        var channels = recent.Select(item => item.ChannelId).Distinct().ToArray();
        var hasEnoughChannels = !detection.RequireMultipleChannels || channels.Length > 1;

        foreach (var domain in current.Domains)
        {
            if (scamLinkDetector.IsSuspicious(domain, settings.ScamLinks, out var reason))
            {
                return CreateResult(SpamTriggerType.ScamLink, reason, 1.0, recent.Where(item => item.Domains.Contains(domain)));
            }
        }

        var suspiciousExtensions = current.AttachmentExtensions
            .Where(extension => settings.Attachments.SuspiciousExtensionFilteringEnabled &&
                settings.Attachments.SuspiciousExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (suspiciousExtensions.Length > 0)
        {
            return CreateResult(
                SpamTriggerType.SuspiciousAttachment,
                $"suspicious attachment extension {string.Join(", ", suspiciousExtensions)}",
                1.0,
                [current],
                "reason_suspicious_attachment",
                new Dictionary<string, string> { ["extensions"] = string.Join(", ", suspiciousExtensions) });
        }

        if (user is not null)
        {
            var riskScore = userRiskService.Score(user, current.Urls.Count > 0, settings.UserRisk, out var riskReason);
            if (riskScore >= settings.UserRisk.PunishAtScore)
            {
                return CreateResult(
                    SpamTriggerType.UserRisk,
                    $"user risk score {riskScore}: {riskReason}",
                    1.0,
                    [current],
                    "reason_user_risk",
                    new Dictionary<string, string> { ["score"] = riskScore.ToString(), ["details"] = riskReason });
            }
        }

        // Burst checks run before content similarity so coordinated cross-channel floods are stopped
        // even when the attacker slightly edits every message.
        if (recent.Count >= maxMessagesBeforePunishment && channels.Length > 1)
        {
            return CreateResult(
                SpamTriggerType.FastMultiChannelPosting,
                "very fast multi-channel posting",
                1.0,
                recent,
                "reason_fast_multichannel");
        }

        var similar = recent
            .Select(item => new
            {
                Message = item,
                Score = similarity.Compare(current.NormalizedContent, item.NormalizedContent)
            })
            .Where(item => item.Score >= similarityThreshold)
            .ToArray();

        if (similar.Length >= minimumSpamCount)
        {
            return CreateResult(
                SpamTriggerType.SimilarMultiChannelMessages,
                hasEnoughChannels
                    ? "similar messages posted in multiple channels"
                    : "repeated similar messages",
                similar.Average(item => item.Score),
                similar.Select(item => item.Message),
                hasEnoughChannels ? "reason_similar_multichannel" : "reason_repeated_similar");
        }

        var repeatedUrlMessages = recent
            .Where(item => item.Urls.Count > 0)
            .GroupBy(item => string.Join("|", item.Urls.Order(StringComparer.Ordinal)))
            .Where(group => group.Key.Length > 0)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();

        if (repeatedUrlMessages is not null &&
            repeatedUrlMessages.Count() >= detection.RepeatedLinkThreshold &&
            hasEnoughChannels)
        {
            return CreateResult(
                SpamTriggerType.RepeatedSuspiciousLinks,
                "repeated suspicious links",
                1.0,
                repeatedUrlMessages,
                "reason_repeated_links");
        }

        var repeatedAttachments = recent
            .Where(item => item.AttachmentKeys.Count > 0)
            .GroupBy(item => string.Join("|", item.AttachmentKeys.Order(StringComparer.Ordinal)))
            .Where(group => group.Key.Length > 0)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();

        if (repeatedAttachments is not null &&
            repeatedAttachments.Count() >= detection.RepeatedAttachmentThreshold &&
            hasEnoughChannels)
        {
            return CreateResult(
                SpamTriggerType.RepeatedAttachments,
                "repeated attachments or images",
                1.0,
                repeatedAttachments,
                "reason_repeated_attachments");
        }

        var massMentions = recent.Where(item => item.MentionsEveryone).ToArray();
        if (massMentions.Length >= detection.MassMentionThreshold &&
            recent.Count >= minimumSpamCount &&
            hasEnoughChannels)
        {
            return CreateResult(
                SpamTriggerType.MassMentions,
                "mass mentions detected",
                1.0,
                massMentions,
                "reason_mass_mentions");
        }

        if (recent.Count >= maxMessagesBeforePunishment)
        {
            return CreateResult(
                SpamTriggerType.MessageCountBurst,
                "message burst exceeded configured limit",
                1.0,
                recent,
                "reason_message_burst");
        }

        return SpamDetectionResult.Clean;
    }

    private static SpamDetectionResult CreateResult(
        SpamTriggerType triggerType,
        string reason,
        double similarityScore,
        IEnumerable<TrackedMessage> messages,
        string reasonKey = "",
        IReadOnlyDictionary<string, string>? reasonValues = null)
    {
        var messageList = messages
            .GroupBy(item => item.MessageId)
            .Select(group => group.First())
            .OrderBy(item => item.Timestamp)
            .ToArray();

        return new SpamDetectionResult
        {
            IsSpam = true,
            TriggerType = triggerType,
            Reason = reason,
            ReasonKey = string.IsNullOrWhiteSpace(reasonKey) ? TriggerReasonKey(triggerType) : reasonKey,
            ReasonValues = reasonValues ?? new Dictionary<string, string>(),
            SimilarityScore = similarityScore,
            Messages = messageList,
            AffectedChannelIds = messageList.Select(item => item.ChannelId).Distinct().ToArray()
        };
    }

    private static string TriggerReasonKey(SpamTriggerType triggerType) => triggerType switch
    {
        SpamTriggerType.ScamLink => "reason_scam_link",
        SpamTriggerType.RepeatedSuspiciousLinks => "reason_repeated_links",
        SpamTriggerType.RepeatedAttachments => "reason_repeated_attachments",
        SpamTriggerType.MassMentions => "reason_mass_mentions",
        SpamTriggerType.FastMultiChannelPosting => "reason_fast_multichannel",
        SpamTriggerType.MessageCountBurst => "reason_message_burst",
        SpamTriggerType.UserRisk => "reason_user_risk",
        SpamTriggerType.SuspiciousAttachment => "reason_suspicious_attachment",
        _ => "reason_repeated_similar"
    };

    private sealed class UserMessageWindow
    {
        public object SyncRoot { get; } = new();
        public List<TrackedMessage> Messages { get; } = [];
    }
}
