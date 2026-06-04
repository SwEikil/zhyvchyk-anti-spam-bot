# Roadmap

Цей roadmap описує ймовірні напрями розвитку проекту. Це не release promise: пріоритети можуть змінюватися через Discord APIs, moderation needs або security risks.

## Найближчі задачі

- Додати focused tests для access-control behavior, особливо first setup, administrator access і manager roles.
- Покращити moderation review UX: зрозуміліші action outcomes і менше duplicate reports під час великих атак.
- Розширити presets з описаними tradeoffs для small, public, strict і emergency modes.
- Додати більше прикладів для `/importconfig` і `/exportconfig` workflows.
- Тримати англійську й українську документацію синхронною для кожної user-facing feature.

## Detection

- Тюнити similarity scoring для mixed-language spam, emoji-heavy spam і messages з invisible Unicode characters.
- Покращити repeated attachment detection і документацію optional hashing.
- Додати більше scam-link examples для lookalike domains, confusables і leetspeak.
- Переглядати default thresholds на основі реального server volume.

## Operations

- Додати sample systemd unit files для VPS hosting.
- Детальніше описати backup restore flows.
- Додати deployment notes для log rotation і data-directory permissions.
- Розглянути health checks для container deployments.

## Security

- Тримати token-handling guidance помітним у setup docs.
- Додати більше anti-nuke audit-log scenarios.
- Переглядати import limits і validation щоразу, коли змінюється settings schema.
- Тримати dry-run guidance видимим перед strict або paranoid profiles.

## Не планується

- Centralized hosted bot service не планується. Zhyvchyk задуманий як self-hosted bot.
- Cloud storage для server moderation logs не планується за замовчуванням.
- Cross-server data sharing не планується за замовчуванням.
