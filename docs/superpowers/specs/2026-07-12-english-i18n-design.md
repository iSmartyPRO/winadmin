# English localization via frontend i18n

**Date:** 2026-07-12  
**Status:** Approved for planning  
**Approach:** Frontend `react-i18next` (default `en`); backend/CLI/docs direct English replacement

## Goal

Make WinAdmin English-first everywhere users and operators look: SPA UI, API/CLI messages, product docs, and release scripts. Keep the door open for a future Russian (or other) UI locale without adding a language switcher in v1.

## Scope

### In scope

| Area | Method |
|------|--------|
| Frontend UI (pages, auth, layout, toasts, validation, presets) | `i18next` + `react-i18next`; strings in JSON locale files |
| Ant Design built-in copy | Sync `ConfigProvider.locale` with i18n language (`en_US` default) |
| Backend user-visible strings (API errors, `OperationResult`, CLI help/output) | Direct English literals |
| Public API model / Swagger descriptions where Russian | Direct English |
| Root `README.md`, `docs/00`–`05`, `releases/**` docs and user-facing script text | Direct English rewrite |
| Tests asserting user-facing Russian strings | Update expectations to English |

### Out of scope

- `docs/superpowers/specs/**` and `plans/**` (historical design artifacts)
- Build artifacts (`artifacts/`, `node_modules/`, `target/`, compiled `wwwroot` bundles — regenerate via build)
- Renaming routes, scope identifiers (`system.read`), audit action codes (`service.stop`), DB schema
- ASP.NET Core resource-based localization / Accept-Language negotiation
- In-app language switcher UI (v1 fixed to `en`)

### Explicit keep (not “user language”)

- OS-localized account match aliases such as `"СИСТЕМА"` alongside `"SYSTEM"` for Russian Windows — these are data matchers, not UI copy.

## Decisions

1. **Approach:** i18n for the React SPA; English literals for backend, CLI, and docs (hybrid). Chosen over a full in-place string replace (no future locale path) and over deferring docs to a second pass.
2. **Default language:** `en`, with `en` as fallback.
3. **Optional `ru` dictionary:** Ship a Russian mirror of the same keys so a future switch is cheap; default active locale remains `en`. No switcher in v1.
4. **Namespaces:** Split locale JSON by concern (`common`, `pages`, `logs`) under `src/frontend/src/i18n/locales/{lang}/`.
5. **Backend:** No `.resx` / satellite assemblies in this work — English strings in code are enough for scope A (product English everywhere operators see).
6. **Comments:** Translate Russian comments when editing a file for messages; no dedicated comment-only sweep.

## Frontend design

### Dependencies

- `i18next`
- `react-i18next`

### Layout

```
src/frontend/src/i18n/
  index.ts                 # init, default lng: 'en', fallbackLng: 'en'
  locales/
    en/
      common.json          # nav, auth, shared actions/errors
      pages.json           # Dashboard, Disks, Services, Processes, …
      logs.json            # Event log presets, filters, severity labels
    ru/                    # optional mirror of the same keys
      common.json
      pages.json
      logs.json
```

### Wiring

- Import i18n module from `main.tsx` before `createRoot`.
- Replace `antd/locale/ru_RU` with locale derived from i18n language (`en` → `en_US`, `ru` → `ru_RU` if ever activated).
- Components use `useTranslation('<ns>')` and `t('key')`; dynamic values via interpolation (`t('services.unknownAction', { action })`).
- No hardcoded Cyrillic (or English prose that belongs in dictionaries) left in TSX for user-visible text.

### Migration rule

Every user-visible string currently in Russian moves to `en/*.json` (English) and, if the `ru` mirror is kept, to `ru/*.json` (current Russian). Components only reference keys.

## Backend / CLI / docs design

- Controllers, infrastructure operation messages, and CLI (`UserCommands`, `CliRunner`) return/print English.
- Product documentation rewritten in clear American English; keep product name **WinAdmin** and existing technical terms (API key, scopes, Security log).
- PowerShell scripts: English `Write-Host` / comments that operators read.

## Verification

1. Search for Cyrillic outside excluded paths; remaining hits only OS aliases and `docs/superpowers/**`.
2. `npm --prefix src/frontend run build`
3. `dotnet test`
4. Manual spot-check: login, sidebar labels, one event-log page, one API error path.

## Non-goals / risks

- **Mixed language during partial deploy:** Frontend EN + backend RU until both land — ship as one change set.
- **Stale compiled wwwroot:** Rebuild frontend into API wwwroot as part of release process; do not hand-edit artifacts.
- **Windows Event Log payload text:** Event messages from the OS stay in the OS language; only WinAdmin chrome is localized.

## Success criteria

- Default UI is English (Ant Design + app strings).
- API/CLI user-facing messages are English.
- Product README and `docs/00`–`05` / release docs are English.
- `ru` locale files ship as a key-compatible mirror of `en` (no switcher; default remains `en`).
- Historical superpowers specs/plans untouched.
- OS match aliases like `СИСТЕМА` retained.
