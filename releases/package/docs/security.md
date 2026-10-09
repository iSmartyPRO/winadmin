# Безопасность — кратко

## Вход в веб-интерфейс

1. Создайте пользователя (один раз):
   ```powershell
   .\WinAdmin.exe user add --login admin --password "<пароль>" --scopes admin
   ```
2. Войдите на странице логина логином и паролем.

## API-ключи

- Альтернатива JWT — заголовок `X-API-Key: sp_<секрет>`.
- Стартовый ключ: `bootstrap-key.txt` (создаётся при первом запуске, если ключей нет).
- **Удалите** `bootstrap-key.txt` после копирования.

## Важные переменные окружения

| Переменная | Назначение |
|---|---|
| `WinAdmin__DatabasePath` | Путь к SQLite (`WinAdmin.db`) |
| `WinAdmin__Jwt__Secret` | Постоянный JWT-секрет (без него сессии сбрасываются при рестарте) |
| `WinAdmin__BootstrapKey` | Задать свой bootstrap-ключ заранее (опционально) |

Пример (машина, от администратора):

```powershell
[Environment]::SetEnvironmentVariable("WinAdmin__DatabasePath", "C:\ProgramData\WinAdmin\WinAdmin.db", "Machine")
[Environment]::SetEnvironmentVariable("WinAdmin__Jwt__Secret", "<длинная-случайная-строка>", "Machine")
```

## Scopes (для API-ключей и пользователей)

| Scope | Доступ |
|---|---|
| `system.read` | Система, метрики |
| `disks.read` | Диски |
| `services.read` / `services.manage` | Службы |
| `processes.read` / `processes.manage` | Процессы |
| `printers.read` / `printers.manage` | Принтеры |
| `power.manage` | Перезагрузка/выключение |
| `eventlogs.read` | Журналы Windows |
| `admin` | Всё + ключи, пользователи, аудит |

## Аудит

Все управляющие действия пишутся в журнал аудита (UI: **Аудит**, API: `GET /api/v1/audit`).

## Рекомендации для сервера

- Запуск под учётной записью с правами локального администратора.
- Задать `WinAdmin__Jwt__Secret` до продакшн-использования.
- Хранить БД в `C:\ProgramData\WinAdmin\`, не в папке приложения.
- Оставить режим «только этот компьютер» либо включить режим «Сеть» только для нужных подсетей (`WinAdmin.exe network set` или **Настройки → Сеть**). Правило брандмауэра WinAdmin создаёт сам.
- Служба при старте ограничивает права: папка приложения — запись только Администраторы/SYSTEM, `C:\ProgramData\WinAdmin` — доступ только Администраторы/SYSTEM.
- Использовать HTTPS за reverse proxy или IIS, если доступ извне.
