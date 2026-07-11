# Справочник API

Базовый URL: `http(s)://<host>:<port>/api/v1`
Аутентификация: заголовок `X-API-Key: sp_<секрет>` (кроме `/health`).
Интерактивная спецификация: `/swagger`, машиночитаемая: `/swagger/v1/swagger.json`.

Коды ответов: `200` OK · `201` создано · `400` неверный запрос · `401` нет/неверный ключ ·
`403` нет нужного scope · `404` не найдено · `409` действие не выполнено · `429` лимит запросов.

---

## Система

### `GET /system` — scope `system.read`
Сводная информация о машине.

```json
{
  "hostname": "WS-01", "domain": "CORP", "osName": "Windows 11 Pro",
  "osVersion": "10.0.26200", "osArchitecture": "64-bit",
  "manufacturer": "Dell Inc.", "model": "Latitude", "biosVersion": "1.2.3",
  "cpuName": "Intel Core Ultra 7", "cpuPhysicalCores": 16, "cpuLogicalCores": 22,
  "totalMemoryBytes": 16730009600, "lastBootTime": "2026-06-23T08:00:00+06:00",
  "uptime": "10:02:33", "networkAdapters": [
    { "name": "Ethernet", "macAddress": "AA:BB:...", "ipAddresses": ["192.168.1.5"], "isUp": true, "speedBitsPerSec": 1000000000 }
  ]
}
```

### `GET /system/metrics` — scope `system.read`
Мгновенные метрики.

```json
{ "cpuUsagePercent": 7.6, "memoryTotalBytes": 16730009600, "memoryUsedBytes": 14500000000,
  "memoryUsagePercent": 86.8, "networkBytesSentPerSec": 92600, "networkBytesReceivedPerSec": 19000,
  "timestamp": "2026-06-23T12:00:00Z" }
```

## Диски

### `GET /disks` — scope `disks.read`
Физические диски с томами.

```json
[ { "model": "SSSTC CL4-8D1024", "interfaceType": "SCSI", "mediaType": "SSD",
    "sizeBytes": 1024209543168, "partitions": 3,
    "volumes": [ { "drive": "C:", "label": "iDisk", "fileSystem": "NTFS",
      "driveType": "Fixed", "totalBytes": 1023117619200, "freeBytes": 565251514368,
      "usedBytes": 457866104832, "usedPercent": 44.8 } ] } ]
```

## Службы

### `GET /services` — scope `services.read`
```json
[ { "name": "Spooler", "displayName": "Диспетчер печати", "status": "Running",
    "startType": "Auto", "canStop": true, "canPauseAndContinue": false, "account": "LocalSystem" } ]
```

### `POST /services/{name}/{action}` — scope `services.manage`
`action` ∈ `start` | `stop` | `restart`. Ответ: `OperationResult` (`200` или `409`).

```json
{ "success": true, "message": "Служба 'Spooler' перезапущена" }
```

## Процессы

### `GET /processes` — scope `processes.read`
```json
[ { "pid": 1234, "name": "chrome", "mainWindowTitle": "...", "workingSetBytes": 650000000,
    "threadCount": 42, "startTime": "...", "hasWindow": true } ]
```

### `DELETE /processes/{pid}` — scope `processes.manage`
Завершает процесс (с дочерним деревом). Ответ: `OperationResult`.

## Принтеры

### `GET /printers` — scope `printers.read`
```json
[ { "name": "HP M428", "portName": "192.168.1.50", "driverName": "HP ...",
    "isDefault": true, "isShared": false, "workOffline": false, "status": "Idle", "queuedJobs": 0 } ]
```

### `POST /printers/{name}/{action}` — scope `printers.manage`
`action` ∈ `pause` | `resume` | `purge`. Ответ: `OperationResult`.

## Питание

### `POST /power/reboot` · `POST /power/shutdown` — scope `power.manage`
Тело (опционально):

```json
{ "delaySeconds": 30, "comment": "Плановое обслуживание", "force": false }
```

### `POST /power/cancel` — scope `power.manage`
Отменяет запланированное действие. Ответ: `OperationResult`.

## Администрирование

### `GET /apikeys` · `GET /apikeys/scopes` · `POST /apikeys` · `DELETE /apikeys/{id}` — scope `admin`
Создание:

```json
// Запрос
{ "name": "monitoring", "scopes": ["system.read","disks.read"], "expiresAt": "2027-01-01T00:00:00Z" }
// Ответ 201
{ "key": { "id": "...", "name": "monitoring", "scopes": [...], "hint": "Ab12" },
  "plaintextKey": "sp_..." }   // показывается один раз
```

### `GET /audit?limit=200&actor=<имя>` — scope `admin`
Записи журнала аудита (по убыванию времени).

## Служебное

### `GET /health` — без ключа
```json
{ "status": "ok", "machine": "WS-01", "time": "2026-06-23T12:00:00Z" }
```
