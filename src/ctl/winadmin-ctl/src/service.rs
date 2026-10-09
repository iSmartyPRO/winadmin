use std::os::windows::process::CommandExt;
use std::process::{Command, Output};
use std::time::Duration;

pub const CREATE_NO_WINDOW: u32 = 0x0800_0000;

#[derive(PartialEq, Eq, Clone, Copy, Debug)]
pub enum Presence {
    NotInstalled,
    Stopped,
    Running,
    Paused,
    Unknown,
}

pub struct ServiceStatus {
    pub presence: Presence,
    pub display: String,
}

fn sc(args: &[&str]) -> std::io::Result<Output> {
    Command::new("sc.exe")
        .args(args)
        .creation_flags(CREATE_NO_WINDOW)
        .output()
}

/// Classify service state from `sc query` output. State keywords (RUNNING, STOPPED, …)
/// stay English regardless of Windows UI language; the STATE label is localized.
fn classify_sc_output(stdout: &str, exit_code: i32) -> ServiceStatus {
    if exit_code == 1060 || stdout.contains("1060") {
        return ServiceStatus {
            presence: Presence::NotInstalled,
            display: "Not installed".into(),
        };
    }

    let upper = stdout.to_uppercase();
    let (presence, display) = if upper.contains("RUNNING") {
        (Presence::Running, "Running")
    } else if upper.contains("PENDING") {
        (Presence::Unknown, "Changing...")
    } else if upper.contains("PAUSED") {
        (Presence::Paused, "Paused")
    } else if upper.contains("STOPPED") {
        (Presence::Stopped, "Stopped")
    } else if exit_code != 0 {
        (Presence::NotInstalled, "Not installed")
    } else {
        (Presence::Unknown, "Unknown")
    };

    ServiceStatus {
        presence,
        display: display.into(),
    }
}

pub fn status(name: &str) -> ServiceStatus {
    match sc(&["query", name]) {
        Ok(out) => {
            let code = out.status.code().unwrap_or(-1);
            let text = String::from_utf8_lossy(&out.stdout);
            classify_sc_output(&text, code)
        }
        Err(_) => ServiceStatus {
            presence: Presence::NotInstalled,
            display: "Not installed".into(),
        },
    }
}

pub fn start(name: &str) -> Result<(), String> {
    if status(name).presence == Presence::Running {
        return Ok(());
    }
    let out = sc(&["start", name]).map_err(|e| e.to_string())?;
    if !out.status.success() {
        let code = out.status.code().unwrap_or(-1);
        // 1056 = service already running
        if code != 1056 {
            return Err(format!("sc start failed ({code}): {}", String::from_utf8_lossy(&out.stdout).trim()));
        }
    }
    wait_for(name, Presence::Running)
}

pub fn stop(name: &str) -> Result<(), String> {
    if status(name).presence == Presence::Stopped {
        return Ok(());
    }
    let out = sc(&["stop", name]).map_err(|e| e.to_string())?;
    if !out.status.success() {
        let code = out.status.code().unwrap_or(-1);
        // 1062 = not started, 1056 = already running (race)
        if code != 1062 && code != 1056 {
            return Err(format!("sc stop failed ({code}): {}", String::from_utf8_lossy(&out.stdout).trim()));
        }
    }
    wait_for(name, Presence::Stopped)
}

pub fn restart(name: &str) -> Result<(), String> {
    stop(name)?;
    start(name)
}

fn wait_for(name: &str, target: Presence) -> Result<(), String> {
    for _ in 0..60 {
        if status(name).presence == target {
            return Ok(());
        }
        std::thread::sleep(Duration::from_millis(500));
    }
    Err("Timeout waiting for the service to reach the desired state.".into())
}

/// Port of the installed panel: network.json (1.0.6+), else --urls in the service binary path (older installs).
pub fn installed_port(name: &str) -> Option<u16> {
    if let Ok(text) = std::fs::read_to_string(crate::settings::network_file()) {
        if let Some(port) = crate::settings::port_from_network_json(&text) {
            return Some(port);
        }
    }
    let out = sc(&["qc", name]).ok()?;
    let text = String::from_utf8_lossy(&out.stdout);
    let idx = text.find("--urls")?;
    let after = &text[idx..];
    let scheme = after.find("://")?;
    let rest = &after[scheme + 3..];
    let port_start = rest.find(':')? + 1;
    let digits: String = rest[port_start..].chars().take_while(|c| c.is_ascii_digit()).collect();
    digits.parse().ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn running_russian_locale() {
        let sample = r"
SERVICE_NAME: WinAdmin
        TYPE               : 10  WIN32_OWN_PROCESS
        STATE              : 4  RUNNING
                                (STOPPABLE, NOT_PAUSABLE, ACCEPTS_SHUTDOWN)
";
        let s = classify_sc_output(sample, 0);
        assert_eq!(s.presence, Presence::Running);
        assert_eq!(s.display, "Running");
    }

    #[test]
    fn stopped_service() {
        let sample = "STATE : 1  STOPPED";
        let s = classify_sc_output(sample, 0);
        assert_eq!(s.presence, Presence::Stopped);
    }

    #[test]
    fn not_installed_1060() {
        let s = classify_sc_output("", 1060);
        assert_eq!(s.presence, Presence::NotInstalled);
    }
}
