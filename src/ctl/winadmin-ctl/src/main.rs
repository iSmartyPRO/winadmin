#![windows_subsystem = "windows"]

mod github;
mod installer;
mod service;
mod settings;
mod users;
mod version;

use github::ReleaseInfo;
use native_windows_gui as nwg;
use service::Presence;
use settings::{Settings, REPO, SERVICE_NAME};
use std::cell::RefCell;
use std::os::windows::process::CommandExt;
use std::process::Command;
use std::rc::Rc;
use std::sync::mpsc::{self, Receiver, Sender};
use std::thread;

enum UiMsg {
    Log(String),
    Progress(u32),
    LocalStatus(LocalStatusData),
    GithubStatus(GithubStatusData),
    OpDone(Result<(), String>),
}

/// Fast, offline status (version on disk + Windows service). Applied immediately.
struct LocalStatusData {
    installed: bool,
    version_display: String,
    presence: Presence,
    service_display: String,
    live_port: u16,
}

/// Result of the (slow) GitHub check. Applied when the network call returns.
struct GithubStatusData {
    github_display: String,
    update_display: String,
    update_available: bool,
    latest: Option<ReleaseInfo>,
}

struct State {
    settings: Settings,
    latest: Option<ReleaseInfo>,
    busy: bool,
    refreshing: bool,
    installed: bool,
    presence: Presence,
    update_available: bool,
    live_port: u16,
    log: String,
}

struct Ui {
    window: nwg::Window,
    app_icon: nwg::Icon,
    header_icon: nwg::Icon,
    logo: nwg::ImageFrame,
    title_lbl: nwg::Label,
    notice: nwg::Notice,
    tx: Sender<UiMsg>,
    rx: Receiver<UiMsg>,
    state: RefCell<State>,

    tabs: nwg::TabsContainer,
    control_tab: nwg::Tab,
    users_tab: nwg::Tab,

    // Control tab
    install_path: nwg::TextInput,
    browse_btn: nwg::Button,
    port: nwg::TextInput,
    local_val: nwg::Label,
    github_val: nwg::Label,
    update_val: nwg::Label,
    install_btn: nwg::Button,
    update_btn: nwg::Button,
    service_val: nwg::Label,
    url_val: nwg::Label,
    open_btn: nwg::Button,
    start_btn: nwg::Button,
    stop_btn: nwg::Button,
    restart_btn: nwg::Button,
    uninstall_btn: nwg::Button,
    log_box: nwg::TextBox,
    progress: nwg::ProgressBar,
    refresh_btn: nwg::Button,

    // Users tab
    users_list: nwg::ListView,
    users_refresh_btn: nwg::Button,
    add_login: nwg::TextInput,
    add_pw: nwg::TextInput,
    add_scopes: nwg::TextInput,
    add_btn: nwg::Button,
    reset_pw: nwg::TextInput,
    reset_btn: nwg::Button,
    delete_btn: nwg::Button,
    users_status: nwg::Label,
}

impl Ui {
    fn sync_settings(&self) -> Result<(), String> {
        let path = self.install_path.text().trim().to_string();
        if path.is_empty() {
            return Err("Install path cannot be empty.".into());
        }
        let port: u16 = self
            .port
            .text()
            .trim()
            .parse()
            .map_err(|_| "Port must be a number between 1 and 65535.".to_string())?;
        if port == 0 {
            return Err("Port must be between 1 and 65535.".into());
        }
        let mut st = self.state.borrow_mut();
        st.settings.install_path = path;
        st.settings.port = port;
        st.settings.save();
        Ok(())
    }

    fn append_log(&self, line: &str) {
        let text = {
            let mut st = self.state.borrow_mut();
            if !st.log.is_empty() {
                st.log.push_str("\r\n");
            }
            st.log.push_str(line);
            st.log.clone()
        };
        self.log_box.set_text(&text);
    }

    fn set_busy(&self, busy: bool) {
        self.state.borrow_mut().busy = busy;
        self.update_controls();
    }

