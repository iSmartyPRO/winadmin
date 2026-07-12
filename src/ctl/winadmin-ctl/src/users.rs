use crate::service::CREATE_NO_WINDOW;
use crate::settings::Settings;
use std::os::windows::process::CommandExt;
use std::process::Command;

pub struct UserRow {
    pub login: String,
    pub scopes: String,
    pub created: String,
    pub active: bool,
}

fn run(s: &Settings, args: &[&str]) -> Result<String, String> {
    let exe = s.exe_path();
    if !exe.exists() {
        return Err(format!("WinAdmin is not installed ({} not found).", exe.display()));
    }

    let out = Command::new(&exe)
        .args(args)
        .current_dir(&s.install_path)
        .env("WinAdmin__DatabasePath", s.db_path())
        .creation_flags(CREATE_NO_WINDOW)
        .output()
        .map_err(|e| e.to_string())?;

    let stdout = String::from_utf8_lossy(&out.stdout).into_owned();
    if !out.status.success() {
        let stderr = String::from_utf8_lossy(&out.stderr);
        return Err(format!(
            "WinAdmin.exe {} failed ({}): {}{}",
            args.join(" "),
            out.status.code().unwrap_or(-1),
            stderr.trim(),
            stdout.trim()
        ));
    }
    Ok(stdout)
}

pub fn list(s: &Settings) -> Result<Vec<UserRow>, String> {
    Ok(parse(&run(s, &["user", "list"])?))
}

pub fn add(s: &Settings, login: &str, password: &str, scopes: &str) -> Result<(), String> {
    let mut args = vec!["user", "add", "--login", login, "--password", password];
    let scopes = scopes.trim();
    if !scopes.is_empty() {
        args.push("--scopes");
        args.push(scopes);
    }
    run(s, &args).map(|_| ())
}

pub fn reset_password(s: &Settings, login: &str, password: &str) -> Result<(), String> {
    run(s, &["user", "password", login, password]).map(|_| ())
}

pub fn delete(s: &Settings, login: &str) -> Result<(), String> {
    run(s, &["user", "delete", login]).map(|_| ())
}

fn parse(output: &str) -> Vec<UserRow> {
    let mut rows = Vec::new();
    let mut past_header = false;

    for raw in output.lines() {
        let line = raw.trim_end();
        if line.is_empty() {
            continue;
        }
        if line.trim_start().to_uppercase().starts_with("LOGIN") {
            past_header = true;
            continue;
        }
        if line.contains('─') {
            continue;
        }
        if !past_header {
            continue;
        }

        let parts: Vec<&str> = line.split_whitespace().collect();
        if parts.len() < 3 {
            continue;
        }

        let login = parts[0].to_string();
        let active = parts[parts.len() - 1].eq_ignore_ascii_case("yes");
        let created = parts[parts.len() - 2].to_string();
        let scopes = if parts.len() >= 4 {
            parts[1..parts.len() - 2].join(" ")
        } else {
            String::new()
        };

        rows.push(UserRow { login, scopes, created, active });
    }

    rows
}
