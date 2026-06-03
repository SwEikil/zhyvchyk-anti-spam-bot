namespace AntiSpamBot.Models;

public sealed record TrackedMessage(
    ulong GuildId,
    ulong UserId,
    ulong ChannelId,
    ulong MessageId,
    string RawContent,
    string NormalizedContent,
    IReadOnlyList<string> Urls,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Mentions,
    IReadOnlyList<string> AttachmentKeys,
    IReadOnlyList<string> AttachmentExtensions,
    IReadOnlyList<TrackedAttachment> Attachments,
    bool MentionsEveryone,
    DateTimeOffset Timestamp);

public sealed record TrackedAttachment(
    string Url,
    string Filename,
    string? ContentType,
    long Size,
    bool IsImage);
