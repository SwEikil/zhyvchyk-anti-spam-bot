# Docker Hosting

## Requirements

- Docker
- Docker Compose
- A Discord bot token

## Start

Create `.env`:

```bash
DISCORD_BOT_TOKEN=your-token-here
```

Start the bot:

```bash
docker compose up -d --build
```

View logs:

```bash
docker compose logs -f
```

Stop:

```bash
docker compose down
```

Update:

```bash
git pull
docker compose up -d --build
```

## Data

The compose file mounts:

```text
./data:/app/data
```

This stores guild settings, moderation logs, tempban state, lockdown recovery state, strikes, and backups.

Back up `data/` if you need persistence.
