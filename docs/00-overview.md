# WinAdmin — обзор

**WinAdmin** — автономное веб-приложение для мониторинга и управления Windows-машиной
(рабочей станцией или сервером), размещаемое под IIS. По одному экземпляру на машину;
каждый экземпляр управляет своей локальной системой и предоставляет REST API для
интеграции с внешними системами.

## Возможности

- **Техническая информация:** ОС, версия, архитектура, производитель/модель, BIOS,
  CPU (ядра/потоки), объём RAM, время работы, сетевые адаптеры и адреса.
- **Диски «как в проводнике»:** физические диски → разделы → логические тома с
  цветными полосами заполнения, файловой системой и свободным местом.
- **Live-метрики:** загрузка CPU, использование памяти, сетевой трафик (приём/передача).
- **Службы:** список со статусом и типом запуска; запуск/остановка/перезапуск.
- **Процессы:** список приложений и фоновых процессов; завершение по PID.
- **Принтеры:** статус и очередь; пауза/возобновление/очистка очереди.
- **Питание:** перезагрузка, выключение, отмена запланированного действия.
- **API-доступ:** ключи с гранулярными scopes, локальная админка ключей, журнал аудита.
- **Документация:** Swagger UI + гайды интеграции (curl, PowerShell, C#, Python).

## Технологии

| Слой | Технология |
|------|------------|
| Backend | ASP.NET Core 10 (C#), `net10.0-windows` |
| Сбор данных | WMI/CIM (`System.Management`), `ServiceController`, `Process`, `PerformanceCounter`, P/Invoke |
| Хранилище | SQLite (EF Core) — API-ключи (хеш) и аудит |
| Frontend | React 19 + Vite + TypeScript + Ant Design + AG Grid + Recharts |
| Хостинг | IIS (ASP.NET Core Module, in-process); SPA отдаётся из `wwwroot` |
| API | REST, префикс `/api/v1`, аутентификация по `X-API-Key` |

## Структура решения

```
WinAdmin.slnx
├─ src/WinAdmin.Core            модели, DTO, интерфейсы, scopes
├─ src/WinAdmin.Infrastructure  сбор данных и действия (WMI/P/Invoke), хранилище (EF/SQLite)
├─ src/WinAdmin.Api             Web API + хостинг SPA (Program.cs, контроллеры, auth)
├─ web                          React-фронтенд (собирается в src/WinAdmin.Api/wwwroot)
├─ deploy                       web.config, install.ps1
└─ docs                         документация
```

## Требования

- Windows 10/11 или Windows Server 2019/2022.
- IIS с модулем ASP.NET Core (**ASP.NET Core Hosting Bundle**).
- Для полного управления (службы, питание) — пул приложения под учётной записью с
  правами локального администратора (см. [01-deploy-iis.md](01-deploy-iis.md)).

## Быстрый старт (разработка)

```powershell
# Backend (Kestrel, порт 5099)
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project src/WinAdmin.Api --urls http://localhost:5099

# Frontend (Vite dev, проксирует /api на backend)
npm --prefix web run dev
```

При первом запуске создаётся стартовый admin-ключ — он выводится в лог и сохраняется в
`bootstrap-key.txt` рядом с приложением.

См. также: [01-deploy-iis.md](01-deploy-iis.md) · [02-security.md](02-security.md) ·
[03-api-reference.md](03-api-reference.md) · [04-integration-guides.md](04-integration-guides.md) ·
[05-troubleshooting.md](05-troubleshooting.md)
