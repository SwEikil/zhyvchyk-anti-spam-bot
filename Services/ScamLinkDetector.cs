using AntiSpamBot.Configuration;
using AntiSpamBot.Utilities;

namespace AntiSpamBot.Services;

public interface IScamLinkDetector
{
    bool IsSuspicious(string domain, ScamLinkSettings settings, out string reason);
}

public sealed class ScamLinkDetector(IMessageSimilarity similarity) : IScamLinkDetector
{
    public bool IsSuspicious(string domain, ScamLinkSettings settings, out string reason)
    {
        reason = "";
        if (!settings.Enabled || string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        domain = DomainNormalizer.NormalizeHost(domain);
        var asciiDomain = DomainNormalizer.ToAsciiHost(domain);
        var skeletonDomain = DomainNormalizer.ToSkeleton(domain, settings.ConfusableDetectionEnabled, settings.LeetspeakDetectionEnabled);

        foreach (var blacklisted in settings.BlacklistedDomains.Select(NormalizeDomain))
        {
            if (DomainMatches(domain, asciiDomain, skeletonDomain, blacklisted, settings))
            {
                reason = $"blacklisted domain {domain}";
                return true;
            }
        }

        foreach (var protectedDomain in settings.ProtectedDomains.Select(NormalizeDomain))
        {
            var protectedAscii = DomainNormalizer.ToAsciiHost(protectedDomain);
            var protectedSkeleton = DomainNormalizer.ToSkeleton(protectedDomain, settings.ConfusableDetectionEnabled, settings.LeetspeakDetectionEnabled);
            if (domain.Equals(protectedDomain, StringComparison.OrdinalIgnoreCase) ||
                domain.EndsWith("." + protectedDomain, StringComparison.OrdinalIgnoreCase) ||
                asciiDomain.Equals(protectedAscii, StringComparison.OrdinalIgnoreCase) ||
                asciiDomain.EndsWith("." + protectedAscii, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var score = Math.Max(
                similarity.Compare(domain, protectedDomain),
                similarity.Compare(skeletonDomain, protectedSkeleton));
            if (score >= settings.FuzzyDomainThreshold ||
                IsLikelyProtectedDomainTypo(skeletonDomain, protectedSkeleton))
            {
                reason = $"domain {domain} is similar to {protectedDomain}";
                return true;
            }
        }

        return false;
    }

    private static string NormalizeDomain(string value) =>
        DomainNormalizer.NormalizeHost(value);

    private static bool DomainMatches(string domain, string asciiDomain, string skeletonDomain, string target, ScamLinkSettings settings)
    {
        var asciiTarget = DomainNormalizer.ToAsciiHost(target);
        var skeletonTarget = DomainNormalizer.ToSkeleton(target, settings.ConfusableDetectionEnabled, settings.LeetspeakDetectionEnabled);
        return domain.Equals(target, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith("." + target, StringComparison.OrdinalIgnoreCase) ||
            asciiDomain.Equals(asciiTarget, StringComparison.OrdinalIgnoreCase) ||
            asciiDomain.EndsWith("." + asciiTarget, StringComparison.OrdinalIgnoreCase) ||
            skeletonDomain.Equals(skeletonTarget, StringComparison.OrdinalIgnoreCase) ||
            skeletonDomain.EndsWith("." + skeletonTarget, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyProtectedDomainTypo(string domain, string protectedDomain)
    {
        var left = FirstLabel(domain);
        var right = FirstLabel(protectedDomain);
        if (left.Length < 6 || right.Length < 6 ||
            left[0] != right[0] ||
            left[^1] != right[^1])
        {
            return false;
        }

        var distance = Distance(left, right);
        return distance <= 2;
    }

    private static string FirstLabel(string domain)
    {
        var index = domain.IndexOf('.');
        return index < 0 ? domain : domain[..index];
    }

    private static int Distance(string left, string right)
    {
        if (left.Length > right.Length)
        {
            (left, right) = (right, left);
        }

        var previous = new int[left.Length + 1];
        var current = new int[left.Length + 1];

        for (var i = 0; i <= left.Length; i++)
        {
            previous[i] = i;
        }

        for (var j = 1; j <= right.Length; j++)
        {
            current[0] = j;
            for (var i = 1; i <= left.Length; i++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[i] = Math.Min(Math.Min(current[i - 1] + 1, previous[i] + 1), previous[i - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[left.Length];
    }
}
