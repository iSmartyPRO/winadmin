# HTTPS для панели (часть 2 платформы модулей)

**Дата:** 2026-10-14
**Статус:** реализуется (ветка `feat/https`)

## Зачем

В режиме «Сеть» панель работает по HTTP: пароли, JWT и cookie `wa_refresh` идут открытым текстом, а вход
учёткой домена по паролю разрешён только с loopback (см. `2026-10-09-module-platform-design.md`).
Часть 2 добавляет HTTPS, после чего пароль домена принимается и по сети (`LoginTransport` уже
разрешает `Request.IsHttps`).

## Решения

| Вопрос | Решение | Почему |
|---|---|---|
| Где хранить | Объект `https` в `network.json` рядом с `mode/port/allow` | Тот же файл уже перепривязывает Kestrel на лету, тот же откат и CLI |
| Источник сертификата | `store` (отпечаток в `LocalMachine\My` или `CurrentUser\My`), `pfx` (путь + пароль), `selfSigned` | Корпоративный ЦС → `store`; выгрузка из другого места → `pfx`; «быстро включить» → `selfSigned` |
| Пароль PFX | В `network.json` только зашифрованным `ISecretProtector`, purpose `network.https.pfx`; API отдаёт «задан/не задан» | Секреты не лежат открытым текстом; значение нельзя подставить в другое поле |
| Самоподписанный | `CertificateRequest`, RSA 2048, SHA-256, SAN = FQDN + короткое имя + `localhost` (+ имена от администратора), 2 года, EKU serverAuth. Файл `keys\https.pfx` (каталог ключей с защищённым DACL), случайный пароль зашифрован в `keys\https.pfx.password` (purpose `network.https.selfsigned`) | Пароль не связан с `network.json`: повторная генерация заменяет оба файла сразу |
| Основной порт | При `enabled` — только HTTPS, endpoint `Https` с `Protocols = Http1` | **Negotiate/Kerberos SSO не работает по HTTP/2** (аутентификация привязана к TCP-соединению, обработчик Negotiate на HTTP/2 отказывает), браузер по ALPN выбрал бы HTTP/2 |
| Сертификат в Kestrel | `ConfigureHttpsDefaults → ServerCertificateSelector` берёт сертификат из `HttpsCertificateHolder`; holder обновляется при перечитывании `network.json` | Пароль PFX не попадает в `IConfiguration`; смена сертификата не требует остановки слушателя |
| Перенаправление | `httpRedirectPort` (необязательно): HTTP-слушатель на том же адресе, отвечает **308** на `https://{host}:{port}{путь}` | 308 сохраняет метод и тело (POST API) |
| HSTS | `Strict-Transport-Security: max-age=31536000` только на HTTPS-ответах, без `includeSubDomains` | Требование; браузеры игнорируют HSTS при ошибке сертификата, поэтому для самоподписанного он фактически не действует |
| Cookie `wa_refresh` | `Secure`, если `Request.IsHttps`; иначе как раньше (`!IsDevelopment`) | По HTTPS cookie никогда не уйдёт открытым текстом |
| Применение | Тот же путь, что смена порта: проверка → ответ 200 → брандмауэр + запись файла → перепривязка Kestrel → `NetworkApplyWatchdog` | Единый механизм отката |
| Подтверждение в watchdog | Для HTTPS кроме «порт слушается» выполняется TLS-рукопожатие с `127.0.0.1:{port}`; holder без сертификата → откат | «Порт слушается» не значит, что TLS работает |
| Проверка перед применением | Сертификат найден, есть закрытый ключ, срок действия начался и не истёк, при наличии EKU — есть serverAuth; иначе 400 с русским текстом | Нельзя включить HTTPS, который заведомо не заработает |
| Брандмауэр | Одно правило `WinAdmin (managed)`, `localport={port},{httpRedirectPort}` | Остаётся одно управляемое правило |
| Битый сертификат при старте | HTTPS не выключается автоматически: ошибка в журнале и в проверке `platform.https`; аварийный путь — `WinAdmin.exe network https --disable` | Тихий откат на HTTP снизил бы защиту без ведома администратора |

## API (право `platform.network.manage`)

- `GET /api/v1/settings/network` — добавлено поле `https`: `enabled`, `source`, `thumbprint`, `storeLocation`,
  `pfxPath`, `pfxPasswordSet`, `httpRedirectPort`, `certificate` (сведения: отпечаток, субъект, DNS из SAN,
  срок действия) и `certificateError`.
- `PUT /api/v1/settings/network` — необязательное поле `https` (`enabled`, `source`, `thumbprint`,
  `storeLocation`, `pfxPath`, `pfxPassword`, `httpRedirectPort`). `https` не передан → прежние настройки HTTPS.
  `pfxPassword: null` — оставить сохранённый пароль.
- `GET /api/v1/settings/network/certificates` — сертификаты из `LocalMachine\My` и `CurrentUser\My`
  с закрытым ключом.
- `POST /api/v1/settings/network/https/self-signed` `{ dnsNames?: [] }` — сгенерировать, вернуть сведения.

## CLI

```
WinAdmin.exe network https --show
WinAdmin.exe network https --enable --thumbprint X [--store LocalMachine|CurrentUser]
WinAdmin.exe network https --enable --pfx C:\certs\panel.pfx [--pfx-password ...]
WinAdmin.exe network https --enable --self-signed [--dns имя ...]
WinAdmin.exe network https --redirect-port 80      (0 — выключить перенаправление)
WinAdmin.exe network https --disable
```

## Проверка окружения `platform.https` (`HttpsCheck`, модуль `platform`)

- «Сеть» без HTTPS → предупреждение «пароли доменных учёток по сети недоступны; используйте HTTPS».
- Сертификат не найден / нет ключа / истёк → ошибка; истекает менее чем через 30 дней → предупреждение;
  DNS-имя машины не входит в SAN → предупреждение.

## Не входит

- Привязка через http.sys/netsh, ACME/Let's Encrypt, автоматический выпуск из корпоративного ЦС.
- Клиентские сертификаты, выбор версий TLS (используются значения ОС).
