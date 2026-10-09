# Модули «Пользователи AD» и «Папки» (часть 3 из 4)

**Дата:** 2026-10-10
**Статус:** на ревью
**Основа:** ветка `feat/module-platform` (платформа модулей, роли с областью, подключение к домену, вход AD — части 1a–1c)
**Источник функций:** портал Access (`C:\dev\access`, ASP.NET 4.7.1 + PostgreSQL): `api/Users.ashx`, `UserAttributes`, `UserPhoto`, `UserMove`, `UserPassword`, `UserAccount`, `Groups`, `GroupMembership`, `App_Code/AdUser*`, `AdGroupWriter`, `AdSettingsTester`, `Default.aspx` (`parseFolderFromGroup`, `buildFolderCatalog`, `smartFolderSearch`, `getUserFolderMembership`)

## Контекст и цель

Перенести в WinAdmin управление пользователями AD и доступом к сетевым папкам через группы безопасности `sg_*` из портала Access — двумя **отдельными модулями** (каждый включается, настраивается и делегируется сам), с общим слоем работы с AD и **проверкой окружения** (AD + файловый сервер). Дополнительно к Access — мастер «Новая папка», который создаёт пару групп и выставляет NTFS ACL.

## Решения, принятые на этапе обсуждения

| Вопрос | Решение |
|---|---|
| Объём | Пользователи + Папки, полный функционал Access для них. Рассылки, метаданные проектов, Exchange, импорт — позже |
| Учётка записи в AD | Обе: «служебная учётка» (логин + пароль, шифруется) **или** «учётка службы» (компьютер/gMSA) — выбор в настройках |
| Проверка окружения | AD + файловый сервер (существование папок, NTFS ACL) |
| Папки | Членство + создание пары групп + NTFS ACL |
| Устройство | Общий слой AD + два модуля `ad-users`, `ad-folders` |

## Не входит

Почтовые рассылки (`Mail *`), метаданные и карта проектов, проверка Exchange, импорт из PostgreSQL Access (`role_principals`, `excluded_ous`, OU уволенных) — часть 4. HTTPS — часть 2. Удаление групп/папок, переименование папок, создание/удаление пользователей AD.

---

## 1. Общий слой AD

### 1.1 Структура каталога (`AdStructureSettings`)

Хранится в `PlatformSettings`, ключ `ad-structure`; правится в «Настройки → Active Directory» с правом `platform.directory.manage`.

| Поле | По умолчанию | Смысл |
|---|---|---|
| `RootOu` | — (обязательно для работы модулей) | DN корневой OU (аналог `ldapPath` Access). **Проекты** — OU первого уровня под ней |
| `UsersOuName` | `Users` | Имя OU пользователей внутри проекта (цель переноса/восстановления) |
| `HiddenOus` | `[]` | Имена OU первого уровня, скрытые из списков и недоступные для записи (аналог `excluded_ous`) |
| `WriteMode` | `ServiceAccount` | `ServiceAccount` — логин/пароль ниже; `ProcessAccount` — учётка процесса службы (компьютер или gMSA) |
| `WriteLogin` | — | `ДОМЕН\логин` или UPN служебной учётки |
| `WritePassword` | — | `[Secret]`: шифруется `ISecretProtector` (AES-256-GCM, AAD = `ad-structure.WritePassword`); в API отдаётся только «задан/не задан» |

Пока `RootOu` не задана или подключение к домену выключено, модули `ad-users`/`ad-folders` **недоступны** (`Available=false`, причина в «Модулях»). Требование модулей: `DomainJoined`.

### 1.2 Модель

- **Проект** — OU первого уровня под `RootOu`, не из `HiddenOus`: `{ Dn, Name }`. Проект объекта — первый сегмент OU его DN относительно `RootOu` (порт `DnUtils.GetRelativeOuPath`).
- Объект **в зоне управления**, если его DN заканчивается на `RootOu` и его проект не скрыт.

### 1.3 Область прав

