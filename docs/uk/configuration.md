# Налаштування

Більшість server settings змінюється через `/antispam`. Розширені значення зберігаються у `data/guilds/<guildId>/settings.json`, а переносити їх між серверами можна через `/exportconfig` і `/importconfig`.

## Як застосовуються налаштування

- `/setup` записує безпечні first-run defaults і позначає сервер як налаштований.
- `/antispam` відкриває головну панель для найчастіших settings.
- `/access` фокусується на log channels, notification channels і manager roles.
- `/language` змінює мову UI бота.
- `/preset` може змінити одразу кілька detection і punishment значень.
- `/importconfig` замінює поточні guild settings JSON payload і позначає setup як завершений.

## Channels

- `Channels.Mode`: визначає, де бот перевіряє повідомлення.
  - `AllTextChannels`: перевіряти всі text channels, які бот може читати.
  - `SelectedOnly`: перевіряти тільки `SelectedChannelIds`.
  - `AllExceptIgnored`: перевіряти всі text channels, крім `IgnoredChannelIds`.
- `Channels.SelectedChannelIds`: channel IDs для режиму `SelectedOnly`.
- `Channels.IgnoredChannelIds`: channel IDs, які пропускаються у режимі `AllExceptIgnored`.
- `Channels.LogChannelId`: канал для moderation logs і admin review reports.
- `Channels.NotificationChannelId`: канал для важливих admin notifications. Якщо не заданий, бот може використовувати тільки moderation log channel.

## Detection

- `Detection.Enabled`: головний перемикач anti-spam detection.
- `Detection.TimeWindowSeconds`: наскільки далеко назад порівнюються recent messages. Менше вікно ловить швидкі bursts; більше вікно ловить повільніший spam.
- `Detection.CleanupIntervalSeconds`: як часто старі in-memory detection entries видаляються.
- `Detection.MinimumSpamCount`: мінімальна кількість схожих повідомлень, після якої подія вважається spam.
- `Detection.MaxMessagesBeforePunishment`: додатковий ліміт repeated messages перед punishment.
- `Detection.SimilarityThreshold`: score схожості повідомлень від `0.0` до `1.0`. Вище значення вимагає майже ідентичні повідомлення; нижче ловить більше варіацій, але може дати більше false positives.
- `Detection.RequireMultipleChannels`: коли true, повтори в кількох channels вважаються підозрілішими.
- `Detection.MassMentionThreshold`: кількість mass mentions, яка може тригерити detection. Тримайте низьким, якщо часто буває `@everyone`/`@here` spam.
- `Detection.RepeatedLinkThreshold`: скільки повторних однакових або схожих links потрібно для link spam.
- `Detection.RepeatedAttachmentThreshold`: скільки повторних attachments потрібно для attachment spam.
- `Detection.StripPunctuation`: ігнорує пунктуацію під час порівняння повідомлень.
- `Detection.StripEmoji`: ігнорує emoji під час порівняння.
- `Detection.StripInvisibleCharacters`: прибирає invisible Unicode characters, якими обходять прості фільтри.

## Punishment

- `Punishment.EnableTimeout`: застосовує Discord timeouts, коли spam підтверджено.
- `Punishment.EnableBan`: вмикає tempban escalation.
- `Punishment.EnablePermanentBan`: дозволяє permanent bans, а не тільки tempbans, коли escalation доходить до ліміту.
- `Punishment.TimeoutDurationsMinutes`: ladder timeout у хвилинах. Default `10, 60, 1440` означає 10 хвилин, 1 годину і 24 години.
- `Punishment.TimeoutDurationsSeconds`: optional second-based timeout ladder для тестування або дуже коротких punishments.
- `Punishment.TempBanDurationsSeconds`: tempban ladder у секундах. Defaults: 1 година, 1 день і 7 днів.
- `Punishment.BanAfterDetections`: кількість detections перед ban escalation.
- `Punishment.BanDeleteMessageDays`: за скільки днів Discord має видалити повідомлення користувача під час ban.
- `Punishment.DmUserBeforeBan`: надсилає DM перед ban, якщо можливо.
- `Punishment.BanDmTemplate`: текст DM для tempban. Підтримує placeholders `{guild}`, `{until}` і `{reason}`.
- `Punishment.CooldownSeconds`: не дає карати одного користувача повторно занадто швидко.
- `Punishment.ReasonTemplate`: audit-log reason template. Підтримує `{reason}`.

