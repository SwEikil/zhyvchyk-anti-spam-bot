using Discord;
using Discord.WebSocket;

namespace AntiSpamBot.Services;

internal enum InteractionResponseOperation
{
    None,
    DeferEphemeral,
    RespondEphemeral,
    ModifyOriginalResponse
}

internal readonly record struct DeferredInteractionResponsePlan(
    InteractionResponseOperation Acknowledge,
    InteractionResponseOperation Complete);

internal static class InteractionResponseFlow
{
    private static readonly TimeSpan InitialResponseWindow = TimeSpan.FromSeconds(3);

    internal static DeferredInteractionResponsePlan ModerationComponent { get; } = new(
        InteractionResponseOperation.DeferEphemeral,
        InteractionResponseOperation.ModifyOriginalResponse);

    internal static bool ShouldPassToSetupUi(bool moderationHandled) => !moderationHandled;

    internal static InteractionResponseOperation GetErrorResponseOperation(
        bool hasResponded,
        DateTimeOffset createdAt,
        DateTimeOffset now)
    {
        if (hasResponded)
        {
            return InteractionResponseOperation.ModifyOriginalResponse;
        }

        return now - createdAt < InitialResponseWindow
            ? InteractionResponseOperation.RespondEphemeral
            : InteractionResponseOperation.None;
    }

    internal static Task ExecuteAsync(
        SocketInteraction interaction,
        InteractionResponseOperation operation,
        string? content = null) => operation switch
        {
            InteractionResponseOperation.DeferEphemeral => interaction.DeferAsync(ephemeral: true),
            InteractionResponseOperation.RespondEphemeral => interaction.RespondAsync(content, ephemeral: true),
            InteractionResponseOperation.ModifyOriginalResponse => interaction.ModifyOriginalResponseAsync(
                properties => properties.Content = content),
            InteractionResponseOperation.None => Task.CompletedTask,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
}