Модули `ad-users` и `ad-folders` объявляют `IScopeProvider` «Проекты»:
- элемент области — **DN OU проекта** (нормализуется: пробелы вокруг `,`/`=` убираются, сравнение без учёта регистра);
- `IsSubsetOf(a, b)` — каждый DN из `a` равен какому-либо из `b` или лежит под ним;
- проверка в сервисе: объект в зоне управления **и** его проект ∈ области действующего лица (`Unrestricted` — все проекты).
- В редакторе ролей — выбор проектов из списка (вместо текстового списка из части 1).

### 1.4 Чтение

Учёткой компьютера через существующее подключение к домену (`LdapDirectoryService`), с **постраничным поиском** (`PageResultRequestControl`, страница 500) для списков пользователей и групп. Новый интерфейс `IAdReader` (Infrastructure):

- `ListProjects()`, `ListUsers(projectDn?)`, `GetUser(sam | sid | dn)`, `GetUserGroups(dn)` (прямые `memberOf` + primary group), `ListGroups(prefix)` (с `member`, `description`, `info`, `whenCreated`), `ResolveMembers(dns)` (пакетами по 30, как в Access), `GetPhoto(dn)`, `GetObjectSid(dn)`, `ReadEffective(dn)` (для проверки окружения: `allowedAttributesEffective`, `allowedChildClassesEffective` — **от имени учётки записи**).

`GET /api/v1/ad/projects` — общий слой (не принадлежит модулю): доступен, если включён хотя бы один из модулей `ad-users`/`ad-folders`; право `ad.users.read` или `ad.folders.read`; возвращает только проекты из области.

### 1.5 Запись (`IAdWriter`)

Одна реализация на `System.DirectoryServices.Protocols`: `LdapConnection` к тому же DC, что и чтение, Negotiate + Signing + Sealing (389) или LDAPS (636); учётные данные — по `WriteMode` (`NetworkCredential` служебной учётки или учётка процесса).

| Операция | LDAP |
|---|---|
| `ModifyAttributes(dn, changes)` | `ModifyRequest` Replace/Delete (пустое значение → Delete) |
| `AddMember(groupDn, memberDn)` / `RemoveMember` | `ModifyRequest member` Add/Delete; «уже участник» (`entryAlreadyExists`/`attributeOrValueExists`) и «не участник» (`noSuchAttribute`/`unwillingToPerform` на Delete) → идемпотентный успех с флагом |
| `SetPrimaryGroup(userDn, groupDn)` | `primaryGroupID` = RID группы (группа должна быть уже среди `member`) |
| `SetEnabled(userDn, bool)` | `userAccountControl` ± `ACCOUNTDISABLE` (0x2), читается текущее значение |
| `ResetPassword(userDn, password, mustChange)` | `unicodePwd` Replace (`"пароль"` в UTF-16LE); `mustChange` → `pwdLastSet = 0`. Только по зашифрованному каналу |
| `Move(userDn, targetOuDn)` | `ModifyDNRequest` (новый родитель, RDN прежний) → новый DN |
| `CreateGroup(ouDn, cn, description)` | `AddRequest`: `objectClass=group`, `sAMAccountName=cn`, `groupType = 0x80000002` (global security), `description` → DN и SID |
| `SetPhoto(userDn, bytes?)` | `thumbnailPhoto` Replace/Delete |

Ошибки LDAP → `AdWriteException(code, message)` с русским текстом:

| Код | Текст |
|---|---|
| 50 `insufficientAccessRights` | «Учётке {логин} не хватает прав на {операция} для {объект}. См. проверку окружения.» |
| 32 `noSuchObject` | «Объект не найден в AD.» |
| 68 `entryAlreadyExists` | «Объект уже существует.» |
| 19 `constraintViolation` (пароль) | «Пароль не соответствует политике домена (длина, сложность, история).» |
| 53 `unwillingToPerform` | «AD отклонил операцию: {детали}.» |
| 81/52 и сеть | `DirectoryUnavailableException` → 503 |

Технические детали (`ErrorMessage` сервера, DN) — в лог службы; клиенту — текст из таблицы.

### 1.6 Охрана записи (`AdGuard`)

