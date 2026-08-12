using AntiSpamBot.AntiSpam;
using AntiSpamBot.Configuration;
using AntiSpamBot.Handlers;
using AntiSpamBot.Localization;
using AntiSpamBot.Moderation;
using AntiSpamBot.Services;
using AntiSpamBot.Utilities;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

LoadDotEnv();

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "ANTISPAM_")
    .AddCommandLine(args);

builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SectionName));

builder.Services.AddSingleton(_ => new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents =
        GatewayIntents.Guilds |
        GatewayIntents.GuildMessages |
        GatewayIntents.MessageContent |
        GatewayIntents.GuildMembers |
        GatewayIntents.GuildBans |
        GatewayIntents.GuildWebhooks,
    LogGatewayIntentWarnings = true,
    AlwaysDownloadUsers = false,
    MessageCacheSize = 100,
    AuditLogCacheSize = 50
}));

builder.Services.AddSingleton<IGuildSettingsStore, JsonGuildSettingsStore>();
builder.Services.AddSingleton<IUserStrikeStore, JsonUserStrikeStore>();
builder.Services.AddSingleton<IModerationIncidentStore, JsonModerationIncidentStore>();
builder.Services.AddSingleton<ITempBanStore, JsonTempBanStore>();
builder.Services.AddSingleton<IRaidLockdownStore, JsonRaidLockdownStore>();
builder.Services.AddSingleton<ITextLocalizer, TextLocalizer>();
builder.Services.AddSingleton<IMessageNormalizer, MessageNormalizer>();
builder.Services.AddSingleton<IMessageSimilarity, LevenshteinMessageSimilarity>();
builder.Services.AddSingleton<IScamLinkDetector, ScamLinkDetector>();
builder.Services.AddSingleton<IUserRiskService, UserRiskService>();
builder.Services.AddSingleton<IThreatService, ThreatService>();
builder.Services.AddSingleton<IRaidLockdownService, RaidLockdownService>();
builder.Services.AddSingleton<IAntiNukeService, AntiNukeService>();
builder.Services.AddSingleton<ILocalModerationLogService, LocalModerationLogService>();
builder.Services.AddSingleton<IConfigBackupService, ConfigBackupService>();
builder.Services.AddSingleton<IConfigPresetService, ConfigPresetService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IAntiSpamService, AntiSpamService>();
builder.Services.AddSingleton<IModerationService, ModerationService>();
builder.Services.AddSingleton<IAccessControlService, AccessControlService>();
builder.Services.AddSingleton<ISetupUiService, SetupUiService>();
builder.Services.AddSingleton<DiscordEventHandler>();
builder.Services.AddHostedService<DiscordBotHostedService>();
builder.Services.AddHostedService<AntiSpamCleanupService>();
builder.Services.AddHostedService<TempBanCleanupService>();
builder.Services.AddHostedService<RaidLockdownMonitorService>();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

await builder.Build().RunAsync();

static void LoadDotEnv()
{
    const string dotEnvPath = ".env";
    if (!File.Exists(dotEnvPath))
    {
        return;
    }

    foreach (var rawLine in File.ReadAllLines(dotEnvPath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');
        if (Environment.GetEnvironmentVariable(key) is null)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    var dockerStyleToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
    if (!string.IsNullOrWhiteSpace(dockerStyleToken) &&
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTISPAM_Bot__Token")))
    {
        Environment.SetEnvironmentVariable("ANTISPAM_Bot__Token", dockerStyleToken);
    }
}
