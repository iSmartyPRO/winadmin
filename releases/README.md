# Сборка релиза

Готовый пакет **не хранится в git** (~150–180 МБ). Соберите локально:

```powershell
# По умолчанию → releases/dist/
.\releases\build.ps1

# Версионированная папка
.\releases\build.ps1 -OutputPath ".\releases\v1.0.0"
```

Скрипт `build.ps1`:

1. Собирает фронтенд (`npm run build` → `wwwroot`)
2. Публикует backend self-contained (`win-x64`, .NET внутри пакета)
3. Копирует документацию и `install-service.ps1` из `releases/package/`

## Файлы

| Файл | Назначение |
|---|---|
| `build.ps1` | Сборка self-contained пакета |
| `install-service.ps1` | Установка как службы Windows (копируется в пакет) |
| `install.ps1` | Установка под IIS |
| `web.config` | Эталон для IIS |
| `package/` | README, VERSION, docs — включаются в каждый релиз |

## Требования для сборки

- .NET 10 SDK
- Node.js (для фронтенда)

На целевой машине (Server 2019+) **ничего ставить не нужно** — пакет self-contained.
