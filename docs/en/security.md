# Security Guide

## Token Safety

- Never commit `.env` or `appsettings.json`.
- If a token is exposed, reset it in the Discord Developer Portal.
- Use environment variables on production hosts.

## Data Safety

The bot stores runtime data in `data/`:

- Guild settings
- Strikes
- Tempban state
- Lockdown recovery state
- Moderation logs
- Backups

Keep this directory private and back it up if needed.

## Safe Rollout

1. Start with dry-run mode.
2. Configure a private moderation log channel.
3. Test with a non-admin account.
4. Enable punishments after confirming logs.
5. Use stricter presets only after testing.

## Import Safety

Only import configs from trusted admins. The bot rejects unsafe paths, but imported thresholds can still make moderation too strict.