    fn update_controls(&self) {
        let (busy, installed, presence, has_latest, update_available) = {
            let st = self.state.borrow();
            (st.busy, st.installed, st.presence, st.latest.is_some(), st.update_available)
        };
        let running = presence == Presence::Running;
        let present = presence != Presence::NotInstalled;
        let can_register = installed && presence == Presence::NotInstalled;
        let can_fresh = !installed && has_latest;

        self.install_btn.set_enabled(!busy && (can_fresh || can_register));
        self.install_btn.set_text(if can_register { "Register" } else { "Install" });
        self.update_btn.set_enabled(!busy && installed && has_latest && update_available);
        self.start_btn.set_enabled(!busy && present && !running);
        self.stop_btn.set_enabled(!busy && running);
        self.restart_btn.set_enabled(!busy && running);
        self.uninstall_btn.set_enabled(!busy && (installed || present));
        let refreshing = self.state.borrow().refreshing;
        self.refresh_btn.set_enabled(!busy && !refreshing);
        self.open_btn.set_enabled(!busy && running);
        self.install_path.set_enabled(!busy);
        self.port.set_enabled(!busy);
        self.browse_btn.set_enabled(!busy);

        let users_enabled = !busy && installed;
        self.users_refresh_btn.set_enabled(users_enabled);
        self.add_login.set_enabled(users_enabled);
        self.add_pw.set_enabled(users_enabled);
        self.add_scopes.set_enabled(users_enabled);
        self.add_btn.set_enabled(users_enabled);
        self.reset_pw.set_enabled(users_enabled);
        self.users_list.set_enabled(!busy);
        let has_sel = self.users_list.selected_item().is_some();
        self.reset_btn.set_enabled(users_enabled && has_sel);
        self.delete_btn.set_enabled(users_enabled && has_sel);
    }

    fn apply_local_status(&self, data: LocalStatusData) {
        {
            let mut st = self.state.borrow_mut();
            st.installed = data.installed;
            st.presence = data.presence;
            st.live_port = data.live_port;
            st.busy = false;
        }
        self.local_val.set_text(&data.version_display);
        self.service_val.set_text(&data.service_display);
        if data.presence == Presence::Running {
            self.url_val.set_text(&format!("http://localhost:{}", data.live_port));
        } else {
            self.url_val.set_text("(service not running)");
        }
        self.progress.set_pos(0);
        self.update_controls();
    }

    fn apply_github_status(&self, data: GithubStatusData) {
        {
            let mut st = self.state.borrow_mut();
            st.update_available = data.update_available;
            st.latest = data.latest;
            st.refreshing = false;
        }
        self.github_val.set_text(&data.github_display);
        self.update_val.set_text(&data.update_display);
        self.update_controls();
    }

    fn spawn_refresh(&self) {
        self.state.borrow_mut().refreshing = true;
        self.github_val.set_text("checking...");
        self.update_val.set_text("checking GitHub for updates...");
        self.append_log("Checking GitHub for the latest version...");
        self.update_controls();
        let settings = self.state.borrow().settings.clone();
        let tx = self.tx.clone();
        let ns = self.notice.sender();
        thread::spawn(move || run_refresh(settings, tx, ns));
    }

    fn drain_messages(&self) {
        while let Ok(msg) = self.rx.try_recv() {
            match msg {
                UiMsg::Log(l) => self.append_log(&l),
                UiMsg::Progress(p) => self.progress.set_pos(p),
                UiMsg::LocalStatus(d) => self.apply_local_status(d),
                UiMsg::GithubStatus(d) => self.apply_github_status(d),
                UiMsg::OpDone(res) => {
                    match res {
                        Ok(()) => self.append_log("Operation completed."),
                        Err(e) => {
                            self.append_log(&format!("ERROR: {e}"));
                            nwg::modal_error_message(&self.window, "WinAdmin.Ctl", &e);
                        }
                    }
                    self.progress.set_pos(0);
                    self.spawn_refresh();
                }
            }
        }
    }

