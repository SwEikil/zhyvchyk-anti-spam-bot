# Можливості

## Spam Detection

Бот відстежує recent messages кожного user і перевіряє repeated content, similar content, fast multi-channel posting, suspicious links, mass mentions і repeated attachments.

## Normalization

Повідомлення нормалізуються: lowercase, replacement URLs і mentions, strip whitespace, optional punctuation/emoji/invisible characters/Unicode tricks.

## Scam Links

Бот перевіряє protected domains і blacklist entries. Він може ловити lookalike domains через fuzzy comparison, Unicode confusables і leetspeak.

Приклади:

- `disk0rd.com`
- `diskоrd.com` з кириличною `о`
- `discord.gg/path`

## Punishment

Бот підтримує timeout escalation і tempban escalation. Tempban state зберігається локально, щоб expired bans можна було зняти після restart.

## Admin Review

Moderation logs містять безпечну цитату видаленого повідомлення, summary вкладень, reupload картинок коли можливо, і quick action buttons для ban, long mute, tempban і temp mute. Mentions і links з видалених повідомлень екрануються у звітах.

Однаковий spam у кількох каналах за короткий проміжок може одразу запускати autoban.

## Raid Lockdown

Під час атаки бот може ввімкнути slowmode і опційно заборонити send messages для `@everyone`. Попередній channel state зберігається і відновлюється після release.

## Anti-Nuke

Бот дивиться destructive audit-log actions і може запускати emergency lockdown.
