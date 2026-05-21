using System.Collections.Concurrent;
using System.Text.Json;
using AntiSpamBot.Configuration;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public interface IGuildSettingsStore
{
    Task<GuildSettings> GetAsync(ulong guildId, CancellationToken cancellationToken = default);
    Task SaveAsync(ulong guildId, GuildSettings settings, CancellationToken cancellationToken = default);
    Task UpdateAsync(ulong guildId, Action<GuildSettings> update, CancellationToken cancellationToken = default);
}

public sealed class JsonGuildSettingsStore(
    IOptions<BotOptions> options,
    IConfigBackupService backups) : IGuildSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BotOptions _options = options.Value;
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<ulong, GuildSettings> _cache = new();

    public async Task<GuildSettings> GetAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(guildId, out var cached))
        {
            return cached;
        }

        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(guildId, out cached))
            {
                return cached;
            }

            var path = GetSettingsPath(guildId);
            if (!File.Exists(path))
            {
                var settings = CloneDefaults();
                _cache[guildId] = settings;
                await SaveInternalAsync(guildId, settings, cancellationToken);
                return settings;
            }

            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<GuildSettings>(stream, JsonOptions, cancellationToken);
            var result = loaded ?? CloneDefaults();
            _cache[guildId] = result;
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(ulong guildId, GuildSettings settings, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(guildId, out var existing) &&
                existing.SetupCompleted &&
                existing.Backups.AutomaticBackupsEnabled)
            {
                await backups.BackupAsync(guildId, existing, cancellationToken);
            }

            await SaveInternalAsync(guildId, settings, cancellationToken);
            _cache[guildId] = settings;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateAsync(ulong guildId, Action<GuildSettings> update, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var settings = _cache.TryGetValue(guildId, out var cached)
                ? cached
                : await LoadWithoutLockAsync(guildId, cancellationToken);
            var updated = Clone(settings);

            if (settings.SetupCompleted && settings.Backups.AutomaticBackupsEnabled)
            {
                await backups.BackupAsync(guildId, settings, cancellationToken);
            }

            update(updated);
            await SaveInternalAsync(guildId, updated, cancellationToken);
            _cache[guildId] = updated;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<GuildSettings> LoadWithoutLockAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var path = GetSettingsPath(guildId);
        if (!File.Exists(path))
        {
            return CloneDefaults();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<GuildSettings>(stream, JsonOptions, cancellationToken)
            ?? CloneDefaults();
    }

    private async Task SaveInternalAsync(ulong guildId, GuildSettings settings, CancellationToken cancellationToken)
    {
        var path = GetSettingsPath(guildId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    private GuildSettings CloneDefaults()
    {
        var json = JsonSerializer.Serialize(_options.DefaultSettings, JsonOptions);
        return JsonSerializer.Deserialize<GuildSettings>(json, JsonOptions) ?? GuildSettings.CreateDefault();
    }

    private static GuildSettings Clone(GuildSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        return JsonSerializer.Deserialize<GuildSettings>(json, JsonOptions) ?? GuildSettings.CreateDefault();
    }

    private string GetSettingsPath(ulong guildId) =>
        Path.Combine(_options.DataDirectory, "guilds", guildId.ToString(), "settings.json");
}
