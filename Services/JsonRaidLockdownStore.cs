using System.Collections.Concurrent;
using System.Text.Json;
using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public interface IRaidLockdownStore
{
    Task<LockdownRecord?> GetAsync(ulong guildId, CancellationToken cancellationToken = default);
    Task SaveAsync(LockdownRecord record, CancellationToken cancellationToken = default);
    Task RemoveAsync(ulong guildId, CancellationToken cancellationToken = default);
}

public sealed class JsonRaidLockdownStore(IOptions<BotOptions> options) : IRaidLockdownStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BotOptions _options = options.Value;
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

    public async Task<LockdownRecord?> GetAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetPath(guildId);
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<LockdownRecord>(stream, JsonOptions, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(LockdownRecord record, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(record.GuildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetPath(record.GuildId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RemoveAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetPath(guildId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetPath(ulong guildId) =>
        Path.Combine(_options.DataDirectory, "guilds", guildId.ToString(), "lockdown.json");
}
