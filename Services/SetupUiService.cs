using AntiSpamBot.Configuration;
using AntiSpamBot.Localization;
using Discord;
using Discord.WebSocket;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AntiSpamBot.Services;

public interface ISetupUiService
{
    Task ShowSetupAsync(SocketInteraction interaction, GuildSettings settings);
    Task ShowMainPanelAsync(SocketInteraction interaction, GuildSettings settings);
    Task ShowAccessPanelAsync(SocketInteraction interaction, GuildSettings settings);
    Task ShowStatusAsync(SocketInteraction interaction, GuildSettings settings);
    Task ShowHelpAsync(SocketInteraction interaction, GuildSettings settings);
    Task HandleComponentAsync(SocketMessageComponent component);
    Task HandleModalAsync(SocketModal modal);
}

public sealed partial class SetupUiService(
    IGuildSettingsStore settingsStore,
    IAccessControlService accessControl,
    ITextLocalizer localizer,
    IThreatService threatService) : ISetupUiService
{
    private const string Prefix = "antispam";

    // The setup UI is split into a simple guided panel and advanced modals so new admins
    // can configure safely while experienced admins can tune thresholds directly.
    public async Task ShowSetupAsync(SocketInteraction interaction, GuildSettings settings)
    {
        if (!TryGetGuildUser(interaction, out var user) || !accessControl.CanConfigure(user, settings))
        {
            await interaction.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get(settings, "setup_title"))
            .WithDescription(localizer.Get(settings, "setup_description"))
            .WithColor(Color.Blue)
            .Build();

        var components = new ComponentBuilder()
            .WithButton(localizer.Get(settings, "basic_setup"), $"{Prefix}:setup:basic", ButtonStyle.Primary)
            .WithButton(localizer.Get(settings, "advanced_setup"), $"{Prefix}:setup:advanced", ButtonStyle.Secondary)
            .Build();

        await interaction.RespondAsync(embed: embed, components: components, ephemeral: true);
    }

    public async Task ShowMainPanelAsync(SocketInteraction interaction, GuildSettings settings)
    {
        if (!TryGetGuildUser(interaction, out var user) || !accessControl.CanConfigure(user, settings))
        {
            await interaction.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return;
        }

        await interaction.RespondAsync(
            embed: BuildMainPanelEmbed(settings),
            components: BuildMainPanelComponents(settings),
            ephemeral: true);
    }

    public async Task ShowAccessPanelAsync(SocketInteraction interaction, GuildSettings settings)
    {
        if (!TryGetGuildUser(interaction, out var user) || !accessControl.CanConfigure(user, settings))
        {
            await interaction.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get(settings, "access_panel_title"))
            .WithDescription(localizer.Get(settings, "access_panel_description"))
            .WithColor(Color.DarkBlue)
            .Build();

        await interaction.RespondAsync(embed: embed, components: BuildAdminPanelComponents(settings), ephemeral: true);
    }

    public async Task ShowStatusAsync(SocketInteraction interaction, GuildSettings settings)
    {
        if (!TryGetGuildUser(interaction, out var user) || !accessControl.CanConfigure(user, settings))
        {
            await interaction.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return;
        }

        var description =
            $"{localizer.Get(settings, "status_enabled")}: `{settings.Detection.Enabled}`\n" +
            $"{localizer.Get(settings, "status_language")}: `{settings.Language}`\n" +
            $"{localizer.Get(settings, "status_channel_mode")}: `{settings.Channels.Mode}`\n" +
            $"{localizer.Get(settings, "status_log_channel")}: `{FormatChannel(settings.Channels.LogChannelId, settings)}`\n" +
            $"{localizer.Get(settings, "status_notification_channel")}: `{FormatChannel(settings.Channels.NotificationChannelId, settings)}`\n" +
            $"{localizer.Get(settings, "status_time_window")}: `{settings.Detection.TimeWindowSeconds}s`\n" +
            $"{localizer.Get(settings, "status_similarity")}: `{settings.Detection.SimilarityThreshold:0.00}`\n" +
            $"{localizer.Get(settings, "status_minimum_count")}: `{settings.Detection.MinimumSpamCount}`\n" +
            $"{localizer.Get(settings, "status_timeouts")}: `{FormatTimeoutDurations(settings)}`\n" +
            $"{localizer.Get(settings, "status_auto_ban")}: `{settings.Punishment.EnableBan}`";

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get(settings, "status_title"))
            .WithDescription(description)
            .WithColor(Color.Green)
            .Build();

        await interaction.RespondAsync(embed: embed, ephemeral: true);
    }

    public async Task ShowHelpAsync(SocketInteraction interaction, GuildSettings settings)
    {
        if (!TryGetGuildUser(interaction, out var user) || !accessControl.CanConfigure(user, settings))
        {
            await interaction.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return;
        }

        await interaction.RespondAsync(
            embed: BuildHelpEmbed(settings, "overview"),
            components: BuildHelpComponents(settings),
            ephemeral: true);
    }

    public async Task HandleComponentAsync(SocketMessageComponent component)
    {
        if (!component.Data.CustomId.StartsWith(Prefix + ":", StringComparison.Ordinal))
        {
            return;
        }

        if (!TryGetGuildUser(component, out var user))
        {
            return;
        }

        var settings = await settingsStore.GetAsync(user.Guild.Id);
        if (!accessControl.CanConfigure(user, settings))
        {
            await component.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return;
        }

        switch (component.Data.CustomId)
        {
            case $"{Prefix}:setup:basic":
                await ApplyBasicSetupAsync(component, user.Guild.Id, settings);
                break;
            case $"{Prefix}:setup:advanced":
            case $"{Prefix}:panel":
                await component.RespondAsync(
                    embed: BuildMainPanelEmbed(settings),
                    components: BuildMainPanelComponents(settings),
                    ephemeral: true);
                break;
            case $"{Prefix}:section":
                await ShowSectionAsync(component, component.Data.Values.FirstOrDefault() ?? "overview", settings);
                break;
            case $"{Prefix}:toggle:dryrun":
                await ToggleDryRunAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:toggle:detection":
                await ToggleDetectionAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:toggle:multi_channel":
                await ToggleMultiChannelAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:toggle:strip_emoji":
                await ToggleStripEmojiAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:toggle:strip_punctuation":
                await ToggleStripPunctuationAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:toggle:lockdown":
                await ToggleLockdownAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:toggle:scamlinks":
                await ToggleScamLinksAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:toggle:confusables":
                await ToggleConfusablesAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:toggle:leetspeak":
                await ToggleLeetspeakAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:toggle:tempban":
                await ToggleTempBanAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:toggle:user_risk":
                await ToggleUserRiskAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:lockdown_release_level":
                await UpdateLockdownReleaseLevelAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:lockdown_trigger_level":
                await UpdateLockdownTriggerLevelAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:lockdown_auto_release":
                await UpdateLockdownAutoReleaseAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:threat_reset":
                threatService.Reset(user.Guild.Id);
                await component.RespondAsync(localizer.Get(settings, "threat_reset_done"), ephemeral: true);
                break;
            case $"{Prefix}:mode":
                await UpdateChannelModeAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:monitored_channels":
                await UpdateSelectedChannelsAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:ignored_channels":
                await UpdateIgnoredChannelsAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:log_channel":
                await UpdateLogChannelAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:notification_channel":
                await UpdateNotificationChannelAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:language":
                await UpdateLanguageAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:manager_roles":
                await UpdateManagerRolesAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:ignored_roles":
                await UpdateIgnoredRolesAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:whitelist_users":
                await UpdateWhitelistedUsersAsync(component, user.Guild.Id);
                break;
            case $"{Prefix}:open_detection_modal":
                await component.RespondWithModalAsync(BuildDetectionModal(settings));
                break;
            case $"{Prefix}:open_punishment_modal":
                await component.RespondWithModalAsync(BuildPunishmentModal(settings));
                break;
            case $"{Prefix}:open_ping_modal":
                await component.RespondWithModalAsync(BuildPingModal(settings));
                break;
            case $"{Prefix}:open_lockdown_modal":
                await component.RespondWithModalAsync(BuildLockdownModal(settings));
                break;
            case $"{Prefix}:open_scam_modal":
                await component.RespondWithModalAsync(BuildScamLinkModal(settings));
                break;
            case $"{Prefix}:open_risk_modal":
                await component.RespondWithModalAsync(BuildRiskModal(settings));
                break;
            case $"{Prefix}:open_admin_panel":
                await component.RespondAsync(
                    embed: new EmbedBuilder()
                        .WithTitle(localizer.Get(settings, "access_panel_title"))
                        .WithDescription(localizer.Get(settings, "access_panel_description"))
                        .WithColor(Color.DarkBlue)
                        .Build(),
                    components: BuildAdminPanelComponents(settings),
                    ephemeral: true);
                break;
            case $"{Prefix}:help":
                await component.RespondAsync(
                    embed: BuildHelpEmbed(settings, component.Data.Values.FirstOrDefault() ?? "overview"),
                    components: BuildHelpComponents(settings),
                    ephemeral: true);
                break;
        }
    }

    public async Task HandleModalAsync(SocketModal modal)
    {
        if (!modal.Data.CustomId.StartsWith(Prefix + ":", StringComparison.Ordinal))
        {
            return;
        }

        if (!TryGetGuildUser(modal, out var user))
        {
            return;
        }

        var settings = await settingsStore.GetAsync(user.Guild.Id);
        if (!accessControl.CanConfigure(user, settings))
        {
            await modal.RespondAsync(localizer.Get(settings, "not_authorized"), ephemeral: true);
            return;
        }

        switch (modal.Data.CustomId)
        {
            case $"{Prefix}:modal:detection":
                await SaveDetectionModalAsync(modal, user.Guild.Id);
                break;
            case $"{Prefix}:modal:punishment":
                await SavePunishmentModalAsync(modal, user.Guild.Id);
                break;
            case $"{Prefix}:modal:ping":
                await SavePingModalAsync(modal, user.Guild.Id);
                break;
            case $"{Prefix}:modal:lockdown":
                await SaveLockdownModalAsync(modal, user.Guild.Id);
                break;
            case $"{Prefix}:modal:scam":
                await SaveScamLinkModalAsync(modal, user.Guild.Id);
                break;
            case $"{Prefix}:modal:risk":
                await SaveRiskModalAsync(modal, user.Guild.Id);
                break;
        }
    }

    private async Task ApplyBasicSetupAsync(SocketMessageComponent component, ulong guildId, GuildSettings current)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Detection.Enabled = true;
            settings.Channels.Mode = MonitoredChannelMode.AllExceptIgnored;
            settings.Access.AllowDiscordAdministratorsAfterSetup = true;
            settings.Punishment.EnableTimeout = true;
            settings.Punishment.EnableBan = false;
        });

        await component.RespondAsync(
            localizer.Get(current, "settings_saved"),
            embed: BuildMainPanelEmbed(current),
            components: BuildMainPanelComponents(current),
            ephemeral: true);
    }

    private async Task UpdateChannelModeAsync(SocketMessageComponent component, ulong guildId)
    {
        var value = component.Data.Values.FirstOrDefault();
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Channels.Mode = value switch
            {
                "all" => MonitoredChannelMode.AllTextChannels,
                "selected" => MonitoredChannelMode.SelectedOnly,
                _ => MonitoredChannelMode.AllExceptIgnored
            };
        });

        await ReplySavedAsync(component);
    }

    private async Task UpdateSelectedChannelsAsync(SocketMessageComponent component, ulong guildId)
    {
        var ids = GetSelectedChannelIds(component);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Channels.SelectedChannelIds = ids;
            settings.Channels.Mode = MonitoredChannelMode.SelectedOnly;
        });

        await ReplySavedAsync(component);
    }

    private async Task UpdateIgnoredChannelsAsync(SocketMessageComponent component, ulong guildId)
    {
        var ids = GetSelectedChannelIds(component);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Channels.IgnoredChannelIds = ids;
            settings.Channels.Mode = MonitoredChannelMode.AllExceptIgnored;
        });

        await ReplySavedAsync(component);
    }

    private async Task UpdateLogChannelAsync(SocketMessageComponent component, ulong guildId)
    {
        var id = GetSelectedChannelIds(component).FirstOrDefault();
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Channels.LogChannelId = id == 0 ? null : id;
        });

        await ReplySavedAsync(component);
    }

    private async Task UpdateNotificationChannelAsync(SocketMessageComponent component, ulong guildId)
    {
        var id = GetSelectedChannelIds(component).FirstOrDefault();
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Channels.NotificationChannelId = id == 0 ? null : id;
        });

        await ReplySavedAsync(component);
    }

    private async Task UpdateLanguageAsync(SocketMessageComponent component, ulong guildId)
    {
        var language = component.Data.Values.FirstOrDefault() is "uk" ? "uk" : "en";
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Language = language;
        });

        var settings = await settingsStore.GetAsync(guildId);
        await component.RespondAsync(localizer.Get(settings, "language_updated"), ephemeral: true);
    }

    private async Task UpdateManagerRolesAsync(SocketMessageComponent component, ulong guildId)
    {
        var ids = component.Data.Roles?.Select(role => role.Id).ToHashSet() ?? ParseUlongValues(component.Data.Values);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Access.ManagerRoleIds = ids;
        });

        await ReplySavedAsync(component);
    }

    private async Task SaveDetectionModalAsync(SocketModal modal, ulong guildId)
    {
        var values = GetModalValues(modal);

        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            if (int.TryParse(values.GetValueOrDefault("time_window"), out var timeWindow))
            {
                settings.Detection.TimeWindowSeconds = Math.Clamp(timeWindow, 5, 15);
            }

            if (int.TryParse(values.GetValueOrDefault("minimum_count"), out var minimumCount))
            {
                settings.Detection.MinimumSpamCount = Math.Clamp(minimumCount, 2, 20);
            }

            if (double.TryParse(values.GetValueOrDefault("similarity"), out var threshold))
            {
                settings.Detection.SimilarityThreshold = Math.Clamp(threshold, 0.50, 1.0);
            }

            if (int.TryParse(values.GetValueOrDefault("max_messages"), out var maxMessages))
            {
                settings.Detection.MaxMessagesBeforePunishment = Math.Clamp(maxMessages, 2, 50);
            }

            if (int.TryParse(values.GetValueOrDefault("link_threshold"), out var linkThreshold))
            {
                settings.Detection.RepeatedLinkThreshold = Math.Clamp(linkThreshold, 1, 20);
                settings.Detection.RepeatedAttachmentThreshold = Math.Clamp(linkThreshold, 1, 20);
            }
        });

        await ReplySavedAsync(modal);
    }

    private async Task SavePunishmentModalAsync(SocketModal modal, ulong guildId)
    {
        var values = GetModalValues(modal);

        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            var durations = ParseDurationList(values.GetValueOrDefault("timeout_steps") ?? "");

            if (durations is { Count: > 0 })
            {
                settings.Punishment.TimeoutDurationsSeconds = durations;
                settings.Punishment.TimeoutDurationsMinutes = [];
            }

            if (int.TryParse(values.GetValueOrDefault("ban_after"), out var banAfter))
            {
                settings.Punishment.BanAfterDetections = Math.Clamp(banAfter, 0, 20);
                settings.Punishment.EnableBan = banAfter > 0;
            }

            var tempBans = ParseDurationList(values.GetValueOrDefault("tempban_steps") ?? "");
            if (tempBans is { Count: > 0 })
            {
                settings.Punishment.TempBanDurationsSeconds = tempBans;
            }

            if (int.TryParse(values.GetValueOrDefault("cooldown"), out var cooldown))
            {
                settings.Punishment.CooldownSeconds = Math.Clamp(cooldown, 0, 3600);
            }

            if (int.TryParse(values.GetValueOrDefault("delete_days"), out var deleteDays))
            {
                settings.Punishment.BanDeleteMessageDays = Math.Clamp(deleteDays, 0, 7);
            }
        });

        await ReplySavedAsync(modal);
    }

    private async Task SaveLockdownModalAsync(SocketModal modal, ulong guildId)
    {
        var values = GetModalValues(modal);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            if (int.TryParse(values.GetValueOrDefault("slowmode"), out var slowmode))
            {
                settings.RaidLockdown.SlowmodeSeconds = Math.Clamp(slowmode, 0, 21600);
            }

            if (int.TryParse(values.GetValueOrDefault("min_hold"), out var minHold))
            {
                settings.RaidLockdown.MinimumHoldSeconds = Math.Clamp(minHold, 0, 3600);
            }

            if (int.TryParse(values.GetValueOrDefault("max_duration"), out var maxDuration))
            {
                settings.RaidLockdown.DurationSeconds = Math.Clamp(maxDuration, 10, 86400);
            }

            if (Enum.TryParse<ThreatLevel>(values.GetValueOrDefault("release_level"), true, out var releaseLevel))
            {
                settings.RaidLockdown.ReleaseWhenAtOrBelow = releaseLevel;
            }
        });

        await ReplySavedAsync(modal);
    }

    private async Task SaveScamLinkModalAsync(SocketModal modal, ulong guildId)
    {
        var values = GetModalValues(modal);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            if (double.TryParse(values.GetValueOrDefault("domain_similarity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold))
            {
                settings.ScamLinks.FuzzyDomainThreshold = Math.Clamp(threshold, 0.50, 1.0);
            }

            settings.ScamLinks.ProtectedDomains = SplitCsv(values.GetValueOrDefault("protected_domains") ?? "");
            settings.ScamLinks.BlacklistedDomains = SplitCsv(values.GetValueOrDefault("blacklist") ?? "");
        });

        await ReplySavedAsync(modal);
    }

    private async Task SaveRiskModalAsync(SocketModal modal, ulong guildId)
    {
        var values = GetModalValues(modal);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            if (int.TryParse(values.GetValueOrDefault("new_account_hours"), out var newAccountHours))
            {
                settings.UserRisk.NewAccountAgeHours = Math.Clamp(newAccountHours, 0, 720);
            }

            if (int.TryParse(values.GetValueOrDefault("recent_join_minutes"), out var recentJoinMinutes))
            {
                settings.UserRisk.RecentJoinMinutes = Math.Clamp(recentJoinMinutes, 0, 10080);
            }

            if (int.TryParse(values.GetValueOrDefault("punish_score"), out var punishScore))
            {
                settings.UserRisk.PunishAtScore = Math.Clamp(punishScore, 1, 500);
            }
        });

        await ReplySavedAsync(modal);
    }

    private async Task SavePingModalAsync(SocketModal modal, ulong guildId)
    {
        var values = GetModalValues(modal);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Notifications.AdminPingMessage = values.GetValueOrDefault("admin_ping") ?? "";
        });

        await ReplySavedAsync(modal);
    }

    private async Task UpdateIgnoredRolesAsync(SocketMessageComponent component, ulong guildId)
    {
        var ids = component.Data.Roles?.Select(role => role.Id).ToHashSet() ?? ParseUlongValues(component.Data.Values);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.FalsePositiveProtection.IgnoredRoleIds = ids;
        });

        await ReplySavedAsync(component);
    }

    private async Task UpdateWhitelistedUsersAsync(SocketMessageComponent component, ulong guildId)
    {
        var ids = component.Data.Users?.Select(user => user.Id).ToHashSet() ?? ParseUlongValues(component.Data.Values);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.FalsePositiveProtection.WhitelistedUserIds = ids;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleDryRunAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.DryRun.Enabled = !settings.DryRun.Enabled;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleDetectionAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Detection.Enabled = !settings.Detection.Enabled;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleMultiChannelAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Detection.RequireMultipleChannels = !settings.Detection.RequireMultipleChannels;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleStripEmojiAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Detection.StripEmoji = !settings.Detection.StripEmoji;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleStripPunctuationAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Detection.StripPunctuation = !settings.Detection.StripPunctuation;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleLockdownAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.RaidLockdown.Enabled = !settings.RaidLockdown.Enabled;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleScamLinksAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.ScamLinks.Enabled = !settings.ScamLinks.Enabled;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleConfusablesAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.ScamLinks.ConfusableDetectionEnabled = !settings.ScamLinks.ConfusableDetectionEnabled;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleLeetspeakAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.ScamLinks.LeetspeakDetectionEnabled = !settings.ScamLinks.LeetspeakDetectionEnabled;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleTempBanAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.Punishment.EnableBan = !settings.Punishment.EnableBan;
        });

        await ReplySavedAsync(component);
    }

    private async Task ToggleUserRiskAsync(SocketMessageComponent component, ulong guildId)
    {
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.UserRisk.Enabled = !settings.UserRisk.Enabled;
        });

        await ReplySavedAsync(component);
    }

    private async Task UpdateLockdownReleaseLevelAsync(SocketMessageComponent component, ulong guildId)
    {
        var level = ParseThreatLevel(component.Data.Values.FirstOrDefault(), ThreatLevel.Suspicious);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.RaidLockdown.ReleaseWhenAtOrBelow = level;
        });

        await ReplySavedAsync(component);
    }

    private async Task UpdateLockdownTriggerLevelAsync(SocketMessageComponent component, ulong guildId)
    {
        var level = ParseThreatLevel(component.Data.Values.FirstOrDefault(), ThreatLevel.UnderAttack);
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.RaidLockdown.MinimumThreatLevel = level;
        });

        await ReplySavedAsync(component);
    }

    private async Task UpdateLockdownAutoReleaseAsync(SocketMessageComponent component, ulong guildId)
    {
        var enabled = component.Data.Values.FirstOrDefault() != "false";
        await settingsStore.UpdateAsync(guildId, settings =>
        {
            settings.SetupCompleted = true;
            settings.RaidLockdown.AutoReleaseWhenThreatDrops = enabled;
        });

        await ReplySavedAsync(component);
    }

    private async Task ShowSectionAsync(SocketMessageComponent component, string section, GuildSettings settings)
    {
        await component.RespondAsync(
            embed: BuildSectionEmbed(settings, section),
            components: BuildSectionComponents(settings, section),
            ephemeral: true);
    }

    private Embed BuildMainPanelEmbed(GuildSettings settings) =>
        new EmbedBuilder()
            .WithTitle(localizer.Get(settings, "main_panel_title"))
            .WithDescription(localizer.Get(settings, "main_panel_description"))
            .WithColor(Color.Blue)
            .AddField(localizer.Get(settings, "configure_channels"), $"{localizer.Get(settings, "status_channel_mode")}: `{settings.Channels.Mode}`", true)
            .AddField(localizer.Get(settings, "configure_logs"), $"{localizer.Get(settings, "log")}: `{FormatChannel(settings.Channels.LogChannelId, settings)}`", true)
            .AddField(localizer.Get(settings, "configure_language"), $"`{settings.Language}`", true)
            .AddField(localizer.Get(settings, "configure_detection"), $"{localizer.Get(settings, "status_time_window")} `{settings.Detection.TimeWindowSeconds}s`, {localizer.Get(settings, "status_similarity")} `{settings.Detection.SimilarityThreshold:0.00}`", true)
            .AddField(localizer.Get(settings, "configure_punishment"), $"{localizer.Get(settings, "status_timeouts")} `{FormatTimeoutDurations(settings)}`, {localizer.Get(settings, "ban")} `{settings.Punishment.EnableBan}`", true)
            .AddField(localizer.Get(settings, "configure_lockdown"), $"`{settings.RaidLockdown.Enabled}` / `{settings.RaidLockdown.SlowmodeSeconds}s`", true)
            .AddField(localizer.Get(settings, "configure_scam_links"), $"`{settings.ScamLinks.Enabled}` / `{settings.ScamLinks.FuzzyDomainThreshold:0.00}`", true)
            .AddField("Dry-run", $"`{settings.DryRun.Enabled}`", true)
            .Build();

    private MessageComponent BuildMainPanelComponents(GuildSettings settings)
    {
        return new ComponentBuilder()
            .WithSelectMenu($"{Prefix}:section", BuildSectionOptions(settings), localizer.Get(settings, "dashboard_select_placeholder"), row: 0)
            .WithButton(localizer.Get(settings, "advanced_detection_button"), $"{Prefix}:open_detection_modal", ButtonStyle.Secondary, row: 1)
            .WithButton(localizer.Get(settings, "punishment_button"), $"{Prefix}:open_punishment_modal", ButtonStyle.Secondary, row: 1)
            .WithButton(localizer.Get(settings, "lockdown_button"), $"{Prefix}:open_lockdown_modal", ButtonStyle.Secondary, row: 1)
            .WithButton(localizer.Get(settings, "scam_links_button"), $"{Prefix}:open_scam_modal", ButtonStyle.Secondary, row: 2)
            .WithButton(localizer.Get(settings, "risk_button"), $"{Prefix}:open_risk_modal", ButtonStyle.Secondary, row: 2)
            .WithButton(localizer.Get(settings, "logs_access_button"), $"{Prefix}:open_admin_panel", ButtonStyle.Secondary, row: 2)
            .WithButton(localizer.Get(settings, "dryrun_toggle_button"), $"{Prefix}:toggle:dryrun", settings.DryRun.Enabled ? ButtonStyle.Success : ButtonStyle.Secondary, row: 3)
            .WithButton(localizer.Get(settings, "tempban_toggle_button"), $"{Prefix}:toggle:tempban", settings.Punishment.EnableBan ? ButtonStyle.Success : ButtonStyle.Secondary, row: 3)
            .WithButton(localizer.Get(settings, "admin_ping_button"), $"{Prefix}:open_ping_modal", ButtonStyle.Secondary, row: 3)
            .Build();
    }

    private MessageComponent BuildLegacyChannelComponents(GuildSettings settings)
    {
        var channelModeOptions = new List<SelectMenuOptionBuilder>
        {
            new(localizer.Get(settings, "mode_all"), "all", localizer.Get(settings, "mode_all_description")),
            new(localizer.Get(settings, "mode_selected"), "selected", localizer.Get(settings, "mode_selected_description")),
            new(localizer.Get(settings, "mode_ignored"), "ignored", localizer.Get(settings, "mode_ignored_description"))
        };

        return new ComponentBuilder()
            .WithSelectMenu($"{Prefix}:mode", channelModeOptions, localizer.Get(settings, "choose_monitoring_mode"), row: 0)
            .WithSelectMenu($"{Prefix}:monitored_channels", placeholder: localizer.Get(settings, "select_monitored_channels"), minValues: 0, maxValues: 25, row: 1, type: ComponentType.ChannelSelect, channelTypes: [ChannelType.Text, ChannelType.News])
            .WithSelectMenu($"{Prefix}:ignored_channels", placeholder: localizer.Get(settings, "select_ignored_channels"), minValues: 0, maxValues: 25, row: 2, type: ComponentType.ChannelSelect, channelTypes: [ChannelType.Text, ChannelType.News])
            .Build();
    }

    private MessageComponent BuildSectionComponents(GuildSettings settings, string section) => section switch
    {
        "channels" => BuildLegacyChannelComponents(settings),
        "language" => new ComponentBuilder()
            .WithSelectMenu($"{Prefix}:language", BuildLanguageOptions(settings), localizer.Get(settings, "choose_language"), row: 0)
            .Build(),
        "detection" => new ComponentBuilder()
            .WithButton(localizer.Get(settings, "advanced_detection_button"), $"{Prefix}:open_detection_modal", ButtonStyle.Primary, row: 0)
            .WithButton(localizer.Get(settings, "detection_toggle_button"), $"{Prefix}:toggle:detection", settings.Detection.Enabled ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0)
            .WithButton(localizer.Get(settings, "multi_channel_toggle_button"), $"{Prefix}:toggle:multi_channel", settings.Detection.RequireMultipleChannels ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0)
            .WithButton(localizer.Get(settings, "strip_emoji_toggle_button"), $"{Prefix}:toggle:strip_emoji", settings.Detection.StripEmoji ? ButtonStyle.Success : ButtonStyle.Secondary, row: 1)
            .WithButton(localizer.Get(settings, "strip_punctuation_toggle_button"), $"{Prefix}:toggle:strip_punctuation", settings.Detection.StripPunctuation ? ButtonStyle.Success : ButtonStyle.Secondary, row: 1)
            .Build(),
        "punishment" => new ComponentBuilder()
            .WithButton(localizer.Get(settings, "punishment_button"), $"{Prefix}:open_punishment_modal", ButtonStyle.Primary, row: 0)
            .WithButton(localizer.Get(settings, "tempban_toggle_button"), $"{Prefix}:toggle:tempban", settings.Punishment.EnableBan ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0)
            .Build(),
        "lockdown" => new ComponentBuilder()
            .WithButton(localizer.Get(settings, "lockdown_button"), $"{Prefix}:open_lockdown_modal", ButtonStyle.Primary, row: 0)
            .WithButton(localizer.Get(settings, "lockdown_toggle_button"), $"{Prefix}:toggle:lockdown", settings.RaidLockdown.Enabled ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0)
            .WithSelectMenu($"{Prefix}:lockdown_trigger_level", BuildThreatLevelOptions(settings, settings.RaidLockdown.MinimumThreatLevel, includeNormal: false), localizer.Get(settings, "select_lockdown_trigger"), row: 1)
            .WithSelectMenu($"{Prefix}:lockdown_release_level", BuildThreatLevelOptions(settings, settings.RaidLockdown.ReleaseWhenAtOrBelow, includeNormal: true), localizer.Get(settings, "select_lockdown_release"), row: 2)
            .WithSelectMenu($"{Prefix}:lockdown_auto_release", BuildBoolOptions(settings, settings.RaidLockdown.AutoReleaseWhenThreatDrops), localizer.Get(settings, "select_auto_release"), row: 3)
            .WithButton(localizer.Get(settings, "threat_reset_button"), $"{Prefix}:threat_reset", ButtonStyle.Danger, row: 4)
            .Build(),
        "links" => new ComponentBuilder()
            .WithButton(localizer.Get(settings, "scam_links_button"), $"{Prefix}:open_scam_modal", ButtonStyle.Primary, row: 0)
            .WithButton(localizer.Get(settings, "scam_toggle_button"), $"{Prefix}:toggle:scamlinks", settings.ScamLinks.Enabled ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0)
            .WithButton(localizer.Get(settings, "confusables_toggle_button"), $"{Prefix}:toggle:confusables", settings.ScamLinks.ConfusableDetectionEnabled ? ButtonStyle.Success : ButtonStyle.Secondary, row: 1)
            .WithButton(localizer.Get(settings, "leetspeak_toggle_button"), $"{Prefix}:toggle:leetspeak", settings.ScamLinks.LeetspeakDetectionEnabled ? ButtonStyle.Success : ButtonStyle.Secondary, row: 1)
            .Build(),
        "risk" => new ComponentBuilder()
            .WithButton(localizer.Get(settings, "risk_button"), $"{Prefix}:open_risk_modal", ButtonStyle.Primary, row: 0)
            .WithButton(localizer.Get(settings, "user_risk_toggle_button"), $"{Prefix}:toggle:user_risk", settings.UserRisk.Enabled ? ButtonStyle.Success : ButtonStyle.Secondary, row: 0)
            .Build(),
        "logs" => BuildAdminPanelComponents(settings),
        _ => BuildMainPanelComponents(settings)
    };

    private Embed BuildSectionEmbed(GuildSettings settings, string section)
    {
        var description = section switch
        {
            "channels" => $"{localizer.Get(settings, "section_desc_channels")}\n\n{localizer.Get(settings, "status_channel_mode")}: `{settings.Channels.Mode}`",
            "language" => $"{localizer.Get(settings, "section_desc_language")}\n\n{localizer.Get(settings, "status_language")}: `{settings.Language}`",
            "detection" => $"{localizer.Get(settings, "section_desc_detection")}\n\n{localizer.Get(settings, "status_enabled")}: `{settings.Detection.Enabled}`\n{localizer.Get(settings, "multi_channel_required")}: `{settings.Detection.RequireMultipleChannels}`\n{localizer.Get(settings, "status_time_window")}: `{settings.Detection.TimeWindowSeconds}s`\n{localizer.Get(settings, "status_similarity")}: `{settings.Detection.SimilarityThreshold:0.00}`\n{localizer.Get(settings, "status_minimum_count")}: `{settings.Detection.MinimumSpamCount}`",
            "punishment" => $"{localizer.Get(settings, "section_desc_punishment")}\n\n{localizer.Get(settings, "status_timeouts")}: `{FormatTimeoutDurations(settings)}`\n{localizer.Get(settings, "tempban")}: `{settings.Punishment.EnableBan}`\n{localizer.Get(settings, "tempban_steps")}: `{FormatDurationList(settings.Punishment.TempBanDurationsSeconds)}`",
            "lockdown" => $"{localizer.Get(settings, "section_desc_lockdown")}\n\n{localizer.Get(settings, "status_enabled")}: `{settings.RaidLockdown.Enabled}`\n{localizer.Get(settings, "lockdown_trigger_level")}: `{settings.RaidLockdown.MinimumThreatLevel}`\n{localizer.Get(settings, "lockdown_release_level")}: `{settings.RaidLockdown.ReleaseWhenAtOrBelow}`\nSlowmode: `{settings.RaidLockdown.SlowmodeSeconds}s`\nHold: `{settings.RaidLockdown.MinimumHoldSeconds}s`, max `{settings.RaidLockdown.DurationSeconds}s`",
            "links" => $"{localizer.Get(settings, "section_desc_links")}\n\n{localizer.Get(settings, "status_enabled")}: `{settings.ScamLinks.Enabled}`\nConfusables: `{settings.ScamLinks.ConfusableDetectionEnabled}`\nLeetspeak: `{settings.ScamLinks.LeetspeakDetectionEnabled}`\nProtected: `{string.Join(", ", settings.ScamLinks.ProtectedDomains)}`\nBlacklist: `{string.Join(", ", settings.ScamLinks.BlacklistedDomains)}`",
            "risk" => $"{localizer.Get(settings, "section_desc_risk")}\n\n{localizer.Get(settings, "status_enabled")}: `{settings.UserRisk.Enabled}`\nScore: `{settings.UserRisk.PunishAtScore}`",
            "logs" => $"{localizer.Get(settings, "section_desc_logs")}\n\n{localizer.Get(settings, "status_log_channel")}: `{FormatChannel(settings.Channels.LogChannelId, settings)}`\n{localizer.Get(settings, "status_notification_channel")}: `{FormatChannel(settings.Channels.NotificationChannelId, settings)}`",
            _ => localizer.Get(settings, "main_panel_description")
        };

        return new EmbedBuilder()
            .WithTitle(localizer.Get(settings, "dashboard_section_title"))
            .WithDescription(description)
            .WithColor(Color.Blue)
            .Build();
    }

    private List<SelectMenuOptionBuilder> BuildSectionOptions(GuildSettings settings) =>
    [
        new(localizer.Get(settings, "configure_channels"), "channels"),
        new(localizer.Get(settings, "configure_language"), "language"),
        new(localizer.Get(settings, "configure_detection"), "detection"),
        new(localizer.Get(settings, "configure_punishment"), "punishment"),
        new(localizer.Get(settings, "configure_lockdown"), "lockdown"),
        new(localizer.Get(settings, "configure_scam_links"), "links"),
        new(localizer.Get(settings, "configure_user_risk"), "risk"),
        new(localizer.Get(settings, "configure_logs"), "logs")
    ];

    private List<SelectMenuOptionBuilder> BuildLanguageOptions(GuildSettings settings) =>
    [
        new("English", "en", localizer.Get(settings, "english_description"), isDefault: settings.Language.Equals("en", StringComparison.OrdinalIgnoreCase)),
        new("Ukrainian", "uk", localizer.Get(settings, "ukrainian_description"), isDefault: settings.Language.Equals("uk", StringComparison.OrdinalIgnoreCase))
    ];

    private List<SelectMenuOptionBuilder> BuildThreatLevelOptions(GuildSettings settings, ThreatLevel current, bool includeNormal)
    {
        ThreatLevel[] levels = includeNormal
            ? [ThreatLevel.Normal, ThreatLevel.Suspicious, ThreatLevel.UnderAttack, ThreatLevel.Critical]
            : [ThreatLevel.Suspicious, ThreatLevel.UnderAttack, ThreatLevel.Critical];

        return levels
            .Select(level => new SelectMenuOptionBuilder(
                localizer.Get(settings, $"threat_{level.ToString().ToLowerInvariant()}"),
                level.ToString(),
                localizer.Get(settings, $"threat_{level.ToString().ToLowerInvariant()}_description"),
                isDefault: level == current))
            .ToList();
    }

    private List<SelectMenuOptionBuilder> BuildBoolOptions(GuildSettings settings, bool current) =>
    [
        new(localizer.Get(settings, "enabled_option"), "true", localizer.Get(settings, "enabled_option_description"), isDefault: current),
        new(localizer.Get(settings, "disabled_option"), "false", localizer.Get(settings, "disabled_option_description"), isDefault: !current)
    ];

    private MessageComponent BuildAdminPanelComponents(GuildSettings settings)
    {
        return new ComponentBuilder()
            .WithSelectMenu($"{Prefix}:log_channel", placeholder: localizer.Get(settings, "select_log_channel"), minValues: 1, maxValues: 1, row: 0, type: ComponentType.ChannelSelect, channelTypes: [ChannelType.Text, ChannelType.News])
            .WithSelectMenu($"{Prefix}:notification_channel", placeholder: localizer.Get(settings, "select_notification_channel"), minValues: 1, maxValues: 1, row: 1, type: ComponentType.ChannelSelect, channelTypes: [ChannelType.Text, ChannelType.News])
            .WithSelectMenu($"{Prefix}:manager_roles", placeholder: localizer.Get(settings, "select_manager_roles"), minValues: 0, maxValues: 25, row: 2, type: ComponentType.RoleSelect)
            .WithSelectMenu($"{Prefix}:ignored_roles", placeholder: localizer.Get(settings, "select_ignored_roles"), minValues: 0, maxValues: 25, row: 3, type: ComponentType.RoleSelect)
            .WithSelectMenu($"{Prefix}:whitelist_users", placeholder: localizer.Get(settings, "select_whitelisted_users"), minValues: 0, maxValues: 25, row: 4, type: ComponentType.UserSelect)
            .Build();
    }

    private Modal BuildDetectionModal(GuildSettings settings) =>
        new ModalBuilder(localizer.Get(settings, "modal_detection_title"), $"{Prefix}:modal:detection")
            .AddTextInput(localizer.Get(settings, "modal_time_window"), "time_window", value: settings.Detection.TimeWindowSeconds.ToString(), minLength: 1, maxLength: 2, required: true)
            .AddTextInput(localizer.Get(settings, "modal_min_count"), "minimum_count", value: settings.Detection.MinimumSpamCount.ToString(), minLength: 1, maxLength: 2, required: true)
            .AddTextInput(localizer.Get(settings, "modal_similarity"), "similarity", value: settings.Detection.SimilarityThreshold.ToString("0.00"), minLength: 3, maxLength: 4, required: true)
            .AddTextInput(localizer.Get(settings, "modal_max_messages"), "max_messages", value: settings.Detection.MaxMessagesBeforePunishment.ToString(), minLength: 1, maxLength: 2, required: true)
            .AddTextInput(localizer.Get(settings, "modal_link_threshold"), "link_threshold", value: settings.Detection.RepeatedLinkThreshold.ToString(), minLength: 1, maxLength: 2, required: true)
            .Build();

    private Modal BuildPunishmentModal(GuildSettings settings) =>
        new ModalBuilder(localizer.Get(settings, "modal_punishment_title"), $"{Prefix}:modal:punishment")
            .AddTextInput(localizer.Get(settings, "modal_timeout_steps"), "timeout_steps", placeholder: "30s, 5m, 1h", value: FormatTimeoutDurations(settings), minLength: 1, maxLength: 100, required: true)
            .AddTextInput(localizer.Get(settings, "modal_tempban_steps"), "tempban_steps", placeholder: "1h, 1d, 7d", value: FormatDurationList(settings.Punishment.TempBanDurationsSeconds), minLength: 1, maxLength: 100, required: true)
            .AddTextInput(localizer.Get(settings, "modal_ban_after"), "ban_after", value: settings.Punishment.EnableBan ? settings.Punishment.BanAfterDetections.ToString() : "0", minLength: 1, maxLength: 2, required: true)
            .AddTextInput(localizer.Get(settings, "modal_cooldown"), "cooldown", value: settings.Punishment.CooldownSeconds.ToString(), minLength: 1, maxLength: 4, required: true)
            .AddTextInput(localizer.Get(settings, "modal_delete_days"), "delete_days", value: settings.Punishment.BanDeleteMessageDays.ToString(), minLength: 1, maxLength: 1, required: true)
            .Build();

    private Modal BuildLockdownModal(GuildSettings settings) =>
        new ModalBuilder(localizer.Get(settings, "modal_lockdown_title"), $"{Prefix}:modal:lockdown")
            .AddTextInput(localizer.Get(settings, "modal_slowmode"), "slowmode", value: settings.RaidLockdown.SlowmodeSeconds.ToString(), minLength: 1, maxLength: 5, required: true)
            .AddTextInput(localizer.Get(settings, "modal_min_hold"), "min_hold", value: settings.RaidLockdown.MinimumHoldSeconds.ToString(), minLength: 1, maxLength: 5, required: true)
            .AddTextInput(localizer.Get(settings, "modal_max_duration"), "max_duration", value: settings.RaidLockdown.DurationSeconds.ToString(), minLength: 1, maxLength: 5, required: true)
            .Build();

    private Modal BuildScamLinkModal(GuildSettings settings) =>
        new ModalBuilder(localizer.Get(settings, "modal_scam_title"), $"{Prefix}:modal:scam")
            .AddTextInput(localizer.Get(settings, "modal_domain_similarity"), "domain_similarity", value: settings.ScamLinks.FuzzyDomainThreshold.ToString("0.00", CultureInfo.InvariantCulture), minLength: 3, maxLength: 4, required: true)
            .AddTextInput(localizer.Get(settings, "modal_protected_domains"), "protected_domains", TextInputStyle.Paragraph, value: string.Join(", ", settings.ScamLinks.ProtectedDomains), minLength: 0, maxLength: 1000, required: false)
            .AddTextInput(localizer.Get(settings, "modal_blacklist_domains"), "blacklist", TextInputStyle.Paragraph, value: string.Join(", ", settings.ScamLinks.BlacklistedDomains), minLength: 0, maxLength: 1000, required: false)
            .Build();

    private Modal BuildRiskModal(GuildSettings settings) =>
        new ModalBuilder(localizer.Get(settings, "modal_risk_title"), $"{Prefix}:modal:risk")
            .AddTextInput(localizer.Get(settings, "modal_new_account_hours"), "new_account_hours", value: settings.UserRisk.NewAccountAgeHours.ToString(), minLength: 1, maxLength: 4, required: true)
            .AddTextInput(localizer.Get(settings, "modal_recent_join_minutes"), "recent_join_minutes", value: settings.UserRisk.RecentJoinMinutes.ToString(), minLength: 1, maxLength: 5, required: true)
            .AddTextInput(localizer.Get(settings, "modal_punish_score"), "punish_score", value: settings.UserRisk.PunishAtScore.ToString(), minLength: 1, maxLength: 3, required: true)
            .Build();

    private Modal BuildPingModal(GuildSettings settings) =>
        new ModalBuilder(localizer.Get(settings, "modal_ping_title"), $"{Prefix}:modal:ping")
            .AddTextInput(localizer.Get(settings, "modal_ping_label"), "admin_ping", TextInputStyle.Paragraph, value: settings.Notifications.AdminPingMessage, minLength: 0, maxLength: 1000, required: false)
            .Build();

    private async Task ReplySavedAsync(SocketInteraction interaction)
    {
        var guildUser = (SocketGuildUser)interaction.User;
        var settings = await settingsStore.GetAsync(guildUser.Guild.Id);
        await interaction.RespondAsync(localizer.Get(settings, "settings_saved"), ephemeral: true);
    }

    private Embed BuildHelpEmbed(GuildSettings settings, string module)
    {
        var key = module switch
        {
            "setup" => "help_setup",
            "moderation" => "help_moderation",
            "settings" => "help_settings",
            "lockdown" => "help_lockdown",
            "links" => "help_links",
            "risk" => "help_risk",
            "testing" => "help_testing",
            _ => "help_overview"
        };

        return new EmbedBuilder()
            .WithTitle(localizer.Get(settings, "help_title"))
            .WithDescription(localizer.Get(settings, key))
            .WithColor(Color.Purple)
            .Build();
    }

    private MessageComponent BuildHelpComponents(GuildSettings settings)
    {
        var options = new List<SelectMenuOptionBuilder>
        {
            new(localizer.Get(settings, "help_overview_option"), "overview", localizer.Get(settings, "help_overview_option_description")),
            new(localizer.Get(settings, "help_setup_option"), "setup", localizer.Get(settings, "help_setup_option_description")),
            new(localizer.Get(settings, "help_settings_option"), "settings", localizer.Get(settings, "help_settings_option_description")),
            new(localizer.Get(settings, "help_lockdown_option"), "lockdown", localizer.Get(settings, "help_lockdown_option_description")),
            new(localizer.Get(settings, "help_links_option"), "links", localizer.Get(settings, "help_links_option_description")),
            new(localizer.Get(settings, "help_risk_option"), "risk", localizer.Get(settings, "help_risk_option_description")),
            new(localizer.Get(settings, "help_moderation_option"), "moderation", localizer.Get(settings, "help_moderation_option_description")),
            new(localizer.Get(settings, "help_testing_option"), "testing", localizer.Get(settings, "help_testing_option_description"))
        };

        return new ComponentBuilder()
            .WithSelectMenu($"{Prefix}:help", options, localizer.Get(settings, "help_select_placeholder"))
            .Build();
    }

    private static bool TryGetGuildUser(SocketInteraction interaction, out SocketGuildUser user)
    {
        user = interaction.User as SocketGuildUser ?? null!;
        return user is not null;
    }

    private static HashSet<ulong> GetSelectedChannelIds(SocketMessageComponent component)
    {
        if (component.Data.Channels is { Count: > 0 })
        {
            return component.Data.Channels.Select(channel => channel.Id).ToHashSet();
        }

        return ParseUlongValues(component.Data.Values);
    }

    private static HashSet<ulong> ParseUlongValues(IEnumerable<string> values) =>
        values
            .Select(value => ulong.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();

    private static Dictionary<string, string> GetModalValues(SocketModal modal) =>
        modal.Data.Components.ToDictionary(component => component.CustomId, component => component.Value);

    private string FormatChannel(ulong? channelId, GuildSettings settings) => channelId is null ? localizer.Get(settings, "not_set") : $"#{channelId.Value}";

    private static string FormatTimeoutDurations(GuildSettings settings)
    {
        var seconds = settings.Punishment.TimeoutDurationsSeconds is { Count: > 0 }
            ? settings.Punishment.TimeoutDurationsSeconds
            : settings.Punishment.TimeoutDurationsMinutes.Select(minutes => minutes * 60.0).ToList();

        return string.Join(", ", seconds.Where(value => value > 0).Select(FormatDuration));
    }

    private static string FormatDurationList(IEnumerable<double> seconds) =>
        string.Join(", ", seconds.Where(value => value > 0).Select(FormatDuration));

    private static string FormatDuration(double seconds)
    {
        if (seconds < 60)
        {
            return $"{TrimNumber(seconds)}s";
        }

        if (seconds % 3600 == 0)
        {
            return $"{TrimNumber(seconds / 3600)}h";
        }

        if (seconds % 60 == 0)
        {
            return $"{TrimNumber(seconds / 60)}m";
        }

        return $"{TrimNumber(seconds)}s";
    }

    private static string TrimNumber(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static List<double> ParseDurationList(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var matches = DurationRegex().Matches(value);
        if (matches.Count == 0)
        {
            return [];
        }

        var results = new List<double>();
        foreach (Match match in matches.Take(10))
        {
            var numberText = match.Groups["value"].Value.Replace(',', '.');
            if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number <= 0)
            {
                continue;
            }

            var unit = match.Groups["unit"].Value.ToLowerInvariant();
            var seconds = unit switch
            {
                "s" or "sec" or "secs" or "second" or "seconds" => number,
                "h" or "hr" or "hrs" or "hour" or "hours" => number * 3600,
                "d" or "day" or "days" => number * 86400,
                _ => number * 60
            };

            results.Add(Math.Clamp(seconds, 1, 2419200));
        }

        return results;
    }

    private static HashSet<string> SplitCsv(string value) =>
        value.Split([',', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Trim().Trim('.').ToLowerInvariant())
            .Where(item => item.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static ThreatLevel ParseThreatLevel(string? value, ThreatLevel fallback) =>
        Enum.TryParse<ThreatLevel>(value, true, out var level) ? level : fallback;

    [GeneratedRegex(@"(?<!\d)(?<value>\d+(?:[\.,]\d+)?)(?:\s*(?<unit>s|sec|secs|second|seconds|m|min|mins|minute|minutes|h|hr|hrs|hour|hours|d|day|days))?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DurationRegex();
}
