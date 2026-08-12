using System.Collections.Concurrent;
using System.Text.Json;
using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public interface ITempBanStore
{
    Task AddAsync(TempBanRecord record, CancellationToken cancellationToken = default);
    Task UpdateAsync(TempBanRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TempBanRecord>> GetDueAsync(DateTimeOffset now, TimeSpan pendingGrace, CancellationToken cancellationToken = default);
    Task<TempBanRecord?> GetAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);
    Task<bool> RemoveIfIncidentAsync(ulong guildId, ulong userId, string incidentId, CancellationToken cancellationToken = default);
    Task RemoveAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);
}

public sealed class JsonTempBanStore(IOptions<BotOptions> options) : ITempBanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BotOptions _options = options.Value;
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

    public async Task AddAsync(TempBanRecord record, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(record.GuildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAsync(record.GuildId, cancellationToken);
            records.RemoveAll(item => item.UserId == record.UserId);
            records.Add(record);
            await SaveAsync(record.GuildId, records, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task UpdateAsync(TempBanRecord record, CancellationToken cancellationToken = default) =>
        AddAsync(record, cancellationToken);

    public async Task<IReadOnlyList<TempBanRecord>> GetDueAsync(DateTimeOffset now, TimeSpan pendingGrace, CancellationToken cancellationToken = default)
    {
        var due = new List<TempBanRecord>();
        var root = Path.Combine(_options.DataDirectory, "guilds");
        if (!Directory.Exists(root))
        {
            return due;
        }

        foreach (var directory in Directory.GetDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ulong.TryParse(Path.GetFileName(directory), out var guildId))
            {
                continue;
            }

            var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                due.AddRange((await LoadAsync(guildId, cancellationToken)).Where(item =>
                    item.ExpiresAt <= now ||
                    IsPending(item) && now - item.BannedAt >= pendingGrace));
            }
            finally
            {
                gate.Release();
            }
        }

        return due;
    }

    public async Task<TempBanRecord?> GetAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadAsync(guildId, cancellationToken)).FirstOrDefault(item => item.UserId == userId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> RemoveIfIncidentAsync(
        ulong guildId,
        ulong userId,
        string incidentId,
        CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAsync(guildId, cancellationToken);
            var removed = records.RemoveAll(item =>
                item.UserId == userId &&
                item.IncidentId.Equals(incidentId, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                await SaveAsync(guildId, records, cancellationToken);
            }

            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RemoveAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAsync(guildId, cancellationToken);
            records.RemoveAll(item => item.UserId == userId);
            await SaveAsync(guildId, records, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<TempBanRecord>> LoadAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var path = GetPath(guildId);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        var records = await JsonSerializer.DeserializeAsync<List<TempBanRecord>>(stream, JsonOptions, cancellationToken) ?? [];
        foreach (var record in records.Where(item => string.IsNullOrWhiteSpace(item.Status)))
        {
            record.Status = TempBanStatuses.Active;
        }

        return records;
    }

    private async Task SaveAsync(ulong guildId, List<TempBanRecord> records, CancellationToken cancellationToken)
    {
        var path = GetPath(guildId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, records.OrderBy(item => item.ExpiresAt), JsonOptions, cancellationToken);
    }

    private string GetPath(ulong guildId) =>
        Path.Combine(_options.DataDirectory, "guilds", guildId.ToString(), "temp-bans.json");

    private static bool IsPending(TempBanRecord record) =>
        record.Status.Equals(TempBanStatuses.Pending, StringComparison.OrdinalIgnoreCase);
}
