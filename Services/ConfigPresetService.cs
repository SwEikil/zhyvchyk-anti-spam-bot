using AntiSpamBot.Configuration;

namespace AntiSpamBot.Services;

public interface IConfigPresetService
{
    IReadOnlyList<string> Names { get; }
    bool Apply(string name, GuildSettings settings);
}

public sealed class ConfigPresetService : IConfigPresetService
{
    public IReadOnlyList<string> Names { get; } = ["small-server", "community", "strict", "paranoid"];

    public bool Apply(string name, GuildSettings settings)
    {
        switch (name)
        {
            case "small-server":
                settings.Detection.MinimumSpamCount = 3;
                settings.Detection.MaxMessagesBeforePunishment = 5;
                settings.Detection.SimilarityThreshold = 0.86;
                settings.Punishment.TimeoutDurationsSeconds = [600, 3600, 86400];
                settings.RaidLockdown.EnableSlowmode = true;
                settings.RaidLockdown.SlowmodeSeconds = 5;
                return true;
            case "community":
                settings.Detection.MinimumSpamCount = 3;
                settings.Detection.MaxMessagesBeforePunishment = 4;
                settings.Detection.SimilarityThreshold = 0.84;
                settings.Punishment.TimeoutDurationsSeconds = [1800, 7200, 86400];
                settings.RaidLockdown.SlowmodeSeconds = 10;
                return true;
            case "strict":
                settings.Detection.MinimumSpamCount = 2;
                settings.Detection.MaxMessagesBeforePunishment = 4;
                settings.Detection.SimilarityThreshold = 0.80;
                settings.UserRisk.PunishAtScore = 60;
                settings.Punishment.TimeoutDurationsSeconds = [3600, 21600, 86400];
                settings.RaidLockdown.SlowmodeSeconds = 20;
                return true;
            case "paranoid":
                settings.Detection.MinimumSpamCount = 2;
                settings.Detection.MaxMessagesBeforePunishment = 3;
                settings.Detection.SimilarityThreshold = 0.76;
                settings.UserRisk.PunishAtScore = 50;
                settings.Punishment.TimeoutDurationsSeconds = [7200, 86400, 604800];
                settings.RaidLockdown.SlowmodeSeconds = 30;
                settings.RaidLockdown.EnableTemporaryChannelLock = true;
                return true;
            default:
                return false;
        }
    }
}
