# Роли сервера (Server Roles) на Windows Server

**Дата:** 2026-07-13  
**Статус:** утверждено

## Проблема

На Windows Server администратору полезно видеть, какие роли установлены (как в Server Manager). Сейчас SysPanel не различает Server и Client OS и не показывает роли. На клиентских редакциях Windows пункт меню не нужен.

## Цель

- На Windows Server дополнительно показывать в меню пункт **«Роли сервера»**.
- На странице — только **установленные роли** (не Features), только **чтение**.
- По каждой роли: **Display Name** и **Description**.

## Обнаружение Windows Server

Расширить `SystemInfo` полем `bool IsWindowsServer`.

Источник: WMI `Win32_OperatingSystem.ProductType`:

| ProductType | Значение |
|---|---|
| 1 | Workstation → `IsWindowsServer = false` |
| 2 | Domain controller → `true` |
| 3 | Server → `true` |

Не полагаться на `Caption`/`OsName.Contains("Server")` — это зависит от локали и SKU.

`SystemInfoService` уже читает `Win32_OperatingSystem`; добавить `ProductType` в SELECT и выставить флаг.

Фронт: `App.tsx` уже вызывает `api.system()` после логина для hostname. Передавать `isWindowsServer` в `AppLayout` и условно добавлять пункт меню (тот же паттерн, что `isAdmin` для «Пользователи»).

## Меню и маршрутизация

- Пункт: **«Роли сервера»**, ключ `/server-roles`, иконка вроде `CloudServerOutlined`.
- Размещение: рядом с системными разделами (Диски / Службы), не в блоке CP.
- Видимость: только если `IsWindowsServer === true`.
- Маршрут в `App.tsx` → страница `ServerRoles`.
- Прямой заход на `/server-roles` на клиентской ОС: Empty state («доступно только на Windows Server») или редирект на `/` — предпочтение **Empty state**, без скрытой ошибки.

## API и данные

### Endpoint

`GET /api/v1/server-roles`

- Авторизация: scope `system.read` (новый отдельный scope не вводим — это read-only системная информация).
- Ответ: `ServerRoleInfo[]`.

### Модель

```csharp
public sealed class ServerRoleInfo
{
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
}
```

### Источник данных

PowerShell / Server Manager: `Get-WindowsFeature`, фильтр:

- `InstallState -eq 'Installed'`
- только **роли** (`FeatureType -eq 'Role'`), **не** Role Services и **не** Features.

Поля ответа: Display Name + Description. Техническое имя (`Name` / `AD-Domain-Services`) в UI не показываем (можно оставить во внутренней модели позже, в v1 не нужно).

### Поведение на не-Server

- Меню скрыто.
- Endpoint возвращает **пустой массив** `[]` (не 404), чтобы фронт не ломался при прямом URL.
- Если модуль ServerManager недоступен / ошибка выполнения — лог + понятная ошибка API (как у других сервисов); UI показывает Empty/error.

### Слои backend

- `WinAdmin.Core`: `ServerRoleInfo`, `IServerRolesService`
- `WinAdmin.Infrastructure`: `ServerRolesService` (вызов Get-WindowsFeature)
- `WinAdmin.Api`: `ServerRolesController`
- DI в `DependencyInjection.cs`

## Frontend

- `api/types.ts`: `IsWindowsServer` в `SystemInfo`, тип `ServerRoleInfo`
- `api/client.ts`: `serverRoles: () => http.get<ServerRoleInfo[]>('/server-roles')`
- `pages/ServerRoles.tsx`: `PageHeader` («Роли сервера», subtitle с числом, Refresh) + таблица/AG Grid с колонками **Имя** и **Описание**, быстрый поиск по имени; без кнопок install/uninstall
- `AppLayout.tsx`: условный пункт меню
- `App.tsx`: маршрут + проброс `isWindowsServer`
- Документация API в UI (`ApiDocs`), если там перечислены endpoints

## Тестирование

- Юнит/интеграция по возможности: маппинг ProductType → `IsWindowsServer`; фильтр Installed + Role (мок вывода Get-WindowsFeature).
- Ручная проверка:
  - Windows Server с ролями → пункт меню есть, список совпадает с Server Manager (Installed roles).
  - Windows 10/11 → пункта нет, `/server-roles` → Empty state, API → `[]`.
  - Пользователь без `system.read` (и без `admin`) → 403.

## Вне рамок

- Установка / удаление ролей и Features.
- Показ Features и Role Services (только top-level Roles с `FeatureType -eq 'Role'`).
- Отдельный scope `serverroles.read`.
- Дерево зависимостей ролей, restart-required, parent/child hierarchy.
- Изменения в worktree Software / i18n (фича независима; при мерже возможны конфликты в `AppLayout` / `SystemInfo` — решать при интеграции).