    fn on_install(&self) {
        if let Err(e) = self.sync_settings() {
            nwg::modal_error_message(&self.window, "Invalid settings", &e);
            return;
        }
        let settings = self.state.borrow().settings.clone();
        let local = version::read_local(&settings.install_path);
        let svc = service::status(SERVICE_NAME);

        if local.installed && svc.presence == Presence::NotInstalled {
            let choice = nwg::modal_message(
                &self.window,
                &nwg::MessageParams {
                    title: "Register service",
                    content: &format!(
                        "WinAdmin files are installed ({}), but the Windows service is not registered.\n\nRegister and start it now?",
                        local.version.clone().unwrap_or_else(|| "unknown".into())
                    ),
                    buttons: nwg::MessageButtons::YesNo,
                    icons: nwg::MessageIcons::Question,
                },
            );
            if choice != nwg::MessageChoice::Yes {
                return;
            }
            self.set_busy(true);
            self.append_log("Registering Windows service...");
            let tx = self.tx.clone();
            let ns = self.notice.sender();
            thread::spawn(move || run_register(settings, tx, ns));
            return;
        }

        let latest = match self.state.borrow().latest.clone() {
            Some(l) => l,
            None => {
                nwg::modal_error_message(&self.window, "Install", "No release information yet. Click Refresh first.");
                return;
            }
        };
        let choice = nwg::modal_message(
            &self.window,
            &nwg::MessageParams {
                title: "Install WinAdmin",
                content: &format!("Install WinAdmin {} to {}?", latest.version, settings.install_path),
                buttons: nwg::MessageButtons::YesNo,
                icons: nwg::MessageIcons::Question,
            },
        );
        if choice != nwg::MessageChoice::Yes {
            return;
        }
        self.set_busy(true);
        self.append_log(&format!("Installing WinAdmin {}...", latest.version));
        let tx = self.tx.clone();
        let ns = self.notice.sender();
        thread::spawn(move || run_install(settings, latest, false, tx, ns));
    }

    fn on_update(&self) {
        if let Err(e) = self.sync_settings() {
            nwg::modal_error_message(&self.window, "Invalid settings", &e);
            return;
        }
        let settings = self.state.borrow().settings.clone();
        let latest = match self.state.borrow().latest.clone() {
            Some(l) => l,
            None => return,
        };
        let choice = nwg::modal_message(
            &self.window,
            &nwg::MessageParams {
                title: "Update WinAdmin",
                content: &format!("Update WinAdmin to {}?", latest.version),
                buttons: nwg::MessageButtons::YesNo,
                icons: nwg::MessageIcons::Question,
            },
        );
        if choice != nwg::MessageChoice::Yes {
            return;
        }
        self.set_busy(true);
        self.append_log(&format!("Updating WinAdmin to {}...", latest.version));
        let tx = self.tx.clone();
        let ns = self.notice.sender();
        thread::spawn(move || run_install(settings, latest, true, tx, ns));
    }

    fn on_service(&self, action: u8) {
        self.set_busy(true);
        let tx = self.tx.clone();
        let ns = self.notice.sender();
        thread::spawn(move || run_service(action, tx, ns));
    }

    fn on_uninstall(&self) {
        if let Err(e) = self.sync_settings() {
            nwg::modal_error_message(&self.window, "Invalid settings", &e);
            return;
        }
        let settings = self.state.borrow().settings.clone();
        let (installed, presence) = {
            let st = self.state.borrow();
            (st.installed, st.presence)
        };
        if !installed && presence == Presence::NotInstalled {
            nwg::modal_info_message(&self.window, "Uninstall", "Nothing to uninstall.");
            return;
        }

        let confirm = nwg::modal_message(
            &self.window,
            &nwg::MessageParams {
                title: "Uninstall WinAdmin",
                content: "Remove the WinAdmin Windows service?\n\nThe service will be stopped and unregistered.",
                buttons: nwg::MessageButtons::YesNo,
                icons: nwg::MessageIcons::Warning,
            },
        );
        if confirm != nwg::MessageChoice::Yes {
            return;
        }

        // Second, separate question: also wipe the install folder?
        let remove_folder = if std::path::Path::new(&settings.install_path).exists() {
            let c = nwg::modal_message(
                &self.window,
                &nwg::MessageParams {
                    title: "Delete folder",
                    content: &format!(
                        "Also delete the install folder?\n\n{}\n\nThis permanently removes all files there and cannot be undone.",
                        settings.install_path
                    ),
                    buttons: nwg::MessageButtons::YesNo,
                    icons: nwg::MessageIcons::Warning,
                },
            );
            c == nwg::MessageChoice::Yes
        } else {
            false
        };

        self.set_busy(true);
        self.append_log("Uninstalling WinAdmin...");
        let tx = self.tx.clone();
        let ns = self.notice.sender();
        thread::spawn(move || run_uninstall(settings, remove_folder, tx, ns));
    }

