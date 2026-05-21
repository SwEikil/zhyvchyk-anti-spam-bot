# Privacy Policy

Last updated: May 21, 2026

## English

Zhyvchyk Anti-Spam Bot is a self-hosted open-source Discord bot. The project author does not operate a centralized hosted service and does not receive data from your Discord server unless you explicitly send it to the author.

### Data Processed by the Bot

Depending on your configuration, your self-hosted bot instance may process:

- Discord user IDs, usernames, role IDs, channel IDs, guild IDs, and message IDs
- Message content needed for anti-spam detection
- URLs/domains, mentions, attachment metadata, and timestamps
- Moderation actions such as timeouts, temporary bans, deleted message counts, and reasons
- Guild configuration and local backup files

### Where Data Is Stored

Data is stored locally on the machine where you host the bot, usually under the `data/` directory. This can include guild settings, strike history, temporary ban state, lockdown recovery state, backups, and moderation logs.

### Data Sharing

This project does not send your server data to the project author. Discord still receives API requests required for normal bot operation. If you host the bot on a third-party VPS or platform, that provider may have access according to its own policies.

### Data Retention

Retention is controlled by the server owner or host operator. You can delete local bot data by stopping the bot and removing the relevant files from `data/`.

### Your Responsibilities

As the host operator, you are responsible for:

- Protecting your Discord bot token
- Restricting access to the host machine
- Backing up or deleting local data as needed
- Complying with Discord Terms of Service and applicable laws

### Contact

Open a GitHub issue in the project repository for privacy-related project questions. Do not post tokens, private logs, or personal data in public issues.

## Українською

Zhyvchyk Anti-Spam Bot — це self-hosted open-source Discord бот. Автор проекту не керує централізованим hosted-сервісом і не отримує дані з вашого Discord сервера, якщо ви самі їх не надішлете.

### Які дані обробляє бот

Залежно від налаштувань, ваш self-hosted інстанс може обробляти:

- Discord user ID, username, role ID, channel ID, guild ID і message ID
- Вміст повідомлень, потрібний для anti-spam detection
- URL/domains, mentions, metadata вкладень і timestamps
- Moderation actions: timeouts, temporary bans, кількість видалених повідомлень і причини
- Guild configuration і локальні backup files

### Де зберігаються дані

Дані зберігаються локально на машині, де ви запускаєте бота, зазвичай у директорії `data/`. Там можуть бути guild settings, strike history, temporary ban state, lockdown recovery state, backups і moderation logs.

### Передача даних

Цей проект не надсилає дані вашого сервера автору проекту. Discord отримує API-запити, потрібні для нормальної роботи бота. Якщо ви запускаєте бота на VPS або third-party платформі, цей провайдер може мати доступ згідно зі своїми правилами.

### Зберігання даних

Термін зберігання контролює власник сервера або host operator. Щоб видалити локальні дані, зупиніть бота і видаліть потрібні файли з `data/`.

### Ваша відповідальність

Host operator відповідає за:

- Захист Discord bot token
- Обмеження доступу до host machine
- Backup або видалення локальних даних
- Дотримання Discord Terms of Service і застосовних законів

### Контакт

Для privacy-related питань відкрийте GitHub issue. Не публікуйте tokens, приватні logs або personal data у public issues.
