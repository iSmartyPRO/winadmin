# WinAdmin v1.0.5 — быстрый запуск

Self-contained пакет для **Windows Server 2019** (и новее), **x64**.  
.NET на сервере **устанавливать не нужно** — runtime уже внутри папки.

## Содержимое

| Файл / папка | Назначение |
|---|---|
| `WinAdmin.exe` | Основное приложение (API + веб-интерфейс) |
| `wwwroot/` | Собранный фронтенд |
| `install-service.ps1` | Установка как службы Windows |
| `download-release.ps1` | Скачать релиз с GitHub |
| `update-release.ps1` | Обновить установку с GitHub |
| `web.config` | Для размещения под IIS (опционально) |
| `docs/` | Справка: журналы, безопасность, **установка с GitHub** |

После первого запуска рядом появятся:

- `WinAdmin.db` — база (ключи, пользователи, аудит), если путь не задан отдельно
- `bootstrap-key.txt` — одноразовый admin API-ключ (если ключей ещё не было)

---

## 1. Перенос на сервер

1. Скопируйте **всю папку** `v1.0.0` на целевой сервер, например:
   ```
   C:\apps\WinAdmin\
   ```
2. Убедитесь, что на сервере **Windows x64** (не 32-bit).

Установка с GitHub без ручного скачивания ZIP: см. **`docs/github-releases.md`**.

---

## 2. Быстрый старт (консоль)

Откройте **PowerShell от имени администратора**:

```powershell
cd C:\apps\WinAdmin
.\WinAdmin.exe
```

Откройте в браузере на этом компьютере: **http://127.0.0.1:8080**

Адрес и порт задаёт `network.json` рядом с БД (см. п. 5).

Проверка здоровья:

```powershell
Invoke-RestMethod http://127.0.0.1:8080/health
```

Остановка: `Ctrl + C` в окне консоли.

> **Права администратора** нужны для чтения журнала Security («Авторизация») и для
> управления службами/питанием. Без них часть функций вернёт 403 или ошибку доступа.

---

## 3. Первый вход

При первом запуске пользователей ещё нет. Создайте администратора (в **новом** окне
PowerShell, пока приложение работает):

```powershell
cd C:\apps\WinAdmin
.\WinAdmin.exe user add --login admin --password "ВашНадёжныйПароль" --role Администратор
```

Затем войдите в веб-интерфейс: логин `admin`, ваш пароль.

Альтернатива — API-ключ из `bootstrap-key.txt` (роль «Администратор»).  
**Удалите файл** после копирования ключа.

---

## 4. Запуск как службы Windows (рекомендуется)

В PowerShell **от имени администратора**, из папки установки:

```powershell
cd C:\apps\WinAdmin
.\install-service.ps1 -Port 8080
```

Скрипт:

- создаёт службу `WinAdmin` с автозапуском;
- создаёт `C:\ProgramData\WinAdmin\network.json` (только этот компьютер, порт `-Port`);
- ограничивает права: папка приложения — запись только Администраторы/SYSTEM, `C:\ProgramData\WinAdmin` — доступ только Администраторы/SYSTEM;
- удаляет старое правило брандмауэра `WinAdmin HTTP <порт>` (было открыто для всех адресов);
- задаёт `WinAdmin__DatabasePath` → `C:\ProgramData\WinAdmin\WinAdmin.db`.

Управление службой:

```powershell
Start-Service WinAdmin
Stop-Service WinAdmin
Get-Service WinAdmin
```

Удаление службы:

```powershell
Stop-Service WinAdmin
sc.exe delete WinAdmin
```

### Ручная установка службы (без скрипта)

```powershell
sc.exe create WinAdmin binPath="C:\apps\WinAdmin\WinAdmin.exe" start= auto
sc.exe start WinAdmin
```

---

## 5. Сетевой доступ

По умолчанию панель доступна **только с этого компьютера** (`127.0.0.1`), правило брандмауэра не нужно.
Настройки хранятся в `C:\ProgramData\WinAdmin\network.json` и применяются **без перезапуска службы**.

В веб-интерфейсе: **Настройки → Сеть** (нужно право «Сетевой доступ», `platform.network.manage`).

Из консоли администратора:

```powershell
.\WinAdmin.exe network show
.\WinAdmin.exe network set --port 9090
.\WinAdmin.exe network set --mode network --allow "10.77.77.0/24,192.168.88.5"
.\WinAdmin.exe network set --mode local
```

В режиме `network` WinAdmin сам создаёт правило брандмауэра `WinAdmin (managed)` — только для перечисленных
адресов и только в профилях «Домен» и «Частная». Трафик не шифруется (HTTP) — используйте только в доверенной сети.

Под IIS адрес и порт задаёт сайт IIS — эти настройки не действуют.

---

## 5а. База данных и ключ шифрования

По умолчанию WinAdmin хранит данные в SQLite (`C:\ProgramData\WinAdmin\WinAdmin.db`). Можно перейти на PostgreSQL:

