# VPS Hosting

## Recommended Approach

Use Docker Compose on a small Linux VPS.

Basic flow:

```bash
sudo apt update
sudo apt install docker.io docker-compose-plugin git
git clone https://github.com/SwEikil/zhyvchyk-anti-spam-bot.git
cd zhyvchyk-anti-spam-bot
cp .env.example .env
```

If `.env.example` is not present, create `.env` manually:

```bash
DISCORD_BOT_TOKEN=your-token-here
```

Start:

```bash
docker compose up -d --build
```

## Operational Checklist

- Restrict SSH access.
- Keep the OS updated.
- Back up `data/`.
- Rotate the token if it is ever exposed.
- Use `docker compose logs -f` for debugging.
- Use dry-run mode before strict punishments.
