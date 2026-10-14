# Импорт данных из портала Access и вывод Access из эксплуатации (часть 4 из 4)

**Дата:** 2026-10-14
**Статус:** реализация
**Основа:** ветка `feat/module-platform` (платформа модулей, роли, вход AD, модули `ad-users` / `ad-folders` — части 1–3)
**Источник данных:** PostgreSQL портала Access (`C:\dev\access`): `install/schema.sql`, `install/migrate_v2…v11.sql`, `App_Code/AppSettingsRepository.cs`, `SecureConfig.cs`, `RolePrincipalRepository.cs`, `ExcludedOuRepository.cs`, `TerminatedUserOuRepository.cs`, `AdRoleResolver.cs`, `AuthContext.cs` (`PortalUserRepository`), `AuditLog.cs`

## Цель

Одной командой перенести в WinAdmin настройки и права доступа портала Access, показать администратору, что перенесено, что нет и почему, и дать чек-лист отключения Access.

## Команда

```
WinAdmin.exe import access --connection "<строка Npgsql>" [--apply] [--audit] [--overwrite] [--import-service-password]
```

| Флаг | Смысл |
|---|---|
| (без `--apply`) | Только отчёт (dry run): в WinAdmin ничего не меняется, даже роли-шаблоны |
| `--apply` | Выполнить импорт |
| `--audit` | Перенести журнал `audit_log` |
| `--overwrite` | Заменять уже заданные в WinAdmin значения (иначе — «Конфликт») |
| `--import-service-password` | Перенести пароль служебной учётки (в Access хранится открытым текстом) |

Пароль PostgreSQL можно не писать в строку: если в `--connection` нет `Password`, берётся переменная окружения `WINADMIN_ACCESS_PG_PASSWORD`. Строка подключения в вывод не печатается (только хост/порт/база).

Повторный запуск идемпотентен: всё, что уже есть, — «Пропущено — уже есть»; журнал продолжается с последнего импортированного id.

## Сопоставление

| Access | WinAdmin | Правило |
|---|---|---|
| `app_settings.ldap_path` (`LDAP://OU=…,DC=…`, `LDAP://host/OU=…` или DN) | `AdStructureSettings.RootOu` | Не задан — записать; совпадает (без учёта регистра/пробелов) — пропуск; отличается — «Конфликт», замена только с `--overwrite`. DN без `OU=` в начале (корень домена) — «Не импортируется» (WinAdmin требует OU) |
| `ad_service_domain` + `ad_service_username` | `AdStructureSettings.WriteLogin` | Логин `ДОМЕН\имя` (как `SecureConfig.ApplyAdSettings`); те же правила конфликта |
| `ad_service_password` | пароль учётки записи (`IAdStructureStore.SaveAsync(.., newPassword)` — шифруется) | Только с `--import-service-password` и только если итоговый `WriteLogin` — та же учётка. Заглушки (`ЗАМЕНИТЕ…`) и пустые не переносятся. Сравнение с текущим — по расшифрованному значению. **Значение никогда не печатается** |
| `app_settings.fired_users_group` | `AdUsersSettings.FiredGroup` | Правила конфликта как выше |
| `excluded_ous.ou_name` | `AdStructureSettings.HiddenOus` | Объединение (имена OU первого уровня — тот же смысл, что в Access) |
| `terminated_user_ous` с `is_deactivation_default` | `AdUsersSettings.TerminatedOuDn` | Берётся `ou_dn`; без DN — «Не импортируется». Остальные OU уволенных — «Не импортируется» (в WinAdmin одна OU уволенных) |
| `role_principals` (роль, user/group, DN) | назначения ролей по SID | DN → SID через каталог (`IDirectoryService.FindByDnAsync`, новый метод); не найден — «Не найдено в AD». Тип назначения — по объекту каталога (`AdUser` / `AdGroup`) |
| `portal_users` (только `is_active`) | назначения ролей пользователю AD | sAMAccountName → SID (`FindUserAsync`) |
| `local_users` | — | Не импортируются (другой формат хеша) — в отчёте список с советом `WinAdmin.exe user add` |
| `audit_log` | аудит WinAdmin | Только с `--audit` (см. ниже) |
| `project_metadata`, `exchange_url`, `api_settings`/`yandex_maps_api_key`, рассылки проектов (`mail_group_*`), `domain` | — | «Не импортируется», строка в отчёте |

Роли Access → роли WinAdmin:

| Access | WinAdmin |
|---|---|
| `super_admin` | «Администратор» (встроенная) |
| `admin` | «AD: администраторы пользователей» + «AD: администраторы папок» |
| `hr` | «AD: отдел кадров» |

Роли-шаблоны создаются импортом, если их нет (без включения модулей). Создание шаблонов вынесено из `OnFirstEnabledAsync` модулей в общий `AdRoleTemplates.EnsureAsync` — модули вызывают его же. В отчёте — подсказка включить модули «Пользователи AD» и «Папки».

