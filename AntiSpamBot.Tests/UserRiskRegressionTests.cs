using AntiSpamBot.AntiSpam;
using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using AntiSpamBot.Moderation;
using AntiSpamBot.Services;
using AntiSpamBot.Utilities;
using Xunit;

namespace AntiSpamBot.Tests;

public sealed class UserRiskRegressionTests
{
    private static readonly DateTimeOffset Now = new(2034, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserRiskEvaluation FreshRecentRisk = new(
        Score: 94,
        Reason: "fresh account, recent join, strict monitoring user-risk rule",
        IsFreshAccount: true,
        IsRecentJoin: true,
        StrictMonitoringApplies: true,
        StrictMonitoringScoreBonus: 4);

    [Fact]
    public void FreshAccountAndRecentJoin_OnePlainMessage_IsClean()
    {
        var service = CreateService();
        var settings = GuildSettings.CreateDefault();
        var message = Message(1, "Anyone want to play?", "anyone want to play");

        var result = service.InspectTracked([message], message, settings, FreshRecentRisk);

        Assert.False(result.IsSpam);
        Assert.False(result.HasActionableTrigger);
        Assert.Equal(SpamTriggerType.None, result.TriggerType);
    }

    [Fact]
    public void FreshAccountAndRecentJoin_TwoUnrelatedNormalMessages_AreClean()
    {
        var service = CreateService();
        var settings = GuildSettings.CreateDefault();
        var first = Message(1, "Anyone want to play?", "anyone want to play");
        var second = Message(2, "Good morning everyone", "good morning everyone");

        var result = service.InspectTracked([first, second], second, settings, FreshRecentRisk);

        Assert.False(result.IsSpam);
    }

    [Fact]
    public void FreshAccountAndRecentJoin_ConfirmedRepeatedSpam_IsDetected()
    {
        var service = CreateService();
        var settings = GuildSettings.CreateDefault();
        settings.Detection.RequireMultipleChannels = false;
        var first = Message(1, "buy now", "buy now");
        var second = Message(2, "buy now", "buy now");

        var result = service.InspectTracked([first, second], second, settings, FreshRecentRisk);

        Assert.True(result.IsSpam);
        Assert.True(result.HasActionableTrigger);
        Assert.Equal(SpamTriggerType.SimilarMultiChannelMessages, result.TriggerType);
        Assert.True(result.StrictMonitoringApplied);
        Assert.Equal(FreshRecentRisk.Score, result.UserRiskScore);
    }

    [Fact]
    public void FreshAccount_ScamLink_IsDetectedImmediately()
    {
        var service = CreateService();
        var settings = GuildSettings.CreateDefault();
        settings.ScamLinks.BlacklistedDomains.Add("malicious.example");
        var message = Message(
            1,
            "https://malicious.example/login",
            "URL",
            urls: ["https://malicious.example/login"],
            domains: ["malicious.example"]);

        var result = service.InspectTracked([message], message, settings, FreshRecentRisk);

        Assert.True(result.HasActionableTrigger);
        Assert.Equal(SpamTriggerType.ScamLink, result.TriggerType);
    }

    [Fact]
    public void UserRiskTrigger_IsNeverActionable()
    {
        var result = new SpamDetectionResult
        {
            IsSpam = true,
            TriggerType = SpamTriggerType.UserRisk,
            UserRiskScore = 500
        };

        Assert.False(result.HasActionableTrigger);
    }

    [Fact]
    public void UserRiskTrigger_DoesNotRaiseThreatOrQualifyForTimeout()
    {
        var settings = GuildSettings.CreateDefault();
        var detection = new SpamDetectionResult
        {
            IsSpam = true,
            TriggerType = SpamTriggerType.UserRisk,
            StrictMonitoringApplied = true,
            UserRiskScore = 500
        };
        var threat = new ThreatService();

        Assert.Equal(ThreatLevel.Normal, threat.ObserveDetection(10, detection, settings));
        Assert.False(ModerationService.ShouldApplyStrictMonitoringTimeout(settings, detection));
    }

    [Fact]
    public void StrictMonitoring_KeepsSafeMessageBurstFloor()
    {
        var settings = GuildSettings.CreateDefault();
        settings.UserRisk.StrictMonitoringMinimumSpamCount = 2;

        var thresholds = AntiSpamService.CalculateEffectiveThresholds(
            settings.Detection,
            settings.UserRisk,
            ThreatLevel.Normal,
            FreshRecentRisk);

        Assert.Equal(2, thresholds.MinimumSpamCount);
        Assert.True(thresholds.MaxMessagesBeforePunishment >= 4);
    }

    private static AntiSpamService CreateService()
    {
        var similarity = new LevenshteinMessageSimilarity();
        var clock = new FixedClock(Now);
        return new AntiSpamService(
            new MessageNormalizer(),
            similarity,
            new ScamLinkDetector(similarity),
            new UserRiskService(clock),
            new ThreatService(),
            clock);
    }

    private static TrackedMessage Message(
        ulong id,
        string raw,
        string normalized,
        IReadOnlyList<string>? urls = null,
        IReadOnlyList<string>? domains = null) =>
        new(
            GuildId: 10,
            UserId: 20,
            ChannelId: id % 2 + 100,
            MessageId: id,
            RawContent: raw,
            NormalizedContent: normalized,
            Urls: urls ?? [],
            Domains: domains ?? [],
            Mentions: [],
            AttachmentKeys: [],
            AttachmentExtensions: [],
            Attachments: [],
            MentionsEveryone: false,
            Timestamp: Now.AddSeconds(id));

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
