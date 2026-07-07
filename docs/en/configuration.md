# Configuration

Most server settings can be changed from `/antispam`. Advanced values are stored in `data/guilds/<guildId>/settings.json` and can also be moved between servers with `/exportconfig` and `/importconfig`.

## How Settings Are Applied

- `/setup` writes safe first-run defaults and marks the server as configured.
- `/antispam` opens the main dashboard for the most common settings.
- `/access` focuses on log channels, notification channels, and manager roles.
- `/language` changes bot UI language.
- `/preset` can replace several detection and punishment values at once.
- `/importconfig` replaces the current guild settings with a JSON payload and marks setup as completed.

## Channels

- `Channels.Mode`: chooses where the bot watches messages.
  - `AllTextChannels`: watch every text channel the bot can read.
  - `SelectedOnly`: watch only `SelectedChannelIds`.
  - `AllExceptIgnored`: watch all text channels except `IgnoredChannelIds`.
- `Channels.SelectedChannelIds`: channel IDs used by `SelectedOnly`.
- `Channels.IgnoredChannelIds`: channel IDs skipped by `AllExceptIgnored`.
- `Channels.LogChannelId`: channel for moderation logs and admin review reports.
- `Channels.NotificationChannelId`: channel for high-signal admin notifications. If unset, the bot may only use the moderation log channel.

## Detection

- `Detection.Enabled`: master switch for anti-spam detection.
- `Detection.TimeWindowSeconds`: how far back recent messages are compared. Smaller windows catch fast bursts; larger windows catch slower spam.
- `Detection.CleanupIntervalSeconds`: how often old in-memory detection entries are removed.
- `Detection.MinimumSpamCount`: minimum number of matching messages before a spam event is considered.
- `Detection.MaxMessagesBeforePunishment`: extra cap for repeated messages before punishment is applied.
- `Detection.SimilarityThreshold`: message similarity score from `0.0` to `1.0`. Higher values require messages to be nearly identical; lower values catch more variations and can increase false positives.
- `Detection.RequireMultipleChannels`: when true, repeated messages are more suspicious if they appear across channels.
- `Detection.MassMentionThreshold`: number of mass mentions that can trigger detection. Keep low if `@everyone`/`@here` spam is common.
- `Detection.RepeatedLinkThreshold`: repeated identical or similar links needed before link spam is flagged.
- `Detection.RepeatedAttachmentThreshold`: repeated attachments needed before attachment spam is flagged.
- `Detection.StripPunctuation`: ignores punctuation while comparing messages.
- `Detection.StripEmoji`: ignores emoji while comparing messages.
- `Detection.StripInvisibleCharacters`: removes invisible Unicode characters used to bypass simple filters.

## Punishment

- `Punishment.EnableTimeout`: applies Discord timeouts when spam is confirmed.
- `Punishment.EnableBan`: enables temporary ban escalation.
- `Punishment.EnablePermanentBan`: allows permanent bans instead of only tempbans when escalation reaches the configured limit.
- `Punishment.TimeoutDurationsMinutes`: timeout ladder in minutes. The default `10, 60, 1440` means 10 minutes, 1 hour, then 24 hours.
- `Punishment.TimeoutDurationsSeconds`: optional second-based timeout ladder for testing or very short punishments.
- `Punishment.TempBanDurationsSeconds`: tempban ladder in seconds. Defaults are 1 hour, 1 day, and 7 days.
- `Punishment.BanAfterDetections`: number of detections before ban escalation is used.
- `Punishment.BanDeleteMessageDays`: how many days of the user's messages Discord should delete when banning.
- `Punishment.DmUserBeforeBan`: sends a DM before banning when possible.
- `Punishment.BanDmTemplate`: DM text for tempbans. Supports placeholders such as `{guild}`, `{until}`, and `{reason}`.
- `Punishment.CooldownSeconds`: prevents repeated punishments for the same user too quickly.
- `Punishment.ReasonTemplate`: audit-log reason template. Supports `{reason}`.

## False Positive Protection

