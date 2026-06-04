# Roadmap

This roadmap tracks likely project directions. It is not a release promise; priorities can change when Discord APIs, moderation needs, or security risks change.

## Near Term

- Add focused tests for access-control behavior, especially first setup, administrator access, and manager roles.
- Improve moderation review ergonomics with clearer action outcomes and fewer duplicate reports during large attacks.
- Expand presets with documented tradeoffs for small, public, strict, and emergency modes.
- Add more examples for `/importconfig` and `/exportconfig` workflows.
- Keep English and Ukrainian documentation in sync for every user-facing feature.

## Detection

- Tune similarity scoring for mixed-language spam, emoji-heavy spam, and messages with invisible Unicode characters.
- Improve attachment repeat detection and optional hashing documentation.
- Add more scam-link examples covering lookalike domains, confusables, and leetspeak.
- Review default thresholds against real-world server volume.

## Operations

- Add sample systemd unit files for VPS hosting.
- Document backup restore flows in more detail.
- Add deployment notes for log rotation and data-directory permissions.
- Consider health checks for container deployments.

## Security

- Keep token-handling guidance prominent in setup docs.
- Add more anti-nuke audit-log scenarios.
- Review import limits and validation whenever the settings schema changes.
- Keep dry-run guidance visible before strict or paranoid profiles.

## Not Planned

- A centralized hosted bot service is not planned. Zhyvchyk is designed as a self-hosted bot.
- Cloud storage of server moderation logs is not planned by default.
- Cross-server data sharing is not planned by default.
