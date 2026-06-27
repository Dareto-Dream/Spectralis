use std::{
    collections::HashMap,
    env,
    net::SocketAddr,
    path::{Path, PathBuf},
    sync::Arc,
};

use anyhow::{Context, Result};
use axum::{
    body::{Body, Bytes},
    extract::{DefaultBodyLimit, Path as AxumPath, Query, State},
    http::{header, HeaderMap, HeaderValue, StatusCode},
    response::{IntoResponse, Response},
    routing::{get, post, put},
    Json, Router,
};
use chrono::{DateTime, Duration, Utc};
use rand::Rng;
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use tokio::{fs, net::TcpListener};
use tower_http::cors::{Any, CorsLayer};

const PROTOCOL_VERSION: &str = "shared-play-v2";
const SESSION_TTL_HOURS: i64 = 12;
const MAX_PACKAGE_BYTES: usize = 512 * 1024 * 1024;
const NO_STORE_CACHE_CONTROL: &str = "no-store, no-cache, must-revalidate, max-age=0";
const SHORT_STATIC_CACHE_CONTROL: &str = "public, max-age=60";
const PRESENCE_TTL_SECONDS: i64 = 45;
const REACTION_TTL_SECONDS: i64 = 90;
const MAX_REACTIONS: usize = 40;
const CHANNEL_HEARTBEAT_TTL_SECONDS: i64 = 30;

#[derive(Clone)]
struct AppState {
    data_dir: Arc<PathBuf>,
    web_share_root: Arc<PathBuf>,
    public_base_url: Option<String>,
}

#[tokio::main]
async fn main() -> Result<()> {
    let port = env::var("PORT")
        .or_else(|_| env::var("SPECTRALIS_BACKEND_PORT"))
        .unwrap_or_else(|_| "8787".to_string())
        .parse::<u16>()
        .context("PORT must be a valid TCP port")?;
    let host = env::var("SPECTRALIS_BACKEND_HOST").unwrap_or_else(|_| "0.0.0.0".to_string());
    let data_dir = PathBuf::from(
        env::var("SPECTRALIS_BACKEND_DATA").unwrap_or_else(|_| "backend/data".to_string()),
    );
    let web_share_root = PathBuf::from(
        env::var("SPECTRALIS_WEB_SHARE_ROOT").unwrap_or_else(|_| "web-share".to_string()),
    );
    let public_base_url = env::var("SPECTRALIS_PUBLIC_BASE_URL")
        .ok()
        .map(|value| value.trim_end_matches('/').to_string())
        .filter(|value| !value.is_empty());

    fs::create_dir_all(data_dir.join("sessions")).await?;

    let state = AppState {
        data_dir: Arc::new(data_dir),
        web_share_root: Arc::new(web_share_root),
        public_base_url,
    };

    let app = Router::new()
        .route("/", get(index))
        .route("/health", get(health))
        .route("/player.js", get(player_js))
        .route("/styles.css", get(styles_css))
        .route("/spectralis/web-share", get(index))
        .route("/spectralis/web-share/index.html", get(index))
        .route("/spectralis/web-share/*path", get(web_share_static))
        .layer(
            CorsLayer::new()
                .allow_origin(Any)
                .allow_methods(Any)
                .allow_headers(Any),
        )
        .layer(DefaultBodyLimit::max(MAX_PACKAGE_BYTES))
        .with_state(state);

    let address: SocketAddr = format!("{host}:{port}")
        .parse()
        .context("SPECTRALIS_BACKEND_HOST produced an invalid socket address")?;
    let listener = TcpListener::bind(address).await?;
    println!("Spectralis backend listening on http://{address}");
    axum::serve(listener, app).await?;
    Ok(())
}

async fn health() -> Json<Value> {
    Json(json!({
        "ok": true,
        "service": "spectralis-backend",
        "runtime": "rust",
        "protocolVersion": PROTOCOL_VERSION
    }))
}

async fn index(State(state): State<AppState>) -> Result<Response, AppError> {
    send_file(state.web_share_root.join("index.html"), None).await
}

