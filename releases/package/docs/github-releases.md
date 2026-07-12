# Установка и обновление с GitHub Releases

Готовый пакет публикуется здесь: **https://github.com/iSmartyPRO/winadmin/releases**

На конечном компьютере **не нужны** git, .NET SDK и Node.js — только PowerShell и права администратора.

---

## Скрипты (в папке установки или `releases/` в репозитории)

| Скрипт | Назначение |
|---|---|
| `download-release.ps1` | Скачать ZIP и распаковать |
| `install-from-github.ps1` | Скачать + установить службу |
| `update-release.ps1` | Обновить существующую установку |
| `install-service.ps1` | Только регистрация службы (если ZIP уже распакован) |

---

## Вариант A — одна команда (первичная установка)

PowerShell **от имени администратора** на целевом сервере.

Скачать скрипты с GitHub и установить последний релиз в `C:\WinAdmin` на порту `8080`:

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$dir = "$env:TEMP\winadmin-scripts"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$base = 'https://raw.githubusercontent.com/iSmartyPRO/winadmin/main/releases'
Invoke-WebRequest "$base/download-release.ps1" -OutFile "$dir\download-release.ps1" -UseBasicParsing
Invoke-WebRequest "$base/install-from-github.ps1" -OutFile "$dir\install-from-github.ps1" -UseBasicParsing
& "$dir\install-from-github.ps1" -Port 8080
```

Конкретная версия и другой порт:

```powershell
& "$dir\install-from-github.ps1" -Version 1.0.0 -InstallPath C:\WinAdmin -Port 9090
```

Создать пользователя:

```powershell
cd C:\WinAdmin
.\WinAdmin.Api.exe user add --login admin --password "YourPassword" --scopes admin
```

---

## Вариант B — только скачать и распаковать

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$dir = "$env:TEMP\winadmin-scripts"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Invoke-WebRequest 'https://raw.githubusercontent.com/iSmartyPRO/winadmin/main/releases/download-release.ps1' `
  -OutFile "$dir\download-release.ps1" -UseBasicParsing

# последний релиз
& "$dir\download-release.ps1" -InstallPath C:\WinAdmin

# или конкретная версия
& "$dir\download-release.ps1" -Version 1.0.0 -InstallPath C:\WinAdmin
```

Затем вручную:

```powershell
cd C:\WinAdmin
.\install-service.ps1 -Port 8080
```

---

## Вариант C — если скрипты уже в папке установки

После первой установки `download-release.ps1` и `update-release.ps1` лежат в `C:\WinAdmin\`.

**Обновление до последней версии:**

```powershell
cd C:\WinAdmin
.\update-release.ps1 -Port 8080
```

**Обновление до конкретной версии:**

```powershell
.\update-release.ps1 -Version 1.0.1 -Port 9090
```

База данных (`C:\ProgramData\WinAdmin\WinAdmin.db`) при обновлении **не удаляется**.

---

## Вариант D — вручную через браузер

1. Откройте https://github.com/iSmartyPRO/winadmin/releases  
2. Скачайте `WinAdmin-x.y.z-win-x64.zip`  
3. Распакуйте в `C:\WinAdmin\`  
4. Запустите `install-service.ps1` (см. README.md в корне пакета)

---

## Вариант E — копировать вставить без скриптов

Последний релиз:

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$installDir = 'C:\WinAdmin'
$repo = 'iSmartyPRO/winadmin'

$release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'WinAdmin' }
$asset = $release.assets | Where-Object { $_.name -like 'WinAdmin-*-win-x64.zip' } | Select-Object -First 1
$zip = Join-Path $env:TEMP $asset.name

Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing
if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
New-Item -ItemType Directory -Path $installDir | Out-Null
Expand-Archive -Path $zip -DestinationPath $installDir -Force

cd $installDir
.\install-service.ps1 -Port 8080
```

Конкретная версия `1.0.0`:

```powershell
$version = '1.0.0'
$release = Invoke-RestMethod "https://api.github.com/repos/iSmartyPRO/winadmin/releases/tags/v$version" -Headers @{ 'User-Agent' = 'WinAdmin' }
$asset = $release.assets | Where-Object { $_.name -eq "WinAdmin-$version-win-x64.zip" }
# ... далее как выше
```

---

## Параметры скриптов

### download-release.ps1

| Параметр | По умолчанию | Описание |
|---|---|---|
| `-Repo` | `iSmartyPRO/winadmin` | Репозиторий GitHub |
| `-Version` | (пусто) | Версия без `v`; пусто = latest |
| `-InstallPath` | `C:\WinAdmin` | Куда распаковать |
| `-Token` | (пусто) | PAT для private repo |

### install-from-github.ps1

Все параметры `download-release.ps1` плюс:

| Параметр | По умолчанию | Описание |
|---|---|---|
| `-Port` | `8080` | Порт HTTP |
| `-ServiceName` | `WinAdmin` | Имя службы Windows |

### update-release.ps1

Те же параметры, что у `install-from-github.ps1`. Останавливает службу перед заменой файлов.

---

## Private repository

```powershell
$token = 'ghp_xxxxxxxx'
& .\download-release.ps1 -Token $token -InstallPath C:\WinAdmin
```

---

## Требования

- Windows Server 2019+ / Windows 10/11 **x64**
- PowerShell 5.1+ (встроен в Windows)
- Права **локального администратора**
- Исходящий HTTPS к `github.com` (или ZIP с флешки/сетевой папки)
