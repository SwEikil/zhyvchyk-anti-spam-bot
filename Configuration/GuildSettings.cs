namespace AntiSpamBot.Configuration;

public sealed class GuildSettings
{
    public string Language { get; set; } = "en";
    public bool SetupCompleted { get; set; }
    public AccessSettings Access { get; set; } = new();
    public ChannelSettings Channels { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public DetectionSettings Detection { get; set; } = new();
    public PunishmentSettings Punishment { get; set; } = new();
    public FalsePositiveProtectionSettings FalsePositiveProtection { get; set; } = new();
    public RaidLockdownSettings RaidLockdown { get; set; } = new();
    public ScamLinkSettings ScamLinks { get; set; } = new();
    public UserRiskSettings UserRisk { get; set; } = new();
    public AttachmentSecuritySettings Attachments { get; set; } = new();
    public ThreatLevelSettings ThreatLevels { get; set; } = new();
    public DryRunSettings DryRun { get; set; } = new();
    public LocalLoggingSettings LocalLogging { get; set; } = new();
    public BackupSettings Backups { get; set; } = new();
    public AntiNukeSettings AntiNuke { get; set; } = new();

    public static GuildSettings CreateDefault() => new();
}

public sealed class AccessSettings
{
    public bool OwnerOnlyBootstrap { get; set; } = true;
    public bool AllowDiscordAdministratorsAfterSetup { get; set; } = true;
    public CommandVisibilityMode CommandVisibility { get; set; } = CommandVisibilityMode.AdministratorOnly;
    public HashSet<ulong> ManagerRoleIds { get; set; } = [];
}

public sealed class ChannelSettings
{
    public MonitoredChannelMode Mode { get; set; } = MonitoredChannelMode.AllExceptIgnored;
    public HashSet<ulong> SelectedChannelIds { get; set; } = [];
    public HashSet<ulong> IgnoredChannelIds { get; set; } = [];
    public ulong? LogChannelId { get; set; }
    public ulong? NotificationChannelId { get; set; }
}

public sealed class NotificationSettings
{
    public string AdminPingMessage { get; set; } =
        "@here Anti-spam action: {reason}. User: {user} ({userId}). Channels: {channels}. Deleted: {deletedCount}. Punishment: {punishment}.";
}

public sealed class DetectionSettings
{
    public bool Enabled { get; set; } = true;
    public int TimeWindowSeconds { get; set; } = 10;
    public int CleanupIntervalSeconds { get; set; } = 30;
    public int MinimumSpamCount { get; set; } = 3;
    public int MaxMessagesBeforePunishment { get; set; } = 5;
    public double SimilarityThreshold { get; set; } = 0.86;
    public bool RequireMultipleChannels { get; set; } = true;
    public int MassMentionThreshold { get; set; } = 1;
    public int RepeatedLinkThreshold { get; set; } = 2;
    public int RepeatedAttachmentThreshold { get; set; } = 2;
    public bool StripPunctuation { get; set; } = true;
    public bool StripEmoji { get; set; } = true;
    public bool StripInvisibleCharacters { get; set; } = true;
}

public sealed class PunishmentSettings
{
    public bool EnableTimeout { get; set; } = true;
    public bool EnableBan { get; set; }
    public bool EnablePermanentBan { get; set; }
    public List<double> TimeoutDurationsSeconds { get; set; } = [];
    public List<int> TimeoutDurationsMinutes { get; set; } = [10, 60, 1440];
    public List<double> TempBanDurationsSeconds { get; set; } = [3600, 86400, 604800];
    public int BanAfterDetections { get; set; } = 4;
    public int BanDeleteMessageDays { get; set; }
    public bool DmUserBeforeBan { get; set; } = true;
    public string BanDmTemplate { get; set; } =
        "You were temporarily banned from {guild} until {until}. Reason: {reason}";
    public int CooldownSeconds { get; set; } = 60;
    public string ReasonTemplate { get; set; } = "Coordinated spam detected: {reason}";
}

public sealed class FalsePositiveProtectionSettings
{
    public bool IgnoreAdministrators { get; set; } = true;
    public HashSet<ulong> IgnoredRoleIds { get; set; } = [];
    public HashSet<ulong> IgnoredUserIds { get; set; } = [];
    public HashSet<ulong> WhitelistedUserIds { get; set; } = [];
}

public sealed class RaidLockdownSettings
{
    public bool Enabled { get; set; } = true;
    public bool EnableSlowmode { get; set; } = true;
    public int SlowmodeSeconds { get; set; } = 10;
    public bool EnableTemporaryChannelLock { get; set; }
    public int DurationSeconds { get; set; } = 300;
    public int MinimumHoldSeconds { get; set; } = 60;
    public bool AutoReleaseWhenThreatDrops { get; set; } = true;
    public ThreatLevel ReleaseWhenAtOrBelow { get; set; } = ThreatLevel.Suspicious;
    public int MonitorIntervalSeconds { get; set; } = 10;
    public ThreatLevel MinimumThreatLevel { get; set; } = ThreatLevel.UnderAttack;
}

public sealed class ScamLinkSettings
{
    public bool Enabled { get; set; } = true;
    public HashSet<string> BlacklistedDomains { get; set; } = [];
    public HashSet<string> ProtectedDomains { get; set; } = ["discord.com", "discord.gg", "steamcommunity.com"];
    public double FuzzyDomainThreshold { get; set; } = 0.88;
    public bool ConfusableDetectionEnabled { get; set; } = true;
    public bool LeetspeakDetectionEnabled { get; set; } = true;
}

public sealed class UserRiskSettings
{
    public bool Enabled { get; set; } = true;
    public int NewAccountAgeHours { get; set; } = 24;
    public int RecentJoinMinutes { get; set; } = 10;
    public int PunishAtScore { get; set; } = 70;
    public int NewAccountScore { get; set; } = 35;
    public int RecentJoinScore { get; set; } = 30;
    public int FirstMessageLinkScore { get; set; } = 25;
}

public sealed class AttachmentSecuritySettings
{
    public bool SuspiciousExtensionFilteringEnabled { get; set; } = true;
    public bool HashingEnabled { get; set; }
    public HashSet<string> SuspiciousExtensions { get; set; } = [".exe", ".scr", ".bat", ".cmd", ".ps1", ".js", ".vbs", ".jar", ".apk", ".msi", ".dll"];
}

public sealed class ThreatLevelSettings
{
    public bool Enabled { get; set; } = true;
    public int WindowSeconds { get; set; } = 60;
    public int SuspiciousScore { get; set; } = 3;
    public int UnderAttackScore { get; set; } = 6;
    public int CriticalScore { get; set; } = 12;
}

public sealed class DryRunSettings
{
    public bool Enabled { get; set; }
}

public sealed class LocalLoggingSettings
{
    public bool Enabled { get; set; } = true;
    public string Directory { get; set; } = "logs";
}

public sealed class BackupSettings
{
    public bool AutomaticBackupsEnabled { get; set; } = true;
    public string Directory { get; set; } = "backups";
    public int MaxBackupsPerGuild { get; set; } = 20;
}

public sealed class AntiNukeSettings
{
    public bool Enabled { get; set; } = true;
    public int WindowSeconds { get; set; } = 60;
    public int MaxDestructiveActions { get; set; } = 4;
    public bool TriggerLockdown { get; set; } = true;
    public bool IgnoreBotManagers { get; set; } = true;
    public HashSet<ulong> TrustedUserIds { get; set; } = [];
    public HashSet<ulong> TrustedRoleIds { get; set; } = [];
}

public enum MonitoredChannelMode
{
    AllTextChannels,
    SelectedOnly,
    AllExceptIgnored
}

public enum ThreatLevel
{
    Normal,
    Suspicious,
    UnderAttack,
    Critical
}

public enum CommandVisibilityMode
{
    AdministratorOnly,
    VisibleWithRuntimeChecks
}