    fn on_open_url(&self) {
        let port = self.state.borrow().live_port;
        let url = format!("http://localhost:{port}");
        let _ = Command::new("rundll32.exe")
            .arg("url.dll,FileProtocolHandler")
            .arg(&url)
            .creation_flags(service::CREATE_NO_WINDOW)
            .spawn();
    }

    fn on_browse(&self) {
        let mut dialog = nwg::FileDialog::default();
        let start = {
            let p = self.install_path.text();
            let parent = std::path::Path::new(&p)
                .parent()
                .map(|x| x.to_string_lossy().into_owned());
            parent.filter(|d| std::path::Path::new(d).exists())
        };
        let mut builder = nwg::FileDialog::builder()
            .title("Select install folder")
            .action(nwg::FileDialogAction::OpenDirectory);
        if let Some(dir) = start.as_deref() {
            builder = builder.default_folder(dir);
        }
        if builder.build(&mut dialog).is_err() {
            return;
        }
        if dialog.run(Some(&self.window)) {
            if let Ok(dir) = dialog.get_selected_item() {
                self.install_path.set_text(&dir.to_string_lossy());
            }
        }
    }

    fn selected_login(&self) -> Option<String> {
        let row = self.users_list.selected_item()?;
        self.users_list.item(row, 0, 260).map(|i| i.text)
    }

    fn load_users(&self) {
        if let Err(e) = self.sync_settings() {
            nwg::modal_error_message(&self.window, "Users", &e);
            return;
        }
        let settings = self.state.borrow().settings.clone();
        self.users_status.set_text("Loading users...");
        match users::list(&settings) {
            Ok(rows) => {
                self.users_list.clear();
                for (i, u) in rows.iter().enumerate() {
                    let active = if u.active { "Yes" } else { "No" };
                    self.users_list.insert_items_row(
                        Some(i as i32),
                        &[u.login.as_str(), u.scopes.as_str(), u.created.as_str(), active],
                    );
                }
                self.users_status.set_text(&format!("{} user(s).", rows.len()));
            }
            Err(e) => {
                self.users_status.set_text("Failed to load users.");
                nwg::modal_error_message(&self.window, "Users", &e);
            }
        }
        self.update_controls();
    }

    fn add_user(&self) {
        if let Err(e) = self.sync_settings() {
            nwg::modal_error_message(&self.window, "Users", &e);
            return;
        }
        let login = self.add_login.text().trim().to_string();
        let password = self.add_pw.text();
        let scopes = self.add_scopes.text();
        if login.is_empty() || password.is_empty() {
            nwg::modal_error_message(&self.window, "Add user", "Login and password are required.");
            return;
        }
        let settings = self.state.borrow().settings.clone();
        match users::add(&settings, &login, &password, &scopes) {
            Ok(()) => {
                self.add_login.set_text("");
                self.add_pw.set_text("");
                self.add_scopes.set_text("");
                self.load_users();
            }
            Err(e) => {
                nwg::modal_error_message(&self.window, "Add user", &e);
            }
        }
    }

    fn reset_selected(&self) {
        let login = match self.selected_login() {
            Some(l) => l,
            None => return,
        };
        let password = self.reset_pw.text();
        if password.is_empty() {
            nwg::modal_error_message(&self.window, "Reset password", "Enter a new password first.");
            return;
        }
        if let Err(e) = self.sync_settings() {
            nwg::modal_error_message(&self.window, "Users", &e);
            return;
        }
        let settings = self.state.borrow().settings.clone();
        match users::reset_password(&settings, &login, &password) {
            Ok(()) => {
                self.reset_pw.set_text("");
                nwg::modal_info_message(&self.window, "Reset password", &format!("Password updated for {login}."));
            }
            Err(e) => {
                nwg::modal_error_message(&self.window, "Reset password", &e);
            }
        }
    }

    fn delete_selected(&self) {
        let login = match self.selected_login() {
            Some(l) => l,
            None => return,
        };
        let choice = nwg::modal_message(
            &self.window,
            &nwg::MessageParams {
                title: "Delete user",
                content: &format!("Delete user '{login}'? This cannot be undone."),
                buttons: nwg::MessageButtons::YesNo,
                icons: nwg::MessageIcons::Warning,
            },
        );
        if choice != nwg::MessageChoice::Yes {
            return;
        }
        if let Err(e) = self.sync_settings() {
            nwg::modal_error_message(&self.window, "Users", &e);
            return;
        }
        let settings = self.state.borrow().settings.clone();
        match users::delete(&settings, &login) {
            Ok(()) => self.load_users(),
            Err(e) => {
                nwg::modal_error_message(&self.window, "Delete user", &e);
            }
        }
    }
}

