# Commands

All commands are Discord slash commands. Most configuration commands respond ephemerally, so only the user who ran the command sees the setup UI or export result.

## Setup and UI

- `/setup` opens the guided first-run setup. Use Basic setup for safe defaults, or Advanced setup when you want to tune thresholds immediately.
- `/antispam` opens the main control panel for channels, detection, punishment, raid lockdown, scam links, user risk, logs/access, dry-run, tempban, and admin pings.
- `/language` opens language settings. The bot UI supports English and Ukrainian.
- `/access` opens log and access settings: moderation log channel, admin notification channel, admin ping role, and manager roles.
- `/status` shows the current configuration summary. On a fresh install it is useful for checking defaults before setup is completed.
- `/help` opens interactive help with topic selection.

## Moderation

- `/unmute user:<member> reason:<optional>` removes an active Discord timeout from a member. The optional reason is used for audit-log context.
- `/strikes user:<optional>` shows anti-spam strike counts. Without `user`, it shows the current caller's strike information where applicable.
- `/clearstrikes user:<member>` clears stored anti-spam strike history for one member.

## Testing and Profiles

- `/dryrun enabled:true|false` toggles dry-run mode. When enabled, the bot logs detections and review reports but does not apply punishments.
- `/preset name:<small-server|community|strict|paranoid>` applies a predefined configuration profile:
  - `small-server`: forgiving defaults for low-volume communities.
  - `community`: balanced defaults for active public servers.
  - `strict`: faster punishment for servers that often receive spam.
  - `paranoid`: aggressive detection for high-risk periods; test with dry-run first.
- `/threat` shows the current dynamic threat level: `Normal`, `Suspicious`, `UnderAttack`, or `Critical`.

## Config and Domains

- `/backupconfig` creates a local backup of the current guild settings.
- `/exportconfig` exports guild settings as a JSON file.
- `/importconfig file:<json> json:<optional>` imports guild settings from a JSON attachment or inline JSON text. Import accepts files up to 256 KB.
- `/blacklistdomain domain:<domain>` adds a suspicious or scam domain to the guild blacklist. Use only the domain name, for example `example.com`.
- `/removedomain domain:<domain>` removes a domain from the guild blacklist.

## Visibility

Commands are Discord administrator-only by default. Set `Access:CommandVisibility` to `VisibleWithRuntimeChecks` if manager roles without Administrator permission must see commands.

Before setup is completed, this branch allows the server owner and Discord administrators to run setup. After setup, the server owner, Discord administrators when enabled, and configured manager roles can manage the bot.
