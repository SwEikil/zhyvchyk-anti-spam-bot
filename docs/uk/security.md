# Security Guide

## Token Safety

- Ніколи не комітьте `.env` або `appsettings.json`.
- Якщо token засвітився, reset token у Discord Developer Portal.
- На production hosts використовуйте environment variables.

## Data Safety

Бот зберігає runtime data у `data/`:

- Guild settings
- Strikes
- Tempban state
- Lockdown recovery state
- Moderation logs
- Backups

Тримайте цю директорію приватною і робіть backup за потреби.

## Safe Rollout

1. Почніть із dry-run mode.
2. Налаштуйте private moderation log channel.
3. Протестуйте non-admin account.
4. Увімкніть punishments після перевірки logs.
5. Використовуйте stricter presets тільки після тестування.

## Import Safety

Імпортуйте configs тільки від trusted admins. Бот відхиляє unsafe paths, але imported thresholds все одно можуть зробити moderation занадто суворою.
