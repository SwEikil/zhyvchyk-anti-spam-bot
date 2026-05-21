# Discord Permissions

Рекомендований invite permissions integer:

```text
1101659187382
```

Invite URL:

```text
https://discord.com/oauth2/authorize?client_id=YOUR_CLIENT_ID&permissions=1101659187382&scope=bot%20applications.commands
```

## Навіщо потрібні ці permissions

- View Channels: читати monitored channels.
- Send Messages: надсилати setup replies і moderation logs.
- Manage Messages: видаляти spam.
- Moderate Members: timeout users.
- Ban Members: temporary bans.
- Manage Channels: застосовувати і відновлювати lockdown slowmode/channel locks.
- View Audit Log: anti-nuke monitoring.
- Use Slash Commands: Discord command UI.

## Role Position

Bot role має бути вище ролей/users, яких бот повинен timeout або ban.
