# Zhyvchyk Anti-Spam Bot

Self-hosted Discord бот для антиспаму, raid-захисту та базового anti-nuke моніторингу. Написаний на C#, .NET 8 і Discord.Net. Налаштування зберігаються локально у JSON, а керування ботом відбувається через Discord slash-команди та інтерактивне меню `/antispam`.

English documentation: [README.md](README.md)

## Можливості

- Виявлення spam burst у кількох каналах
- Порівняння схожих повідомлень після нормалізації
- Обробка emoji, пунктуації, невидимих символів і Unicode-трюків
- Scam link detection з blacklist, fuzzy domains, confusables і leetspeak перевірками
- Виявлення повторних вкладень і підозрілих розширень файлів
- User risk scoring для нових акаунтів і spam-after-join поведінки
- Admin review reports з цитатою видаленого повідомлення, reupload картинок і quick action buttons
- Миттєвий autoban за однаковий spam у кількох каналах за короткий проміжок
- Ескалація timeout/tempban
- Raid lockdown зі slowmode, опційним channel lock і recovery після рестарту
- Anti-nuke моніторинг audit log
- Dry-run режим для безпечного тестування
- Per-guild JSON налаштування, import/export, backups і локальні moderation logs
- Зручне інтерактивне меню `/antispam`
- Англійська та українська локалізація

## Швидкий старт

1. Встановіть [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Створіть Discord application і bot у Discord Developer Portal.
3. Увімкніть privileged intents:
   - Server Members Intent
   - Message Content Intent
4. Запросіть бота на сервер:

```text
https://discord.com/oauth2/authorize?client_id=YOUR_CLIENT_ID&permissions=1101659187382&scope=bot%20applications.commands
```

5. Скопіюйте приклад конфігу:

```bash
cp appsettings.json.example appsettings.json
```

6. Додайте token у `appsettings.json` або використайте environment variable:

```bash
export ANTISPAM_Bot__Token="your-token-here"
```

7. Запустіть бота:

```bash
dotnet restore
dotnet run
```

8. У Discord запустіть `/setup` від імені власника сервера.

## Docker

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

## Discord Permissions

Рекомендований permission integer:

```text
1101659187382
```

Він включає права, потрібні для читання повідомлень, видалення spam, timeout/tempban, lockdown, audit-log моніторингу, slash-команд і moderation logs.

## Документація

- [Встановлення](docs/uk/setup.md)
- [Docker hosting](docs/uk/docker.md)
- [VPS hosting](docs/uk/vps-hosting.md)
- [Налаштування](docs/uk/configuration.md)
- [Команди](docs/uk/commands.md)
- [Можливості](docs/uk/features.md)
- [Права Discord](docs/uk/permissions.md)
- [Troubleshooting](docs/uk/troubleshooting.md)
- [Security](docs/uk/security.md)

## Юридична інформація

- [License](LICENSE)
- [Privacy Policy](PRIVACY.md)
- [Terms of Service](TERMS.md)
- [Security Policy](SECURITY.md)

## Важливо про безпеку

- Ніколи не комітьте `.env`, `appsettings.json`, `data/` або moderation logs.
- Якщо token засвітився, одразу reset token у Discord Developer Portal.
- Перед суворими покараннями використовуйте dry-run.
- Це self-hosted проект. Ви контролюєте token, hosting, logs і data.

## License

Copyright (c) 2026 SwEikil.

Released under the MIT License. See [LICENSE](LICENSE).
