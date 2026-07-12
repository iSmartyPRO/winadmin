use serde::Deserialize;
use std::io::{Read, Write};
use std::path::Path;

#[derive(Clone)]
pub struct ReleaseInfo {
    pub version: String,
    pub asset_name: String,
    pub download_url: String,
    pub size: u64,
}

#[derive(Deserialize)]
struct ReleaseDto {
    tag_name: String,
    #[serde(default)]
    assets: Vec<AssetDto>,
}

#[derive(Deserialize)]
struct AssetDto {
    name: String,
    browser_download_url: String,
    #[serde(default)]
    size: u64,
}

pub fn fetch_latest(repo: &str) -> Result<ReleaseInfo, String> {
    let url = format!("https://api.github.com/repos/{repo}/releases/latest");
    let resp = ureq::get(&url)
        .set("User-Agent", "WinAdmin-Ctl")
        .set("Accept", "application/vnd.github+json")
        .call()
        .map_err(|e| e.to_string())?;

    let dto: ReleaseDto = resp.into_json().map_err(|e| e.to_string())?;

    let asset = dto
        .assets
        .into_iter()
        .find(|a| {
            let n = a.name.to_lowercase();
            n.starts_with("winadmin-") && n.ends_with("-win-x64.zip")
        })
        .ok_or_else(|| format!("No WinAdmin-*-win-x64.zip asset in release {}", dto.tag_name))?;

    let version = dto.tag_name.trim_start_matches(['v', 'V']).to_string();

    Ok(ReleaseInfo {
        version,
        asset_name: asset.name,
        download_url: asset.browser_download_url,
        size: asset.size,
    })
}

pub fn download_asset(
    release: &ReleaseInfo,
    dest: &Path,
    mut progress: impl FnMut(u64, Option<u64>),
) -> Result<(), String> {
    let resp = ureq::get(&release.download_url)
        .set("User-Agent", "WinAdmin-Ctl")
        .set("Accept", "application/octet-stream")
        .call()
        .map_err(|e| e.to_string())?;

    let total = resp
        .header("Content-Length")
        .and_then(|s| s.parse::<u64>().ok())
        .or(if release.size > 0 { Some(release.size) } else { None });

    let mut reader = resp.into_reader();
    let mut file = std::fs::File::create(dest).map_err(|e| e.to_string())?;
    let mut buffer = [0u8; 131072];
    let mut received: u64 = 0;

    loop {
        let read = reader.read(&mut buffer).map_err(|e| e.to_string())?;
        if read == 0 {
            break;
        }
        file.write_all(&buffer[..read]).map_err(|e| e.to_string())?;
        received += read as u64;
        progress(received, total);
    }

    Ok(())
}
