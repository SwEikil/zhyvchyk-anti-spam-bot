# VPS Hosting

## Рекомендований варіант

Використовуйте Docker Compose на Linux VPS.

Базовий flow:

```bash
sudo apt update
sudo apt install docker.io docker-compose-plugin git
git clone https://github.com/SwEikil/zhyvchyk-anti-spam-bot.git
cd zhyvchyk-anti-spam-bot
```

Створіть `.env`:

```bash
DISCORD_BOT_TOKEN=your-token-here
```

Запуск:

```bash
docker compose up -d --build
```

## Operational checklist

- Обмежте SSH access.
- Оновлюйте OS.
- Робіть backup `data/`.
- Reset token, якщо він засвітився.
- Для debug використовуйте `docker compose logs -f`.
- Перед суворими punishments використовуйте dry-run.
