use serde::{Deserialize, Serialize};
use std::path::PathBuf;

pub const REPO: &str = "iSmartyPRO/winadmin";
pub const SERVICE_NAME: &str = "WinAdmin";
pub const DATA_PATH: &str = r"C:\ProgramData\WinAdmin";
pub const EXE_NAME: &str = "WinAdmin.exe";
pub const VERSION_FILE: &str = "VERSION";
pub const DEFAULT_INSTALL_PATH: &str = r"C:\apps\WinAdmin";
pub const DEFAULT_PORT: u16 = 8080;

#[derive(Serialize, Deserialize, Clone)]
pub struct Settings {
    pub install_path: String,
    pub port: u16,
}

impl Default for Settings {
    fn default() -> Self {
        Settings {
            install_path: DEFAULT_INSTALL_PATH.to_string(),
            port: DEFAULT_PORT,
        }
    }
}

impl Settings {
    pub fn exe_path(&self) -> PathBuf {
        PathBuf::from(&self.install_path).join(EXE_NAME)
    }

    pub fn db_path(&self) -> String {
        PathBuf::from(DATA_PATH)
            .join("WinAdmin.db")
            .to_string_lossy()
            .into_owned()
    }

    fn settings_path() -> Option<PathBuf> {
        std::env::var("LOCALAPPDATA")
            .ok()
            .map(|p| PathBuf::from(p).join("WinAdmin.Ctl").join("settings.json"))
    }

    pub fn load() -> Settings {
        if let Some(path) = Self::settings_path() {
            if let Ok(text) = std::fs::read_to_string(&path) {
                if let Ok(s) = serde_json::from_str::<Settings>(&text) {
                    if !s.install_path.trim().is_empty() && s.port > 0 {
                        return s;
                    }
                }
            }
        }
        Settings::default()
    }

    pub fn save(&self) {
        if let Some(path) = Self::settings_path() {
            if let Some(dir) = path.parent() {
                let _ = std::fs::create_dir_all(dir);
            }
            if let Ok(text) = serde_json::to_string_pretty(self) {
                let _ = std::fs::write(&path, text);
            }
        }
    }
}

pub const NETWORK_FILE: &str = "network.json";

/// network.json in the data folder: { "mode": "local"|"network", "port": N, "allow": [...] }.
pub fn network_file() -> PathBuf {
    PathBuf::from(DATA_PATH).join(NETWORK_FILE)
}

pub fn port_from_network_json(text: &str) -> Option<u16> {
    // Windows PowerShell 5.1 (Set-Content -Encoding UTF8) writes a BOM.
    let v: serde_json::Value = serde_json::from_str(text.trim_start_matches('\u{feff}')).ok()?;
    let port = v.get("port")?.as_u64()?;
    u16::try_from(port).ok().filter(|p| *p > 0)
}

/// Sets the port in network.json content, keeping mode/allow; broken or missing → local defaults.
pub fn merge_network_port(existing: Option<&str>, port: u16) -> String {
    let mut v = existing
        .and_then(|t| serde_json::from_str::<serde_json::Value>(t.trim_start_matches('\u{feff}')).ok())
        .filter(|v| v.is_object())
        .unwrap_or_else(|| serde_json::json!({ "mode": "local", "allow": [] }));
    v["port"] = serde_json::json!(port);
    if v.get("mode").is_none() {
        v["mode"] = serde_json::json!("local");
    }
    if v.get("allow").is_none() {
        v["allow"] = serde_json::json!([]);
    }
    serde_json::to_string_pretty(&v).unwrap_or_default()
}

/// Refuses drive roots and shared system folders: icacls /reset /T there would break the machine.
pub fn is_safe_acl_target(path: &str) -> bool {
    let norm = path.trim_end_matches(['\\', '/']).to_lowercase();
    if norm.len() <= 2 {
        return false; // "c:" or empty
    }
    let shared = [
        r"c:\programdata",
        r"c:\program files",
        r"c:\program files (x86)",
        r"c:\windows",
        r"c:\users",
    ];
    !shared.contains(&norm.as_str())
}

pub fn write_network_port(port: u16) -> Result<(), String> {
    let path = network_file();
    std::fs::create_dir_all(DATA_PATH).map_err(|e| e.to_string())?;
    let existing = std::fs::read_to_string(&path).ok();
    std::fs::write(&path, merge_network_port(existing.as_deref(), port)).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_port_from_network_json() {
        assert_eq!(port_from_network_json(r#"{ "mode": "local", "port": 9090, "allow": [] }"#), Some(9090));
    }

    #[test]
    fn refuses_acl_reset_on_roots_and_shared_folders() {
        assert!(!is_safe_acl_target(r"C:\"));
        assert!(!is_safe_acl_target(r"C:\ProgramData"));
        assert!(!is_safe_acl_target(r"C:\Program Files\"));
        assert!(!is_safe_acl_target(r"c:\windows"));
        assert!(!is_safe_acl_target(r"C:\Users"));
        assert!(is_safe_acl_target(r"C:\apps\WinAdmin"));
        assert!(is_safe_acl_target(r"C:\ProgramData\WinAdmin"));
    }

    #[test]
    fn handles_utf8_bom_written_by_windows_powershell() {
        let with_bom = "\u{feff}{ \"mode\": \"network\", \"port\": 9090, \"allow\": [\"10.0.0.0/8\"] }";
        assert_eq!(port_from_network_json(with_bom), Some(9090));
        let v: serde_json::Value = serde_json::from_str(&merge_network_port(Some(with_bom), 9191)).unwrap();
        assert_eq!(v["mode"], "network");
        assert_eq!(v["allow"][0], "10.0.0.0/8");
    }

    #[test]
    fn rejects_missing_or_invalid_port() {
        assert_eq!(port_from_network_json(r#"{ "mode": "local" }"#), None);
        assert_eq!(port_from_network_json(r#"{ "port": 0 }"#), None);
        assert_eq!(port_from_network_json(r#"{ "port": 70000 }"#), None);
        assert_eq!(port_from_network_json("not json"), None);
    }

    #[test]
    fn merges_port_preserving_mode_and_allow() {
        let merged = merge_network_port(Some(r#"{ "mode": "network", "port": 8080, "allow": ["10.0.0.0/8"] }"#), 9191);
        let v: serde_json::Value = serde_json::from_str(&merged).unwrap();
        assert_eq!(v["mode"], "network");
        assert_eq!(v["port"], 9191);
        assert_eq!(v["allow"][0], "10.0.0.0/8");
    }

    #[test]
    fn merge_creates_local_defaults_for_missing_or_broken_file() {
        for input in [None, Some("garbage"), Some("[1,2]")] {
            let v: serde_json::Value = serde_json::from_str(&merge_network_port(input, 8181)).unwrap();
            assert_eq!(v["mode"], "local");
            assert_eq!(v["port"], 8181);
            assert!(v["allow"].as_array().unwrap().is_empty());
        }
    }
}