async fn player_js(State(state): State<AppState>) -> Result<Response, AppError> {
    send_file(state.web_share_root.join("player.js"), None).await
}

async fn styles_css(State(state): State<AppState>) -> Result<Response, AppError> {
    send_file(state.web_share_root.join("styles.css"), None).await
}

async fn web_share_static(
    State(state): State<AppState>,
    AxumPath(path): AxumPath<String>,
) -> Result<Response, AppError> {
    let safe_path = clean_relative_path(&path)?;
    send_file(state.web_share_root.join(safe_path), None).await
}

// ── Room code generation ──────────────────────────────────────────────────────

fn generate_room_code() -> (String, String) {
    let seed: [u8; 32] = rand::thread_rng().gen();
    let hash = Sha256::digest(&seed);
    let n = u32::from_be_bytes([hash[0], hash[1], hash[2], hash[3]]);
    let code = to_base36_6((n as u64) % 36u64.pow(6));
    let session_key = bytes_to_hex(&hash);
    (code, session_key)
}

fn to_base36_6(mut n: u64) -> String {
    const CHARS: &[u8] = b"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    let mut buf = [b'0'; 6];
    for i in (0..6).rev() {
        buf[i] = CHARS[(n % 36) as usize];
        n /= 36;
    }
    String::from_utf8(buf.to_vec()).unwrap()
}

fn display_room_code(code: &str) -> String {
    if code.len() == 6 {
        format!("{}-{}", &code[..3], &code[3..])
    } else {
        code.to_string()
    }
}

fn clean_room_code(value: &str) -> Result<String, AppError> {
    let cleaned: String = value
        .trim()
        .chars()
        .filter(|ch| ch.is_ascii_alphanumeric())
        .map(|ch| ch.to_ascii_uppercase())
        .take(7)
        .collect();
    if cleaned.len() != 6 {
        return Err(AppError::bad_request("Room code must be 6 alphanumeric characters."));
    }
    Ok(cleaned)
}

fn bytes_to_hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

// ── Storage helpers ───────────────────────────────────────────────────────────

fn session_root(state: &AppState, room_code: &str) -> Result<PathBuf, AppError> {
    Ok(state.data_dir.join("sessions").join(clean_room_code(room_code)?))
}

async fn existing_session_root(state: &AppState, room_code: &str) -> Result<PathBuf, AppError> {
    let root = session_root(state, room_code)?;
    if fs::metadata(&root).await.is_err() {
        return Err(AppError::not_found("Shared Play session was not found."));
    }
    if is_session_expired(&root).await {
        let _ = fs::remove_dir_all(&root).await;
        return Err(AppError::not_found("Shared Play session has expired."));
    }
    Ok(root)
}

fn channel_path(state: &AppState, channel_id: &str) -> Result<PathBuf, AppError> {
    Ok(state
        .data_dir
        .join("channels")
        .join(format!("{}.json", clean_channel_id(channel_id)?)))
}

async fn cleanup_expired_sessions(state: &AppState) {
    let sessions_root = state.data_dir.join("sessions");
    let Ok(mut entries) = fs::read_dir(&sessions_root).await else {
        return;
    };
    while let Ok(Some(entry)) = entries.next_entry().await {
        let path = entry.path();
        if is_session_expired(&path).await {
            let _ = fs::remove_dir_all(path).await;
        }
    }
}

async fn is_session_expired(session_root: &Path) -> bool {
    let Ok(meta) = fs::metadata(session_root).await else {
        return false;
    };
    if !meta.is_dir() {
        return false;
    }
    let Ok(bytes) = fs::read(session_root.join("manifest.json")).await else {
        return false;
    };
    let Ok(manifest) = serde_json::from_slice::<Value>(&bytes) else {
        return false;
    };
    let Some(expires_at) = first_string(&manifest, &["expiresAtUtc"]) else {
        return false;
    };
    DateTime::parse_from_rfc3339(&expires_at)
        .map(|v| v.with_timezone(&Utc) <= Utc::now())
        .unwrap_or(false)
}

