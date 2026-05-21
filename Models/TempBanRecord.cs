namespace AntiSpamBot.Models;

public sealed class TempBanRecord
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public DateTimeOffset BannedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = TempBanStatuses.Active;
    public string Reason { get; set; } = "";
}

public static class TempBanStatuses
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Failed = "failed";
}
