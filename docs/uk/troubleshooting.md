# Troubleshooting

## Bot token is missing

Задайте одне з:

- `Bot:Token` у `appsettings.json`
- `ANTISPAM_Bot__Token`
- `DISCORD_BOT_TOKEN` у `.env` для Docker

## Commands не видно

- Reinvite bot зі scope `applications.commands`.
- Зачекайте guild command registration.
- Перевірте `Access:CommandVisibility`.
- За замовчуванням commands бачать тільки Discord administrators.

## Bot не читає message content

Увімкніть Message Content Intent у Discord Developer Portal.

## Bot не timeout або ban

- Перевірте Discord permissions.
- Перемістіть bot role вище target user's role.
- Перевірте ignored roles/users і admin exemptions.

## Lockdown не застосовується

Боту потрібен Manage Channels у кожному target channel.

## Import fails

- Використовуйте valid JSON з `/exportconfig`.
- Attachment має бути менше 256 KB.
- Не використовуйте absolute або parent paths у log/backup directories.

## Docker command missing

Спочатку встановіть Docker і Docker Compose на host.
