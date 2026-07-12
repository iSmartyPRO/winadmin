# Скрытие системных учётных записей в журнале «Авторизация»

**Дата:** 2026-07-12
**Статус:** утверждено

## Проблема

Пресет «Авторизация» (`EVENT_LOG_PRESETS.auth`, журнал `Security`, события 4624/4625/4634/4647/4648/4672/4720/4722/4725/4726/4738/4767/4776) в основном показывает служебные входы под встроенными учётками (SYSTEM и т.п.). Реальные пользовательские авторизации теряются в этом шуме.

## Область действия

Изменение затрагивает **только пресет «Авторизация»** (`presetKey === 'auth'`). Остальные пресеты (Система, Приложения, PowerShell, Установка ПО, произвольный журнал) не меняются — колонка «Пользователь» там тоже есть, но фильтр по системным учёткам для них не нужен.

## Критерий «системная учётная запись» — по SID, не по имени

**Важно:** `TargetUserName`/`SubjectUserName` в XML события — это уже переведённое на язык интерфейса Windows значение (LSA резолвит SID в читаемое имя на языке той машины, где произошло событие). Поэтому сравнение с литералами вроде `SYSTEM`/`СИСТЕМА` работало бы только для одного языка Windows и рассинхронизировалось бы на других. Правильный language-independent идентификатор — **SID** (`S-1-5-18` и т.п.), он не переводится и одинаков на любой локали.

Для событий безопасности рядом с `TargetUserName` всегда идёт парное поле `TargetUserSid`, аналогично `SubjectUserName`/`SubjectUserSid`. Используем эту пару вместо одного лишь имени.

- `EventLogQueryHelpers.ExtractUserInfo(string recordXml)` — заменяет текущий `ExtractUserNameFromXml`, возвращает `(string? Name, string? Sid)`. Перебирает поля в порядке `TargetUserName`+`TargetUserSid` → `SubjectUserName`+`SubjectUserSid` → `AccountName`+`null` (для `AccountName` парного SID-поля в схеме нет). Останавливается на первом непустом и не `"-"` имени; `Sid` берётся из парного поля той же записи, если оно есть.
- `EventLogQueryHelpers.IsSystemAccount(string? sid, string? name)`:
  - если `sid` — один из встроенных: `S-1-5-18` (SYSTEM), `S-1-5-19` (LOCAL SERVICE), `S-1-5-20` (NETWORK SERVICE) → `true`;
  - иначе если `name` (без необязательного префикса `ДОМЕН\`) оканчивается на `$` → `true` (машинный аккаунт — это соглашение именования NetBIOS, не перевод, поэтому суффикс одинаков на любом языке);
  - иначе → `false`. Если SID недоступен (редкий случай событий с одним `AccountName`), по имени больше ничего не угадываем — это честнее, чем ловить локализованные строки вслепую.

Всё остальное (реальные доменные/локальные логины: `ivanov`, `Administrator`, `DOMAIN\petrov` и т.д.) считается пользовательской учёткой и не скрывается — независимо от языка Windows.

## Backend

- `EventLogQueryRequest` (Core/Models): новое поле `bool ExcludeSystemAccounts`.
- `EventLogsController.Query`: новый query-параметр `excludeSystemAccounts` (default `false`), пробрасывается в `EventLogQueryRequest`.
- `EventLogService.Query`: заменить вызов `ExtractUserNameFromXml` на `ExtractUserInfo`, получить `(name, sid)`. `user` (для отображения) — по-прежнему `name ?? TryTranslateSid(record.UserId)`. Если `request.ExcludeSystemAccounts && EventLogQueryHelpers.IsSystemAccount(sid, name)`, запись пропускается (`continue`), как и существующий фильтр по `request.User`. Порядок важен: фильтрация происходит **до** учёта `maxRecords`/`truncated`, как и остальные фильтры, чтобы лимит и флаг усечения считались по итоговой, а не по сырой выборке.

## Frontend

- `api/types.ts`: добавить `excludeSystemAccounts?: boolean` в параметры запроса.
- `api/client.ts`: пробросить параметр в `eventLogs.query`.
- `pages/EventLogs.tsx`:
  - Новое состояние `const [excludeSystem, setExcludeSystem] = useState(true)` (дефолт — включено).
  - Условный рендер `<Switch checked={excludeSystem} onChange={...} />` с подписью «Только пользователи» в строке фильтров — только когда `presetKey === 'auth'`.
  - При изменении переключателя — `refresh()`, аналогично остальным фильтрам.
  - В параметры `api.eventLogs.query(...)` добавляется `excludeSystemAccounts: presetKey === 'auth' ? excludeSystem : undefined`.

## Тестирование

`EventLogQueryHelpersTests.cs`: новые тест-кейсы.

`IsSystemAccount(sid, name)`:
- Положительные: `("S-1-5-18", "SYSTEM")`, `("S-1-5-18", "СИСТЕМА")` (SID решает независимо от имени), `("S-1-5-19", "Local Service")`, `("S-1-5-20", null)`, `(null, "DESKTOP-01$")`, `(null, "DOMAIN\PC$")`.
- Отрицательные: `(null, null)`, `(null, "")`, `(null, "Administrator")`, `(null, "ivanov")`, `("S-1-5-21-...-1001", "ivanov")` (обычный доменный пользователь — SID не входит в тройку встроенных).

`ExtractUserInfo`: проверить, что для XML с `TargetUserName`+`TargetUserSid` возвращается пара из этих полей, а для XML только с `AccountName` — `Sid == null`.

Ручная проверка: открыть пресет «Авторизация» на русской и (если есть доступ) на английской Windows, убедиться что по умолчанию встроенные учётки не отображаются одинаково в обоих случаях и счётчик записей корректен; выключить переключатель — системные записи должны появиться.

## Вне рамок

- Настройка списка системных учёток пользователем (захардкожено).
- Сохранение состояния переключателя между сессиями (каждый заход на пресет — дефолт `true`, как и остальные фильтры пресета).
- Изменение поведения на других пресетах.