- `FalsePositiveProtection.IgnoreAdministrators`: skips moderation actions against Discord administrators.
- `FalsePositiveProtection.IgnoredRoleIds`: roles ignored by moderation.
- `FalsePositiveProtection.IgnoredUserIds`: users ignored by moderation.
- `FalsePositiveProtection.WhitelistedUserIds`: users fully whitelisted from anti-spam actions.

## Raid Lockdown

- `RaidLockdown.Enabled`: master switch for automated lockdown.
- `RaidLockdown.EnableSlowmode`: applies slowmode to affected channels during an attack.
- `RaidLockdown.SlowmodeSeconds`: slowmode delay to apply.
- `RaidLockdown.EnableTemporaryChannelLock`: temporarily denies sending messages for `@everyone` when lockdown starts.
- `RaidLockdown.DurationSeconds`: maximum lockdown duration before release.
- `RaidLockdown.MinimumHoldSeconds`: minimum time to keep lockdown active even if threat drops quickly.
- `RaidLockdown.AutoReleaseWhenThreatDrops`: releases lockdown automatically when the threat level becomes low enough.
- `RaidLockdown.ReleaseWhenAtOrBelow`: threat level required for auto-release.
- `RaidLockdown.MonitorIntervalSeconds`: how often the bot checks whether lockdown can be released.
- `RaidLockdown.MinimumThreatLevel`: threat level required to trigger lockdown.

## Scam Links

- `ScamLinks.Enabled`: master switch for scam-link checks.
- `ScamLinks.BlacklistedDomains`: guild-specific blocked domains.
- `ScamLinks.ProtectedDomains`: high-value domains used for lookalike detection, such as `discord.com`, `discord.gg`, and `steamcommunity.com`.
- `ScamLinks.FuzzyDomainThreshold`: similarity threshold for lookalike domains. Higher values are stricter; lower values catch more lookalikes but may flag legitimate domains.
- `ScamLinks.ConfusableDetectionEnabled`: detects Unicode characters that visually imitate Latin characters.
- `ScamLinks.LeetspeakDetectionEnabled`: detects substitutions such as `0` for `o` or `1` for `l`.

## User Risk

- `UserRisk.Enabled`: master switch for account-risk scoring.
- `UserRisk.NewAccountAgeDays`: accounts younger than this receive risk points and strict monitoring. Default is `60`. Configurable from the User Risk panel and JSON.
- `UserRisk.RecentJoinDays`: members who joined within this window receive risk points and strict monitoring. Default is `60`. Configurable from the User Risk panel and JSON.
- `UserRisk.NewAccountAgeHours`: legacy account-age window kept for older JSON configs.
- `UserRisk.RecentJoinMinutes`: legacy recent-join window kept for older JSON configs.
- `UserRisk.PunishAtScore`: score at which user-risk context can push an event into punishment.
- `UserRisk.NewAccountScore`: points added for a new account. Advanced JSON setting.
- `UserRisk.RecentJoinScore`: points added for a recent join. Advanced JSON setting.
- `UserRisk.FirstMessageLinkScore`: points added when a user's first observed message contains a link. Advanced JSON setting.
- `UserRisk.StrictMonitoringEnabled`: lowers spam thresholds for fresh accounts or recent joins. Toggleable from the User Risk panel and JSON.
- `UserRisk.StrictMonitoringScoreBonus`: extra points when a user is both a fresh account and a recent join. Advanced JSON setting.
- `UserRisk.StrictMonitoringTimeoutOnConfirmedSpam`: applies timeout when confirmed spam comes from a strict-monitoring user, while still respecting dry-run. Toggleable from the User Risk panel and JSON.
- `UserRisk.StrictMonitoringMinimumSpamCount`: minimum repeated/similar message count used for strict-monitoring users. Configurable from the User Risk panel and JSON.

## Attachments

- `Attachments.SuspiciousExtensionFilteringEnabled`: flags files with risky extensions.
- `Attachments.HashingEnabled`: enables attachment hashing for repeated-file detection when supported.
- `Attachments.SuspiciousExtensions`: blocked or suspicious extensions, such as `.exe`, `.scr`, `.bat`, `.ps1`, `.jar`, and `.apk`.

