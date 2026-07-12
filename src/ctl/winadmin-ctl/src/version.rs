use crate::settings::{EXE_NAME, VERSION_FILE};
use std::path::Path;

pub struct LocalInfo {
    pub installed: bool,
    pub version: Option<String>,
}

pub fn read_local(install_path: &str) -> LocalInfo {
    let exe = Path::new(install_path).join(EXE_NAME);
    if !exe.exists() {
        return LocalInfo { installed: false, version: None };
    }

    let version_file = Path::new(install_path).join(VERSION_FILE);
    let version = std::fs::read_to_string(&version_file)
        .ok()
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty());

    LocalInfo { installed: true, version }
}

/// True when `remote` is a newer version than `local`.
pub fn is_newer(remote: &str, local: Option<&str>) -> bool {
    let local = match local {
        Some(l) if !l.trim().is_empty() => l,
        _ => return !remote.trim().is_empty(),
    };
    if remote.trim().is_empty() {
        return false;
    }

    match (parse(remote), parse(local)) {
        (Some(r), Some(l)) => r > l,
        _ => remote > local,
    }
}

fn parse(v: &str) -> Option<Vec<u32>> {
    let v = v.trim().trim_start_matches(['v', 'V']);
    v.split('.').map(|p| p.parse::<u32>().ok()).collect()
}