fn run_refresh(settings: Settings, tx: Sender<UiMsg>, ns: nwg::NoticeSender) {
    // Phase 1: fast, offline status. Sent immediately so service controls stay responsive.
    let local = version::read_local(&settings.install_path);
    let svc = service::status(SERVICE_NAME);
    let live_port = service::installed_port(SERVICE_NAME).unwrap_or(settings.port);

    let service_display = if svc.presence == Presence::NotInstalled && local.installed {
        "Not registered".to_string()
    } else {
        svc.display.clone()
    };
    let version_display = local.version.clone().unwrap_or_else(|| {
        if local.installed {
            "installed (version unknown)".into()
        } else {
            "Not installed".into()
        }
    });
    let _ = tx.send(UiMsg::LocalStatus(LocalStatusData {
        installed: local.installed,
        version_display,
        presence: svc.presence,
        service_display,
        live_port,
    }));
    ns.notice();

    // Phase 2: GitHub check over the network (may take a moment).
    let gh = match github::fetch_latest(REPO) {
        Ok(rel) => {
            let newer = version::is_newer(&rel.version, local.version.as_deref());
            let (update_display, update_available, note) = if !local.installed {
                (format!("Ready to install {}", rel.version), false, "ready to install")
            } else if newer {
                (format!("Update available: {}", rel.version), true, "update available")
            } else {
                ("Up to date".to_string(), false, "up to date")
            };
            let _ = tx.send(UiMsg::Log(format!("GitHub: {} ({note})", rel.version)));
            GithubStatusData {
                github_display: rel.version.clone(),
                update_display,
                update_available,
                latest: Some(rel),
            }
        }
        Err(e) => {
            let _ = tx.send(UiMsg::Log(format!("GitHub: unreachable ({e})")));
            GithubStatusData {
                github_display: "Offline".to_string(),
                update_display: "Could not reach GitHub".to_string(),
                update_available: false,
                latest: None,
            }
        }
    };
    let _ = tx.send(UiMsg::GithubStatus(gh));
    ns.notice();
}

fn run_install(settings: Settings, latest: ReleaseInfo, is_update: bool, tx: Sender<UiMsg>, ns: nwg::NoticeSender) {
    let log = |m: &str| {
        let _ = tx.send(UiMsg::Log(m.to_string()));
        ns.notice();
    };
    let mut last = 101u32;
    let mut progress = |received: u64, total: Option<u64>| {
        if let Some(total) = total {
            if total > 0 {
                let p = ((received.saturating_mul(100) / total) as u32).min(100);
                if p != last {
                    last = p;
                    let _ = tx.send(UiMsg::Progress(p));
                    ns.notice();
                }
            }
        }
    };
    let res = installer::install_or_update(&settings, &latest, is_update, &log, &mut progress);
    let _ = tx.send(UiMsg::OpDone(res));
    ns.notice();
}

fn run_register(settings: Settings, tx: Sender<UiMsg>, ns: nwg::NoticeSender) {
    let log = |m: &str| {
        let _ = tx.send(UiMsg::Log(m.to_string()));
        ns.notice();
    };
    let res = installer::register_service_only(&settings, &log);
    let _ = tx.send(UiMsg::OpDone(res));
    ns.notice();
}

fn run_uninstall(settings: Settings, remove_folder: bool, tx: Sender<UiMsg>, ns: nwg::NoticeSender) {
    let log = |m: &str| {
        let _ = tx.send(UiMsg::Log(m.to_string()));
        ns.notice();
    };
    let res = installer::uninstall(&settings, remove_folder, &log);
    let _ = tx.send(UiMsg::OpDone(res));
    ns.notice();
}

