# Сборка и установка релизов

## На конечном компьютере (Server 2019+)

Готовый ZIP: **https://github.com/iSmartyPRO/winadmin/releases**

Подробная инструкция со скриптами и блоками copy-paste:  
**[package/docs/github-releases.md](package/docs/github-releases.md)**

### Быстрая установка (PowerShell от администратора)

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$dir = "$env:TEMP\winadmin-scripts"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$base = 'https://raw.githubusercontent.com/iSmartyPRO/winadmin/main/releases'
Invoke-WebRequest "$base/download-release.ps1" -OutFile "$dir\download-release.ps1" -UseBasicParsing
Invoke-WebRequest "$base/install-from-github.ps1" -OutFile "$dir\install-from-github.ps1" -UseBasicParsing
& "$dir\install-from-github.ps1" -Port 8080
```

### Обновление (если скрипты уже в `C:\apps\WinAdmin\`)

```powershell
cd C:\apps\WinAdmin
.\update-release.ps1 -Port 8080
```

---

## Скрипты

| Скрипт | Где | Назначение |
|---|---|---|
| `download-release.ps1` | `releases/` + в ZIP | Скачать и распаковать с GitHub |
| `install-from-github.ps1` | `releases/` + в ZIP | Скачать + установить службу |
| `update-release.ps1` | `releases/` + в ZIP | Обновить установку |
| `install-service.ps1` | `releases/` + в ZIP | Только служба Windows |
| `build.ps1` | `releases/` | Сборка пакета (разработчик) |
| `install.ps1` | `releases/` | Установка под IIS |

---

## Публикация нового релиза (разработчик)

Готовый пакет **не хранится в git** (~150–180 МБ). Публикуется через GitHub Actions:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

Через несколько минут ZIP появится на https://github.com/iSmartyPRO/winadmin/releases

### Собрать локально

```powershell
.\releases\build.ps1
.\releases\build.ps1 -OutputPath ".\releases\v1.0.0"
```

`build.ps1`:

1. Собирает фронтенд (`npm run build` → `wwwroot`)
2. Публикует backend self-contained (`win-x64`)
3. Копирует документацию и скрипты из `releases/package/` и `releases/*.ps1`

## Требования для сборки

- .NET 10 SDK
- Node.js

На целевой машине **ничего ставить не нужно** — пакет self-contained.