async fn read_session_listener_count(state: &AppState, room_code: &str) -> Result<usize, AppError> {
    let code = clean_room_code(room_code)?;
    let presence_path = existing_session_root(state, &code)
        .await?
        .join("presence.json");
    let mut presence = read_json(presence_path)
        .await
        .unwrap_or_else(|_| empty_presence(&code));
    prune_presence(&mut presence);
    Ok(presence.get("listenerCount").and_then(Value::as_u64).unwrap_or(0) as usize)
}

// ── Manifest helpers ──────────────────────────────────────────────────────────

fn active_track_id(manifest: &Value) -> Option<String> {
    first_string(manifest, &["activeTrackId", "currentTrackId", "trackId"])
}

fn active_track_entry(manifest: &Value, active_track_id: &Option<String>) -> Option<Value> {
    let track_id = active_track_id.as_deref()?;
    manifest.get("tracks").and_then(|t| t.get(track_id)).cloned()
}

fn payload_track_id(payload: &Value) -> Option<String> {
    first_string(payload, &["activeTrackId", "currentTrackId", "trackId"])
}

fn upsert_active_track(manifest: &mut Value, track_id: &str, track: Value, package: Value) {
    manifest["activeTrackId"] = json!(track_id);
    manifest["currentTrackId"] = json!(track_id);
    manifest["trackId"] = json!(track_id);
    manifest["track"] = track.clone();
    manifest["package"] = package.clone();
    upsert_track_entry(manifest, track_id, track, package, "active");
}

fn upsert_pending_track(manifest: &mut Value, track_id: &str, track: Value, package: Value) {
    upsert_track_entry(manifest, track_id, track, package, "pending-upload");
}

fn upsert_track_entry(manifest: &mut Value, track_id: &str, track: Value, package: Value, status: &str) {
    if !manifest.get("tracks").is_some_and(Value::is_object) {
        manifest["tracks"] = json!({});
    }
    if let Some(tracks) = manifest.get_mut("tracks").and_then(Value::as_object_mut) {
        tracks.insert(
            track_id.to_string(),
            json!({
                "trackId": track_id,
                "assetKey": track_asset_key(track_id),
                "status": status,
                "track": track,
                "package": package,
                "updatedAtUtc": Utc::now().to_rfc3339()
            }),
        );
    }
}

