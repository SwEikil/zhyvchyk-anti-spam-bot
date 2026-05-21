# Commands

## Setup and UI

- `/setup` opens guided setup.
- `/antispam` opens the main dashboard.
- `/language` opens language settings.
- `/access` opens access/log settings.
- `/status` shows current configuration.
- `/help` opens interactive help.

## Moderation

- `/unmute user:<member> reason:<optional>` removes a timeout.
- `/strikes user:<optional>` shows anti-spam strikes.
- `/clearstrikes user:<member>` resets strikes.

## Testing and Profiles

- `/dryrun enabled:true|false` logs detections without punishment.
- `/preset name:<small-server|community|strict|paranoid>` applies a preset.
- `/threat` shows current threat level.

## Config and Domains

- `/backupconfig` creates a local backup.
- `/exportconfig` exports guild settings.
- `/importconfig file:<json>` imports guild settings.
- `/blacklistdomain domain:<domain>` adds a domain blacklist entry.
- `/removedomain domain:<domain>` removes a blacklist entry.

## Visibility

Commands are Discord administrator-only by default. Set `Access:CommandVisibility` to `VisibleWithRuntimeChecks` if manager roles without Administrator permission must see commands.
