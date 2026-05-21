namespace AntiSpamBot.Models;

public sealed class LockdownRecord
{
    public ulong GuildId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset Until { get; set; }
    public bool LockedChannels { get; set; }
    public Dictionary<ulong, LockdownChannelRecord> Channels { get; set; } = [];
}

public sealed class LockdownChannelRecord
{
    public int? PreviousSlowmodeSeconds { get; set; }
    public bool HadEveryoneOverwrite { get; set; }
    public ulong EveryoneAllowValue { get; set; }
    public ulong EveryoneDenyValue { get; set; }
}
