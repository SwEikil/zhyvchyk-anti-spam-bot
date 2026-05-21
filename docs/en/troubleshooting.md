# Troubleshooting

## Bot token is missing

Set one of:

- `Bot:Token` in `appsettings.json`
- `ANTISPAM_Bot__Token`
- `DISCORD_BOT_TOKEN` in `.env` for Docker

## Commands are not visible

- Reinvite the bot with `applications.commands` scope.
- Wait for guild command registration.
- Check `Access:CommandVisibility`.
- By default, commands are visible only to Discord administrators.

## Bot does not read message content

Enable Message Content Intent in the Discord Developer Portal.

## Bot does not timeout or ban

- Check Discord permissions.
- Move the bot role above the target user's role.
- Check ignored roles/users and admin exemptions.

## Lockdown does not apply

The bot needs Manage Channels in each target channel.

## Import fails

- Use valid JSON exported by `/exportconfig`.
- Keep attachments under 256 KB.
- Do not use absolute or parent paths in log/backup directories.

## Docker command missing

Install Docker and Docker Compose on the host first.
