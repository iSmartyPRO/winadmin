# WinAdmin.Ctl Icon + Header Branding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the WinAdmin icon in the WinAdmin.Ctl title bar/taskbar and add a header row above the tabs with a 32×32 logo plus the text `WinAdmin.Ctl`.

**Architecture:** Reuse the already-embedded `app.ico` (RC resource id `1`). Load it via `nwg::EmbedResource` into `nwg::Icon`, pass one icon to `Window::builder().icon(...)`, and display a 32×32 copy in an `ImageFrame` next to a title `Label` above `TabsContainer`. Grow the window height so tab content is not squeezed.

**Tech Stack:** Rust, `native-windows-gui` 1.x (default `all` features include `embed-resource`), existing `winadmin-ctl.rc` / `app.ico`.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-12-winadmin-ctl-icon-design.md`
- Source icon: existing `app.ico` / RC id `1` — do **not** add PNG/BMP assets or enable extra crates beyond what NWG already provides
- Header: 32×32 logo + text **WinAdmin.Ctl** above the tabs
- No Control/Users layout redesign beyond shifting tabs down and growing window height
- Manual GUI verification (no automated UI test harness in this crate)

## File map

| File | Responsibility |
|------|----------------|
| `src/ctl/winadmin-ctl/src/main.rs` | Load icons; set window icon; header `ImageFrame` + `Label`; shift tabs; grow window |
| `src/ctl/winadmin-ctl/Cargo.toml` | No change expected (`native-windows-gui` default features already include `embed-resource`) |
| `src/ctl/winadmin-ctl/winadmin-ctl.rc` | No change expected (`1 ICON` already present) |
| `releases/ctl/build-ctl.ps1` | Rebuild entrypoint (unchanged script) |

---

### Task 1: Wire window icon and in-window header

**Files:**
- Modify: `src/ctl/winadmin-ctl/src/main.rs`

**Interfaces:**
- Produces (fields on `Ui`, kept alive for the process lifetime):
  - `app_icon: nwg::Icon` — window / taskbar icon (system sizes)
  - `header_icon: nwg::Icon` — 32×32 for the header
  - `logo: nwg::ImageFrame`
  - `title_lbl: nwg::Label`
- Consumes: embedded resource id `1` from the running module via `nwg::EmbedResource::load(None)`

- [ ] **Step 1: Confirm the RC icon is still embedded**

Open `src/ctl/winadmin-ctl/winadmin-ctl.rc` and verify it still contains:

```rc
1 ICON "..\\..\\..\\app.ico"
```

Expected: present. Do not edit unless the line is missing.

- [ ] **Step 2: Extend `Ui` with icon + header fields**

In `struct Ui` (near `window:`), add:

```rust
    app_icon: nwg::Icon,
    header_icon: nwg::Icon,
    logo: nwg::ImageFrame,
    title_lbl: nwg::Label,
```

In `build_ui()`, initialize them in the `Ui { ... }` literal with `Default::default()` alongside the other fields.

- [ ] **Step 3: Load icons and set the window icon**

Replace the current window builder block in `build_ui()` with the following (header constants and icon load happen **before** `Window::builder`):

```rust
    const HEADER_H: i32 = 44;
    const WIN_W: i32 = 860;
    const WIN_H: i32 = 665 + HEADER_H;
    const TABS_W: i32 = 844;
    const TABS_H: i32 = 649;

    let embed = nwg::EmbedResource::load(None).expect("embed resource");

    nwg::Icon::builder()
        .source_embed(Some(&embed))
        .source_embed_id(1)
        .strict(true)
        .build(&mut ui.app_icon)
        .expect("app icon");

    nwg::Icon::builder()
        .source_embed(Some(&embed))
        .source_embed_id(1)
        .size(Some((32, 32)))
        .strict(true)
        .build(&mut ui.header_icon)
        .expect("header icon");

    nwg::Window::builder()
        .size((WIN_W, WIN_H))
        .center(true)
        .title("WinAdmin.Ctl")
        .icon(Some(&ui.app_icon))
        .flags(nwg::WindowFlags::WINDOW | nwg::WindowFlags::MINIMIZE_BOX | nwg::WindowFlags::VISIBLE)
        .build(&mut ui.window)
        .unwrap();
