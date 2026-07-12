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