## Журнал (`--audit`)

- `Action = "access." + action`, `Timestamp = created_at`, `Actor = actor_sam`, `Target = target_id`, `Success = true`.
- `Details` — JSON из `details` + `accessId` и `targetType`. **Ключи, содержащие `password` (без учёта регистра), удаляются** — в Access `user.account.activate` писал новый пароль пользователя в журнал (known-issues №4).
- Порциями по 500 записей в порядке `id`; каждая порция и отметка «последний id» (`PlatformSettings`, ключ `import.access.audit`, `{ "lastId": N }`) сохраняются в одной транзакции — прерванный импорт продолжается без дублей.
- Пишется напрямую через `WinAdminDbContext` (не `IAuditService.WriteAsync` — он сохраняет по одной записи).

## Отчёт

Таблица: **что / откуда / куда / статус / примечание**. Статусы: «Создано», «Будет создано», «Пропущено — уже есть», «Конфликт», «Не найдено в AD», «Не импортируется». Итог — счётчики по статусам. С `--apply` в конце пишется аудит WinAdmin `import.access` (actor `cli`) с итогом. Код выхода: 0 — успех (конфликты не ошибка), 1 — ошибка подключения/каталога.

## Устройство

- `WinAdmin.Core/Import`: `IAccessSource` (чтение Access), модели снимка, `AccessImportOptions`, `AccessImportReport`.
- `WinAdmin.Infrastructure/Import/NpgsqlAccessSource` — чтение через Npgsql; терпит старые схемы (нет `role_principals`, нет колонок v5–v11 — значения пустые).
- `WinAdmin.Infrastructure/Import/AccessImporter` — сначала строит план (все строки отчёта и действия), затем, только с `--apply`, выполняет действия. Работает с `IAccessSource`, `IAdStructureStore`, `IModuleRegistry`, `IRoleService`, `IDirectoryService`, `WinAdminDbContext`, `PermissionCatalog`. Тестируется на фейке `IAccessSource` + SQLite в памяти.
- Каталог AD недоступен — импорт останавливается до применения изменений (код 1), а не помечает всех «Не найдено в AD».
- CLI `import access` (`Cli/ImportCommands.cs`) — собирает DI как служба (БД, ключ шифрования, каталог, модули).
- Интеграционный тест чтения (`WINADMIN_TEST_POSTGRES`): временная БД `access_import_test_<guid>`, к ней `schema.sql` + миграции Access (копии в ресурсах тестов), строки, чтение `NpgsqlAccessSource`, удаление БД.

## Решения и цена ошибки

| Решение | Почему | Цена ошибки |
|---|---|---|
| Импорт — при **остановленной** службе WinAdmin (или перезапуск после) | Служба кэширует настройки модулей и структуру AD в памяти; сохранение из UI старого кэша затёрло бы импорт | Импортированные настройки не видны до перезапуска / затираются при сохранении из UI |
| Импорт создаёт строку модуля `ad-users` при записи настроек, поэтому `OnFirstEnabledAsync` потом не сработает — шаблоны создаёт сам импорт | Иначе после импорта не было бы шаблонов | Нет |
| Пароли из `audit_log.details` вырезаются | Открытые пароли пользователей в журнале | Потеря этих полей (умышленно) |
| Пароль adService — только по флагу и только к той же учётке | Пароль без своей учётки бесполезен и опасен | Нужно ввести пароль вручную в «Настройки → Active Directory» |
| `portal_users` даёт роль в дополнение к ролям из групп (в Access override заменял роль) | В WinAdmin роли суммируются, «понижающего» назначения нет | Пользователь сохраняет более широкие права из группы — в отчёте подсказка проверить |
| Корневая OU проверяется по правилам `AdStructureSettings.Normalize` ещё на этапе плана | Неверный DN не должен ронять `--apply` на полпути | Нет |
| Неактивные `portal_users`, OU уволенных без признака «по умолчанию» — не импортируются | Нет соответствия в WinAdmin | Нужно настроить вручную (видно в отчёте) |

## Вывод Access из эксплуатации (чек-лист, в документации)

1. Остановить службу WinAdmin, `import access` без `--apply`, разобрать отчёт (конфликты, «Не найдено в AD»).
2. `--apply` (при необходимости `--audit`, `--import-service-password`), запустить службу.
3. Включить модули «Пользователи AD» и «Папки», проверить окружение.
4. Проверить вход: администратор, сотрудник кадров (группа), пользователь из `portal_users`; роли в «Роли».
5. Отключить сайт Access в IIS (остановить сайт и пул), убрать DNS/ссылки.
6. Сделать бэкап PostgreSQL Access (`pg_dump -Fc`).
7. Удалить пароль adService из БД Access: `UPDATE app_settings SET ad_service_password = '' WHERE id = 1;` и сменить пароль учётки adService в AD, если он мог утечь.
