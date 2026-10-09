use crate::github::{self, ReleaseInfo};
use crate::service::{self, Presence, CREATE_NO_WINDOW};
use crate::settings::{Settings, DATA_PATH, SERVICE_NAME};
use std::os::windows::process::CommandExt;
use std::path::Path;
use std::process::Command;
use std::time::Duration;

pub fn register_service_only(s: &Settings, log: &dyn Fn(&str)) -> Result<(), String> {
    log("Registering Windows service from existing files...");
    register_service(s, log)?;
    log(&format!("Starting service {SERVICE_NAME}..."));
    service::start(SERVICE_NAME)?;
    log(&format!("Done. Service {SERVICE_NAME} is running at http://127.0.0.1:{}", s.port));
    Ok(())
}

pub fn install_or_update(
    s: &Settings,
    release: &ReleaseInfo,
    is_update: bool,
    log: &dyn Fn(&str),
    progress: &mut dyn FnMut(u64, Option<u64>),
) -> Result<(), String> {
    if is_update && service::status(SERVICE_NAME).presence == Presence::Running {
        log(&format!("Stopping service {SERVICE_NAME}..."));
        service::stop(SERVICE_NAME)?;
    }

    let zip = std::env::temp_dir().join(&release.asset_name);
    log(&format!(
        "Downloading {} ({:.1} MB)...",
        release.asset_name,
        release.size as f64 / 1_048_576.0
    ));
    github::download_asset(release, &zip, |r, t| progress(r, t))?;

    log(&format!("Extracting to {}...", s.install_path));
    extract(&zip, &s.install_path)?;
    let _ = std::fs::remove_file(&zip);

    log("Registering Windows service...");
    register_service(s, log)?;

    log(&format!("Starting service {SERVICE_NAME}..."));
    service::start(SERVICE_NAME)?;

    log(&format!(
        "Done. WinAdmin {} is ready at http://127.0.0.1:{}",
        release.version, s.port
    ));
    Ok(())
}

/// Removes the Windows service (and firewall rule) and, when requested, the install folder.
pub fn uninstall(s: &Settings, remove_folder: bool, log: &dyn Fn(&str)) -> Result<(), String> {
    // Capture the live port before deleting the service (needed for the firewall rule name).
    let port = service::installed_port(SERVICE_NAME).unwrap_or(s.port);

    let presence = service::status(SERVICE_NAME).presence;
    if presence != Presence::NotInstalled {
        if presence == Presence::Running {
            log(&format!("Stopping service {SERVICE_NAME}..."));
            let _ = service::stop(SERVICE_NAME);
        }
        log(&format!("Deleting service {SERVICE_NAME}..."));
        let out = Command::new("sc.exe")
            .args(["delete", SERVICE_NAME])
            .creation_flags(CREATE_NO_WINDOW)
            .output()
            .map_err(|e| e.to_string())?;
        if !out.status.success() {
            let code = out.status.code().unwrap_or(-1);
            // 1060 = does not exist, 1072 = already marked for deletion
            if code != 1060 && code != 1072 {
                return Err(format!(
                    "sc delete failed ({code}): {}",
                    String::from_utf8_lossy(&out.stdout).trim()
                ));
            }
        }
        std::thread::sleep(Duration::from_secs(1));
    } else {
        log("Service is not registered; skipping service removal.");
    }

    remove_firewall(port, log);

    if remove_folder {
        let target = Path::new(&s.install_path);
        if target.exists() {
            log(&format!("Deleting folder {}...", s.install_path));
            std::fs::remove_dir_all(target).map_err(|e| {
                format!("Could not delete {}: {e}. Is a file still in use?", s.install_path)
            })?;
        } else {
            log(&format!("Folder {} does not exist; nothing to delete.", s.install_path));
        }
    }

    log("Uninstall complete.");
    Ok(())
}

/// Removes the managed rule and the legacy "WinAdmin HTTP <port>" rule (open to any address).
fn remove_firewall(port: u16, log: &dyn Fn(&str)) {
    for rule in ["WinAdmin (managed)".to_string(), format!("WinAdmin HTTP {port}"), "WinAdmin HTTP 8080".to_string()] {
        let _ = Command::new("netsh.exe")
            .args(["advfirewall", "firewall", "delete", "rule", &format!("name={rule}")])
            .creation_flags(CREATE_NO_WINDOW)
            .output();
        log(&format!("Firewall rule removed (if present): {rule}"));
    }
}

