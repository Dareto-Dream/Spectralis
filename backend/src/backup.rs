//! Backups of the data that can't be rebuilt: published content (warnings, changelog, release index,
//! community, creator keys), permanent rooms and their members. Everything else in Redis is a session
//! with a TTL and is not worth keeping.
//!
//! Once a day the server writes one JSON snapshot to its own bucket under `backups/` and keeps the
//! newest fourteen. The snapshot holds room owner tokens, so it never leaves the bucket except
//! through the admin routes below.

use std::time::Duration;

use axum::{
    extract::{Path as AxumPath, State},
    http::{header, HeaderMap, HeaderValue},
    response::{IntoResponse, Response},
    Json,
};
use bytes::Bytes;
use chrono::Utc;
use serde_json::{json, Map, Value};

use crate::{content, AppError, AppState};

/// Key patterns worth keeping. Anything not matched here expires on its own.
const DURABLE_PATTERNS: [&str; 3] = ["content:*", "sp:chan:*", "player:member:*"];
const INDEX_KEY: &str = "backup:index";
const KEEP: usize = 14;
const CHECK_EVERY: Duration = Duration::from_secs(60 * 60);
const FIRST_CHECK_AFTER: Duration = Duration::from_secs(5 * 60);

fn blob_key(name: &str) -> String {
    format!("backups/{name}")
}

/// `spectralis-2026-11-15.json`: one per day, so a second run on the same day replaces the first.
fn name_for(date: &str) -> String {
    format!("spectralis-{date}.json")
}

fn valid_name(name: &str) -> bool {
    name.len() == "spectralis-0000-00-00.json".len()
        && name.starts_with("spectralis-")
        && name.ends_with(".json")
        && name["spectralis-".len()..name.len() - ".json".len()].chars().all(|c| c.is_ascii_digit() || c == '-')
}

async fn read_index(state: &AppState) -> Result<Vec<Value>, AppError> {
    Ok(state
        .store
        .get_json(INDEX_KEY)
        .await
        .map_err(AppError::internal)?
        .and_then(|v| v.as_array().cloned())
        .unwrap_or_default())
}

/// Takes a snapshot now, records it in the index and deletes the ones past the retention count.
pub async fn run(state: &AppState) -> Result<Value, AppError> {
    let mut keys = Map::new();
    for pattern in DURABLE_PATTERNS {
        for key in state.store.scan_keys(pattern).await.map_err(AppError::internal)? {
            if let Some(value) = state.store.get_json(&key).await.map_err(AppError::internal)? {
                keys.insert(key, value);
            }
        }
    }

    let now = Utc::now();
    let name = name_for(&now.format("%Y-%m-%d").to_string());
    let count = keys.len();
    let snapshot = json!({
        "schema": "spectralis.backup.v1",
        "takenAt": now.to_rfc3339_opts(chrono::SecondsFormat::Secs, true),
        "keys": keys,
    });
    let bytes = serde_json::to_vec(&snapshot).map_err(AppError::internal)?;
    let size = bytes.len();
    state
        .store
        .put_blob(&blob_key(&name), Bytes::from(bytes), "application/json")
        .await
        .map_err(AppError::internal)?;

    let entry = json!({ "name": name, "takenAt": snapshot["takenAt"], "keys": count, "bytes": size });
    let mut index: Vec<Value> = read_index(state).await?.into_iter().filter(|e| e["name"] != entry["name"]).collect();
    index.insert(0, entry.clone());
    for old in index.iter().skip(KEEP) {
        if let Some(old_name) = old["name"].as_str().filter(|n| valid_name(n)) {
            state.store.delete_blob(&blob_key(old_name)).await.map_err(AppError::internal)?;
        }
    }
    index.truncate(KEEP);
    state.store.put_json(INDEX_KEY, &Value::Array(index), None).await.map_err(AppError::internal)?;
    Ok(entry)
}

async fn due(state: &AppState) -> bool {
    let today = name_for(&Utc::now().format("%Y-%m-%d").to_string());
    match read_index(state).await {
        Ok(index) => index.first().and_then(|e| e["name"].as_str()) != Some(today.as_str()),
        Err(_) => false, // can't read the index: don't pile snapshots up blindly
    }
}

/// Checks hourly and snapshots when today has none yet, so a restart neither skips a day nor
/// takes a second one. Only runs against the real store; a dev server has nothing to keep.
pub fn spawn_daily(state: AppState) {
    if !state.store.is_live() {
        return;
    }
    tokio::spawn(async move {
        tokio::time::sleep(FIRST_CHECK_AFTER).await;
        let mut interval = tokio::time::interval(CHECK_EVERY);
        loop {
            interval.tick().await;
            if due(&state).await {
                match run(&state).await {
                    Ok(entry) => eprintln!("backup: wrote {} ({} keys)", entry["name"], entry["keys"]),
                    Err(err) => eprintln!("backup: failed: {}", err.message),
                }
            }
        }
    });
}

/// `GET /spectralis/v1/admin/backups`: what is kept, newest first.
pub async fn list(State(state): State<AppState>, headers: HeaderMap) -> Result<Json<Value>, AppError> {
    content::require_admin(&headers)?;
    Ok(Json(json!({ "keep": KEEP, "backups": read_index(&state).await? })))
}

