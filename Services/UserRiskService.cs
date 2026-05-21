using AntiSpamBot.Configuration;
using Discord.WebSocket;

namespace AntiSpamBot.Services;

public interface IUserRiskService
{
    int Score(SocketGuildUser user, bool messageHasLinks, UserRiskSettings settings, out string reason);
}

public sealed class UserRiskService : IUserRiskService
{
    public int Score(SocketGuildUser user, bool messageHasLinks, UserRiskSettings settings, out string reason)
    {
        reason = "";
        if (!settings.Enabled)
        {
            return 0;
        }

        var score = 0;
        var reasons = new List<string>();
        var now = DateTimeOffset.UtcNow;

        if (now - user.CreatedAt < TimeSpan.FromHours(settings.NewAccountAgeHours))
        {
            score += settings.NewAccountScore;
            reasons.Add("new account");
        }

        if (user.JoinedAt is not null && now - user.JoinedAt.Value < TimeSpan.FromMinutes(settings.RecentJoinMinutes))
        {
            score += settings.RecentJoinScore;
            reasons.Add("recent join");
        }

        if (messageHasLinks && user.JoinedAt is not null && now - user.JoinedAt.Value < TimeSpan.FromMinutes(settings.RecentJoinMinutes))
        {
            score += settings.FirstMessageLinkScore;
            reasons.Add("link soon after joining");
        }

        reason = string.Join(", ", reasons);
        return score;
    }
}
