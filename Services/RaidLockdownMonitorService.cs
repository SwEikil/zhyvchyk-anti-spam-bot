using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AntiSpamBot.Services;

public sealed class RaidLockdownMonitorService(
    DiscordSocketClient client,
    IGuildSettingsStore settingsStore,
    IThreatService threatService,
    IRaidLockdownService lockdownService,
    ILogger<RaidLockdownMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var guild in client.Guilds)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    var settings = await settingsStore.GetAsync(guild.Id, stoppingToken);
                    var threatLevel = threatService.GetCurrentLevel(guild.Id, settings);
                    await lockdownService.RecoverAsync(guild, settings, threatLevel, stoppingToken);
                    await lockdownService.ReleaseIfSafeAsync(guild, settings, threatLevel, stoppingToken);
                }

                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Raid lockdown monitor failed.");
            }
        }
    }
}
