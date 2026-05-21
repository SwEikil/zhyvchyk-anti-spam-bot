# Features

## Spam Detection

The bot tracks recent messages per user and checks for repeated content, similar content, fast multi-channel posting, suspicious links, mass mentions, and repeated attachments.

## Normalization

Messages are normalized by lowercasing, replacing URLs and mentions, stripping extra whitespace, and optionally removing punctuation, emoji, invisible characters, and Unicode tricks.

## Scam Links

The bot checks protected domains and blacklist entries. It can detect lookalike domains using fuzzy comparison, Unicode confusables, and leetspeak.

Examples:

- `disk0rd.com`
- `diskоrd.com` with Cyrillic `о`
- `discord.gg/path`

## Punishment

The bot supports timeout escalation and tempban escalation. Tempban state is stored locally so expired bans can be removed after restart.

## Raid Lockdown

During attacks, the bot can enable slowmode and optionally deny sending messages for `@everyone`. Previous channel state is stored and restored after release.

## Anti-Nuke

The bot watches destructive audit-log actions and can trigger emergency lockdown.
