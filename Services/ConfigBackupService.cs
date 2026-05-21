using System.Text.Json;
using AntiSpamBot.Configuration;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public interface IConfigBackupService
{
    Task<string> BackupAsync(ulong guildId, GuildSettings settings, CancellationToken cancellationToken = default);
    Task<string> ExportAsync(GuildSettings settings);
    Task<GuildSettings?> ImportAsync(string json, CancellationToken cancellationToken = default);
}

public sealed class ConfigBackupService(IOptions<BotOptions> options) : IConfigBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BotOptions _options = options.Value;

    public async Task<string> BackupAsync(ulong guildId, GuildSettings settings, CancellationToken cancellationToken = default)
    {
        var configured = string.IsNullOrWhiteSpace(settings.Backups.Directory) ? "backups" : settings.Backups.Directory;
        var root = Path.IsPathRooted(configured)
            ? Path.Combine(configured, guildId.ToString())
            : Path.Combine(_options.DataDirectory, configured, guildId.ToString());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"settings-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, await ExportAsync(settings), cancellationToken);
        TrimOldBackups(root, settings.Backups.MaxBackupsPerGuild);
        return path;
    }

    public Task<string> ExportAsync(GuildSettings settings) =>
        Task.FromResult(JsonSerializer.Serialize(settings, JsonOptions));

    public Task<GuildSettings?> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<GuildSettings>(json, JsonOptions);
            return Task.FromResult(settings is not null && HasSafeLocalPaths(settings) ? settings : null);
        }
        catch (JsonException)
        {
            return Task.FromResult<GuildSettings?>(null);
        }
    }

    private static void TrimOldBackups(string root, int maxBackups)
    {
        if (maxBackups <= 0)
        {
            return;
        }

        var files = Directory.GetFiles(root, "settings-*.json")
            .OrderByDescending(File.GetCreationTimeUtc)
            .Skip(maxBackups);

        foreach (var file in files)
        {
            File.Delete(file);
        }
    }

    private static bool HasSafeLocalPaths(GuildSettings settings) =>
        IsSafeDirectoryName(settings.LocalLogging.Directory) &&
        IsSafeDirectoryName(settings.Backups.Directory);

    private static bool IsSafeDirectoryName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            Path.IsPathRooted(value) ||
            value.Contains("..", StringComparison.Ordinal) ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        return value.All(character => !Path.GetInvalidFileNameChars().Contains(character));
    }
}