/// `POST /spectralis/v1/admin/backups`: take one now (before a risky deploy, say).
pub async fn create(State(state): State<AppState>, headers: HeaderMap) -> Result<Json<Value>, AppError> {
    content::require_admin(&headers)?;
    Ok(Json(json!({ "ok": true, "backup": run(&state).await? })))
}

/// `GET /spectralis/v1/admin/backups/{name}`: the snapshot itself. It holds owner tokens.
pub async fn download(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(name): AxumPath<String>,
) -> Result<Response, AppError> {
    content::require_admin(&headers)?;
    if !valid_name(&name) {
        return Err(AppError::not_found("No such backup."));
    }
    let blob = state
        .store
        .get_blob(&blob_key(&name))
        .await
        .map_err(AppError::internal)?
        .ok_or_else(|| AppError::not_found("No such backup."))?;
    let mut response = blob.bytes.into_response();
    response.headers_mut().insert(header::CONTENT_TYPE, HeaderValue::from_static("application/json"));
    response.headers_mut().insert(header::CACHE_CONTROL, HeaderValue::from_static("no-store"));
    Ok(response)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::collab::Rooms;
    use axum::http::StatusCode;
    use std::{path::PathBuf, sync::Arc};

    const TOKEN: &str = "test-admin-token-0123456789abcdef";

    async fn test_state() -> AppState {
        std::env::remove_var("REDIS_URL");
        std::env::set_var("SPECTRALIS_ADMIN_TOKEN", TOKEN);
        AppState {
            store: Arc::new(crate::store::Store::from_env().await.unwrap()),
            rooms: Rooms::default(),
            sq_rooms: Rooms::default(),
            web_share_root: Arc::new(PathBuf::new()),
            public_base_url: Some("https://api.test".to_string()),
            stripe_secret_key: None,
            stripe_webhook_secret: None,
            stripe_connect_client_id: None,
            stripe_publishable_key: None,
            ward_issuer: Arc::new("http://ward.test".to_string()),
            ward_enabled: true,
        }
    }

    fn admin() -> HeaderMap {
        let mut headers = HeaderMap::new();
        headers.insert(header::AUTHORIZATION, HeaderValue::from_str(&format!("Bearer {TOKEN}")).unwrap());
        headers
    }

    #[tokio::test]
    async fn a_backup_holds_the_durable_keys_and_nothing_else() {
        let state = test_state().await;
        state.store.put_json("content:changelog", &json!([{ "version": "7.1.0" }]), None).await.unwrap();
        state.store.put_json("sp:chan:abc", &json!({ "channelId": "abc", "ownerToken": "secret" }), None).await.unwrap();
        state.store.put_json("sp:session:XYZ", &json!({ "ephemeral": true }), None).await.unwrap();

        let entry = run(&state).await.unwrap();
        assert_eq!(entry["keys"], 2);

        let name = entry["name"].as_str().unwrap().to_string();
        let response = download(State(state.clone()), admin(), AxumPath(name.clone())).await.unwrap();
        let body = axum::body::to_bytes(response.into_body(), usize::MAX).await.unwrap();
        let snapshot: Value = serde_json::from_slice(&body).unwrap();
        assert_eq!(snapshot["keys"]["content:changelog"][0]["version"], "7.1.0");
        assert_eq!(snapshot["keys"]["sp:chan:abc"]["ownerToken"], "secret");
        assert!(snapshot["keys"].get("sp:session:XYZ").is_none());

        // same day again: replaced, not duplicated, and no longer due
        run(&state).await.unwrap();
        let listed = list(State(state.clone()), admin()).await.unwrap().0;
        assert_eq!(listed["backups"].as_array().unwrap().len(), 1);
        assert!(!due(&state).await);
    }

    #[tokio::test]
    async fn old_backups_are_pruned_and_their_files_deleted() {
        let state = test_state().await;
        let mut index = Vec::new();
        for day in 1..=KEEP {
            let name = name_for(&format!("2020-01-{day:02}"));
            state.store.put_blob(&blob_key(&name), Bytes::from_static(b"{}"), "application/json").await.unwrap();
            index.insert(0, json!({ "name": name }));
        }
        state.store.put_json(INDEX_KEY, &Value::Array(index), None).await.unwrap();
        assert!(due(&state).await);

        run(&state).await.unwrap();
        let kept = read_index(&state).await.unwrap();
        assert_eq!(kept.len(), KEEP);
        assert!(!state.store.blob_exists(&blob_key(&name_for("2020-01-01"))).await.unwrap());
        assert!(state.store.blob_exists(&blob_key(&name_for("2020-01-02"))).await.unwrap());
    }

    #[tokio::test]
    async fn backups_are_admin_only_and_names_are_checked() {
        let state = test_state().await;
        assert_eq!(list(State(state.clone()), HeaderMap::new()).await.unwrap_err().status, StatusCode::UNAUTHORIZED);
        assert_eq!(create(State(state.clone()), HeaderMap::new()).await.unwrap_err().status, StatusCode::UNAUTHORIZED);
        for bad in ["../content", "spectralis-2026-11-15.json.bak", "spectralis-../../x.json"] {
            let err = download(State(state.clone()), admin(), AxumPath(bad.to_string())).await.unwrap_err();
            assert_eq!(err.status, StatusCode::NOT_FOUND);
        }
    }
}
