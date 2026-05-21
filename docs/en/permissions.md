# Discord Permissions

Recommended invite permissions integer:

```text
1101659187382
```

Invite URL:

```text
https://discord.com/oauth2/authorize?client_id=YOUR_CLIENT_ID&permissions=1101659187382&scope=bot%20applications.commands
```

## Why These Permissions Are Needed

- View Channels: read monitored channels.
- Send Messages: send setup replies and moderation logs.
- Manage Messages: delete spam.
- Moderate Members: timeout users.
- Ban Members: temporary bans.
- Manage Channels: apply and restore lockdown slowmode/channel locks.
- View Audit Log: anti-nuke monitoring.
- Use Slash Commands: Discord command UI.

## Role Position

The bot role must be above roles/users it needs to timeout or ban.
