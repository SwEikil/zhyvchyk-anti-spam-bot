using System.Collections.Concurrent;
using System.Text.Json;
using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public interface IUserStrikeStore
{
    Task<UserStrikeState> RegisterDetectionAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);
    Task<UserStrikeState?> GetAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);
    Task SetLastPunishmentAsync(ulong guildId, ulong userId, DateTimeOffset timestamp, CancellationToken cancellationToken = default);
    Task ResetAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);
}

public sealed class JsonUserStrikeStore(IOptions<BotOptions> options) : IUserStrikeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BotOptions _options = options.Value;
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<ulong, GuildStrikeState> _cache = new();

    public async Task<UserStrikeState> RegisterDetectionAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await GetGuildStateWithoutLockAsync(guildId, cancellationToken);
            if (!state.Users.TryGetValue(userId, out var userState))
            {
                userState = new UserStrikeState();
                state.Users[userId] = userState;
            }

            userState.DetectionCount++;
            userState.LastDetectionAt = DateTimeOffset.UtcNow;
            await SaveInternalAsync(guildId, state, cancellationToken);
            return userState;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<UserStrikeState?> GetAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        var state = await GetGuildStateAsync(guildId, cancellationToken);
        return state.Users.GetValueOrDefault(userId);
    }

    public async Task SetLastPunishmentAsync(ulong guildId, ulong userId, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await GetGuildStateWithoutLockAsync(guildId, cancellationToken);
            if (!state.Users.TryGetValue(userId, out var userState))
            {
                userState = new UserStrikeState();
                state.Users[userId] = userState;
            }

            userState.LastPunishmentAt = timestamp;
            await SaveInternalAsync(guildId, state, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ResetAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await GetGuildStateWithoutLockAsync(guildId, cancellationToken);
            state.Users.Remove(userId);
            await SaveInternalAsync(guildId, state, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<GuildStrikeState> GetGuildStateAsync(ulong guildId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(guildId, out var cached))
        {
            return cached;
        }

        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await GetGuildStateWithoutLockAsync(guildId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<GuildStrikeState> GetGuildStateWithoutLockAsync(ulong guildId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(guildId, out var cached))
        {
            return cached;
        }

        var path = GetStatePath(guildId);
        if (!File.Exists(path))
        {
            var state = new GuildStrikeState();
            _cache[guildId] = state;
            return state;
        }

        await using var stream = File.OpenRead(path);
        var loaded = await JsonSerializer.DeserializeAsync<GuildStrikeState>(stream, JsonOptions, cancellationToken)
            ?? new GuildStrikeState();
        _cache[guildId] = loaded;
        return loaded;
    }

    private async Task SaveInternalAsync(ulong guildId, GuildStrikeState state, CancellationToken cancellationToken)
    {
        _cache[guildId] = state;
        var path = GetStatePath(guildId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
    }

    private string GetStatePath(ulong guildId) =>
        Path.Combine(_options.DataDirectory, "guilds", guildId.ToString(), "strikes.json");
}
