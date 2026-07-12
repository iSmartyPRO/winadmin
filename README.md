# WinAdmin

Автономное веб-приложение для **мониторинга и управления Windows-машиной** (рабочей
станцией или сервером). Один экземпляр на машину; управляет локальной системой и
предоставляет REST API для интеграции с внешними системами.

Репозиторий: https://github.com/iSmartyPRO/winadmin

- Техническая информация: ОС, CPU, RAM, BIOS, сеть
- Диски и разделы с заполнением
- Live-метрики CPU/память/сеть
- Управление: службы, процессы, принтеры, питание
- REST API с ключами и гранулярными scopes, аудит действий
- Swagger + гайды интеграции

## Стек

ASP.NET Core 10 (C#) · WMI/CIM · EF Core + SQLite · React 19 + Ant Design + AG Grid + Recharts

## Структура

```
WinAdmin/
├── src/
│   ├── backend/        # ASP.NET Core API (C#)
│   │   ├── WinAdmin.Api
│   │   ├── WinAdmin.Core
│   │   └── WinAdmin.Infrastructure
│   ├── frontend/       # React SPA (TypeScript + Vite)
│   └── tests/          # xUnit тесты
├── releases/           # Скрипты сборки, установки, готовые пакеты
└── docs/               # Документация
```

## Разработка

```powershell
# Backend (Kestrel на 5099)
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project src/backend/WinAdmin.Api --urls http://localhost:5099

# Frontend (Vite dev, проксирует /api на backend)
npm --prefix src/frontend install
npm --prefix src/frontend run dev        # http://localhost:5188
```

Стартовый admin-ключ создаётся при первом запуске и сохраняется в `bootstrap-key.txt`
рядом с приложением.

## Скачать готовый релиз

Готовый ZIP (~150 МБ, self-contained, Windows x64):  
**https://github.com/iSmartyPRO/winadmin/releases**

Полная инструкция для конечных компьютеров (скрипты + copy-paste):  
**[releases/package/docs/github-releases.md](releases/package/docs/github-releases.md)**

### Быстрая установка на сервере

PowerShell **от имени администратора**:

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$dir = "$env:TEMP\winadmin-scripts"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$base = 'https://raw.githubusercontent.com/iSmartyPRO/winadmin/main/releases'
Invoke-WebRequest "$base/download-release.ps1" -OutFile "$dir\download-release.ps1" -UseBasicParsing
Invoke-WebRequest "$base/install-from-github.ps1" -OutFile "$dir\install-from-github.ps1" -UseBasicParsing
& "$dir\install-from-github.ps1" -Port 8080
```

Создать пользователя:

```powershell
cd C:\WinAdmin
.\WinAdmin.Api.exe user add --login admin --password "YourPassword" --scopes admin
```

### Обновление

```powershell
cd C:\WinAdmin
.\update-release.ps1 -Port 8080
```

### Публикация нового релиза (разработчик)

```powershell
git tag v1.0.1
git push origin v1.0.1
```

См. также [releases/README.md](releases/README.md).

## Сборка пакета для переноса (без зависимостей на целевой машине)

```powershell
.\releases\build.ps1                             # → releases/dist/
.\releases\build.ps1 -OutputPath releases\v1.0.0 # версионированная папка
.\releases\build.ps1 -OutputPath C:\MyBuild      # своя папка
```

Готовый пакет (~150 МБ) **не коммитится в git** — собирается скриптом.  
См. [releases/README.md](releases/README.md).

Скопируйте папку на любую Windows-машину и запустите:

```powershell
# Напрямую — нулевые зависимости
.\WinAdmin.Api.exe --urls http://localhost:8080

# Как Windows Service
sc.exe create WinAdmin binPath="C:\WinAdmin\WinAdmin.Api.exe --urls http://localhost:8080"
sc.exe start WinAdmin
```

## Тесты

```powershell
dotnet test
```

## Журналы Windows: размер журнала Security

Пресет **«Авторизация»** читает журнал `Security`. По умолчанию Windows хранит его в
режиме **Circular** с лимитом **20 МБ** — при заполнении старые записи перезаписываются.
На активной машине это часто даёт охват **минут или часов**, а не дней: фильтр «30 дней»
в WinAdmin не вернёт события, которых уже нет на диске.

Чтение `Security` требует запуска WinAdmin **от имени администратора** (иначе API вернёт
403).

### Проверить текущие настройки

```powershell
# wevtutil (встроенная утилита)
wevtutil gl Security

# или PowerShell
Get-WinEvent -ListLog Security | Select-Object LogName, LogMode, RecordCount,
  @{n='MaxSizeMB';e={[math]::Round($_.MaximumSizeInBytes/1MB,1)}},
  @{n='FileSizeMB';e={[math]::Round($_.FileSize/1MB,1)}}

# самая старая и новая запись (реальный охват журнала)
(Get-WinEvent -LogName Security -Oldest -MaxEvents 1).TimeCreated
(Get-WinEvent -LogName Security -MaxEvents 1).TimeCreated
```

### Увеличить размер (нужны права администратора)

Размер задаётся в байтах (`/ms:`). Примеры готовых значений:

```powershell
# 128 МБ  — заметно лучше дефолта, обычно хватает на рабочей станции
wevtutil sl Security /ms:134217728

# 512 МБ  — рекомендуемый минимум, если нужна история за дни
wevtutil sl Security /ms:536870912

# 1 ГБ    — для серверов или интенсивного аудита
wevtutil sl Security /ms:1073741824
```

Тот же эффект через PowerShell:

```powershell
Limit-EventLog -LogName Security -MaximumSize 512MB
```

Проверка после изменения:

```powershell
wevtutil gl Security | findstr /i "maxSize"
Get-WinEvent -ListLog Security | Select-Object MaximumSizeInBytes, FileSize
```

**Важно:** увеличение лимита не восстанавливает уже перезаписанные события — история
начнёт накапливаться только с момента применения настройки.

### Дополнительно (по желанию)

Включить журнал, если отключён:

```powershell
wevtutil sl Security /e:true
```

Задать политику при заполнении (по умолчанию — перезапись, `Circular`):

```powershell
# перезаписывать старые (типично для Security)
wevtutil sl Security /rt:true

# не перезаписывать — новые события не пишутся, пока журнал полон (редко нужно)
wevtutil sl Security /rt:false
```

На нескольких машинах тот же лимит можно задать через GPO:
**Конфигурация компьютера → Политики → Административные шаблоны →
Компоненты Windows → Event Log Service → Security**.

## Развёртывание под IIS

См. [docs/01-deploy-iis.md](docs/01-deploy-iis.md) и `releases/install.ps1`.

## Документация

- [docs/00-overview.md](docs/00-overview.md) — обзор и архитектура
- [docs/01-deploy-iis.md](docs/01-deploy-iis.md) — установка под IIS
- [docs/02-security.md](docs/02-security.md) — ключи, scopes, аудит
- [docs/03-api-reference.md](docs/03-api-reference.md) — справочник API
- [docs/04-integration-guides.md](docs/04-integration-guides.md) — примеры интеграции
- [docs/05-troubleshooting.md](docs/05-troubleshooting.md) — диагностика
