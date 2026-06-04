using AntiSpamBot.Utilities;
using Xunit;

namespace AntiSpamBot.Tests;

public sealed class DomainNormalizerTests
{
    [Fact]
    public void ExtractHost_NormalizesSchemeHostAndPath()
    {
        var host = DomainNormalizer.ExtractHost("https://Discord.COM/some/path");

        Assert.Equal("discord.com", host);
    }

    [Fact]
    public void ExtractHost_WorksWhenSchemeIsMissing()
    {
        var host = DomainNormalizer.ExtractHost("discord.gg/invite");

        Assert.Equal("discord.gg", host);
    }

    [Fact]
    public void ToSkeleton_MapsLeetspeakWhenEnabled()
    {
        var skeleton = DomainNormalizer.ToSkeleton(
            "disc0rd.com",
            mapConfusables: false,
            mapLeetspeak: true);

        Assert.Equal("discord.com", skeleton);
    }

    [Fact]
    public void ToSkeleton_MapsCyrillicConfusableWhenEnabled()
    {
        var skeleton = DomainNormalizer.ToSkeleton(
            "discоrd.com",
            mapConfusables: true,
            mapLeetspeak: false);

        Assert.Equal("discord.com", skeleton);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceInput_DoesNotThrow(string value)
    {
        var extractException = Record.Exception(() => DomainNormalizer.ExtractHost(value));
        var skeletonException = Record.Exception(() => DomainNormalizer.ToSkeleton(
            value,
            mapConfusables: true,
            mapLeetspeak: true));

        Assert.Null(extractException);
        Assert.Null(skeletonException);
    }
}
