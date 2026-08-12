namespace AntiSpamBot.Models;

public sealed class ModerationIncident
{
    public string IncidentId { get; set; } = "";
    public ulong GuildId { get; set; }
    public ulong TargetUserId { get; set; }
    public SpamTriggerType TriggerType { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public List<ModerationIncidentMessage> Messages { get; set; } = [];
    public HashSet<ulong> DeletedMessageIds { get; set; } = [];
    public Dictionary<ulong, ulong> RestoredMessageIds { get; set; } = [];
    public bool StrikeRegistered { get; set; }
    public string PunishmentType { get; set; } = ModerationPunishmentTypes.None;
    public DateTimeOffset? PunishmentAppliedAt { get; set; }
    public DateTimeOffset? PunishmentExpiresAt { get; set; }
    public string PunishmentReasonMarker { get; set; } = "";
    public int ReviewDetectionCount { get; set; }
    public string Status { get; set; } = ModerationIncidentStatuses.Active;
    public ulong? FalsePositiveModeratorId { get; set; }
    public DateTimeOffset? FalsePositiveAt { get; set; }
    public List<string> RevertedParts { get; set; } = [];
    public List<string> UnrevertedParts { get; set; } = [];
}

public sealed class ModerationIncidentMessage
{
    public ulong ChannelId { get; set; }
    public ulong MessageId { get; set; }
    public string RawContent { get; set; } = "";
    public List<TrackedAttachment> Attachments { get; set; } = [];
}

public static class ModerationIncidentStatuses
{
    public const string Active = "active";
    public const string Undoing = "undoing";
    public const string FalsePositive = "false_positive";
}

public static class ModerationPunishmentTypes
{
    public const string None = "none";
    public const string Timeout = "timeout";
    public const string TempBan = "tempban";
    public const string PermanentBan = "permanent_ban";
}

public enum IncidentClaimResult
{
    Claimed,
    NotFound,
    AlreadyHandled
}
