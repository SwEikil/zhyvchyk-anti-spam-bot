# Configuration

Most server settings can be changed from `/antispam`.

## Core Sections

- Channels: choose monitored and ignored channels.
- Detection: time window, minimum spam count, similarity threshold, link/attachment thresholds.
- Punishment: timeout durations, tempban durations, cooldown, ban message deletion days.
- Raid Lockdown: slowmode, minimum hold, max duration, trigger threat level, release threat level.
- Scam Links: protected domains, blacklist, fuzzy threshold, Unicode tricks, leetspeak.
- User Risk: new account age, recent join window, punish score.
- Logs and Access: moderation log channel, admin notification channel, admin ping role, manager roles, ignored roles, whitelisted users.
- Language: English or Ukrainian.

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
