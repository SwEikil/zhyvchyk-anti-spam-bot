using AntiSpamBot.AntiSpam;
using AntiSpamBot.Configuration;
using AntiSpamBot.Localization;
using AntiSpamBot.Moderation;
using AntiSpamBot.Services;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text;

namespace AntiSpamBot.Handlers;

public sealed class DiscordEventHandler(
    DiscordSocketClient client,
    IGuildSettingsStore settingsStore,
    IAntiSpamService antiSpam,
    IModerationService moderation,
    IAccessControlService accessControl,
    ISetupUiService setupUi,
    IUserStrikeStore strikeStore,
    ITextLocalizer localizer,
    IThreatService threatService,
    IRaidLockdownService lockdownService,
    IAntiNukeService antiNukeService,
    IConfigBackupService backupService,
    IConfigPresetService presetService,
    IOptions<BotOptions> options,
    ILogger<DiscordEventHandler> logger)
{
    private const int MaxImportBytes = 262_144;
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private bool _initialized;

    public Task InitializeAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        client.Log += LogAsync;
        client.Ready += ReadyAsync;
        client.MessageReceived += MessageReceivedAsync;
        client.InteractionCreated += InteractionCreatedAsync;
        client.AuditLogCreated += AuditLogCreatedAsync;
        _initialized = true;
        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        logger.LogInformation("Connected as {User}.", client.CurrentUser);

        if (!options.Value.RegisterGuildCommandsOnReady)
        {
            return;
        }

        foreach (var guild in client.Guilds)
        {
            try
            {
                var settings = await settingsStore.GetAsync(guild.Id);
                await guild.BulkOverwriteApplicationCommandAsync(BuildCommands(settings));
                logger.LogInformation("Registered slash commands for guild {GuildId}.", guild.Id);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to register slash commands for guild {GuildId}.", guild.Id);
            }
        }
    }

    private async Task MessageReceivedAsync(SocketMessage socketMessage)
    {
        try
        {
            if (socketMessage is not SocketUserMessage message ||
                message.Author.IsBot ||
                message.Author.IsWebhook ||
                message.Channel is not SocketGuildChannel guildChannel ||
                message.Author is not SocketGuildUser user)
            {
                return;
            }

            var settings = await settingsStore.GetAsync(guildChannel.Guild.Id);
            if (!IsChannelMonitored(guildChannel.Id, settings) ||
                accessControl.IsExemptFromModeration(user, settings))
            {
                return;
            }

            var detection = antiSpam.Inspect(message, guildChannel.Guild.Id, settings);
            if (!detection.IsSpam)
            {
                return;
            }

            var threatLevel = threatService.ObserveDetection(guildChannel.Guild.Id, detection, settings);
            await lockdownService.MaybeActivateAsync(guildChannel.Guild, settings, threatLevel, detection.Reason);
            await moderation.ApplyAsync(guildChannel.Guild, user, detection, settings);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Message processing failed.");
        }
    }

    private async Task AuditLogCreatedAsync(SocketAuditLogEntry entry, SocketGuild guild)
    {
        try
        {
            var settings = await settingsStore.GetAsync(guild.Id);
            await antiNukeService.ObserveAsync(entry, guild, settings);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Anti-nuke audit log processing failed.");
        }
    }

    private async Task InteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            switch (interaction)
            {
                case SocketSlashCommand command:
                    await HandleSlashCommandAsync(command);
                    break;
                case SocketMessageComponent component:
                    await setupUi.HandleComponentAsync(component);
                    break;
                case SocketModal modal:
                    await setupUi.HandleModalAsync(modal);
                    break;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Interaction processing failed.");

            if (!interaction.HasResponded)
            {
                await interaction.RespondAsync("An internal error occurred while processing this interaction.", ephemeral: true);
            }
        }
    }

    private async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser user)
        {
            await command.RespondAsync("This command can only be used in a server.", ephemeral: true);
            return;
        }

        var settings = await settingsStore.GetAsync(user.Guild.Id);

        switch (command.CommandName)
        {
            case "setup":
                await setupUi.ShowSetupAsync(command, settings);
                break;
            case "antispam":
                await setupUi.ShowMainPanelAsync(command, settings);
                break;
            case "language":
                await setupUi.ShowMainPanelAsync(command, settings);
                break;
            case "access":
                await setupUi.ShowAccessPanelAsync(command, settings);
                break;
            case "status":
                await setupUi.ShowStatusAsync(command, settings);
                break;
            case "help":
                await setupUi.ShowHelpAsync(command, settings);
                break;
            case "unmute":
                await HandleUnmuteAsync(command, user, settings);
                break;
            case "strikes":
                await HandleStrikesAsync(command, user, settings);
                break;
            case "clearstrikes":
                await HandleClearStrikesAsync(command, user, settings);
                break;
            case "dryrun":
                await HandleDryRunAsync(command, user, settings);
                break;
            case "preset":
                await HandlePresetAsync(command, user, settings);
                break;
            case "threat":
                await HandleThreatAsync(command, user, settings);
                break;
            case "backupconfig":
                await HandleBackupConfigAsync(command, user, settings);
                break;
            case "exportconfig":
                await HandleExportConfigAsync(command, user, settings);
                break;
            case "importconfig":
                await HandleImportConfigAsync(command, user, settings);
                break;
            case "blacklistdomain":
                await HandleBlacklistDomainAsync(command, user, settings);
                break;
            case "removedomain":
                await HandleRemoveDomainAsync(command, user, settings);
                break;
            default:
                await command.RespondAsync("Unknown command.", ephemeral: true);
                break;
        }
    }

    private async Task HandleUnmuteAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!accessControl.CanConfigure(actor, settings))
        {
            await command.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return;
        }

        var target = GetGuildUserOption(command, actor.Guild, "user");
        if (target is null)
        {
            await command.RespondAsync(localizer.Get(settings, "user_not_found"), ephemeral: true);
            return;
        }

        var reason = GetStringOption(command, "reason") ?? $"Manual unmute by {actor.Username}";
        await moderation.RemoveTimeoutAsync(target, reason);
        await command.RespondAsync(localizer.Format(settings, "unmute_success", new Dictionary<string, string>
        {
            ["user"] = target.Mention
        }), ephemeral: true);
    }

    private async Task HandleStrikesAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!accessControl.CanConfigure(actor, settings))
        {
            await command.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return;
        }

        var target = GetGuildUserOption(command, actor.Guild, "user") ?? actor;
        var strikes = await strikeStore.GetAsync(actor.Guild.Id, target.Id);
        var count = strikes?.DetectionCount ?? 0;
        await command.RespondAsync(localizer.Format(settings, "strikes_status", new Dictionary<string, string>
        {
            ["user"] = target.Mention,
            ["count"] = count.ToString(),
            ["lastDetection"] = strikes?.LastDetectionAt.ToString("u") ?? localizer.Get(settings, "never")
        }), ephemeral: true);
    }

    private async Task HandleClearStrikesAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!accessControl.CanConfigure(actor, settings))
        {
            await command.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return;
        }

        var target = GetGuildUserOption(command, actor.Guild, "user");
        if (target is null)
        {
            await command.RespondAsync(localizer.Get(settings, "user_not_found"), ephemeral: true);
            return;
        }

        await strikeStore.ResetAsync(actor.Guild.Id, target.Id);
        await command.RespondAsync(localizer.Format(settings, "clearstrikes_success", new Dictionary<string, string>
        {
            ["user"] = target.Mention
        }), ephemeral: true);
    }

    private async Task HandleDryRunAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!await EnsureCanConfigureAsync(command, actor, settings))
        {
            return;
        }

        var enabled = GetBoolOption(command, "enabled");
        await settingsStore.UpdateAsync(actor.Guild.Id, item => item.DryRun.Enabled = enabled);
        await command.RespondAsync(localizer.Format(settings, "dryrun_updated", new Dictionary<string, string>
        {
            ["enabled"] = enabled.ToString()
        }), ephemeral: true);
    }

    private async Task HandlePresetAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!await EnsureCanConfigureAsync(command, actor, settings))
        {
            return;
        }

        var name = GetStringOption(command, "name") ?? "";
        if (!presetService.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            await command.RespondAsync(localizer.Format(settings, "preset_unknown", new Dictionary<string, string>
            {
                ["presets"] = string.Join("`, `", presetService.Names)
            }), ephemeral: true);
            return;
        }

        await settingsStore.UpdateAsync(actor.Guild.Id, item =>
        {
            item.SetupCompleted = true;
            presetService.Apply(name, item);
        });
        await command.RespondAsync(localizer.Format(settings, "preset_applied", new Dictionary<string, string>
        {
            ["name"] = name
        }), ephemeral: true);
    }

    private async Task HandleThreatAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!await EnsureCanConfigureAsync(command, actor, settings))
        {
            return;
        }

        await command.RespondAsync(localizer.Format(settings, "threat_status", new Dictionary<string, string>
        {
            ["level"] = threatService.GetCurrentLevel(actor.Guild.Id, settings).ToString()
        }), ephemeral: true);
    }

    private async Task HandleBackupConfigAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!await EnsureCanConfigureAsync(command, actor, settings))
        {
            return;
        }

        var path = await backupService.BackupAsync(actor.Guild.Id, settings);
        await command.RespondAsync(localizer.Format(settings, "backup_created", new Dictionary<string, string>
        {
            ["path"] = path
        }), ephemeral: true);
    }

    private async Task HandleExportConfigAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!await EnsureCanConfigureAsync(command, actor, settings))
        {
            return;
        }

        var json = await backupService.ExportAsync(settings);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await command.RespondWithFileAsync(stream, $"guild-{actor.Guild.Id}-settings.json", localizer.Get(settings, "export_message"), ephemeral: true);
    }

    private async Task HandleImportConfigAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!await EnsureCanConfigureAsync(command, actor, settings))
        {
            return;
        }

        var json = GetStringOption(command, "json");
        var attachment = GetAttachmentOption(command, "file");
        if (attachment is not null)
        {
            json = await DownloadImportAttachmentAsync(attachment);
            if (json is null)
            {
                await command.RespondAsync(localizer.Get(settings, "import_failed"), ephemeral: true);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            await command.RespondAsync(localizer.Get(settings, "import_missing"), ephemeral: true);
            return;
        }

        var imported = await backupService.ImportAsync(json);
        if (imported is null)
        {
            await command.RespondAsync(localizer.Get(settings, "import_failed"), ephemeral: true);
            return;
        }

        imported.SetupCompleted = true;
        await settingsStore.SaveAsync(actor.Guild.Id, imported);
        await command.RespondAsync(localizer.Get(settings, "import_success"), ephemeral: true);
    }

    private async Task HandleBlacklistDomainAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!await EnsureCanConfigureAsync(command, actor, settings))
        {
            return;
        }

        var domain = NormalizeDomain(GetStringOption(command, "domain") ?? "");
        await settingsStore.UpdateAsync(actor.Guild.Id, item => item.ScamLinks.BlacklistedDomains.Add(domain));
        await command.RespondAsync(localizer.Format(settings, "domain_blacklisted", new Dictionary<string, string>
        {
            ["domain"] = domain
        }), ephemeral: true);
    }

    private async Task HandleRemoveDomainAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (!await EnsureCanConfigureAsync(command, actor, settings))
        {
            return;
        }

        var domain = NormalizeDomain(GetStringOption(command, "domain") ?? "");
        await settingsStore.UpdateAsync(actor.Guild.Id, item => item.ScamLinks.BlacklistedDomains.Remove(domain));
        await command.RespondAsync(localizer.Format(settings, "domain_removed", new Dictionary<string, string>
        {
            ["domain"] = domain
        }), ephemeral: true);
    }

    private static ApplicationCommandProperties[] BuildCommands(GuildSettings settings) =>
    [
        Command(new SlashCommandBuilder()
            .WithName("setup")
            .WithDescription("Open the guided anti-spam setup."), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("antispam")
            .WithDescription("Open the anti-spam configuration panel."), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("language")
            .WithDescription("Open language configuration."), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("access")
            .WithDescription("Configure bot managers, log channel, and admin notification channel."), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("status")
            .WithDescription("Show the current anti-spam configuration summary."), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("help")
            .WithDescription("Show anti-spam bot commands and setup help."), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("unmute")
            .WithDescription("Remove a timeout from a user.")
            .AddOption("user", ApplicationCommandOptionType.User, "User to unmute.", isRequired: true)
            .AddOption("reason", ApplicationCommandOptionType.String, "Audit log reason.", isRequired: false), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("strikes")
            .WithDescription("Show anti-spam strike count for a user.")
            .AddOption("user", ApplicationCommandOptionType.User, "User to inspect.", isRequired: false), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("clearstrikes")
            .WithDescription("Clear anti-spam strike history for a user.")
            .AddOption("user", ApplicationCommandOptionType.User, "User to clear.", isRequired: true), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("dryrun")
            .WithDescription("Enable or disable dry-run mode.")
            .AddOption("enabled", ApplicationCommandOptionType.Boolean, "Log detections without punishment.", isRequired: true), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("preset")
            .WithDescription("Apply a predefined anti-spam configuration preset.")
            .AddOption("name", ApplicationCommandOptionType.String, "small-server, community, strict, or paranoid.", isRequired: true), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("threat")
            .WithDescription("Show the current dynamic threat level."), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("backupconfig")
            .WithDescription("Create a local backup of this guild configuration."), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("exportconfig")
            .WithDescription("Export this guild configuration as a JSON file."), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("importconfig")
            .WithDescription("Import guild configuration from JSON text or a JSON file.")
            .AddOption("file", ApplicationCommandOptionType.Attachment, "Settings JSON file exported by /exportconfig.", isRequired: false)
            .AddOption("json", ApplicationCommandOptionType.String, "JSON settings payload.", isRequired: false), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("blacklistdomain")
            .WithDescription("Add a suspicious/scam domain to the guild blacklist.")
            .AddOption("domain", ApplicationCommandOptionType.String, "Domain to blacklist.", isRequired: true), settings)
            .Build(),
        Command(new SlashCommandBuilder()
            .WithName("removedomain")
            .WithDescription("Remove a domain from the guild blacklist.")
            .AddOption("domain", ApplicationCommandOptionType.String, "Domain to remove.", isRequired: true), settings)
            .Build()
    ];

    private static SlashCommandBuilder Command(SlashCommandBuilder builder, GuildSettings settings) =>
        settings.Access.CommandVisibility is CommandVisibilityMode.VisibleWithRuntimeChecks
            ? builder
            : builder.WithDefaultMemberPermissions(GuildPermission.Administrator);

    private async Task<bool> EnsureCanConfigureAsync(SocketSlashCommand command, SocketGuildUser actor, GuildSettings settings)
    {
        if (accessControl.CanConfigure(actor, settings))
        {
            return true;
        }

        await command.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
        return false;
    }

    private static SocketGuildUser? GetGuildUserOption(SocketSlashCommand command, SocketGuild guild, string name)
    {
        var value = command.Data.Options.FirstOrDefault(option => option.Name == name)?.Value;
        return value switch
        {
            SocketGuildUser guildUser => guildUser,
            SocketUser socketUser => guild.GetUser(socketUser.Id),
            IUser user => guild.GetUser(user.Id),
            _ => null
        };
    }

    private static string? GetStringOption(SocketSlashCommand command, string name) =>
        command.Data.Options.FirstOrDefault(option => option.Name == name)?.Value as string;

    private static IAttachment? GetAttachmentOption(SocketSlashCommand command, string name) =>
        command.Data.Options.FirstOrDefault(option => option.Name == name)?.Value as IAttachment;

    private static bool GetBoolOption(SocketSlashCommand command, string name) =>
        command.Data.Options.FirstOrDefault(option => option.Name == name)?.Value as bool? ?? false;

    private static async Task<string?> DownloadImportAttachmentAsync(IAttachment attachment)
    {
        if (attachment.Size > MaxImportBytes ||
            !attachment.Filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var response = await Http.GetAsync(attachment.Url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode ||
            response.Content.Headers.ContentLength is > MaxImportBytes)
        {
            return null;
        }

        await using var source = await response.Content.ReadAsStreamAsync();
        using var target = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            if (target.Length + read > MaxImportBytes)
            {
                return null;
            }

            target.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(target.ToArray());
    }

    private static string NormalizeDomain(string value)
    {
        return AntiSpamBot.Utilities.DomainNormalizer.ExtractHost(value) is { Length: > 0 } domain
            ? domain
            : value.Trim().Trim('.').ToLowerInvariant();
    }

    private static bool IsChannelMonitored(ulong channelId, GuildSettings settings) =>
        settings.Channels.Mode switch
        {
            MonitoredChannelMode.SelectedOnly => settings.Channels.SelectedChannelIds.Contains(channelId),
            MonitoredChannelMode.AllExceptIgnored => !settings.Channels.IgnoredChannelIds.Contains(channelId),
            _ => true
        };

    private Task LogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Trace,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.Information
        };

        logger.Log(level, message.Exception, "[Discord] {Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
