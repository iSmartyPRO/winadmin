# WinAdmin v1.0.4 — быстрый запуск

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
.\WinAdmin.exe --urls http://0.0.0.0:8080
```

Откройте в браузере: **http://<имя-сервера>:8080**

Проверка здоровья:

```powershell
Invoke-RestMethod http://localhost:8080/health
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
.\WinAdmin.exe user add --login admin --password "ВашНадёжныйПароль" --scopes admin
```

Затем войдите в веб-интерфейс: логин `admin`, ваш пароль.

Альтернатива — API-ключ из `bootstrap-key.txt` (scope `admin`).  
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
- открывает порт в брандмауэре;
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
sc.exe create WinAdmin binPath="C:\apps\WinAdmin\WinAdmin.exe --urls http://0.0.0.0:8080" start= auto
sc.exe start WinAdmin
```

---

## 5. Брандмауэр (если не использовали install-service.ps1)

```powershell
New-NetFirewallRule -DisplayName "WinAdmin HTTP 8080" `
  -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow
```

---

## 6. Постоянные настройки (рекомендуется)

Задайте **до** первого запуска или через переменные окружения машины:

```powershell
# База данных (чтобы не лежала в папке приложения)
[Environment]::SetEnvironmentVariable("WinAdmin__DatabasePath", "C:\ProgramData\WinAdmin\WinAdmin.db", "Machine")

# JWT-секрет (иначе сессии сбросятся при каждом перезапуске)
[Environment]::SetEnvironmentVariable("WinAdmin__Jwt__Secret", "<случайная-длинная-строка-base64>", "Machine")
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
.\WinAdmin.exe user add --login operator --password "..." --scopes "system.read,services.read"
.\WinAdmin.exe user list
.\WinAdmin.exe user set-password --login admin --password "новый"
```

---

## 10. Устранение неполадок

| Симптом | Решение |
|---|---|
| 403 в «Авторизация» | Запуск от имени администратора |
| Мало событий за «30 дней» | Увеличить размер журнала Security (п. 7) |
| 401 при входе | Создать пользователя (`user add`) |
| Порт занят | Сменить порт: `--urls http://0.0.0.0:9090` |
| Сессии сбрасываются | Задать `WinAdmin__Jwt__Secret` |
| Не открывается с другой машины | Брандмауэр (п. 5), `0.0.0.0` в `--urls` |

Swagger API: `http://<сервер>:8080/swagger`

---

## Системные требования

- Windows Server 2019 / 2022 / 2025 или Windows 10/11 **x64**
- ~150 МБ на диске для приложения
- Права локального администратора — для полного функционала

Версия: **1.0.0** · Сборка: self-contained `win-x64` · .NET 10
