using AntiSpamBot.Configuration;
using Discord.WebSocket;

namespace AntiSpamBot.Services;

public interface IUserRiskService
{
    int Score(SocketGuildUser user, bool messageHasLinks, UserRiskSettings settings, out string reason);
    UserRiskEvaluation Evaluate(SocketGuildUser user, bool messageHasLinks, UserRiskSettings settings);
}

public sealed class UserRiskService(IClock clock) : IUserRiskService
{
    public int Score(SocketGuildUser user, bool messageHasLinks, UserRiskSettings settings, out string reason)
    {
        var evaluation = Evaluate(user, messageHasLinks, settings);
        reason = evaluation.Reason;
        return evaluation.Score;
    }

    public UserRiskEvaluation Evaluate(SocketGuildUser user, bool messageHasLinks, UserRiskSettings settings) =>
        Evaluate(user.CreatedAt, user.JoinedAt, messageHasLinks, settings, clock.UtcNow);

    public static UserRiskEvaluation Evaluate(
        DateTimeOffset createdAt,
        DateTimeOffset? joinedAt,
        bool messageHasLinks,
        UserRiskSettings settings,
        DateTimeOffset now)
    {
        if (!settings.Enabled)
        {
            return UserRiskEvaluation.None;
        }

        var score = 0;
        var reasons = new List<string>();
        var isFreshAccount = IsWithinAnyPositiveWindow(
            now - createdAt,
            TimeSpan.FromDays(Math.Max(0, settings.NewAccountAgeDays)),
            TimeSpan.FromHours(Math.Max(0, settings.NewAccountAgeHours)));
        var isRecentJoin = joinedAt is not null && IsWithinAnyPositiveWindow(
            now - joinedAt.Value,
            TimeSpan.FromDays(Math.Max(0, settings.RecentJoinDays)),
            TimeSpan.FromMinutes(Math.Max(0, settings.RecentJoinMinutes)));

        if (isFreshAccount)
        {
            score += settings.NewAccountScore;
            reasons.Add("fresh account");
        }

        if (isRecentJoin)
        {
            score += settings.RecentJoinScore;
            reasons.Add("recent join");
        }

        if (messageHasLinks && isRecentJoin)
        {
            score += settings.FirstMessageLinkScore;
            reasons.Add("link soon after joining");
        }

        var strictMonitoringApplies = settings.StrictMonitoringEnabled && (isFreshAccount || isRecentJoin);
        var strictMonitoringScoreBonus = strictMonitoringApplies && isFreshAccount && isRecentJoin
            ? Math.Max(0, settings.StrictMonitoringScoreBonus)
            : 0;
        if (strictMonitoringScoreBonus > 0)
        {
            score += strictMonitoringScoreBonus;
            reasons.Add("strict monitoring user-risk rule");
        }

        return new UserRiskEvaluation(
            score,
            string.Join(", ", reasons),
            isFreshAccount,
            isRecentJoin,
            strictMonitoringApplies,
            strictMonitoringScoreBonus);
    }

    private static bool IsWithinAnyPositiveWindow(TimeSpan age, params TimeSpan[] windows) =>
        age >= TimeSpan.Zero && windows.Any(window => window > TimeSpan.Zero && age < window);
}

public sealed record UserRiskEvaluation(
    int Score,
    string Reason,
    bool IsFreshAccount,
    bool IsRecentJoin,
    bool StrictMonitoringApplies,
    int StrictMonitoringScoreBonus)
{
    public static UserRiskEvaluation None { get; } = new(0, "", false, false, false, 0);
}
