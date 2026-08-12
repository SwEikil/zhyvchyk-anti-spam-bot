# Встановлення

## 1. Створіть Discord Application

1. Відкрийте Discord Developer Portal.
2. Створіть нову application.
3. Перейдіть на сторінку Bot.
4. Створіть або reset bot token.
5. Зберігайте token тільки локально. Ніколи не комітьте його.

## 2. Увімкніть Intents

Увімкніть:

- Server Members Intent
- Message Content Intent

Без цих intents user risk checks і message-content spam detection не працюватимуть коректно.

## 3. Запросіть бота

Замініть `YOUR_CLIENT_ID` на client ID вашої application:

```text
https://discord.com/oauth2/authorize?client_id=YOUR_CLIENT_ID&permissions=1101659187382&scope=bot%20applications.commands
```

## 4. Налаштуйте token

Варіант A, local config:

```bash
cp appsettings.json.example appsettings.json
```

Відредагуйте `appsettings.json` і задайте `Bot:Token`.

Варіант B, environment variable:

```bash
export ANTISPAM_Bot__Token="your-token-here"
```

Варіант C, Docker `.env`:

```bash
DISCORD_BOT_TOKEN=your-token-here
```

## 5. Запустіть бота

```bash
dotnet restore
dotnet run
```

## 6. Перше налаштування сервера

У Discord запустіть:

```text
/setup
```

До завершення setup у цій гілці доступ має server owner або Discord administrator. Після setup ботом можуть керувати administrators і налаштовані manager roles залежно від command visibility.

Після setup запустіть `/status`, щоб перевірити активні defaults, а потім використовуйте `/antispam` для control panel. Детальне пояснення всіх параметрів є в [Налаштуваннях](configuration.md), а повний список slash-команд - у [Командах](commands.md).
