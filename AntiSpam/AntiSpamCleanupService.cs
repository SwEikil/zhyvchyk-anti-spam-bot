using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AntiSpamBot.AntiSpam;

public sealed class AntiSpamCleanupService(
    IAntiSpamService antiSpam,
    ILogger<AntiSpamCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                antiSpam.Cleanup(TimeSpan.FromMinutes(5));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Anti-spam cleanup failed.");
            }
        }
    }
}
