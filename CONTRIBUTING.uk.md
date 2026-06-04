# Contributing

Дякуємо за допомогу з Zhyvchyk Anti-Spam Bot. Тримайте changes сфокусованими, задокументованими й безпечними для self-hosted server owners.

## Development Setup

1. Встановіть .NET 8 SDK.
2. Склонуйте repository.
3. Скопіюйте `appsettings.json.example` у `appsettings.json`.
4. Додайте Discord bot token локально або використайте `ANTISPAM_Bot__Token`.
5. Запустіть:

```bash
dotnet restore
dotnet build
```

Для bot changes використовуйте test Discord server. Не тестуйте ризикові moderation settings одразу на production community.

## Pull Requests

- Тримайте code changes в межах однієї behavior або feature.
- Оновлюйте англійську й українську документацію разом, якщо змінюється user-facing behavior.
- Оновлюйте command docs, коли змінюються slash commands, options, permissions або visibility rules.
- Оновлюйте configuration docs, коли змінюється `GuildSettings`.
- Для detection або punishment changes спершу використовуйте dry-run testing.
- Не комітьте `.env`, `appsettings.json`, `data/`, logs, backups або Discord tokens.

## Documentation

Документація має бути зрозумілою для non-technical Discord administrators. Пояснюйте, що робить setting, коли його варто змінювати і який risk воно несе. Тримайте `README.md` і `README.uk.md` синхронними.

## Security

Sensitive issues за можливості повідомляйте приватно. Не відкривайте public issues з bot tokens, private server data, moderation logs або exploit details, які можна одразу використати для abuse.
