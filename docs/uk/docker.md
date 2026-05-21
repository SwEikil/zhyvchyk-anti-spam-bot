# Docker Hosting

## Вимоги

- Docker
- Docker Compose
- Discord bot token

## Запуск

Створіть `.env`:

```bash
DISCORD_BOT_TOKEN=your-token-here
```

Запустіть:

```bash
docker compose up -d --build
```

Логи:

```bash
docker compose logs -f
```

Зупинка:

```bash
docker compose down
```

Оновлення:

```bash
git pull
docker compose up -d --build
```

## Дані

Compose монтує:

```text
./data:/app/data
```

Там зберігаються guild settings, moderation logs, tempban state, lockdown recovery state, strikes і backups.

Робіть backup `data/`, якщо потрібна persistence.
