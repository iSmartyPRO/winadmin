# Развёртывание под IIS

## 1. Предварительные требования

1. **IIS** с компонентами Web Server. Включить можно через «Программы и компоненты» →
   «Включение или отключение компонентов Windows» → Internet Information Services.
2. **ASP.NET Core Hosting Bundle** (включает ASP.NET Core Module V2 и среду исполнения):
   <https://dotnet.microsoft.com/download/dotnet/10.0> → «Hosting Bundle».
   После установки выполните `iisreset`.

## 2. Публикация

```powershell
dotnet publish src/WinAdmin.Api -c Release -o C:\inetpub\WinAdmin
```

Фронтенд должен быть собран в `wwwroot` до публикации (входит в проект):

```powershell
npm --prefix web install
npm --prefix web run build   # кладёт сборку в src/WinAdmin.Api/wwwroot
```

## 3. Модель привилегий (важно)

Полное управление (остановка служб, перезагрузка/выключение) требует, чтобы рабочий
процесс IIS имел **права администратора** и привилегию **SeShutdownPrivilege**.
Поэтому пул приложения должен работать под **выделенной сервисной учётной записью**,
а не под `ApplicationPoolIdentity`.

Рекомендуется:

1. Создать доменную или локальную учётную запись, например `svc-WinAdmin`
   (команды — в разделе [«Создание сервисной учётки»](#4-создание-сервисной-учётки)).
2. Добавить её в группу **Администраторы** (локально).
3. Убедиться, что у неё есть право **«Завершение работы системы»**
   (`secpol.msc` → Локальные политики → Назначение прав пользователя →
   «Завершение работы системы»).

> Безопасность компенсируется тем, что доступ к API ограничен ключами со scopes и
> ведётся аудит всех действий. Ограничьте сетевой доступ к порту приложения.

## 4. Создание сервисной учётки

Учётная запись не существует заранее — её создаёт администратор под приложение и сам
задаёт пароль. Выберите один из трёх вариантов.

### Вариант A. Локальная учётка (машина без домена)

Подходит для отдельной рабочей станции/сервера. Выполняется **от имени администратора**:

```powershell
# 1. Создать учётку (пароль придумываете сами)
net user svc-WinAdmin "ВашНадёжныйПароль" /add
Set-LocalUser -Name svc-WinAdmin -PasswordNeverExpires $true

# 2. Добавить в локальные администраторы (нужно для управления службами и питанием)
Add-LocalGroupMember -Group "Администраторы" -Member svc-WinAdmin
# на англоязычной системе группа называется "Administrators"
```

Право «Завершение работы системы» (`SeShutdownPrivilege`) у администраторов есть по
умолчанию. В установке используйте имя `.\svc-WinAdmin` и заданный пароль.

### Вариант B. Доменная учётка (Active Directory)

Учётку создаёт администратор домена (`dsa.msc` или `New-ADUser`) и задаёт ей пароль:

```powershell
# на контроллере домена
New-ADUser -Name svc-WinAdmin -SamAccountName svc-WinAdmin `
  -AccountPassword (Read-Host "Пароль" -AsSecureString) `
  -Enabled $true -PasswordNeverExpires $true
```

Затем на каждой машине добавьте её в локальные администраторы:

```powershell
Add-LocalGroupMember -Group "Администраторы" -Member "DOMAIN\svc-WinAdmin"
```

В установке используйте `DOMAIN\svc-WinAdmin` и доменный пароль.

### Вариант C. gMSA — без пароля (рекомендуется в домене) ⭐

**Group Managed Service Account**: пароль генерирует и автоматически ротирует Active
Directory — хранить и вводить его не нужно.

```powershell
# на контроллере домена (однократно для всего леса нужен KDS Root Key)
Add-KdsRootKey -EffectiveImmediately   # если ещё не создан
New-ADServiceAccount -Name svc-WinAdmin -DNSHostName host.domain.local `
  -PrincipalsAllowedToRetrieveManagedPassword "WS-01$","WS-02$"

# на каждой машине, где будет работать WinAdmin
Install-ADServiceAccount svc-WinAdmin
Test-ADServiceAccount svc-WinAdmin      # должно вернуть True
```

В IIS пул задаётся с identity `DOMAIN\svc-WinAdmin$` и **пустым паролем** — Windows
подставляет его сам. Учётку так же добавьте в локальные администраторы машины.

> Не путайте пароль сервисной учётки (вы задаёте сами / gMSA без пароля) с **bootstrap
> admin-ключом** — это API-ключ, он генерируется приложением автоматически при первом
> запуске (см. раздел «Первый запуск и стартовый ключ»).

## 5. Установка автоматически (скрипт)

```powershell
# от имени администратора
$pwd = Read-Host "Пароль сервисной учётки" -AsSecureString
.\deploy\install.ps1 -SiteName WinAdmin -Port 8080 `
    -PhysicalPath C:\inetpub\WinAdmin `
    -DataPath C:\ProgramData\WinAdmin `
    -ServiceAccount "DOMAIN\svc-WinAdmin" -ServicePassword $pwd
```

Скрипт публикует приложение, создаёт пул (No Managed Code, SpecificUser), сайт на
указанном порту и каталог данных для SQLite.

## 6. Установка вручную

1. Скопируйте опубликованные файлы в `C:\inetpub\WinAdmin`.
2. В **Диспетчере IIS** создайте пул приложения `WinAdmin`:
   - .NET CLR version: **No Managed Code**;
   - Identity: **Custom account** → сервисная учётка.
3. Создайте сайт `WinAdmin`, физический путь — каталог публикации, порт — например 8080,
   пул — `WinAdmin`.
4. Задайте путь к БД вне каталога приложения (чтобы переустановки не затирали данные).
   В `appsettings.Production.json` или через переменную окружения:

   ```json
   {
     "WinAdmin": {
       "DatabasePath": "C:\\ProgramData\\WinAdmin\\WinAdmin.db"
     }
   }
   ```

   Дайте сервисной учётке полный доступ к этому каталогу.

## 7. Первый запуск и стартовый ключ

При первом обращении создаётся стартовый **admin**-ключ. Он:

- выводится в журнал приложения (stdout/Event Log);
- сохраняется в `bootstrap-key.txt` рядом с приложением.

Скопируйте ключ, войдите в UI (`http://<host>:<port>/`), создайте рабочие ключи с
нужными scopes и **удалите** `bootstrap-key.txt`. Опционально задайте свой стартовый
ключ заранее:

```json
{ "WinAdmin": { "BootstrapKey": "sp_ваш_заранее_заданный_ключ" } }
```

## 8. HTTPS

Для продакшена настройте привязку HTTPS на сайте (сертификат в IIS). В не-Development
окружении приложение включает HTTPS-redirect.

## 9. Проверка

```powershell
Invoke-RestMethod http://localhost:8080/health
# { status = ok; machine = <имя>; time = ... }
```

Откройте `http://localhost:8080/swagger` — спецификация API.
