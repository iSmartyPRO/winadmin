# WinAdmin

Автономное веб-приложение для **мониторинга и управления Windows-машиной** (рабочей
станцией или сервером). Один экземпляр на машину; управляет локальной системой и
предоставляет REST API для интеграции с внешними системами.

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

## Сборка пакета для переноса (без зависимостей на целевой машине)

```powershell
.\releases\build.ps1                             # → releases/dist/
.\releases\build.ps1 -OutputPath C:\MyBuild      # своя папка
```

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

## Развёртывание под IIS

См. [docs/01-deploy-iis.md](docs/01-deploy-iis.md) и `releases/install.ps1`.

## Документация

- [docs/00-overview.md](docs/00-overview.md) — обзор и архитектура
- [docs/01-deploy-iis.md](docs/01-deploy-iis.md) — установка под IIS
- [docs/02-security.md](docs/02-security.md) — ключи, scopes, аудит
- [docs/03-api-reference.md](docs/03-api-reference.md) — справочник API
- [docs/04-integration-guides.md](docs/04-integration-guides.md) — примеры интеграции
- [docs/05-troubleshooting.md](docs/05-troubleshooting.md) — диагностика