Перед **любой** записью сервис модуля вызывает проверку:
1. объект (и для переноса — цель) в зоне управления (`RootOu`, не скрытый проект);
2. проект объекта (и цели) — в области действующего лица для требуемого права;
3. для групп папок — имя начинается с префикса модуля «Папки».

Нарушение → `AccessDeniedException` (403) **до** обращения к `IAdWriter`.

### 1.7 Пошаговые сценарии

`ScenarioRunner` для увольнения/восстановления и мастера папки: шаги `{ Name, Status: Ok | Skipped | Failed, Message }`; при `Failed` выполнение останавливается, предыдущие шаги не откатываются; ответ API — список шагов. Каждый шаг идемпотентен (повторный запуск безопасен).

### 1.8 Пароли

Генератор: 20 символов, CSPRNG, гарантированно буква верхнего/нижнего регистра, цифра, спецсимвол (как `AdPasswordGenerator`). Сгенерированный пароль возвращается **только** в ответе на запрос оператора, **не** пишется в журнал и лог.

---

## 2. Модуль «Пользователи AD» (`ad-users`)

### 2.1 Права (область — проекты)

| Право | Что даёт | Опасное |
|---|---|---|
| `ad.users.read` | Список, карточка, группы пользователя, история | |
| `ad.users.edit` | Атрибуты и фото | |
| `ad.users.move` | Перенос в `OU=Users` другого проекта (оба проекта в области) | |
| `ad.users.password` | Сброс пароля | ✓ |
| `ad.users.offboard` | Увольнение и восстановление | ✓ |

Шаблоны ролей создаются при **первом включении** модуля (если ролей с такими именами нет), область — без ограничений (меняется администратором):
- «AD: отдел кадров» — `ad.users.read`, `ad.users.edit`, `ad.users.move`;
- «AD: администраторы» — все права `ad.users.*` и `ad.folders.*`.

### 2.2 Настройки модуля

| Поле | По умолчанию |
|---|---|
| `FiredGroup` | — (имя или DN; без неё увольнение/восстановление недоступны) |
| `TerminatedOuDn` | — (без него увольнение недоступно) |
| `EditableAttributes` | `displayName, givenName, sn, mail, department, title, telephoneNumber, physicalDeliveryOfficeName, description, company` |
| `PhotoMaxKb` | 100 |

### 2.3 Операции

- **Список:** пользователи (`objectCategory=person`, `objectClass=user`) в проектах из области; поля как в Access (`samAccountName, displayName, givenName, sn, mail, department, title, telephoneNumber, physicalDeliveryOfficeName, description, company, enabled, lastLogonTimestamp, whenCreated, project, hasPhoto`). Фильтры: проект, «активные/отключённые/уволенные» (уволенные — в `TerminatedOuDn`, видны при праве `ad.users.offboard`), поиск по ФИО/логину/почте.
- **Атрибуты:** только из `EditableAttributes`; длина ≤ 256 (description ≤ 1024); `mail` — формат адреса; изменение `givenName`/`sn` не меняет `displayName` автоматически. В аудит — было → стало.
- **Фото:** JPEG/PNG ≤ `PhotoMaxKb`, проверка сигнатуры; обрезка — в браузере.
- **Перенос:** цель — `OU={UsersOuName},{проект}`; если OU нет — 409 «В проекте нет OU {UsersOuName}».
- **Пароль:** вручную (с подтверждением) или «сгенерировать»; флажок «сменить при следующем входе» (по умолчанию включён).
- **Увольнение** (порт `AdUserDeactivation`): добавить в Fired Users → primary group = Fired Users → удалить из всех прямых групп, включая «Пользователи домена» (по SID домена + RID 513, не по имени) → случайный пароль (не показывается) → отключить → перенести в `TerminatedOuDn`. `mail` не меняется.
- **Восстановление** (порт `AdUserActivation`): выбор проекта (из области, не скрытый) → «Пользователи домена» + primary group → удалить из Fired Users → перенос в `OU=Users` проекта → случайный пароль (показ оператору один раз) → включить.