## False Positive Protection

- `FalsePositiveProtection.IgnoreAdministrators`: пропускає moderation actions проти Discord administrators.
- `FalsePositiveProtection.IgnoredRoleIds`: ролі, які moderation ігнорує.
- `FalsePositiveProtection.IgnoredUserIds`: користувачі, яких moderation ігнорує.
- `FalsePositiveProtection.WhitelistedUserIds`: користувачі, повністю whitelisted від anti-spam actions.

## Raid Lockdown

- `RaidLockdown.Enabled`: головний перемикач automated lockdown.
- `RaidLockdown.EnableSlowmode`: застосовує slowmode до affected channels під час атаки.
- `RaidLockdown.SlowmodeSeconds`: slowmode delay, який буде встановлено.
- `RaidLockdown.EnableTemporaryChannelLock`: тимчасово забороняє `@everyone` надсилати messages, коли lockdown стартує.
- `RaidLockdown.DurationSeconds`: максимальна тривалість lockdown перед release.
- `RaidLockdown.MinimumHoldSeconds`: мінімальний час, протягом якого lockdown лишається активним, навіть якщо threat швидко впав.
- `RaidLockdown.AutoReleaseWhenThreatDrops`: автоматично знімає lockdown, коли threat level достатньо низький.
- `RaidLockdown.ReleaseWhenAtOrBelow`: threat level, потрібний для auto-release.
- `RaidLockdown.MonitorIntervalSeconds`: як часто бот перевіряє, чи можна зняти lockdown.
- `RaidLockdown.MinimumThreatLevel`: threat level, потрібний для запуску lockdown.

## Scam Links

- `ScamLinks.Enabled`: головний перемикач scam-link checks.
- `ScamLinks.BlacklistedDomains`: guild-specific blocked domains.
- `ScamLinks.ProtectedDomains`: важливі domains для lookalike detection, наприклад `discord.com`, `discord.gg` і `steamcommunity.com`.
- `ScamLinks.FuzzyDomainThreshold`: similarity threshold для lookalike domains. Вище значення суворіше; нижче ловить більше lookalikes, але може зачепити legitimate domains.
- `ScamLinks.ConfusableDetectionEnabled`: знаходить Unicode characters, які візуально схожі на Latin characters.
- `ScamLinks.LeetspeakDetectionEnabled`: знаходить заміни на кшталт `0` замість `o` або `1` замість `l`.

## User Risk

- `UserRisk.Enabled`: головний перемикач account-risk scoring.
- `UserRisk.NewAccountAgeHours`: акаунти молодші за цей вік отримують risk points.
- `UserRisk.RecentJoinMinutes`: учасники, які зайшли протягом цього вікна, отримують risk points.
- `UserRisk.PunishAtScore`: score, після якого user-risk context може підштовхнути подію до punishment.
- `UserRisk.NewAccountScore`: points за новий акаунт.
- `UserRisk.RecentJoinScore`: points за recent join.
- `UserRisk.FirstMessageLinkScore`: points, якщо перше побачене повідомлення користувача містить link.

## Attachments

- `Attachments.SuspiciousExtensionFilteringEnabled`: позначає файли з risky extensions.
- `Attachments.HashingEnabled`: вмикає attachment hashing для repeated-file detection, коли це підтримується.
- `Attachments.SuspiciousExtensions`: blocked або suspicious extensions, наприклад `.exe`, `.scr`, `.bat`, `.ps1`, `.jar` і `.apk`.

## Admin Review

