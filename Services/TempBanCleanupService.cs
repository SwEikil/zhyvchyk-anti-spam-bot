using AntiSpamBot.Models;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AntiSpamBot.Services;

public sealed class TempBanCleanupService(
    DiscordSocketClient client,
    ITempBanStore tempBanStore,
    ILogger<TempBanCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReleaseExpiredBansAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Tempban cleanup failed.");
            }
        }
    }

    private async Task ReleaseExpiredBansAsync(CancellationToken cancellationToken)
    {
        var due = await tempBanStore.GetDueAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2), cancellationToken);
        foreach (var record in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var guild = client.GetGuild(record.GuildId);
            if (guild is null)
            {
                continue;
            }

            try
            {
                if (record.Status.Equals(TempBanStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                {
                    if (!await IsUserBannedAsync(guild, record.UserId))
                    {
                        await tempBanStore.RemoveAsync(record.GuildId, record.UserId, cancellationToken);
                        continue;
                    }

                    record.Status = TempBanStatuses.Active;
                    await tempBanStore.UpdateAsync(record, cancellationToken);
                    if (record.ExpiresAt > DateTimeOffset.UtcNow)
                    {
                        continue;
                    }
                }

                await guild.RemoveBanAsync(record.UserId, new RequestOptions
                {
                    AuditLogReason = "Anti-spam temporary ban expired."
                });
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to remove expired tempban for user {UserId} in guild {GuildId}.", record.UserId, record.GuildId);
                continue;
            }

            await tempBanStore.RemoveAsync(record.GuildId, record.UserId, cancellationToken);
        }
    }

    private static async Task<bool> IsUserBannedAsync(SocketGuild guild, ulong userId)
    {
        try
        {
            return await guild.GetBanAsync(userId) is not null;
        }
        catch (HttpException exception) when ((int?)exception.DiscordCode is 10026)
        {
            return false;
        }
    }
}
