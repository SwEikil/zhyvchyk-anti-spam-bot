# Setup Guide

## 1. Create a Discord Application

1. Open the Discord Developer Portal.
2. Create a new application.
3. Open the Bot page.
4. Create or reset the bot token.
5. Store the token locally only. Never commit it.

## 2. Enable Intents

Enable:

- Server Members Intent
- Message Content Intent

Without these intents, user risk checks and message-content spam detection will not work correctly.

## 3. Invite the Bot

Replace `YOUR_CLIENT_ID` with your application client ID:

```text
https://discord.com/oauth2/authorize?client_id=YOUR_CLIENT_ID&permissions=1101659187382&scope=bot%20applications.commands
```

## 4. Configure the Token

Option A, local config:

```bash
cp appsettings.json.example appsettings.json
```

Edit `appsettings.json` and set `Bot:Token`.

Option B, environment variable:

```bash
export ANTISPAM_Bot__Token="your-token-here"
```

Option C, Docker `.env`:

```bash
DISCORD_BOT_TOKEN=your-token-here
```

## 5. Start the Bot

```bash
dotnet restore
dotnet run
```

## 6. First Server Setup

In Discord, run:

```text
/setup
```

The server owner and Discord administrators have access before setup is completed. After setup, administrators and configured manager roles can manage the bot depending on command visibility settings.