```

- [ ] **Step 4: Build header controls and shift tabs**

Immediately after `Notice::builder...`, replace the tabs builder so the header sits above the tabs:

```rust
    nwg::ImageFrame::builder()
        .parent(&ui.window)
        .size((32, 32))
        .position((12, 10))
        .icon(Some(&ui.header_icon))
        .build(&mut ui.logo)
        .unwrap();

    nwg::Label::builder()
        .text("WinAdmin.Ctl")
        .parent(&ui.window)
        .position((52, 14))
        .size((300, 24))
        .build(&mut ui.title_lbl)
        .unwrap();

    nwg::TabsContainer::builder()
        .parent(&ui.window)
        .position((8, HEADER_H))
        .size((TABS_W, TABS_H))
        .build(&mut ui.tabs)
        .unwrap();
```

Keep the existing `Tab::builder` lines for Control / Users unchanged.

- [ ] **Step 5: Optional title font weight/size for the header label**

After the global default font is set in `main()`, or right after building `title_lbl` in `build_ui()`, apply a slightly larger font to the header only:

```rust
    let mut title_font = nwg::Font::default();
    if nwg::Font::builder()
        .family("Segoe UI")
        .size(22)
        .build(&mut title_font)
        .is_ok()
    {
        ui.title_lbl.set_font(Some(&title_font));
        // Keep the Font alive; store it if set_font borrows, or forget if NWG copies the HFONT.
        std::mem::forget(title_font);
    }
```

If `set_font` is unavailable on `Label` in this NWG version, skip this step and leave the global Segoe UI 18 font — branding still shows via the logo + text.

- [ ] **Step 6: Compile-check**

Run from repo root:

```powershell
$env:Path = "$env:USERPROFILE\.cargo\bin;$env:Path"
Push-Location src\ctl\winadmin-ctl
cargo build --release
Pop-Location
```

Expected: `Finished release` with exit code 0. Fix any compile errors (missing feature, wrong builder method names) before continuing.

- [ ] **Step 7: Commit (only if the user asked to commit)**

```powershell
git add src/ctl/winadmin-ctl/src/main.rs
git commit -m "feat(ctl): show WinAdmin icon in title bar and header"
```

Skip this step unless the user explicitly requested a commit.

---

### Task 2: Package release binary and verify visually

**Files:**
- Output: `releases/ctl/WinAdmin.Ctl.exe` (via existing script)

**Interfaces:**
- Consumes: Task 1 release build under `src/ctl/winadmin-ctl/target/release/winadmin-ctl.exe`

- [ ] **Step 1: Run the packaging script**

```powershell
.\releases\ctl\build-ctl.ps1
```

Expected stdout ends with something like:

```text
Done: C:\apps\SysPanel\releases\ctl\WinAdmin.Ctl.exe (≈2 MB)
```

- [ ] **Step 2: Manual visual checklist**

Run `releases\ctl\WinAdmin.Ctl.exe` elevated (UAC). Confirm:

1. Title bar shows the blue WinAdmin icon left of `WinAdmin.Ctl`
2. Taskbar button shows the same icon
3. Inside the window, above the tabs: 32×32 logo + text `WinAdmin.Ctl`
4. Control and Users tabs still usable; controls not clipped
5. Explorer properties / file icon for `WinAdmin.Ctl.exe` still show `app.ico`

- [ ] **Step 3: Commit packaging artifact (only if the user asked)**

Usually do **not** commit the binary unless the project already tracks `releases/ctl/WinAdmin.Ctl.exe` and the user wants it updated in git.

---

## Spec coverage self-review

| Spec requirement | Task |
|------------------|------|
| Title bar / taskbar icon from `app.ico` | Task 1 Steps 3–6, Task 2 Step 2.1–2.2 |
| Header logo 32×32 + `WinAdmin.Ctl` above tabs | Task 1 Steps 4–5, Task 2 Step 2.3 |
| Grow window / shift tabs | Task 1 Steps 3–4 |
| No new image assets | Global Constraints + Task 1 Step 1 |
| Rebuild via `build-ctl.ps1` | Task 2 Step 1 |
| Out of scope: Control/Users redesign | Not in any task |

Placeholder scan: none. Type names (`app_icon`, `header_icon`, `logo`, `title_lbl`, `HEADER_H`) consistent across steps.
