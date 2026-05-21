using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using Discord.WebSocket;

namespace AntiSpamBot.Utilities;

public interface IMessageNormalizer
{
    TrackedMessage Normalize(SocketUserMessage message, ulong guildId, DetectionSettings settings);
}

public sealed partial class MessageNormalizer : IMessageNormalizer
{
    public TrackedMessage Normalize(SocketUserMessage message, ulong guildId, DetectionSettings settings)
    {
        var content = message.Content ?? "";
        var urls = DomainCandidateRegex().Matches(content)
            .Select(match => match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}').ToLowerInvariant())
            .Distinct()
            .ToArray();
        var domains = urls.Select(DomainNormalizer.ExtractHost).Where(domain => domain.Length > 0).Distinct().ToArray();
        var mentions = MentionRegex().Matches(content).Select(match => match.Value).Distinct().ToArray();
        var normalized = UrlRegex().Replace(content, " URL ");
        normalized = CustomEmojiRegex().Replace(normalized, settings.StripEmoji ? " " : " EMOJI ");
        normalized = MentionRegex().Replace(normalized, " MENTION ");
        normalized = normalized.ToLowerInvariant();

        if (settings.StripEmoji || settings.StripPunctuation)
        {
            normalized = StripCharacters(normalized, settings.StripPunctuation, settings.StripEmoji);
        }

        normalized = WhitespaceRegex().Replace(normalized, " ").Trim();

        var attachmentKeys = message.Attachments
            .Select(attachment => $"{attachment.Size}:{attachment.ContentType}")
            .ToArray();
        var attachmentExtensions = message.Attachments
            .Select(attachment => Path.GetExtension(attachment.Filename).ToLowerInvariant())
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Distinct()
            .ToArray();

        return new TrackedMessage(
            guildId,
            message.Author.Id,
            message.Channel.Id,
            message.Id,
            content,
            normalized,
            urls,
            domains,
            mentions,
            attachmentKeys,
            attachmentExtensions,
            message.MentionedEveryone,
            message.Timestamp);
    }

    private static string StripCharacters(string value, bool stripPunctuation, bool stripEmoji)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (stripPunctuation && IsPunctuation(category))
            {
                builder.Append(' ');
                continue;
            }

            if (stripEmoji && IsEmojiLike(category, rune))
            {
                builder.Append(' ');
                continue;
            }

            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.NonSpacingMark)
            {
                builder.Append(' ');
                continue;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static bool IsPunctuation(UnicodeCategory category) =>
        category is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation;

    private static bool IsEmojiLike(UnicodeCategory category, Rune rune) =>
        category is UnicodeCategory.OtherSymbol
            or UnicodeCategory.Surrogate
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format ||
        rune.Value is >= 0x1F000 and <= 0x1FAFF ||
        rune.Value is >= 0x2600 and <= 0x27BF ||
        rune.Value is 0xFE0E or 0xFE0F or 0x200D;

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?<![@\w])(?:https?://|www\.)?[\p{L}\p{N}](?:[\p{L}\p{N}-]{0,61}[\p{L}\p{N}])?(?:\.[\p{L}\p{N}](?:[\p{L}\p{N}-]{0,61}[\p{L}\p{N}])?)+(?:/[^\s<]*)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DomainCandidateRegex();

    [GeneratedRegex(@"<@!?\d+>|<@&\d+>|<#\d+>|@everyone|@here", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MentionRegex();

    [GeneratedRegex(@"<a?:[a-zA-Z0-9_]+:\d+>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CustomEmojiRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

}
