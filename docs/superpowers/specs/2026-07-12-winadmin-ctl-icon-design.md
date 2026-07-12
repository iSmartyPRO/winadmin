# WinAdmin.Ctl icon and header branding

**Date:** 2026-07-12  
**Status:** Approved design  
**Scope:** Title-bar / taskbar icon + in-window header logo for `WinAdmin.Ctl`

## Problem

`app.ico` is already embedded via `winadmin-ctl.rc` (resource id `1`), but `Window::builder()` does not set an icon, so the title bar and taskbar show no branding. The Control UI also has no product logo.

## Goal

1. Show the WinAdmin icon in the window title bar and taskbar.
2. Show the same icon in a header row above the tabs, next to the text **WinAdmin.Ctl**.

## Approach

Reuse the existing embedded `app.ico` (approach A from brainstorming). No new image assets, no `image-decoder` feature.

## UI layout

```
┌─ [icon] WinAdmin.Ctl ─────────────────────────── □ ■ ✕ ┐
│  [32×32 logo]  WinAdmin.Ctl                        │
│  ┌ Control ┬ Users ─────────────────────────────┐  │
│  │  … existing Control / Users content …         │  │
│  └──────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────┘
```

- Header strip ~40–48 px tall, parented to the main window (above `TabsContainer`).
- Left: `ImageFrame` 32×32 with the app icon.
- Next to it: `Label` with text `WinAdmin.Ctl` (slightly larger than body labels).
- `TabsContainer` moved down by the header height; window client height increased by the same amount so tab content is not squeezed.

## Technical design

### Window icon

- Load `nwg::Icon` from the process embed resource (id `1`, already declared in `winadmin-ctl.rc`).
- Pass it to `nwg::Window::builder().icon(Some(&icon))`.
- Keep the Icon alive for the lifetime of the UI (store on `Ui` or equivalent).

### In-window logo

- Same `Icon` instance (or a second load at 32×32 if NWG requires a sized copy) assigned to `ImageFrame::builder().icon(Some(&icon))`.
- Prefer loading via `Icon::builder().source_embed(...)` / `from_embed` so the shipped exe stays self-contained (no path to `app.ico` at runtime).

### Files touched

| File | Change |
|------|--------|
| `src/ctl/winadmin-ctl/src/main.rs` | Load icon; set window icon; add header `ImageFrame` + `Label`; shift tabs; grow window |
| `src/ctl/winadmin-ctl/Cargo.toml` | Enable NWG `embed-resource` feature if required for `from_embed` |
| `winadmin-ctl.rc` | No change expected (resource id `1` already present) |

### Out of scope

- Redesigning Control/Users layouts beyond the header shift
- New PNG/BMP assets or SVG rasterization
- Changing the product icon artwork

## Verification

1. Rebuild with `.\releases\ctl\build-ctl.ps1`.
2. Run `releases\ctl\WinAdmin.Ctl.exe` (elevated): title bar and taskbar show the blue WinAdmin icon.
3. Confirm header shows 32×32 logo + **WinAdmin.Ctl** above the tabs; Control and Users tabs still layout correctly.
4. Confirm exe Explorer icon still comes from the embedded `.ico` resource.