- `AdminReview.Enabled`: надсилає review reports для moderation actions.
- `AdminReview.IncludeDeletedMessageQuote`: додає коротку цитату deleted content у review report.
- `AdminReview.IncludeAttachments`: reupload дозволених attachments у review report.
- `AdminReview.ActionButtonsEnabled`: додає quick action buttons для admins.
- `AdminReview.MaxReuploadedImages`: максимальна кількість images у review report.
- `AdminReview.MaxAttachmentBytes`: максимальний розмір attachment, який бот reupload.

## Escalation

- `Escalation.AutoBanMultiChannelDuplicate`: одразу банить duplicate spam, розкиданий по кількох channels.
- `Escalation.MultiChannelWindowSeconds`: time window для multi-channel duplicate checks.
- `Escalation.MultiChannelMinimumChannels`: кількість channels, потрібна для цієї escalation.
- `Escalation.SimilarityThreshold`: similarity score, потрібний для duplicate escalation.

## Threat Levels

- `ThreatLevels.Enabled`: вмикає dynamic threat scoring.
- `ThreatLevels.WindowSeconds`: rolling window для threat scoring.
- `ThreatLevels.SuspiciousScore`: score для рівня `Suspicious`.
- `ThreatLevels.UnderAttackScore`: score для рівня `UnderAttack`.
- `ThreatLevels.CriticalScore`: score для рівня `Critical`.

## Dry-Run

- `DryRun.Enabled`: логує detections без punishment. Використовуйте перед strict presets або на нових серверах.

## Access

- `Access.OwnerOnlyBootstrap`: коли true, тільки server owner може завершити setup до `SetupCompleted`. У admin-bootstrap гілці Discord administrators теж дозволені до setup.
- `Access.AllowDiscordAdministratorsAfterSetup`: коли true, Discord administrators можуть керувати ботом після setup.
- `Access.CommandVisibility`: керує видимістю slash-команд.
  - `AdministratorOnly`: Discord показує commands тільки administrators.
  - `VisibleWithRuntimeChecks`: commands видимі ширше, але бот перевіряє access під час виконання.
- `Access.ManagerRoleIds`: ролі, які можуть керувати ботом після setup.

## Notifications і Logs

- `Notifications.AdminPingRoleId`: роль для mention у важливих admin notifications.
- `Notifications.AdminPingMessage`: notification template. Підтримує placeholders `{role}`, `{reason}`, `{user}`, `{userId}`, `{channels}`, `{deletedCount}` і `{punishment}`.
- `LocalLogging.Enabled`: записує local moderation logs на диск.
- `LocalLogging.Directory`: директорія для local moderation logs, relative до bot data directory, якщо шлях не absolute.

## Backups

- `Backups.AutomaticBackupsEnabled`: автоматично створює backup settings після updates.
- `Backups.Directory`: backup directory, relative до bot data directory, якщо шлях не absolute.
- `Backups.MaxBackupsPerGuild`: максимальна кількість backup files для одного guild.

## Anti-Nuke

- `AntiNuke.Enabled`: відстежує destructive audit-log actions.
- `AntiNuke.WindowSeconds`: time window для підрахунку destructive actions.
- `AntiNuke.MaxDestructiveActions`: кількість actions, яка тригерить anti-nuke response.
- `AntiNuke.TriggerLockdown`: запускає raid lockdown, коли anti-nuke threshold досягнуто.
- `AntiNuke.IgnoreBotManagers`: ігнорує користувачів, які можуть керувати ботом.
- `AntiNuke.TrustedUserIds`: користувачі, exempt від anti-nuke checks.
- `AntiNuke.TrustedRoleIds`: ролі, exempt від anti-nuke checks.

## Language і Setup State

- `Language`: мова UI бота. Підтримуються `en` і `uk`.
- `SetupCompleted`: позначає, чи сервер завершив initial setup.

## Config files

- `appsettings.json.example`: public template.
- `appsettings.json`: local private config. Не комітьте.
- `data/guilds/<guildId>/settings.json`: runtime settings конкретного сервера.

## Import і Export

Використовуйте:

```text
/exportconfig
/importconfig
```

Import приймає JSON text або JSON attachment до 256 KB. Небезпечні local paths відхиляються.
