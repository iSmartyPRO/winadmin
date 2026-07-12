# Журналы Windows — настройка на сервере

## Права

- Журнал **Security** (пресет «Авторизация») читается только при запуске WinAdmin
  **от имени администратора** или под учётной записью с правами чтения Security log.
- Без прав API вернёт **403**.

## Почему мало записей за «30 дней»

Журнал Security по умолчанию: **20 МБ**, режим **Circular** — старые события
перезаписываются. Реальный охват может быть **минуты или часы**, не недели.
Фильтр «30 дней» в WinAdmin показывает только то, что ещё есть на диске.

## Проверка

```powershell
wevtutil gl Security

Get-WinEvent -ListLog Security | Select-Object LogMode, RecordCount,
  @{n='MaxMB';e={[math]::Round($_.MaximumSizeInBytes/1MB,1)}}

(Get-WinEvent -LogName Security -Oldest -MaxEvents 1).TimeCreated
(Get-WinEvent -LogName Security -MaxEvents 1).TimeCreated
```

## Увеличение размера (Server 2019)

```powershell
# 128 МБ
wevtutil sl Security /ms:134217728

# 512 МБ — рекомендуется для сервера
wevtutil sl Security /ms:536870912

# 1 ГБ — интенсивный аудит
wevtutil sl Security /ms:1073741824

# через PowerShell
Limit-EventLog -LogName Security -MaximumSize 512MB
```

Проверка:

```powershell
wevtutil gl Security | findstr /i maxSize
```

Увеличение **не восстанавливает** уже удалённые события.

## GPO (несколько серверов)

**Конфигурация компьютера → Административные шаблоны → Компоненты Windows →
Event Log Service → Security** — задать максимальный размер.

## Пресет «Авторизация» в WinAdmin

Автоматически скрываются служебные входы (LogonType 3 и 5) и системные учётки
(переключатель «Только пользователи»). Остаются реальные входы, выходы и изменения
учётных записей.
