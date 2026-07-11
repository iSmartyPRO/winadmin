# Интеграция с внешними системами

Примеры вызова API WinAdmin из других систем. Замените `HOST`, `PORT` и `sp_КЛЮЧ`
на свои значения. Ключ должен иметь нужный scope (см. [02-security.md](02-security.md)).

## curl

```bash
# Информация о системе
curl -H "X-API-Key: sp_КЛЮЧ" http://HOST:PORT/api/v1/system

# Диски
curl -H "X-API-Key: sp_КЛЮЧ" http://HOST:PORT/api/v1/disks

# Перезапуск службы
curl -X POST -H "X-API-Key: sp_КЛЮЧ" \
  http://HOST:PORT/api/v1/services/Spooler/restart

# Перезагрузка через 60 секунд
curl -X POST -H "X-API-Key: sp_КЛЮЧ" -H "Content-Type: application/json" \
  -d '{"delaySeconds":60,"comment":"Обновление"}' \
  http://HOST:PORT/api/v1/power/reboot
```

## PowerShell

```powershell
$h = @{ "X-API-Key" = "sp_КЛЮЧ" }
$base = "http://HOST:PORT/api/v1"

# Информация о системе
Invoke-RestMethod -Uri "$base/system" -Headers $h

# Метрики (для мониторинга)
$m = Invoke-RestMethod -Uri "$base/system/metrics" -Headers $h
"CPU: $($m.cpuUsagePercent)%  RAM: $($m.memoryUsagePercent)%"

# Остановить службу
Invoke-RestMethod -Method Post -Uri "$base/services/Spooler/stop" -Headers $h

# Выключение с принудительным закрытием приложений
$body = @{ delaySeconds = 30; force = $true } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$base/power/shutdown" -Headers $h `
  -Body $body -ContentType "application/json"
```

## C# (.NET HttpClient)

```csharp
using System.Net.Http;
using System.Net.Http.Json;

var http = new HttpClient { BaseAddress = new Uri("http://HOST:PORT/api/v1/") };
http.DefaultRequestHeaders.Add("X-API-Key", "sp_КЛЮЧ");

// Информация о системе
var system = await http.GetFromJsonAsync<JsonElement>("system");
Console.WriteLine(system.GetProperty("hostname").GetString());

// Перезапуск службы
var resp = await http.PostAsync("services/Spooler/restart", null);
Console.WriteLine(await resp.Content.ReadAsStringAsync());

// Перезагрузка
await http.PostAsJsonAsync("power/reboot", new { delaySeconds = 60, comment = "Maintenance" });
```

## Python (requests)

```python
import requests

BASE = "http://HOST:PORT/api/v1"
H = {"X-API-Key": "sp_КЛЮЧ"}

# Информация о системе
r = requests.get(f"{BASE}/system", headers=H, timeout=10)
r.raise_for_status()
print(r.json()["hostname"])

# Метрики
m = requests.get(f"{BASE}/system/metrics", headers=H).json()
print(f"CPU {m['cpuUsagePercent']}%  RAM {m['memoryUsagePercent']}%")

# Завершить процесс по PID
requests.delete(f"{BASE}/processes/1234", headers=H)

# Перезагрузка
requests.post(f"{BASE}/power/reboot", headers=H,
              json={"delaySeconds": 60, "comment": "Update"})
```

## Типовые сценарии

**Мониторинг парка машин.** Система мониторинга периодически опрашивает
`GET /system/metrics` и `GET /disks` на каждой машине (ключ со scopes `system.read`,
`disks.read`) и строит дашборды/алерты (например, том заполнен > 90%).

**Удалённый перезапуск службы из service desk.** При инциденте оркестратор вызывает
`POST /services/{name}/restart` (scope `services.manage`); результат и инициатор
фиксируются в аудите WinAdmin.

**Плановое обслуживание.** Планировщик рассылает `POST /power/reboot` с задержкой и
комментарием (scope `power.manage`); при отмене окна — `POST /power/cancel`.

## Обработка ошибок

- `401` — отсутствует/неверный/отозванный ключ.
- `403` — у ключа нет требуемого scope.
- `409` — действие не выполнено (тело содержит `{ "success": false, "message": "..." }`).
- `429` — превышен лимит запросов; повторите позже.

Всегда проверяйте поле `success` в ответах управляющих действий.