fn run_service(action: u8, tx: Sender<UiMsg>, ns: nwg::NoticeSender) {
    let log = |m: &str| {
        let _ = tx.send(UiMsg::Log(m.to_string()));
        ns.notice();
    };
    log(match action {
        1 => "Starting service...",
        2 => "Stopping service...",
        _ => "Restarting service...",
    });
    let res = match action {
        1 => service::start(SERVICE_NAME),
        2 => service::stop(SERVICE_NAME),
        _ => service::restart(SERVICE_NAME),
    };
    let _ = tx.send(UiMsg::OpDone(res));
    ns.notice();
}

fn label_left(text: &str, parent: &nwg::Tab, y: i32, out: &mut nwg::Label) {
    nwg::Label::builder()
        .text(text)
        .parent(parent)
        .position((20, y))
        .size((160, 26))
        .flags(nwg::LabelFlags::VISIBLE | nwg::LabelFlags::ELIPSIS)
        .build(out)
        .unwrap();
}

fn label_value(text: &str, parent: &nwg::Tab, y: i32, w: i32, out: &mut nwg::Label) {
    nwg::Label::builder()
        .text(text)
        .parent(parent)
        .position((190, y))
        .size((w, 26))
        .flags(nwg::LabelFlags::VISIBLE | nwg::LabelFlags::ELIPSIS)
        .build(out)
        .unwrap();
}

