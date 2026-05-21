using AntiSpamBot.Configuration;
using AntiSpamBot.Handlers;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public sealed class DiscordBotHostedService(
    DiscordSocketClient client,
    DiscordEventHandler eventHandler,
    IOptions<BotOptions> options,
    ILogger<DiscordBotHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Token))
        {
            throw new InvalidOperationException("Bot token is missing. Set Bot:Token in appsettings.json or ANTISPAM_Bot__Token.");
        }

        await eventHandler.InitializeAsync();
        await client.LoginAsync(TokenType.Bot, options.Value.Token);
        await client.StartAsync();
        logger.LogInformation("Discord bot startup requested.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping Discord bot.");
        await client.StopAsync();
        await client.LogoutAsync();
    }
}
