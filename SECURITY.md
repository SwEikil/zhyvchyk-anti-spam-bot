# Security Policy

## Supported Versions

This project is currently maintained from the default branch. Use the latest release or latest commit when self-hosting.

## Reporting a Vulnerability

Please do not open public issues containing:

- Discord bot tokens
- Private server logs
- Personal data
- Exploit details that would immediately endanger public servers

For now, report security concerns through a GitHub issue with sensitive details removed. If private security reporting is enabled for the repository, use GitHub's private vulnerability reporting feature.

## Host Operator Checklist

- Reset any token that was ever printed, committed, uploaded, or shared.
- Keep `.env`, `appsettings.json`, `data/`, and logs out of Git.
- Restrict SSH/VPS access.
- Keep Docker, .NET, and OS packages updated.
- Back up `data/` if you need persistent strikes, tempbans, backups, and guild settings.
- Use dry-run mode before enabling strict punishments.

## Українською

Не публікуйте tokens, private logs, personal data або exploit details у public issues.

Host operator повинен:

- Reset token, якщо він колись був надрукований, закомічений або поширений.
- Не комітити `.env`, `appsettings.json`, `data/` і logs.
- Обмежити SSH/VPS access.
- Оновлювати Docker, .NET і OS packages.
- Робити backup `data/`, якщо потрібна persistence.
- Використовувати dry-run перед суворими punishments.