fn extract(zip: &Path, install_path: &str) -> Result<(), String> {
    let target = Path::new(install_path);
    if target.exists() {
        std::fs::remove_dir_all(target).map_err(|e| e.to_string())?;
    }
    std::fs::create_dir_all(target).map_err(|e| e.to_string())?;

    let file = std::fs::File::open(zip).map_err(|e| e.to_string())?;
    let mut archive = zip::ZipArchive::new(file).map_err(|e| e.to_string())?;
    archive.extract(target).map_err(|e| e.to_string())?;
    Ok(())
}

fn register_service(s: &Settings, log: &dyn Fn(&str)) -> Result<(), String> {
    let exe = s.exe_path();
    if !exe.exists() {
        return Err(format!("Not found: {}", exe.display()));
    }
    std::fs::create_dir_all(DATA_PATH).map_err(|e| e.to_string())?;

    if service::status(SERVICE_NAME).presence != Presence::NotInstalled {
        if service::status(SERVICE_NAME).presence == Presence::Running {
            let _ = service::stop(SERVICE_NAME);
        }
        let _ = Command::new("sc.exe")
            .args(["delete", SERVICE_NAME])
            .creation_flags(CREATE_NO_WINDOW)
            .output();
        std::thread::sleep(Duration::from_secs(2));
    }

    let exe_str = exe.to_string_lossy().into_owned();
    let inner = if exe_str.contains(' ') {
        format!("\"{exe_str}\"")
    } else {
        exe_str
    };
    // sc.exe needs the whole binPath inside one quoted token: binPath= "..."
    // Address/port come from network.json (no --urls).
    let bin_token = format!("\"{inner}\"");
    crate::settings::write_network_port(s.port)?;
    log(&format!("Port {} written to {}", s.port, crate::settings::network_file().display()));

    let mut create = Command::new("sc.exe");
    create.raw_arg("create");
    create.raw_arg(SERVICE_NAME);
    create.raw_arg("binPath=");
    create.raw_arg(&bin_token);
    create.raw_arg("start=");
    create.raw_arg("auto");
    create.raw_arg("DisplayName=");
    create.raw_arg("\"WinAdmin\"");
    let out = create
        .creation_flags(CREATE_NO_WINDOW)
        .output()
        .map_err(|e| e.to_string())?;
    if !out.status.success() {
        return Err(format!(
            "sc create failed ({}): {}",
            out.status.code().unwrap_or(-1),
            String::from_utf8_lossy(&out.stdout).trim()
        ));
    }

    let _ = Command::new("sc.exe")
        .args([
            "description",
            SERVICE_NAME,
            "WinAdmin - monitoring and management for Windows",
        ])
        .creation_flags(CREATE_NO_WINDOW)
        .output();

    let db = s.db_path();
    let _ = Command::new("setx")
        .args(["WinAdmin__DatabasePath", &db, "/M"])
        .creation_flags(CREATE_NO_WINDOW)
        .output();
    log(&format!("WinAdmin__DatabasePath = {db}"));

    remove_firewall(s.port, log);
    harden_acl(&s.install_path, true, log);
    harden_acl(DATA_PATH, false, log);
    Ok(())
}

/// Install folder: Administrators/SYSTEM full, Users read; data folder: Administrators/SYSTEM only.
fn harden_acl(path: &str, users_read: bool, log: &dyn Fn(&str)) {
    let _ = Command::new("icacls.exe")
        .args([path, "/reset", "/T", "/C", "/Q"])
        .creation_flags(CREATE_NO_WINDOW)
        .output();
    let mut grant = Command::new("icacls.exe");
    grant.args([path, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F"]);
    if users_read {
        grant.arg("*S-1-5-32-545:(OI)(CI)RX");
    }
    grant.args(["/C", "/Q"]);
    match grant.creation_flags(CREATE_NO_WINDOW).output() {
        Ok(out) if out.status.success() => log(&format!("Permissions restricted: {path}")),
        _ => log(&format!("Warning: could not restrict permissions on {path}")),
    }
}
