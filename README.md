# LocalPlanner

Windows-first desktop MVP приватного local-first календаря и планировщика.

## Текущий desktop-объём

- `WPF` desktop-приложение в `src/LocalPlanner.Desktop`
- Локальное хранение событий в `SQLite`
- Создание, редактирование, список и soft-delete событий
- Поля события: `title`, `description`, `start/end`, `timezone`, `all-day`, `RRULE`
- Solution и CI зафиксированы на `.NET 6`

## Структура проекта

```text
src/
  LocalPlanner.Desktop/
```

## Запуск

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.dotnet"
dotnet restore LocalPlanner.sln --packages .\.nuget\packages
dotnet build LocalPlanner.sln --configuration Debug --no-restore
```

## Локальные данные

Desktop-приложение хранит базу данных здесь:

```text
%LOCALAPPDATA%\LocalPlanner\localplanner.db
```

## Текущие ограничения

- Пока нет LAN-синхронизации
- Пока нет записей operation log
- Пока нет UI для pairing/auth
- Повторение хранится как сырой текст `RRULE` без продвинутого редактора правил

## Документация

Документация проекта ведётся на русском языке. Технические идентификаторы, имена классов, поля и команды остаются в исходном виде, если перевод ухудшает точность.
