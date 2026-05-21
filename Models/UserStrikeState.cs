namespace AntiSpamBot.Models;

public sealed class GuildStrikeState
{
    public Dictionary<ulong, UserStrikeState> Users { get; set; } = [];
}

public sealed class UserStrikeState
{
    public int DetectionCount { get; set; }
    public DateTimeOffset LastDetectionAt { get; set; }
    public DateTimeOffset LastPunishmentAt { get; set; }
}
