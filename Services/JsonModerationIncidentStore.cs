using System.Collections.Concurrent;
using System.Text.Json;
using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public interface IModerationIncidentStore
{
    Task CreateAsync(ModerationIncident incident, CancellationToken cancellationToken = default);
    Task UpdateAsync(ModerationIncident incident, CancellationToken cancellationToken = default);
    Task<ModerationIncident?> GetAsync(ulong guildId, string incidentId, CancellationToken cancellationToken = default);
    Task<(IncidentClaimResult Result, ModerationIncident? Incident)> TryClaimFalsePositiveAsync(
        ulong guildId,
        string incidentId,
        ulong moderatorId,
        CancellationToken cancellationToken = default);
}

public sealed class JsonModerationIncidentStore(IOptions<BotOptions> options) : IModerationIncidentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BotOptions _options = options.Value;
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

    public async Task CreateAsync(ModerationIncident incident, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(incident.GuildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var incidents = await LoadAsync(incident.GuildId, cancellationToken);
            if (incidents.Any(item => item.IncidentId.Equals(incident.IncidentId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Moderation incident {incident.IncidentId} already exists.");
            }

            incidents.Add(incident);
            await SaveAsync(incident.GuildId, incidents, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateAsync(ModerationIncident incident, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(incident.GuildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var incidents = await LoadAsync(incident.GuildId, cancellationToken);
            var index = incidents.FindIndex(item => item.IncidentId.Equals(incident.IncidentId, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException($"Moderation incident {incident.IncidentId} was not found.");
            }

            incidents[index] = incident;
            await SaveAsync(incident.GuildId, incidents, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ModerationIncident?> GetAsync(ulong guildId, string incidentId, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadAsync(guildId, cancellationToken))
                .FirstOrDefault(item => item.IncidentId.Equals(incidentId, StringComparison.Ordinal));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(IncidentClaimResult Result, ModerationIncident? Incident)> TryClaimFalsePositiveAsync(
        ulong guildId,
        string incidentId,
        ulong moderatorId,
        CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var incidents = await LoadAsync(guildId, cancellationToken);
            var incident = incidents.FirstOrDefault(item => item.IncidentId.Equals(incidentId, StringComparison.Ordinal));
            if (incident is null)
            {
                return (IncidentClaimResult.NotFound, null);
            }

            if (!incident.Status.Equals(ModerationIncidentStatuses.Active, StringComparison.OrdinalIgnoreCase))
            {
                return (IncidentClaimResult.AlreadyHandled, incident);
            }

            incident.Status = ModerationIncidentStatuses.Undoing;
            incident.FalsePositiveModeratorId = moderatorId;
            incident.FalsePositiveAt = DateTimeOffset.UtcNow;
            await SaveAsync(guildId, incidents, cancellationToken);
            return (IncidentClaimResult.Claimed, incident);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<ModerationIncident>> LoadAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var path = GetPath(guildId);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ModerationIncident>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task SaveAsync(ulong guildId, List<ModerationIncident> incidents, CancellationToken cancellationToken)
    {
        var path = GetPath(guildId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, incidents.OrderBy(item => item.DetectedAt), JsonOptions, cancellationToken);
    }

    private string GetPath(ulong guildId) =>
        Path.Combine(_options.DataDirectory, "guilds", guildId.ToString(), "moderation-incidents.json");
}