fn activate_track_by_asset_key(manifest: &mut Value, asset_key: &str) -> bool {
    let entry = manifest
        .get("tracks")
        .and_then(Value::as_object)
        .and_then(|tracks| {
            tracks.values().find(|e| {
                e.get("assetKey")
                    .and_then(Value::as_str)
                    .is_some_and(|v| v == asset_key)
            })
        })
        .cloned();

    let Some(mut entry) = entry else { return false };
    let Some(track_id) = entry.get("trackId").and_then(Value::as_str).map(ToOwned::to_owned) else {
        return false;
    };

    entry["status"] = json!("active");
    entry["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    manifest["activeTrackId"] = json!(&track_id);
    manifest["currentTrackId"] = json!(&track_id);
    manifest["trackId"] = json!(&track_id);
    manifest["track"] = entry.get("track").cloned().unwrap_or_else(|| json!({}));
    manifest["package"] = entry.get("package").cloned().unwrap_or_else(|| json!({}));

    if let Some(tracks) = manifest.get_mut("tracks").and_then(Value::as_object_mut) {
        tracks.insert(track_id, entry);
    }
    true
}

fn enrich_with_active_track(payload: &mut Value, manifest: &Value, base_url: &str, room_code: &str) {
    let active_track_id = active_track_id(manifest).or_else(|| payload_track_id(payload));
    let active_track = active_track_id
        .as_deref()
        .and_then(|id| active_track_entry(manifest, &Some(id.to_string())))
        .or_else(|| active_track_entry(manifest, &active_track_id));
    let package_url = active_track_id
        .as_deref()
        .map(|id| {
            format!(
                "{base_url}/shared-play/v2/sessions/{room_code}/tracks/{}/package",
                track_asset_key(id)
            )
        })
        .unwrap_or_else(|| format!("{base_url}/shared-play/v2/sessions/{room_code}/package"));

    payload["trackId"] = json!(&active_track_id);
    payload["activeTrackId"] = json!(&active_track_id);
    payload["currentTrackId"] = json!(&active_track_id);
    payload["packageUrl"] = json!(&package_url);
    payload["spectralisPackageUrl"] = json!(&package_url);
    payload["assetUrl"] = json!(&package_url);

    if let Some(entry) = active_track {
        payload["track"] = entry.get("track").cloned().unwrap_or_else(|| json!({}));
        payload["package"] = merge_object(
            entry.get("package").cloned().unwrap_or_else(|| json!({})),
            json!({ "assetUrl": &package_url, "url": &package_url }),
        );
    } else {
        payload["track"] = manifest.get("track").cloned().unwrap_or_else(|| json!({}));
        payload["package"] = merge_object(
            manifest.get("package").cloned().unwrap_or_else(|| json!({})),
            json!({ "assetUrl": &package_url, "url": &package_url }),
        );
    }
}

// ── Presence / reactions / queue stubs ───────────────────────────────────────

fn empty_queue(room_code: &str) -> Value {
    json!({
        "protocolVersion": PROTOCOL_VERSION,
        "roomCode": room_code,
        "version": 1,
        "currentIndex": -1,
        "updatedAtUtc": Utc::now().to_rfc3339(),
        "items": []
    })
}

fn empty_presence(room_code: &str) -> Value {
    json!({
        "protocolVersion": PROTOCOL_VERSION,
        "roomCode": room_code,
        "listenerCount": 0,
        "updatedAtUtc": Utc::now().to_rfc3339(),
        "participants": []
    })
}

fn empty_reactions(room_code: &str) -> Value {
    json!({
        "protocolVersion": PROTOCOL_VERSION,
        "roomCode": room_code,
        "updatedAtUtc": Utc::now().to_rfc3339(),
        "items": []
    })
}

fn empty_channel_stats() -> Value {
    json!({
        "totalSharedSeconds": 0,
        "totalListenerSeconds": 0,
        "peakConcurrentListeners": 0,
        "trackStats": []
    })
}

fn prune_presence(presence: &mut Value) {
    if !presence.get("participants").is_some_and(Value::is_array) {
        presence["participants"] = json!([]);
    }
    if let Some(participants) = presence.get_mut("participants").and_then(Value::as_array_mut) {
        participants.retain(|p| {
            p.get("lastSeenUtc")
                .and_then(Value::as_str)
                .is_some_and(|v| is_recent_utc(v, PRESENCE_TTL_SECONDS))
        });
    }
    update_presence_counts(presence);
}

fn update_presence_counts(presence: &mut Value) {
    let count = presence
        .get("participants")
        .and_then(Value::as_array)
        .map_or(0, Vec::len);
    presence["listenerCount"] = json!(count);
    presence["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
}

fn prune_reactions(reactions: &mut Value) {
    if !reactions.get("items").is_some_and(Value::is_array) {
        reactions["items"] = json!([]);
    }
    if let Some(items) = reactions.get_mut("items").and_then(Value::as_array_mut) {
        items.retain(|r| {
            r.get("createdAtUtc")
                .and_then(Value::as_str)
                .is_some_and(|v| is_recent_utc(v, REACTION_TTL_SECONDS))
        });
        if items.len() > MAX_REACTIONS {
            let drain = items.len() - MAX_REACTIONS;
            items.drain(0..drain);
        }
    }
    reactions["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
}

fn normalize_reaction_type(value: &str) -> String {
    let n = value.trim().to_ascii_lowercase();
    match n.as_str() {
        "love" | "fire" | "wow" | "boost" | "spark" => n,
        "plus" | "+1" | "plus-one" => "plus".to_string(),
        _ => "spark".to_string(),
    }
}

fn reaction_label(reaction_type: &str) -> &'static str {
    match reaction_type {
        "love" => "Love",
        "fire" => "Fire",
        "wow" => "Whoa",
        "boost" => "Boost",
        "plus" => "+1",
        _ => "Spark",
    }
}

fn normalize_queue_payload(room_code: &str, payload: Value) -> Value {
    let source = if let Some(q) = payload.get("queue") { q.clone() } else { payload };
    let items = source.get("items").and_then(Value::as_array).cloned().unwrap_or_default();
    json!({
        "protocolVersion": PROTOCOL_VERSION,
        "roomCode": room_code,
        "version": source.get("version").cloned().unwrap_or(json!(1)),
        "currentIndex": source.get("currentIndex").cloned().unwrap_or(json!(-1)),
        "updatedAtUtc": Utc::now().to_rfc3339(),
        "items": items
    })
}

fn normalize_queue_item(payload: Value) -> Result<Value, AppError> {
    let item = if let Some(i) = payload.get("item") { i.clone() } else { payload };
    let url = item
        .get("url")
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|v| !v.is_empty())
        .ok_or_else(|| AppError::bad_request("Queue item URL was empty."))?;

    if !(url.starts_with("https://") || url.starts_with("http://") || url.starts_with("spotify:")) {
        return Err(AppError::bad_request("Queue item URL must be http(s) or spotify:."));
    }

    let title = item.get("title").and_then(Value::as_str).map(str::trim).filter(|v| !v.is_empty()).unwrap_or(url);
    let source_kind = item.get("sourceKind").and_then(Value::as_str).map(str::trim).filter(|v| !v.is_empty()).unwrap_or("url");
    let id = item
        .get("id")
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|v| !v.is_empty())
        .map(|v| v.to_string())
        .unwrap_or_else(|| {
            let code: String = rand::thread_rng()
                .sample_iter(rand::distributions::Alphanumeric)
                .take(16)
                .map(char::from)
                .collect();
            code
        });
    let added_at_utc = item
        .get("addedAtUtc")
        .and_then(Value::as_str)
        .map(|v| v.to_string())
        .unwrap_or_else(|| Utc::now().to_rfc3339());

    Ok(json!({
        "id": id,
        "sourceKind": source_kind,
        "title": title,
        "artist": item.get("artist").cloned().unwrap_or(Value::Null),
        "album": item.get("album").cloned().unwrap_or(Value::Null),
        "url": url,
        "sourceId": item.get("sourceId").cloned().unwrap_or(Value::Null),
        "durationSeconds": item.get("durationSeconds").cloned().unwrap_or(Value::Null),
        "addedBy": item.get("addedBy").and_then(Value::as_str).unwrap_or("remote"),
        "addedAtUtc": added_at_utc,
        "trackId": item.get("trackId").cloned().unwrap_or(Value::Null),
        "packageUrl": item.get("packageUrl").cloned().unwrap_or(Value::Null)
    }))
}

