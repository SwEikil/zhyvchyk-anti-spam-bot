using AntiSpamBot.Services;
using Xunit;

namespace AntiSpamBot.Tests;

public sealed class InteractionResponseFlowTests
{
    [Fact]
    public void ModerationComponent_DefersThenModifiesOriginalResponse()
    {
        var plan = InteractionResponseFlow.ModerationComponent;

        Assert.Equal(InteractionResponseOperation.DeferEphemeral, plan.Acknowledge);
        Assert.Equal(InteractionResponseOperation.ModifyOriginalResponse, plan.Complete);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SetupUi_RunsOnlyWhenModerationDidNotHandleComponent(
        bool moderationHandled,
        bool expectedSetupHandling)
    {
        Assert.Equal(
            expectedSetupHandling,
            InteractionResponseFlow.ShouldPassToSetupUi(moderationHandled));
    }

    [Fact]
    public void ErrorAfterAcknowledgement_ModifiesOriginalResponse()
    {
        var now = DateTimeOffset.UtcNow;

        var operation = InteractionResponseFlow.GetErrorResponseOperation(
            hasResponded: true,
            createdAt: now.AddMinutes(-1),
            now);

        Assert.Equal(InteractionResponseOperation.ModifyOriginalResponse, operation);
    }

    [Fact]
    public void ExpiredUnacknowledgedError_DoesNotAttemptInitialResponse()
    {
        var now = DateTimeOffset.UtcNow;

        var operation = InteractionResponseFlow.GetErrorResponseOperation(
            hasResponded: false,
            createdAt: now.AddSeconds(-3),
            now);

        Assert.Equal(InteractionResponseOperation.None, operation);
    }

    [Fact]
    public void FreshUnacknowledgedError_UsesInitialResponse()
    {
        var now = DateTimeOffset.UtcNow;

        var operation = InteractionResponseFlow.GetErrorResponseOperation(
            hasResponded: false,
            createdAt: now.AddSeconds(-1),
            now);

        Assert.Equal(InteractionResponseOperation.RespondEphemeral, operation);
    }
}
