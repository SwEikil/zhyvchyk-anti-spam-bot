namespace AntiSpamBot.Models;

public sealed class GuildStrikeState
{
    public Dictionary<ulong, UserStrikeState> Users { get; set; } = [];
}

public sealed class UserStrikeState
{
    public int DetectionCount { get; set; }
    public HashSet<string> DetectionIds { get; set; } = [];
    public Dictionary<string, DateTimeOffset> DetectionTimes { get; set; } = [];
    public DateTimeOffset LegacyLastDetectionAt { get; set; }
    public Dictionary<string, DateTimeOffset> PunishmentTimes { get; set; } = [];
    public DateTimeOffset LegacyLastPunishmentAt { get; set; }
    public DateTimeOffset LastDetectionAt { get; set; }
    public DateTimeOffset LastPunishmentAt { get; set; }
}
