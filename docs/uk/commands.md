# Команди

## Setup і UI

- `/setup` відкриває guided setup.
- `/antispam` відкриває main dashboard.
- `/language` відкриває language settings.
- `/access` відкриває access/log settings.
- `/status` показує current configuration.
- `/help` відкриває interactive help.

## Moderation

- `/unmute user:<member> reason:<optional>` знімає timeout.
- `/strikes user:<optional>` показує anti-spam strikes.
- `/clearstrikes user:<member>` скидає strikes.

## Testing і Profiles

- `/dryrun enabled:true|false` логує detections без punishment.
- `/preset name:<small-server|community|strict|paranoid>` застосовує preset.
- `/threat` показує current threat level.

## Config і Domains

- `/backupconfig` створює local backup.
- `/exportconfig` експортує guild settings.
- `/importconfig file:<json>` імпортує guild settings.
- `/blacklistdomain domain:<domain>` додає domain blacklist entry.
- `/removedomain domain:<domain>` видаляє blacklist entry.

## Visibility

За замовчуванням команди бачать тільки Discord administrators. Поставте `Access:CommandVisibility` у `VisibleWithRuntimeChecks`, якщо manager roles без Administrator permission повинні бачити commands.
