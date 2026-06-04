# Contributing

Thanks for helping improve Zhyvchyk Anti-Spam Bot. Keep changes focused, documented, and safe for self-hosted server owners.

## Development Setup

1. Install the .NET 8 SDK.
2. Clone the repository.
3. Copy `appsettings.json.example` to `appsettings.json`.
4. Add your Discord bot token locally or use `ANTISPAM_Bot__Token`.
5. Run:

```bash
dotnet restore
dotnet build
```

Use a test Discord server for bot changes. Do not test risky moderation settings on a production community first.

## Pull Requests

- Keep code changes scoped to one behavior or feature.
- Update English and Ukrainian documentation together when user-facing behavior changes.
- Update command docs when slash commands, options, permissions, or visibility rules change.
- Update configuration docs when `GuildSettings` changes.
- Prefer dry-run testing for detection or punishment changes.
- Do not commit `.env`, `appsettings.json`, `data/`, logs, backups, or Discord tokens.

## Documentation

Documentation should be clear for non-technical Discord administrators. Explain what a setting does, when to change it, and what risk it carries. Keep `README.md` and `README.uk.md` aligned.

## Security

Report sensitive issues privately when possible. Do not open public issues containing bot tokens, private server data, moderation logs, or exploit details that could be abused immediately.
