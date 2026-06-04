using AntiSpamBot.Utilities;
using Xunit;

namespace AntiSpamBot.Tests;

public sealed class LevenshteinMessageSimilarityTests
{
    private readonly LevenshteinMessageSimilarity _similarity = new();

    [Fact]
    public void EqualStrings_ReturnOne()
    {
        var score = _similarity.Compare("free nitro now", "free nitro now");

        Assert.Equal(1.0, score);
    }

    [Theory]
    [InlineData("", "free nitro now")]
    [InlineData("free nitro now", "")]
    [InlineData("   ", "free nitro now")]
    [InlineData("free nitro now", "   ")]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void EmptyOrWhitespaceInput_ReturnsZero(string left, string right)
    {
        var score = _similarity.Compare(left, right);

        Assert.Equal(0.0, score);
    }

    [Fact]
    public void SimilarMessages_ScoreHigherThanUnrelatedMessages()
    {
        var similar = _similarity.Compare(
            "free nitro claim now",
            "free nitro claim today");
        var unrelated = _similarity.Compare(
            "free nitro claim now",
            "server rules are updated");

        Assert.True(similar > unrelated);
    }

    [Fact]
    public void TokenOverlap_IsDetectedWhenWordOrderChanges()
    {
        var score = _similarity.Compare(
            "free nitro claim now",
            "claim now free nitro");

        Assert.Equal(1.0, score);
    }
}
