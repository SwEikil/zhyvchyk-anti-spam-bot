# Налаштування

Більшість server settings змінюється через `/antispam`.

## Основні розділи

- Channels: monitored і ignored channels.
- Detection: time window, minimum spam count, similarity threshold, link/attachment thresholds.
- Punishment: timeout durations, tempban durations, cooldown, ban message deletion days.
- Raid Lockdown: slowmode, minimum hold, max duration, trigger threat level, release threat level.
- Scam Links: protected domains, blacklist, fuzzy threshold, Unicode tricks, leetspeak.
- User Risk: new account age, recent join window, punish score.
- Logs and Access: moderation log channel, notification channel, manager roles, ignored roles, whitelisted users.
- Language: English або Ukrainian.

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