// ── String / validation helpers ───────────────────────────────────────────────

fn clean_asset_key(value: &str) -> Result<String, AppError> {
    let cleaned: String = value
        .trim()
        .chars()
        .filter(|ch| ch.is_ascii_alphanumeric() || matches!(ch, '_' | '-'))
        .take(128)
        .collect();
    if cleaned.is_empty() {
        Err(AppError::bad_request("Asset key was empty."))
    } else {
        Ok(cleaned)
    }
}

fn track_asset_key(track_id: &str) -> String {
    let cleaned: String = track_id
        .trim()
        .chars()
        .map(|ch| if ch.is_ascii_alphanumeric() || ch == '-' || ch == '_' { ch } else { '-' })
        .collect();
    let trimmed = cleaned.trim_matches('-');
    if trimmed.is_empty() {
        let code: String = rand::thread_rng()
            .sample_iter(rand::distributions::Alphanumeric)
            .take(16)
            .map(char::from)
            .collect();
        code
    } else {
        trimmed.chars().take(96).collect()
    }
}

fn clean_channel_id(value: &str) -> Result<String, AppError> {
    let cleaned: String = value
        .trim()
        .chars()
        .filter(|ch| ch.is_ascii_alphanumeric() || matches!(ch, '_' | '-'))
        .take(64)
        .collect();
    if cleaned.is_empty() {
        Err(AppError::bad_request("Channel ID was empty."))
    } else {
        Ok(cleaned)
    }
}

