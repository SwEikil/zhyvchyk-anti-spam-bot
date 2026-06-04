using AntiSpamBot.Configuration;
using AntiSpamBot.Services;
using AntiSpamBot.Utilities;
using Xunit;

namespace AntiSpamBot.Tests;

public sealed class ScamLinkDetectorTests
{
    private readonly ScamLinkDetector _detector = new(new LevenshteinMessageSimilarity());

    [Fact]
    public void DisabledDetection_ReturnsFalseForBlacklistedDomain()
    {
        var settings = Settings();
        settings.Enabled = false;
        settings.BlacklistedDomains.Add("bad.example");

        var result = _detector.IsSuspicious("bad.example", settings, out var reason);

        Assert.False(result);
        Assert.Equal("", reason);
    }

    [Fact]
    public void ExactBlacklistedDomain_IsDetected()
    {
        var settings = Settings();
        settings.BlacklistedDomains.Add("bad.example");

        var result = _detector.IsSuspicious("bad.example", settings, out var reason);

        Assert.True(result);
        Assert.Contains("blacklisted domain", reason);
    }

    [Fact]
    public void BlacklistedSubdomain_IsDetected()
    {
        var settings = Settings();
        settings.BlacklistedDomains.Add("bad.example");

        var result = _detector.IsSuspicious("cdn.bad.example", settings, out var reason);

        Assert.True(result);
        Assert.Contains("blacklisted domain", reason);
    }

    [Fact]
    public void RealProtectedDomain_IsNotFlagged()
    {
        var result = _detector.IsSuspicious("discord.com", Settings(), out var reason);

        Assert.False(result);
        Assert.Equal("", reason);
    }

    [Fact]
    public void LeetspeakProtectedDomainLookalike_IsFlagged()
    {
        var result = _detector.IsSuspicious("disc0rd.com", Settings(), out var reason);

        Assert.True(result);
        Assert.Contains("similar to discord.com", reason);
    }

    [Fact]
    public void CyrillicConfusableProtectedDomainLookalike_IsFlagged()
    {
        var result = _detector.IsSuspicious("discоrd.com", Settings(), out var reason);

        Assert.True(result);
        Assert.Contains("similar to discord.com", reason);
    }

    [Fact]
    public void UnrelatedDomain_IsNotFlagged()
    {
        var result = _detector.IsSuspicious("example.com", Settings(), out var reason);

        Assert.False(result);
        Assert.Equal("", reason);
    }

    private static ScamLinkSettings Settings() => new()
    {
        Enabled = true,
        ProtectedDomains = ["discord.com"],
        FuzzyDomainThreshold = 0.88,
        ConfusableDetectionEnabled = true,
        LeetspeakDetectionEnabled = true
    };
}
