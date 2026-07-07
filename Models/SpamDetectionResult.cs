namespace AntiSpamBot.Models;

public sealed class SpamDetectionResult
{
    public bool IsSpam { get; init; }
    public SpamTriggerType TriggerType { get; init; }
    public string Reason { get; init; } = "";
    public string ReasonKey { get; init; } = "";
    public IReadOnlyDictionary<string, string> ReasonValues { get; init; } = new Dictionary<string, string>();
    public double SimilarityScore { get; init; }
    public IReadOnlyList<TrackedMessage> Messages { get; init; } = [];
    public IReadOnlyList<ulong> AffectedChannelIds { get; init; } = [];
    public bool StrictMonitoringApplied { get; init; }
    public int UserRiskScore { get; init; }
    public string UserRiskDetails { get; init; } = "";

    public static SpamDetectionResult Clean { get; } = new();
}

public enum SpamTriggerType
{
    None,
    SimilarMultiChannelMessages,
    RepeatedSuspiciousLinks,
    RepeatedAttachments,
    MassMentions,
    FastMultiChannelPosting,
    MessageCountBurst,
    ScamLink,
    UserRisk,
    SuspiciousAttachment
}
