# WinAdmin.Ctl

Автономная GUI-утилита для установки, обновления и управления службой **WinAdmin**.

Один файл: `WinAdmin.Ctl.exe` (нативный, Windows x64, **~2 МБ**, без зависимостей от рантайма). Запуск с правами администратора (UAC).

## Возможности

- Установка WinAdmin с GitHub Releases
- Обновление до последней версии (база в `C:\ProgramData\WinAdmin` сохраняется)
- Проверка локальной и удалённой версии
- Статус службы `WinAdmin`, запуск / остановка / перезапуск
- Регистрация службы, если файлы установлены, но служба не зарегистрирована
- Удаление: остановка и снятие службы + правила фаервола, с отдельным вопросом об удалении папки установки
- Редактируемые путь установки и порт (сохраняются между запусками)
- Кликабельная ссылка на запущенный экземпляр (кнопка **Open**)
- Управление пользователями: просмотр, добавление, сброс пароля, удаление (вкладка **Users**)

## Дефолты

| Параметр | Значение |
|----------|----------|
| Репозиторий | `iSmartyPRO/winadmin` |
| Путь установки | `C:\apps\WinAdmin` |
| Порт | `8080` |
| Служба | `WinAdmin` |
| Данные | `C:\ProgramData\WinAdmin` |

## Сборка (разработчик)

Требуется:

- Rust toolchain: `winget install Rustlang.Rustup` (по умолчанию `stable-x86_64-pc-windows-msvc`)
- MSVC-линкер: Visual Studio Build Tools + Windows SDK (компонент «VC.Tools.x86.x64»)

```powershell
.\releases\ctl\build-ctl.ps1
```

Результат: `releases\ctl\WinAdmin.Ctl.exe` (~2 МБ)

## Использование

1. Скачайте или соберите `WinAdmin.Ctl.exe`
2. Запустите от имени администратора (или подтвердите UAC)
3. Вкладка **Control**:
   - **Install** — если WinAdmin ещё не установлен (или **Register**, если файлы есть, но служба не зарегистрирована)
   - **Update** — если на GitHub есть более новая версия
   - **Start / Stop / Restart** — управление службой
   - **Uninstall** — остановить и удалить службу; вторым вопросом предложит удалить папку установки
   - **Open** — открыть `http://localhost:<порт>` в браузере
4. Вкладка **Users** (после установки):
   - **Add user**, **Reset password**, **Delete user**

Пользователей также можно создавать вручную:

```powershell
cd C:\apps\WinAdmin
.\WinAdmin.exe user add --login admin --password "YourPassword" --scopes admin
```

Откройте: http://localhost:8080

## Исходники

`src/ctl/winadmin-ctl/` — Rust + [native-windows-gui](https://crates.io/crates/native-windows-gui).
Управление службой выполняется через `sc.exe`, операции с пользователями — через `WinAdmin.exe`.