```powershell
.\WinAdmin.exe db show
.\WinAdmin.exe db set --provider postgresql --connection "Host=db01;Database=winadmin;Username=winadmin;Password=..."
.\WinAdmin.exe db set --provider sqlite
Restart-Service WinAdmin
```

`db set` проверяет подключение и создаёт таблицы. Данные между базами **не переносятся**.
Настройка хранится в `C:\ProgramData\WinAdmin\database.json`, пароль в ней зашифрован.

Пароли и ключи (пароль PostgreSQL, JWT-секрет, секреты модулей) хранятся зашифрованными ключом
`C:\ProgramData\WinAdmin\keys\master.key` (защищён DPAPI машины, доступ — только администраторы и SYSTEM).
Без этого ключа зашифрованные значения не восстановить. Сохраните его копию:

```powershell
.\WinAdmin.exe keys export --file D:\backup\winadmin-key.bin --password "<надёжный пароль>"
.\WinAdmin.exe keys import --file D:\backup\winadmin-key.bin --password "<пароль>"   # на новом сервере
```

`keys import` не заменяет существующий ключ без `--force`.

## 5б. Роли и модули

Права выдаются **ролями**: роль — набор прав вида `модуль.действие` (например `services.manage`),
для модулей с областью (Active Directory) — ещё и список OU. Роли создаются в разделе **Роли**
и назначаются пользователям WinAdmin и API-ключам (позже — пользователям и группам AD).

- Встроенная роль **«Администратор»** — все права; её нельзя изменить или удалить, а последнее
  назначение «Администратор» снять нельзя.
- Выдавать можно только права, которые есть у вас самих, и в пределах вашей области.
- **Модули** включаются и выключаются в разделе «Модули»: выключенный модуль скрыт у всех,
  его API отвечает 404.

При обновлении со старой версии scopes пользователей и ключей автоматически превращаются в роли:
`admin` → «Администратор», остальные наборы — в роли «Импорт: …».

---

## 6. Постоянные настройки (рекомендуется)

Задайте **до** первого запуска или через переменные окружения машины:

```powershell
# База данных (чтобы не лежала в папке приложения)
[Environment]::SetEnvironmentVariable("WinAdmin__DatabasePath", "C:\ProgramData\WinAdmin\WinAdmin.db", "Machine")

```

После изменения переменных **перезапустите** приложение или службу.

---

## 7. Журнал Security (раздел «Авторизация»)

По умолчанию журнал Security — **20 МБ**, старые записи быстро перезаписываются.
Для истории за дни увеличьте размер (от имени администратора):

```powershell
# 512 МБ — рекомендуемый минимум для сервера
wevtutil sl Security /ms:536870912

# проверка
wevtutil gl Security | findstr /i maxSize
```

Подробнее: `docs/event-logs.md`

---

## 8. Размещение под IIS (опционально)

Для IIS нужен **ASP.NET Core Hosting Bundle** на сервере (~25 МБ).  
Self-contained exe можно запускать **без IIS** — это проще для Server 2019.

Если всё же IIS:

1. Установите [Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) для .NET 10.
2. Скопируйте папку в `C:\inetpub\WinAdmin`.
3. Настройте сайт на `web.config` из этой папки (in-process).
4. Пул приложения — **No Managed Code**, учётная запись с правами локального администратора.

Полная инструкция: `docs/deploy-iis.md` (в репозитории исходников).

---

## 9. CLI: управление пользователями

```powershell
.\WinAdmin.exe user add --login operator --password "..." --role "Наблюдатель"
.\WinAdmin.exe role list
.\WinAdmin.exe role assign --role Администратор --local admin     # аварийное восстановление доступа
.\WinAdmin.exe user list
.\WinAdmin.exe user set-password --login admin --password "новый"
.\WinAdmin.exe network show
.\WinAdmin.exe network set --port 9090
```

---

## 10. Устранение неполадок

| Симптом | Решение |
|---|---|
| 403 в «Авторизация» | Запуск от имени администратора |
| Мало событий за «30 дней» | Увеличить размер журнала Security (п. 7) |
| 401 при входе | Создать пользователя (`user add`) |
| Порт занят | `.\WinAdmin.exe network set --port 9090` |
| Сессии сбрасываются | Проверить `C:\ProgramData\WinAdmin\keys\` (ключ и `jwt.key` должны сохраняться между перезапусками) |
| Не открывается с другой машины | Режим «Сеть» с нужной подсетью (п. 5) |
| Панель пропала после смены порта | `.\WinAdmin.exe network show` / `network set --port 8080` из консоли администратора |

Swagger API: `http://<сервер>:8080/swagger`

---

## Системные требования

- Windows Server 2019 / 2022 / 2025 или Windows 10/11 **x64**
- ~150 МБ на диске для приложения
- Права локального администратора — для полного функционала

Версия: **1.0.0** · Сборка: self-contained `win-x64` · .NET 10