### 2.4 API (`/api/v1/ad/users`, модуль `ad-users`)

| Метод | Путь | Право |
|---|---|---|
| GET | `/ad/users?project=&status=&q=` | `ad.users.read` |
| GET | `/ad/users/{sam}` | `ad.users.read` (атрибуты + группы) |
| GET | `/ad/users/{sam}/photo` | `ad.users.read` |
| PUT | `/ad/users/{sam}/attributes` | `ad.users.edit` |
| PUT / DELETE | `/ad/users/{sam}/photo` | `ad.users.edit` |
| POST | `/ad/users/{sam}/move` `{ projectDn }` | `ad.users.move` |
| POST | `/ad/users/{sam}/password` `{ password?, generate, mustChange }` | `ad.users.password` |
| POST | `/ad/users/{sam}/deactivate` | `ad.users.offboard` |
| POST | `/ad/users/{sam}/activate` `{ projectDn }` | `ad.users.offboard` |

`{sam}` ищется по всему домену, затем проверяется зона управления и область (404 — не найден, 403 — вне области).

### 2.5 Аудит

`user.attributes.update`, `user.photo.update`, `user.photo.remove`, `user.move`, `user.password.reset`, `user.account.deactivate`, `user.account.activate` — `Target` = DN, `Details` = изменения/шаги (без паролей).

### 2.6 Интерфейс

Страница «Пользователи AD»: слева дерево проектов из области, таблица с поиском и фильтрами; карточка (панель) — вкладки «Атрибуты», «Группы», «Папки» (если модуль `ad-folders` включён и есть `ad.folders.read`), «История» (аудит по DN); кнопки действий — по правам. Результат сценариев — список шагов; сгенерированный пароль — в модальном окне с «Скопировать», один раз.

---

## 3. Модуль «Папки» (`ad-folders`)

### 3.1 Каталог (порт логики `Default.aspx` на сервер)