fn clean_owner_token(value: &str) -> Result<String, AppError> {
    let cleaned: String = value
        .trim()
        .chars()
        .filter(|ch| ch.is_ascii_alphanumeric() || matches!(ch, '_' | '-' | '.'))
        .take(128)
        .collect();
    if cleaned.len() < 16 {
        Err(AppError::bad_request("Channel owner token was invalid."))
    } else {
        Ok(cleaned)
    }
}

fn clean_client_id(value: &str) -> Option<String> {
    let cleaned: String = value
        .trim()
        .chars()
        .filter(|ch| ch.is_ascii_alphanumeric() || matches!(ch, '.' | '_' | ':' | '-'))
        .take(80)
        .collect();
    if cleaned.is_empty() { None } else { Some(cleaned) }
}

fn clean_short_text(value: &str, max_chars: usize) -> Option<String> {
    let cleaned = value
        .trim()
        .chars()
        .filter(|ch| !ch.is_control())
        .take(max_chars)
        .collect::<String>()
        .trim()
        .to_string();
    if cleaned.is_empty() { None } else { Some(cleaned) }
}

fn clean_relative_path(path: &str) -> Result<PathBuf, AppError> {
    let candidate = Path::new(path);
    if candidate.components().any(|c| matches!(c, std::path::Component::ParentDir)) {
        return Err(AppError::forbidden("Forbidden."));
    }
    Ok(candidate.to_path_buf())
}

fn track_id_to_string(value: &Value) -> String {
    value
        .as_str()
        .map(str::trim)
        .filter(|t| !t.is_empty())
        .map(ToOwned::to_owned)
        .unwrap_or_else(|| {
            let (code, _) = generate_room_code();
            code
        })
}

fn first_string(value: &Value, names: &[&str]) -> Option<String> {
    names.iter().find_map(|name| {
        value
            .get(name)
            .and_then(Value::as_str)
            .map(str::trim)
            .filter(|t| !t.is_empty())
            .map(ToOwned::to_owned)
    })
}

fn merge_object(mut base: Value, additions: Value) -> Value {
    if let (Some(base), Some(additions)) = (base.as_object_mut(), additions.as_object()) {
        for (k, v) in additions {
            base.insert(k.clone(), v.clone());
        }
    }
    base
}

fn is_recent_utc(value: &str, ttl_seconds: i64) -> bool {
    DateTime::parse_from_rfc3339(value)
        .map(|t| Utc::now() - t.with_timezone(&Utc) <= Duration::seconds(ttl_seconds))
        .unwrap_or(false)
}

fn parse_utc(value: &str) -> Option<DateTime<Utc>> {
    DateTime::parse_from_rfc3339(value)
        .map(|t| t.with_timezone(&Utc))
        .ok()
}

fn base_url(state: &AppState, headers: &HeaderMap) -> String {
    if let Some(public_base_url) = &state.public_base_url {
        return public_base_url.trim_end_matches('/').to_string();
    }
    let proto = header_string(headers, "x-forwarded-proto").unwrap_or_else(|| "https".to_string());
    let host = header_string(headers, "x-forwarded-host")
        .or_else(|| header_string(headers, "host"))
        .unwrap_or_else(|| "localhost".to_string());
    format!("{proto}://{host}").trim_end_matches('/').to_string()
}

fn header_string(headers: &HeaderMap, name: &str) -> Option<String> {
    headers
        .get(name)
        .and_then(|v| v.to_str().ok())
        .map(|v| v.to_string())
        .filter(|v| !v.trim().is_empty())
}

