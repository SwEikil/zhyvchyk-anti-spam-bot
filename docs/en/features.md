# Features

## Spam Detection

The bot tracks recent messages per user and checks for repeated content, similar content, fast multi-channel posting, suspicious links, mass mentions, and repeated attachments.

Account age and recent-join risk are contextual only. They can enable safer strict monitoring and help confirm a real repeated pattern, but cannot independently delete a message, add a strike, punish a member, or raise raid threat. One ordinary message from a new member always passes.

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

## Admin Review

Moderation logs include a safe deleted-message quote, attachment summary, reuploaded images when possible, and quick action buttons for ban, long mute, tempban, and temp mute. Mentions and links from deleted messages are escaped in reports.

Authorized bot managers can mark one logged incident as a false positive. The bot reverses that incident's exact strike, removes only a matching timeout or ban when safe, and posts a clearly labeled bot representation of deleted content. Ordinary members never need buttons, prompts, commands, DMs, or other bot interaction.

Duplicate spam across several channels in a short window can trigger an immediate autoban.

## Raid Lockdown

During attacks, the bot can enable slowmode and optionally deny sending messages for `@everyone`. Previous channel state is stored and restored after release.

## Anti-Nuke

The bot watches destructive audit-log actions and can trigger emergency lockdown.
