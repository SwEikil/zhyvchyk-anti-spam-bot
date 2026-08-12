using System.Collections.Concurrent;
using AntiSpamBot.Configuration;
using AntiSpamBot.Models;

namespace AntiSpamBot.Services;

public interface IThreatService
{
    ThreatLevel GetCurrentLevel(ulong guildId);
    ThreatLevel GetCurrentLevel(ulong guildId, GuildSettings settings);
    ThreatLevel ObserveDetection(ulong guildId, SpamDetectionResult detection, GuildSettings settings);
    void ObserveRaidSignal(ulong guildId, int score, GuildSettings settings);
    void Reset(ulong guildId);
}

public sealed class ThreatService : IThreatService
{
    private readonly ConcurrentDictionary<ulong, List<DateTimeOffset>> _events = new();

    public ThreatLevel GetCurrentLevel(ulong guildId)
    {
        var fallback = GuildSettings.CreateDefault();
        return GetCurrentLevel(guildId, fallback);
    }

    public ThreatLevel GetCurrentLevel(ulong guildId, GuildSettings settings)
    {
        if (!_events.TryGetValue(guildId, out var events))
        {
            return ThreatLevel.Normal;
        }

        lock (events)
        {
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-Math.Max(1, settings.ThreatLevels.WindowSeconds));
            events.RemoveAll(item => item < cutoff);
            if (events.Count == 0)
            {
                _events.TryRemove(guildId, out _);
                return ThreatLevel.Normal;
            }

            var (suspiciousScore, underAttackScore, criticalScore) = NormalizeThresholds(settings);
            return events.Count switch
            {
                var count when count >= criticalScore => ThreatLevel.Critical,
                var count when count >= underAttackScore => ThreatLevel.UnderAttack,
                var count when count >= suspiciousScore => ThreatLevel.Suspicious,
                _ => ThreatLevel.Normal
            };
        }
    }

    public ThreatLevel ObserveDetection(ulong guildId, SpamDetectionResult detection, GuildSettings settings)
    {
        if (!detection.HasActionableTrigger)
        {
            return GetCurrentLevel(guildId, settings);
        }

        var score = detection.TriggerType switch
        {
            SpamTriggerType.FastMultiChannelPosting => 3,
            SpamTriggerType.ScamLink => 2,
            SpamTriggerType.SuspiciousAttachment => 2,
            _ => 1
        };

        ObserveRaidSignal(guildId, score, settings);
        return GetCurrentLevel(guildId, settings);
    }

    public void ObserveRaidSignal(ulong guildId, int score, GuildSettings settings)
    {
        if (!settings.ThreatLevels.Enabled || score <= 0)
        {
            return;
        }

        var events = _events.GetOrAdd(guildId, _ => []);
        lock (events)
        {
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-Math.Max(1, settings.ThreatLevels.WindowSeconds));
            events.RemoveAll(item => item < cutoff);
            for (var i = 0; i < score; i++)
            {
                events.Add(DateTimeOffset.UtcNow);
            }
        }
    }

    public void Reset(ulong guildId) => _events.TryRemove(guildId, out _);

    private static (int Suspicious, int UnderAttack, int Critical) NormalizeThresholds(GuildSettings settings)
    {
        var suspicious = Math.Max(1, settings.ThreatLevels.SuspiciousScore);
        var underAttack = Math.Max(suspicious + 1, settings.ThreatLevels.UnderAttackScore);
        var critical = Math.Max(underAttack + 1, settings.ThreatLevels.CriticalScore);
        return (suspicious, underAttack, critical);
    }
}