// ── File serving ──────────────────────────────────────────────────────────────

async fn send_file(path: PathBuf, content_type: Option<&str>) -> Result<Response, AppError> {
    let metadata = fs::metadata(&path).await.map_err(|_| AppError::not_found("File not found."))?;
    if !metadata.is_file() {
        return Err(AppError::not_found("File not found."));
    }
    let bytes = fs::read(&path).await?;
    let mime = content_type
        .map(|v| v.to_string())
        .or_else(|| mime_guess::from_path(&path).first().map(|m| m.to_string()))
        .unwrap_or_else(|| "application/octet-stream".to_string());
    let cache_control = cache_control_for_path(&path);

    let mut response = Response::new(Body::from(bytes));
    response.headers_mut().insert(
        header::CONTENT_TYPE,
        HeaderValue::from_str(&mime).unwrap_or(HeaderValue::from_static("application/octet-stream")),
    );
    response.headers_mut().insert(
        header::CACHE_CONTROL,
        HeaderValue::from_static(cache_control),
    );
    if cache_control == NO_STORE_CACHE_CONTROL {
        response.headers_mut().insert(header::PRAGMA, HeaderValue::from_static("no-cache"));
        response.headers_mut().insert(header::EXPIRES, HeaderValue::from_static("0"));
    }
    response.headers_mut().insert(
        "cross-origin-resource-policy",
        HeaderValue::from_static("cross-origin"),
    );
    Ok(response)
}

fn cache_control_for_path(path: &Path) -> &'static str {
    match path.extension().and_then(|v| v.to_str()) {
        Some("css" | "html" | "js" | "zip") => NO_STORE_CACHE_CONTROL,
        _ => SHORT_STATIC_CACHE_CONTROL,
    }
}

async fn read_json(path: PathBuf) -> Result<Value, AppError> {
    let bytes = fs::read(path).await.map_err(|_| AppError::not_found("Not found."))?;
    Ok(serde_json::from_slice(&bytes)?)
}

async fn write_json(path: PathBuf, value: &Value) -> Result<(), AppError> {
    let temp_path = path.with_extension(format!(
        "{}.tmp",
        path.extension().and_then(|v| v.to_str()).unwrap_or("json")
    ));
    let bytes = serde_json::to_vec_pretty(value)?;
    fs::write(&temp_path, bytes).await?;
    fs::rename(temp_path, path).await?;
    Ok(())
}

// ── Error type ────────────────────────────────────────────────────────────────

#[derive(Debug)]
struct AppError {
    status: StatusCode,
    message: String,
}

impl AppError {
    fn bad_request(message: &str) -> Self {
        Self { status: StatusCode::BAD_REQUEST, message: message.to_string() }
    }
    fn forbidden(message: &str) -> Self {
        Self { status: StatusCode::FORBIDDEN, message: message.to_string() }
    }
    fn not_found(message: &str) -> Self {
        Self { status: StatusCode::NOT_FOUND, message: message.to_string() }
    }
    fn payload_too_large(message: &str) -> Self {
        Self { status: StatusCode::PAYLOAD_TOO_LARGE, message: message.to_string() }
    }
}

impl IntoResponse for AppError {
    fn into_response(self) -> Response {
        let body = Json(json!({ "error": self.message, "status": self.status.as_u16() }));
        (self.status, body).into_response()
    }
}

impl From<anyhow::Error> for AppError {
    fn from(e: anyhow::Error) -> Self {
        Self { status: StatusCode::INTERNAL_SERVER_ERROR, message: e.to_string() }
    }
}

impl From<std::io::Error> for AppError {
    fn from(e: std::io::Error) -> Self {
        Self { status: StatusCode::INTERNAL_SERVER_ERROR, message: e.to_string() }
    }
}

impl From<serde_json::Error> for AppError {
    fn from(e: serde_json::Error) -> Self {
        Self { status: StatusCode::BAD_REQUEST, message: e.to_string() }
    }
}