fn build_ui() -> Ui {
    let (tx, rx) = mpsc::channel::<UiMsg>();
    let mut ui = Ui {
        window: Default::default(),
        app_icon: Default::default(),
        header_icon: Default::default(),
        logo: Default::default(),
        title_lbl: Default::default(),
        notice: Default::default(),
        tx,
        rx,
        state: RefCell::new(State {
            settings: Settings::load(),
            latest: None,
            busy: false,
            refreshing: false,
            installed: false,
            presence: Presence::NotInstalled,
            update_available: false,
            live_port: settings::DEFAULT_PORT,
            log: String::new(),
        }),
        tabs: Default::default(),
        control_tab: Default::default(),
        users_tab: Default::default(),
        install_path: Default::default(),
        browse_btn: Default::default(),
        port: Default::default(),
        local_val: Default::default(),
        github_val: Default::default(),
        update_val: Default::default(),
        install_btn: Default::default(),
        update_btn: Default::default(),
        service_val: Default::default(),
        url_val: Default::default(),
        open_btn: Default::default(),
        start_btn: Default::default(),
        stop_btn: Default::default(),
        restart_btn: Default::default(),
        uninstall_btn: Default::default(),
        log_box: Default::default(),
        progress: Default::default(),
        refresh_btn: Default::default(),
        users_list: Default::default(),
        users_refresh_btn: Default::default(),
        add_login: Default::default(),
        add_pw: Default::default(),
        add_scopes: Default::default(),
        add_btn: Default::default(),
        reset_pw: Default::default(),
        reset_btn: Default::default(),
        delete_btn: Default::default(),
        users_status: Default::default(),
    };

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

    nwg::Notice::builder().parent(&ui.window).build(&mut ui.notice).unwrap();

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

    let mut title_font = nwg::Font::default();
    if nwg::Font::builder()
        .family("Segoe UI")
        .size(22)
        .build(&mut title_font)
        .is_ok()
    {
        ui.title_lbl.set_font(Some(&title_font));
        std::mem::forget(title_font);
    }

    nwg::TabsContainer::builder()
        .parent(&ui.window)
        .position((8, HEADER_H))
        .size((TABS_W, TABS_H))
        .build(&mut ui.tabs)
        .unwrap();

    nwg::Tab::builder().text("Control").parent(&ui.tabs).build(&mut ui.control_tab).unwrap();
    nwg::Tab::builder().text("Users").parent(&ui.tabs).build(&mut ui.users_tab).unwrap();

    let t = &ui.control_tab;

    // Settings
    let mut lbl = nwg::Label::default();
    label_left("Install path:", t, 18, &mut lbl);
    std::mem::forget(lbl);
    nwg::TextInput::builder()
        .text(&ui.state.borrow().settings.install_path)
        .parent(t)
        .position((190, 16))
        .size((455, 28))
        .build(&mut ui.install_path)
        .unwrap();
    nwg::Button::builder()
        .text("Browse...")
        .parent(t)
        .position((655, 15))
        .size((120, 30))
        .build(&mut ui.browse_btn)
        .unwrap();

    let mut lbl2 = nwg::Label::default();
    label_left("Port:", t, 50, &mut lbl2);
    std::mem::forget(lbl2);
    nwg::TextInput::builder()
        .text(&ui.state.borrow().settings.port.to_string())
        .parent(t)
        .position((190, 48))
        .size((100, 28))
        .build(&mut ui.port)
        .unwrap();

    // Version block (gap after settings)
    let mut l3 = nwg::Label::default();
    label_left("Local version:", t, 98, &mut l3);
    std::mem::forget(l3);
    label_value("", t, 98, 400, &mut ui.local_val);

    let mut l4 = nwg::Label::default();
    label_left("GitHub version:", t, 130, &mut l4);
    std::mem::forget(l4);
    label_value("", t, 130, 400, &mut ui.github_val);

    let mut l5 = nwg::Label::default();
    label_left("Update:", t, 162, &mut l5);
    std::mem::forget(l5);
    label_value("", t, 162, 500, &mut ui.update_val);

    nwg::Button::builder()
        .text("Install")
        .parent(t)
        .position((20, 205))
        .size((120, 34))
        .build(&mut ui.install_btn)
        .unwrap();
    nwg::Button::builder()
        .text("Update")
        .parent(t)
        .position((150, 205))
        .size((120, 34))
        .build(&mut ui.update_btn)
        .unwrap();

    // Service block
    let mut l6 = nwg::Label::default();
    label_left("Service status:", t, 258, &mut l6);
    std::mem::forget(l6);
    label_value("", t, 258, 400, &mut ui.service_val);

    let mut l7 = nwg::Label::default();
    label_left("URL:", t, 290, &mut l7);
    std::mem::forget(l7);
    label_value("", t, 290, 380, &mut ui.url_val);
    nwg::Button::builder()
        .text("Open")
        .parent(t)
        .position((580, 288))
        .size((110, 30))
        .build(&mut ui.open_btn)
        .unwrap();

    nwg::Button::builder()
        .text("Start")
        .parent(t)
        .position((20, 330))
        .size((110, 34))
        .build(&mut ui.start_btn)
        .unwrap();
    nwg::Button::builder()
        .text("Stop")
        .parent(t)
        .position((140, 330))
        .size((110, 34))
        .build(&mut ui.stop_btn)
        .unwrap();
    nwg::Button::builder()
        .text("Restart")
        .parent(t)
        .position((260, 330))
        .size((120, 34))
        .build(&mut ui.restart_btn)
        .unwrap();
    nwg::Button::builder()
        .text("Uninstall")
        .parent(t)
        .position((400, 330))
        .size((130, 34))
        .build(&mut ui.uninstall_btn)
        .unwrap();

    nwg::TextBox::builder()
        .parent(t)
        .position((20, 380))
        .size((800, 220))
        .readonly(true)
        .flags(nwg::TextBoxFlags::VISIBLE | nwg::TextBoxFlags::VSCROLL | nwg::TextBoxFlags::AUTOVSCROLL)
        .build(&mut ui.log_box)
        .unwrap();

    nwg::ProgressBar::builder()
        .parent(t)
        .position((20, 615))
        .size((680, 26))
        .range(0..100)
        .build(&mut ui.progress)
        .unwrap();
    nwg::Button::builder()
        .text("Refresh")
        .parent(t)
        .position((710, 613))
        .size((110, 30))
        .build(&mut ui.refresh_btn)
        .unwrap();

    // Users tab
    let u = &ui.users_tab;
    nwg::ListView::builder()
        .parent(u)
        .position((20, 20))
        .size((800, 280))
        .list_style(nwg::ListViewStyle::Detailed)
        .ex_flags(nwg::ListViewExFlags::FULL_ROW_SELECT | nwg::ListViewExFlags::GRID)
        .build(&mut ui.users_list)
        .unwrap();
    ui.users_list.set_headers_enabled(true);
    for (i, (name, width)) in [("Login", 200), ("Scopes", 340), ("Created", 150), ("Active", 80)]
        .into_iter()
        .enumerate()
    {
        ui.users_list.insert_column(nwg::InsertListViewColumn {
            index: Some(i as i32),
            fmt: None,
            width: Some(width),
            text: Some(name.to_string()),
        });
    }

    nwg::Button::builder()
        .text("Refresh users")
        .parent(u)
        .position((20, 315))
        .size((140, 30))
        .build(&mut ui.users_refresh_btn)
        .unwrap();

    let mut ul1 = nwg::Label::default();
    label_left("Login:", u, 365, &mut ul1);
    std::mem::forget(ul1);
    nwg::TextInput::builder()
        .parent(u)
        .position((190, 363))
        .size((170, 28))
        .build(&mut ui.add_login)
        .unwrap();

    let mut ul2 = nwg::Label::default();
    label_left("Password:", u, 397, &mut ul2);
    std::mem::forget(ul2);
    nwg::TextInput::builder()
        .parent(u)
        .position((190, 395))
        .size((170, 28))
        .password(Some('*'))
        .build(&mut ui.add_pw)
        .unwrap();

    let mut ul3 = nwg::Label::default();
    label_left("Scopes:", u, 429, &mut ul3);
    std::mem::forget(ul3);
    nwg::TextInput::builder()
        .parent(u)
        .position((190, 427))
        .size((420, 28))
        .build(&mut ui.add_scopes)
        .unwrap();
    nwg::Button::builder()
        .text("Add user")
        .parent(u)
        .position((620, 426))
        .size((120, 30))
        .build(&mut ui.add_btn)
        .unwrap();

    let mut ul4 = nwg::Label::default();
    label_left("New password:", u, 475, &mut ul4);
    std::mem::forget(ul4);
    nwg::TextInput::builder()
        .parent(u)
        .position((190, 473))
        .size((220, 28))
        .password(Some('*'))
        .build(&mut ui.reset_pw)
        .unwrap();
    nwg::Button::builder()
        .text("Reset password")
        .parent(u)
        .position((420, 472))
        .size((150, 30))
        .build(&mut ui.reset_btn)
        .unwrap();
    nwg::Button::builder()
        .text("Delete user")
        .parent(u)
        .position((580, 472))
        .size((130, 30))
        .build(&mut ui.delete_btn)
        .unwrap();

    label_value("", u, 520, 780, &mut ui.users_status);

    ui
}

