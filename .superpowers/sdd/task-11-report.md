# Task 11 Report: Verification gate

## Automated verification

- `dotnet test src/tests/WinAdmin.Tests/WinAdmin.Tests.csproj`
  - Result: PASS
  - Tests: 92 total, 92 passed, 0 failed, 0 skipped
  - Warnings: `NU1903` for `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, high severity advisory GHSA-2m69-gcr7-jv3q

- `dotnet build src/backend/WinAdmin.Api/WinAdmin.Api.csproj`
  - Result: PASS
  - Errors: 0
  - Warnings: 4 `NU1903` warnings for `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 / GHSA-2m69-gcr7-jv3q

- `npm --prefix src/frontend run build`
  - Result: PASS
  - TypeScript: `tsc -b` completed
  - Vite: 1685 modules transformed, production assets emitted to `src/backend/WinAdmin.Api/wwwroot`
  - Warning: main JS chunk is 2,904.25 kB minified / 855.54 kB gzip, above Vite's 500 kB warning threshold

## Read-only API smoke check

Started a local WinAdmin API instance using a temporary bootstrap key and performed only non-destructive requests.

- `GET /health`: `ok`, machine `INOTEBOOK`
- `GET /api/v1/software/applications`: PASS, returned 367 applications
- `GET /api/v1/software/updates`: PASS, returned 4 updates
- `GET /api/v1/software/jobs/active`: PASS, returned `204 No Content` via bounded `curl.exe`

No uninstall, rollback, process termination, reboot, or other destructive operation was performed.

## Manual elevated uninstall checklist

The following require a human on a Windows machine running WinAdmin elevated with a known disposable package or cancel-friendly uninstall flow:

1. Open Software -> Applications; verify the list is non-empty and the system-app toggle changes visibility.
2. Open Software -> Updates; verify KB rows are shown.
3. Uninstall a safe disposable test app; verify the drawer moves Running -> Succeeded/Failed and refreshes the list on success.
4. Start a second uninstall while the first runs; verify `409` / error toast.
5. Refresh the page mid-job; verify the drawer restores via `/jobs/active`.
6. Verify the audit log records completion after the job finishes.

## Fixes and commits

- Fixes made: none
- Commits made: none

## Concerns

- Existing dependency warning: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 has high severity advisory GHSA-2m69-gcr7-jv3q.
- Frontend build succeeds but emits a large-chunk warning for the main bundle.

## Whole-branch review fixes

- Fixed `SoftwareAppHelpers.SplitCommand` to preserve quoted command behavior and resolve existing unquoted `.exe`/`.msi`/`.bat`/`.cmd` paths with spaces before falling back to first-space splitting.
- Updated software job audit entries to use the display name as `Target` and include the target id in `Details`.
- Handled `restoreActive()` rejection on Applications and Updates mount so read-only users do not produce unhandled promise rejections.
- Verification: `SoftwareJobServiceTests` PASS (12), `SoftwareAppHelpersTests` PASS (14), full `WinAdmin.Tests` PASS (93), `npm --prefix src/frontend run lint` PASS with warnings, `npm --prefix src/frontend run build` PASS with large-chunk warning.