1. Группы `(&(objectClass=group)(cn={Prefix}*))` под `RootOu`, без скрытых проектов.
2. **Разбор** (`FolderDescriptionParser`): текст = `description`, иначе `info`; делится по `;`; первая часть, похожая на путь (`X:\…`, `\\…`, `/…`), — путь; всё после неё — тип доступа: содержит `full` (или равно `f`) → `Full`, содержит `read` (или `r`/`ro`) → `Read`, иначе — как написано («прочий»). Нет пути → группа не папка (показывается в предупреждениях). Нет типа → по суффиксу имени: `_full`/`_f`/`…_full_access` → Full, `_read`/`_r`/`_ro`/`…_read_only` → Read.
3. **Объединение** (`FolderCatalog`): ключ — путь без учёта регистра и завершающего `\`; папка = `{ Path, Project, Full?, Read?, Others[], Warnings[] }`. Предупреждения: «две группы Full», «две группы Read», «нет группы Read/Full», «описание не разобрано», «буква диска не сопоставлена».
4. **Поиск** (порт `smartFolderSearch`): запрос-путь → точное и частичное совпадение; иначе — все слова должны встретиться в «стоге» (проект, путь, сегменты пути, имена групп).
5. **Доступы пользователя** (порт `getUserFolderMembership`): по проектам — папки, где пользователь в Full и/или Read.

Каталог кэшируется в памяти на 60 с; любое изменение членства/создание папки сбрасывает кэш.

### 3.2 Права (область — проекты; проект папки — проект её групп)

| Право | Что даёт | Опасное |
|---|---|---|
| `ad.folders.read` | Каталог, участники, доступы пользователя | |
| `ad.folders.membership` | Добавить/удалить участника (пользователь или группа) в Full/Read | |
| `ad.folders.create` | Мастер «Новая папка», «Исправить права NTFS» | ✓ |

### 3.3 Членство

`POST /ad/folders/membership { groupDn, memberDn | sam, action: add|remove, removeFromOther }`:
- `AdGuard` (зона, область, префикс); группа должна быть **security** (`groupType & 0x80000000`); участник найден в AD;
- при `add` в Full и `removeFromOther=true` (по умолчанию) — участник удаляется из Read этой же папки (и наоборот); оба шага в ответе;
- аудит `group.member.add` / `group.member.remove` (`Details`: участник, группа, папка, проект, `alreadyMember`).

### 3.4 Мастер «Новая папка»

`POST /ad/folders { projectDn, path, baseName, orgCode?, createDirectory }` (`ad.folders.create`, проект в области):

1. Проверки: путь — `X:\…` или `\\…`; буква сопоставлена с UNC (§3.5); `baseName` — латиница/цифры/`_`/`-`, ≤ 40; итоговые имена ≤ 64 символов.
2. Имена: `{Prefix}{orgCode}_{baseName}_full` / `_read`; `orgCode` по умолчанию — самая частая вторая часть имён существующих групп проекта (`sg_<org>_…`), иначе имя проекта в нижнем регистре без пробелов.
3. Шаг «Группа Full»: существует с таким `cn` в OU групп проекта → используется (если её описание указывает на другой путь — `Failed` «Имя занято другой папкой»), иначе `CreateGroup` с описанием `{path};Full Access`. Так же «Группа Read» (`{path};Read Only`).
4. Шаг «Папка»: UNC существует → `Skipped`; нет и `createDirectory` → создать; нет и не просили → `Failed`.
5. Шаг «Права NTFS» (§3.5).
6. Аудит `folder.create` со всеми шагами; кэш каталога сбрасывается.

OU групп внутри проекта — настройка `GroupsOuName` (пусто = сама OU проекта).

### 3.5 NTFS

- **Сопоставление путей** (настройка `DriveMappings`: `[{ Drive: "A", Unc: "\\fs01\Projects" }]`): `A:\X\Y` → `\\fs01\Projects\X\Y`; UNC-путь используется как есть.
- **Учётная запись:** `ProcessAccount` — от имени службы; `ServiceAccount` — `LogonUser(LOGON32_LOGON_NEW_CREDENTIALS)` + `WindowsIdentity.RunImpersonated` (доступ к шаре под служебной учёткой).
- **Права** (настройки `FullRights` = `Modify`, `ReadRights` = `ReadAndExecute`): правила **Allow** по **SID** групп, наследование `ContainerInherit | ObjectInherit`, `PropagationFlags.None`. Существующие правила других субъектов не меняются; если правило для этого SID уже есть с ≥ нужных прав — `Skipped`.
- **Проверка ACL папки** (`FolderAclInspector`): папка есть; есть Allow-правило для SID Full с правами ⊇ `FullRights` и наследованием; то же для Read; лишние Allow-права у Read (например, Write) — предупреждение.
- **«Исправить права NTFS»:** `POST /ad/folders/acl-fix { path }` (`ad.folders.create`) — добавляет недостающие правила, аудит `folder.acl.fix`.

### 3.6 Настройки модуля

| Поле | По умолчанию |
|---|---|
| `GroupPrefix` | `sg_` |
| `GroupsOuName` | пусто (OU проекта) |
| `DriveMappings` | `[]` |
| `FullRights` / `ReadRights` | `Modify` / `ReadAndExecute` |

### 3.7 API (модуль `ad-folders`)

| Метод | Путь | Право |
|---|---|---|
| GET | `/ad/folders?project=&q=` | `ad.folders.read` |
| GET | `/ad/folders/user/{sam}` | `ad.folders.read` (+ пользователь в области) |
| POST | `/ad/folders/membership` | `ad.folders.membership` |
| POST | `/ad/folders` | `ad.folders.create` |
| GET | `/ad/folders/acl?path=` | `ad.folders.read` |
| POST | `/ad/folders/acl-fix` | `ad.folders.create` |

### 3.8 Интерфейс

Страница «Папки»: слева проекты, справа папки с бейджами `Full (N)` / `Read (N)` и предупреждениями, умный поиск; панель папки — две колонки участников (поиск AD для добавления, удаление), «Проверить права NTFS», «Исправить»; кнопка «Новая папка» — мастер с предпросмотром имён групп и UNC, результат — шаги.

---

## 4. Проверка окружения

### 4.1 Механизм

```csharp
public interface IEnvironmentCheck
{
    string ModuleId { get; }                  // "platform", "ad-users", "ad-folders"
    Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct);
}
public enum CheckStatus { Ok, Warning, Failed, Skipped }
public enum CheckDepth { Quick, Full }        // Full — с NTFS-сканом папок
public sealed record CheckResult(string Code, string Title, CheckStatus Status, string Message, string? Fix);
```

- Проверки модуля выполняются, только если модуль включён (или явно по кнопке «Проверить» на его карточке); если предыдущая обязательная проверка `Failed` — зависимые `Skipped` с причиной.
- API: `GET /api/v1/environment?module=&depth=quick|full` (право `platform.environment.check`, новое, опасное: нет); `Full` — до 500 папок за запуск, с таймаутом на папку 5 с.
- Фон: `Quick` раз в час и при включении модуля; итог (`Ok`/`Warning`/`Failed`) — значок на карточке модуля и в `/me`; в аудит — только смена статуса (`environment.status`).
- Включение модуля при `Failed` разрешено, с предупреждением в ответе.

### 4.2 Проверки

**Платформа / слой AD (`platform`):**

| Код | Проверка | Fix |
|---|---|---|
| `ad.directory` | Подключение к домену включено, DC отвечает | «Настройки → Подключение к домену → Проверить» |
| `ad.root` | `RootOu` задана и существует; число проектов; скрытые OU существуют | Указать корневую OU |
| `ad.writer` | Вход учёткой записи; служебная учётка не отключена/не заблокирована, срок пароля (предупреждение < 14 дней) | Задать/обновить пароль |
| `ad.channel` | Канал записи зашифрован (Sealing или LDAPS) | Включить LDAPS / проверить Kerberos |
| `ad.rights.users` | На образце пользователя каждого проекта (`OU=Users`): `allowedAttributesEffective` ⊇ `EditableAttributes` + `thumbnailPhoto`; перечисляются недостающие и проекты | «Делегируйте {логин} Write Property {атрибуты} на OU=Users проекта …» |
| `ad.rights.groups` | На образце `sg_*` каждого проекта: `member` записываем | «Write members на OU с группами …» |
| `ad.rights.create` | На OU групп проекта: `allowedChildClassesEffective` ∋ `group` (только если модуль «Папки» включён) | «Create Group objects на OU …» |

**`ad-users`:**

| Код | Проверка |
|---|---|
| `users.fired` | Группа Fired Users найдена, `member` записываем |
| `users.domainUsers` | «Пользователи домена» (RID 513): `member` записываем |
| `users.terminatedOu` | OU уволенных существует, `allowedChildClassesEffective` ∋ `user` |
| `users.resetPassword` | ACL `OU=Users` образцового проекта даёт учётке записи право Reset Password (GUID `00299570-246d-11d0-a768-00aa006e0529`) — по `nTSecurityDescriptor`, без смены пароля |
| `users.usersOu` | В каждом проекте есть `OU={UsersOuName}` (предупреждение со списком проектов) |

**`ad-folders`:**

| Код | Проверка |
|---|---|
| `folders.groups` | Число групп, неразобранные описания, дубли Full/Read, папки без пары (предупреждения со списком) |
| `folders.mappings` | Все буквы дисков из описаний сопоставлены с UNC |
| `folders.share` | Каждый UNC-корень: SMB 445 доступен, корень открывается учёткой записи; право изменять ACL на корне (`FileSystemRights.ChangePermissions` в эффективных правах) |
| `folders.acl` (только `Full`) | Для каждой папки: существует; ACL содержит SID Full/Read с нужными правами; список расхождений с кнопкой «Исправить» |

---

## 5. Ошибки

- DC недоступен → страницы модулей 503 «Контроллер домена недоступен»; записи не выполняются; локальный вход работает.
- Модуль не настроен (`RootOu`, `FiredGroup`, `TerminatedOuDn`, `DriveMappings`) → действие недоступно (кнопка скрыта/неактивна с подсказкой), API 409 с указанием настройки.
- Нет прав у учётки записи → 403 с текстом §1.5 и ссылкой на проверку окружения.
- Вне зоны/области → 403 до записи.
- NTFS: отказ доступа → шаг `Failed` с текстом «Нет прав изменять ACL {UNC}»; путь не сопоставлен → мастер не стартует.
- Исключения — в лог службы; клиенту — общий текст.

## 6. Безопасность (отличия от Access)

| Access | WinAdmin |
|---|---|
| Пароль adService открытым текстом в БД | `[Secret]` AES-256-GCM; или учётка процесса без пароля |
| Членство меняется в любой `sg_*` домена | Только в `RootOu`, не скрытый проект, в области оператора |
| Пароль при восстановлении пишется в журнал | Показ один раз, в журнал и лог — нет |
| Нет CSRF-защиты | JWT в заголовке, cookie не используется для API |
| `ex.Message` клиенту | Сопоставленные тексты, детали — в лог |
| Роли захардкожены | Роли и области WinAdmin, делегирование без повышения прав |

## 7. Тесты

Проект `src/tests/WinAdmin.Tests` (xUnit, Moq, `WebApplicationFactory`).

- **Чистая логика:** `FolderDescriptionParser` (все формы из Access: `;Full Access`, `;F`, `;ro`, без типа + суффиксы, UNC, `/`, без пути, пустое, лишние `;`), `FolderCatalog` (объединение, регистр, завершающий `\`, предупреждения), поиск папок, доступы пользователя, `DnUtils` (проект по DN, экранированные запятые), нормализация/`IsSubsetOf` области, генератор имён групп и `orgCode`, генератор паролей, сопоставление путей.
- **Сервисы на фейках** (`FakeDirectory` + `FakeAdReader` + `FakeAdWriter`, журнал вызовов): `AdGuard` (вне `RootOu`, скрытый проект, вне области → 403 без вызова writer); атрибуты (вне списка → 400, было/стало в аудите); перенос (цель вне области); увольнение/восстановление (порядок шагов, RID 513 по SID, идемпотентность, остановка на ошибке, пароль не в аудите); членство (`removeFromOther`, не security-группа, «уже участник»); мастер папки (повторное использование групп, «имя занято», повторный запуск).
- **NTFS — на настоящей временной папке** (`%TEMP%`): добавление правил по SID встроенных групп (`BUILTIN\Users` и т.п.), наследование, сохранение чужих правил, `Skipped` при достаточных правах, инспектор ACL.
- **Проверка окружения:** на фейках — порядок, `Skipped` зависимых, тексты Fix.
- **API:** 404 при выключенном модуле, 403 без права/вне области, 409 без настроек.
- **Интеграция с AD** — `[AdFact]` по `WINADMIN_TEST_AD_*`, только в `OU=WinAdmin-Test` (переменная `WINADMIN_TEST_AD_ROOT`): создание группы, членство, атрибуты, перенос между двумя тестовыми OU, пароль; иначе Skip.
- **UI** — `npm run build` + `oxlint` + проход в браузере.

## 8. Порядок реализации

| План | Содержание | Результат |
|---|---|---|
| 3a | Общий слой: `AdStructureSettings` (+ UI), проекты, область «Проекты», `IAdReader` (постранично), `IAdWriter`, `AdGuard`, `ScenarioRunner`, генератор паролей, `IEnvironmentCheck` + платформенные проверки AD, страница «Проверка окружения» | Настройка AD и проверка окружения работают |
| 3b | Модуль `ad-users` (§2) + его проверки | Пользователи AD как в Access |
| 3c | Модуль `ad-folders` (§3) + NTFS + его проверки | Папки + мастер + NTFS |

После каждого плана — выкатка на DC PCS (резервная копия базы и приложения, откат при неудаче). Запись в AD боевого домена проверяется **только в `OU=WinAdmin-Test`**, пока пользователь не разрешит иное. От пользователя для проверки записи: `OU=WinAdmin-Test` с тестовыми пользователями и группами, служебная учётка с делегированием на неё, тестовая шара.