## Admin Review

- `AdminReview.Enabled`: sends review reports for moderation actions.
- `AdminReview.IncludeDeletedMessageQuote`: includes a short quote of deleted content in the review report.
- `AdminReview.IncludeAttachments`: reuploads allowed attachments into the review report.
- `AdminReview.ActionButtonsEnabled`: adds quick action buttons for admins.
- `AdminReview.MaxReuploadedImages`: maximum number of images attached to a review report.
- `AdminReview.MaxAttachmentBytes`: maximum attachment size the bot will reupload.

## Escalation

- `Escalation.AutoBanMultiChannelDuplicate`: immediately bans duplicate spam spread across several channels.
- `Escalation.MultiChannelWindowSeconds`: time window for multi-channel duplicate checks.
- `Escalation.MultiChannelMinimumChannels`: number of channels required for this escalation.
- `Escalation.SimilarityThreshold`: similarity score required for duplicate escalation.

## Threat Levels

- `ThreatLevels.Enabled`: enables dynamic threat scoring.
- `ThreatLevels.WindowSeconds`: rolling window used for threat scoring.
- `ThreatLevels.SuspiciousScore`: score needed for `Suspicious`.
- `ThreatLevels.UnderAttackScore`: score needed for `UnderAttack`.
- `ThreatLevels.CriticalScore`: score needed for `Critical`.

## Dry-Run

- `DryRun.Enabled`: logs detections without applying punishment. Use this before strict presets or on new servers.

## Access

- `Access.OwnerOnlyBootstrap`: when true, only the server owner can complete setup before `SetupCompleted`. In the admin-bootstrap branch, Discord administrators are allowed before setup as well.
- `Access.AllowDiscordAdministratorsAfterSetup`: when true, Discord administrators can manage the bot after setup.
- `Access.CommandVisibility`: controls slash-command visibility.
  - `AdministratorOnly`: Discord only shows commands to administrators.
  - `VisibleWithRuntimeChecks`: commands are visible more broadly, but the bot checks access at runtime.
- `Access.ManagerRoleIds`: roles allowed to manage the bot after setup.

## Notifications and Logs

- `Notifications.AdminPingRoleId`: role to mention for important admin notifications.
- `Notifications.AdminPingMessage`: notification template. Supported placeholders include `{role}`, `{reason}`, `{user}`, `{userId}`, `{channels}`, `{deletedCount}`, and `{punishment}`.
- `LocalLogging.Enabled`: writes local moderation logs to disk.
- `LocalLogging.Directory`: directory for local moderation logs, relative to the bot data directory unless absolute.

## Backups

- `Backups.AutomaticBackupsEnabled`: automatically backs up settings after updates.
- `Backups.Directory`: backup directory, relative to the bot data directory unless absolute.
- `Backups.MaxBackupsPerGuild`: maximum retained backup files per guild.

## Anti-Nuke

- `AntiNuke.Enabled`: watches destructive audit-log actions.
- `AntiNuke.WindowSeconds`: time window for counting destructive actions.
- `AntiNuke.MaxDestructiveActions`: number of actions that triggers anti-nuke response.
- `AntiNuke.TriggerLockdown`: starts raid lockdown when the anti-nuke threshold is hit.
- `AntiNuke.IgnoreBotManagers`: ignores users who can manage the bot.
- `AntiNuke.TrustedUserIds`: users exempt from anti-nuke checks.
- `AntiNuke.TrustedRoleIds`: roles exempt from anti-nuke checks.

## Language and Setup State

- `Language`: bot UI language. Supported values are `en` and `uk`.
- `SetupCompleted`: marks whether the server has completed initial setup.

## Config Files

- `appsettings.json.example`: public template.
- `appsettings.json`: local private config. Do not commit.
- `data/guilds/<guildId>/settings.json`: per-server runtime settings.

## Import and Export

Use:

```text
/exportconfig
/importconfig
```

Import accepts JSON text or a JSON attachment up to 256 KB. Unsafe local paths are rejected.
