# Команди

Усі команди є Discord slash-командами. Більшість команд налаштування відповідає ephemeral-повідомленням, тому setup UI або export бачить тільки користувач, який запустив команду.

## Setup і UI

- `/setup` відкриває майстер першого налаштування. Basic setup ставить безпечні значення, Advanced setup дає одразу змінити пороги.
- `/antispam` відкриває головну панель для каналів, детекції, покарань, raid lockdown, scam-посилань, user risk, logs/access, dry-run, tempban і admin ping.
- `/language` відкриває налаштування мови. UI бота підтримує англійську та українську.
- `/access` відкриває налаштування логів і доступу: moderation log channel, admin notification channel, admin ping role і manager roles.
- `/status` показує короткий стан поточної конфігурації. Після свіжого встановлення ним зручно перевірити defaults до завершення setup.
- `/help` відкриває інтерактивну довідку з вибором теми.

## Moderation

- `/unmute user:<member> reason:<optional>` знімає активний Discord timeout з учасника. Необов'язковий reason використовується для audit-log контексту.
- `/strikes user:<optional>` показує кількість anti-spam strikes. Без `user` показує інформацію для поточного користувача, якщо вона є.
- `/clearstrikes user:<member>` очищає збережену strike-історію одного учасника.

## Testing і Profiles

- `/dryrun enabled:true|false` вмикає або вимикає dry-run. Коли режим увімкнений, бот логує detections і review reports, але не застосовує покарання.
- `/preset name:<small-server|community|strict|paranoid>` застосовує готовий профіль конфігурації:
  - `small-server`: м'які defaults для невеликих спільнот.
  - `community`: збалансовані defaults для активних публічних серверів.
  - `strict`: швидші покарання для серверів, які часто ловлять spam.
  - `paranoid`: агресивна детекція для ризикових періодів; спочатку тестуйте з dry-run.
- `/threat` показує поточний dynamic threat level: `Normal`, `Suspicious`, `UnderAttack` або `Critical`.

## Config і Domains

- `/backupconfig` створює локальний backup поточних guild settings.
- `/exportconfig` експортує guild settings у JSON-файл.
- `/importconfig file:<json> json:<optional>` імпортує guild settings з JSON attachment або inline JSON text. Import приймає файли до 256 KB.
- `/blacklistdomain domain:<domain>` додає підозрілий або scam domain у blacklist сервера. Передавайте тільки domain name, наприклад `example.com`.
- `/removedomain domain:<domain>` видаляє domain з blacklist.

## Visibility

За замовчуванням команди бачать тільки Discord administrators. Поставте `Access:CommandVisibility` у `VisibleWithRuntimeChecks`, якщо manager roles без Administrator permission повинні бачити commands.

До завершення setup у цій гілці запускати `/setup` може server owner або Discord administrator. Після setup ботом можуть керувати server owner, Discord administrators коли це дозволено, і налаштовані manager roles.
