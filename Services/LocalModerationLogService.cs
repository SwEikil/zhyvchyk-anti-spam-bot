using System.Text.Json;
using AntiSpamBot.Configuration;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public interface ILocalModerationLogService
{
    Task WriteAsync(ulong guildId, GuildSettings settings, string eventType, object payload, CancellationToken cancellationToken = default);
}

public sealed class LocalModerationLogService(IOptions<BotOptions> options) : ILocalModerationLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly BotOptions _options = options.Value;

    public async Task WriteAsync(ulong guildId, GuildSettings settings, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        if (!settings.LocalLogging.Enabled)
        {
            return;
        }

        var directory = Path.IsPathRooted(settings.LocalLogging.Directory)
            ? settings.LocalLogging.Directory
            : Path.Combine(_options.DataDirectory, settings.LocalLogging.Directory);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyy-MM-dd}.moderation.jsonl");
        var record = new
        {
            timestamp = DateTimeOffset.UtcNow,
            guildId,
            eventType,
            payload
        };

        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine, cancellationToken);
    }
}
