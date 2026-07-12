# Task 10 Report: Docs and ApiDocs

## Status
**Complete**

## Changes

### `docs/03-api-reference.md`
- Added global response codes `202` and `204` to the legend.
- New **Software** section documenting all 7 routes with scopes, JSON examples, and behavior:
  - `GET /software/applications`, `POST .../uninstall`
  - `GET /software/updates`, `POST .../uninstall`, `POST .../rollback`
  - `GET /software/jobs/{jobId}`, `GET /software/jobs/active`
- Documented in-memory jobs, single concurrent operation, `409` on conflict, `204` when no active job.

### `docs/00-overview.md`
- Added Software capability bullet (applications, updates, uninstall/rollback with progress drawer).

### `README.md`
- Added feature bullet: installed apps and Windows updates with progress-tracked removal.

### `src/frontend/src/pages/ApiDocs.tsx`
- Appended 7 Software endpoint rows to the in-app API table.

## Commit
```
docs: document Software API and UI feature
```

## Concerns
- `docs/04-integration-guides.md` not updated (out of scope for this task); Software curl/PowerShell examples could be added later.
- ApiDocs `rowKey="path"` remains unique across all rows; no collision with software paths.

## Next
Task 11 per plan (if any remaining integration/docs work).

---

## Task 10 Review Fixes

### Status
**Complete**

### Changes (`docs/03-api-reference.md`)
- JSON examples now use prefixed ids: `reg:…`, `store:…`, `upd:KB5039893`.
- Documented id prefixes and URL-encoding requirement for path params.
- Documented `409` response body: `{ "message": "…", "activeJobId": "…" }`.
- Noted 30-minute `Running` job timeout → `Failed`.

### Commit
```
docs: fix Software API reference review findings
```