fn main() {
    nwg::init().expect("Failed to init native-windows-gui");
    let mut font = nwg::Font::default();
    if nwg::Font::builder()
        .family("Segoe UI")
        .size(18)
        .build(&mut font)
        .is_ok()
    {
        nwg::Font::set_global_default(Some(font));
    }

    let ui = Rc::new(build_ui());
    ui.update_controls();
    ui.spawn_refresh();

    let ui_handler = ui.clone();
    let handler = nwg::full_bind_event_handler(&ui.window.handle, move |evt, _data, handle| {
        use nwg::Event as E;
        match evt {
            E::OnWindowClose => {
                if handle == ui_handler.window.handle {
                    nwg::stop_thread_dispatch();
                }
            }
            E::OnNotice => {
                if handle == ui_handler.notice.handle {
                    ui_handler.drain_messages();
                }
            }
            E::OnListViewItemChanged => ui_handler.update_controls(),
            E::OnButtonClick => {
                if handle == ui_handler.install_btn.handle {
                    ui_handler.on_install();
                } else if handle == ui_handler.update_btn.handle {
                    ui_handler.on_update();
                } else if handle == ui_handler.start_btn.handle {
                    ui_handler.on_service(1);
                } else if handle == ui_handler.stop_btn.handle {
                    ui_handler.on_service(2);
                } else if handle == ui_handler.restart_btn.handle {
                    ui_handler.on_service(3);
                } else if handle == ui_handler.uninstall_btn.handle {
                    ui_handler.on_uninstall();
                } else if handle == ui_handler.refresh_btn.handle {
                    let _ = ui_handler.sync_settings();
                    ui_handler.spawn_refresh();
                } else if handle == ui_handler.open_btn.handle {
                    ui_handler.on_open_url();
                } else if handle == ui_handler.browse_btn.handle {
                    ui_handler.on_browse();
                } else if handle == ui_handler.users_refresh_btn.handle {
                    ui_handler.load_users();
                } else if handle == ui_handler.add_btn.handle {
                    ui_handler.add_user();
                } else if handle == ui_handler.reset_btn.handle {
                    ui_handler.reset_selected();
                } else if handle == ui_handler.delete_btn.handle {
                    ui_handler.delete_selected();
                }
            }
            _ => {}
        }
    });

    nwg::dispatch_thread_events();
    nwg::unbind_event_handler(&handler);
}
