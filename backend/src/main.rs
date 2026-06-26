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
use rand::{distributions::Alphanumeric, Rng};
use serde_json::{json, Value};
use tokio::{fs, net::TcpListener};
use tower_http::cors::{Any, CorsLayer};

const PROTOCOL_VERSION: &str = "shared-play-v1";
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
        .route("/shared-play/v1/upload-urls", post(create_upload_session))
        .route(
            "/shared-play/v1/uploads/:session_id/spectralis-package",
            put(upload_package),
        )
        .route(
            "/shared-play/v1/uploads/:session_id/tracks/:track_key/spectralis-package",
            put(upload_track_package),
        )
        .route("/shared-play/v1/sessions/:session_id", get(get_session))
        .route(
            "/shared-play/v1/sessions/:session_id/manifest",
            get(get_session),
        )
        .route(
            "/shared-play/v1/sessions/:session_id/tracks",
            post(create_track_upload),
        )
        .route(
            "/shared-play/v1/sessions/:session_id/state",
            get(get_state).post(post_state),
        )
        .route(
            "/shared-play/v1/sessions/:session_id/queue",
            get(get_queue).post(post_queue),
        )
        .route(
            "/shared-play/v1/sessions/:session_id/queue/items",
            post(post_queue_item),
        )
        .route(
            "/shared-play/v1/sessions/:session_id/presence",
            get(get_presence).post(post_presence),
        )
        .route(
            "/shared-play/v1/sessions/:session_id/reactions",
            get(get_reactions).post(post_reaction),
        )
        .route(
            "/shared-play/v1/sessions/:session_id/tracks/:track_key/activate",
            post(activate_track),
        )
        .route(
            "/shared-play/v1/channels/:channel_id",
            get(get_channel).put(put_channel),
        )
        .route(
            "/shared-play/v1/channels/:channel_id/stats",
            get(get_channel_stats),
        )
        .route("/shared-play/v1/join/:session_id", get(get_session))
        .route("/shared-play/join/:session_id", get(get_session))
        .route(
            "/shared-play/v1/packages/:session_id/spectralis-rich.zip",
            get(get_package),
        )
        .route(
            "/shared-play/v1/packages/:session_id/tracks/:track_key/spectralis-rich.zip",
            get(get_track_package),
        )
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
        "runtime": "rust"
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

async fn create_upload_session(
    State(state): State<AppState>,
    headers: HeaderMap,
    Json(payload): Json<Value>,
) -> Result<impl IntoResponse, AppError> {
    cleanup_expired_sessions(&state).await;

    let session_id = random_session_id();
    let session_root = session_root(&state, &session_id)?;
    fs::create_dir_all(&session_root).await?;

    let now = Utc::now();
    let expires_at = now + Duration::hours(SESSION_TTL_HOURS);
    let track = payload.get("track").cloned().unwrap_or_else(|| json!({}));
    let package = payload.get("package").cloned().unwrap_or_else(|| json!({}));
    let playback = payload.get("playback").cloned();
    let track_id = package
        .get("trackId")
        .or_else(|| payload.get("trackId"))
        .cloned()
        .unwrap_or_else(|| json!(&session_id));
    let track_id_text = track_id_to_string(&track_id);

    let mut manifest = json!({
        "protocolVersion": PROTOCOL_VERSION,
        "sessionId": &session_id,
        "createdAtUtc": now.to_rfc3339(),
        "expiresAtUtc": expires_at.to_rfc3339(),
        "activeTrackId": &track_id_text,
        "trackId": &track_id_text,
        "track": track.clone(),
        "package": package.clone(),
        "tracks": {},
        "capabilities": payload.get("capabilities").cloned().unwrap_or_else(|| json!({}))
    });
    upsert_active_track(&mut manifest, &track_id_text, track, package);
    write_json(session_root.join("manifest.json"), &manifest).await?;

    if let Some(playback) = playback {
        write_playback_state(&state, &session_id, playback).await?;
    }

    let base_url = base_url(&state, &headers);
    let upload_url =
        format!("{base_url}/shared-play/v1/uploads/{session_id}/spectralis-package");
    let package_url =
        format!("{base_url}/shared-play/v1/packages/{session_id}/spectralis-rich.zip");
    let state_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/state");
    let queue_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/queue");
    let presence_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/presence");
    let reactions_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/reactions");
    let join_url = format!("{base_url}/spectralis/web-share/index.html?session={session_id}");

    Ok((
        StatusCode::CREATED,
        Json(json!({
            "sessionId": &session_id,
            "trackId": &track_id_text,
            "joinUrl": &join_url,
            "stateUrl": &state_url,
            "queueUrl": &queue_url,
            "presenceUrl": &presence_url,
            "reactionsUrl": &reactions_url,
            "expiresAtUtc": expires_at.to_rfc3339(),
            "uploads": [{
                "name": "spectralis-package",
                "method": "PUT",
                "uploadUrl": &upload_url,
                "assetUrl": &package_url,
                "headers": {
                    "content-type": "application/vnd.spectralis.shared-play+zip"
                }
            }]
        })),
    ))
}

async fn upload_package(
    State(state): State<AppState>,
    AxumPath(session_id): AxumPath<String>,
    body: Bytes,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    if body.is_empty() {
        return Err(AppError::bad_request("Upload body was empty."));
    }
    if body.len() > MAX_PACKAGE_BYTES {
        return Err(AppError::payload_too_large("Upload body is too large."));
    }

    let session_root = existing_session_root(&state, &session_id).await?;
    let temp_path = session_root.join(format!("{}.tmp", uuid::Uuid::new_v4()));
    let package_path = session_root.join("spectralis-rich.zip");
    fs::write(&temp_path, &body).await?;
    fs::rename(&temp_path, &package_path).await?;

    if let Ok(manifest) = read_json(session_root.join("manifest.json")).await {
        if let Some(track_id) = active_track_id(&manifest) {
            let track_root = session_root.join("tracks").join(track_asset_key(&track_id));
            fs::create_dir_all(&track_root).await?;
            fs::copy(&package_path, track_root.join("spectralis-rich.zip")).await?;
        }
    }

    Ok(Json(json!({
        "ok": true,
        "bytes": body.len()
    })))
}

async fn upload_track_package(
    State(state): State<AppState>,
    AxumPath((session_id, track_key)): AxumPath<(String, String)>,
    Query(query): Query<HashMap<String, String>>,
    body: Bytes,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let track_key = clean_asset_key(&track_key)?;
    if body.is_empty() {
        return Err(AppError::bad_request("Upload body was empty."));
    }
    if body.len() > MAX_PACKAGE_BYTES {
        return Err(AppError::payload_too_large("Upload body is too large."));
    }

    let session_root = existing_session_root(&state, &session_id).await?;
    let track_root = session_root.join("tracks").join(&track_key);
    fs::create_dir_all(&track_root).await?;

    let temp_path = track_root.join(format!("{}.tmp", uuid::Uuid::new_v4()));
    let package_path = track_root.join("spectralis-rich.zip");
    fs::write(&temp_path, &body).await?;
    fs::rename(&temp_path, &package_path).await?;

    if query
        .get("activate")
        .is_none_or(|value| !value.eq_ignore_ascii_case("false"))
    {
        activate_track_package(&state, &session_id, &track_key, None).await?;
    } else {
        let manifest_path = session_root.join("manifest.json");
        if let Ok(mut manifest) = read_json(manifest_path.clone()).await {
            if let Some(entry) = manifest
                .get_mut("tracks")
                .and_then(Value::as_object_mut)
                .and_then(|tracks| {
                    tracks.values_mut().find(|entry| {
                        entry
                            .get("assetKey")
                            .and_then(Value::as_str)
                            .is_some_and(|value| value == track_key)
                    })
                })
            {
                entry["status"] = json!("ready");
                entry["packageReadyAtUtc"] = json!(Utc::now().to_rfc3339());
                manifest["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
                write_json(manifest_path, &manifest).await?;
            }
        }
    }

    Ok(Json(json!({
        "ok": true,
        "bytes": body.len()
    })))
}

async fn create_track_upload(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(session_id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<impl IntoResponse, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let session_root = existing_session_root(&state, &session_id).await?;
    let manifest_path = session_root.join("manifest.json");
    let mut manifest = read_json(manifest_path.clone()).await?;
    let track = payload.get("track").cloned().unwrap_or_else(|| json!({}));
    let package = payload.get("package").cloned().unwrap_or_else(|| json!({}));
    let track_id = package
        .get("trackId")
        .or_else(|| payload.get("trackId"))
        .cloned()
        .unwrap_or_else(|| json!(random_session_id()));
    let track_id_text = track_id_to_string(&track_id);
    let track_key = track_asset_key(&track_id_text);
    let activate_on_upload = payload
        .get("activateOnUpload")
        .and_then(Value::as_bool)
        .unwrap_or(true);

    upsert_pending_track(&mut manifest, &track_id_text, track, package);
    manifest["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(manifest_path, &manifest).await?;

    let base_url = base_url(&state, &headers);
    let upload_url = if activate_on_upload {
        format!("{base_url}/shared-play/v1/uploads/{session_id}/tracks/{track_key}/spectralis-package")
    } else {
        format!("{base_url}/shared-play/v1/uploads/{session_id}/tracks/{track_key}/spectralis-package?activate=false")
    };
    let package_url = format!(
        "{base_url}/shared-play/v1/packages/{session_id}/tracks/{track_key}/spectralis-rich.zip"
    );
    let state_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/state");
    let queue_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/queue");
    let presence_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/presence");
    let reactions_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/reactions");
    let join_url = format!("{base_url}/spectralis/web-share/index.html?session={session_id}");

    Ok((
        StatusCode::CREATED,
        Json(json!({
            "sessionId": &session_id,
            "trackId": &track_id_text,
            "joinUrl": &join_url,
            "stateUrl": &state_url,
            "queueUrl": &queue_url,
            "presenceUrl": &presence_url,
            "reactionsUrl": &reactions_url,
            "expiresAtUtc": manifest.get("expiresAtUtc").cloned().unwrap_or(Value::Null),
            "uploads": [{
                "name": "spectralis-package",
                "method": "PUT",
                "uploadUrl": &upload_url,
                "assetUrl": &package_url,
                "headers": {
                    "content-type": "application/vnd.spectralis.shared-play+zip"
                }
            }]
        })),
    ))
}

async fn activate_track_package(
    state: &AppState,
    session_id: &str,
    track_key: &str,
    playback_payload: Option<Value>,
) -> Result<Value, AppError> {
    let session_root = existing_session_root(state, session_id).await?;
    let track_package = session_root
        .join("tracks")
        .join(track_key)
        .join("spectralis-rich.zip");
    if fs::metadata(&track_package).await.is_err() {
        return Err(AppError::not_found("Shared Play track package was not found."));
    }

    fs::copy(&track_package, session_root.join("spectralis-rich.zip")).await?;

    let manifest_path = session_root.join("manifest.json");
    let mut manifest = read_json(manifest_path.clone()).await?;
    if !activate_track_by_asset_key(&mut manifest, track_key) {
        return Err(AppError::not_found("Shared Play track was not found."));
    }

    manifest["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(manifest_path, &manifest).await?;

    let state_body = if let Some(payload) = playback_payload {
        Some(write_playback_state(state, session_id, payload).await?)
    } else {
        None
    };

    Ok(json!({
        "ok": true,
        "sessionId": session_id,
        "trackKey": track_key,
        "trackId": active_track_id(&manifest),
        "state": state_body
    }))
}

async fn get_session(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(session_id): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    Ok(Json(read_session_payload(&state, &headers, &session_id).await?))
}

async fn get_state(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(session_id): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    Ok(Json(read_state_payload(&state, &headers, &session_id).await?))
}

async fn post_state(
    State(state): State<AppState>,
    AxumPath(session_id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let body = write_playback_state(&state, &session_id, payload).await?;
    Ok(Json(body))
}

async fn get_queue(
    State(state): State<AppState>,
    AxumPath(session_id): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let queue_path = existing_session_root(&state, &session_id)
        .await?
        .join("queue.json");

    let queue = read_json(queue_path).await.unwrap_or_else(|_| empty_queue(&session_id));
    Ok(Json(queue))
}

async fn post_queue(
    State(state): State<AppState>,
    AxumPath(session_id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let session_root = existing_session_root(&state, &session_id).await?;
    let mut queue = normalize_queue_payload(&session_id, payload);
    queue["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(session_root.join("queue.json"), &queue).await?;
    Ok(Json(queue))
}

async fn post_queue_item(
    State(state): State<AppState>,
    AxumPath(session_id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let session_root = existing_session_root(&state, &session_id).await?;
    let queue_path = session_root.join("queue.json");
    let mut queue = read_json(queue_path.clone())
        .await
        .unwrap_or_else(|_| empty_queue(&session_id));

    let item = normalize_queue_item(payload)?;
    let duplicate_id = item
        .get("id")
        .and_then(Value::as_str)
        .map(|value| value.to_string());

    let items = queue
        .get_mut("items")
        .and_then(Value::as_array_mut)
        .ok_or_else(|| AppError::bad_request("Queue payload was invalid."))?;

    if let Some(duplicate_id) = duplicate_id {
        if !items.iter().any(|existing| {
            existing
                .get("id")
                .and_then(Value::as_str)
                .is_some_and(|id| id == duplicate_id)
        }) {
            items.push(item);
        }
    } else {
        items.push(item);
    }

    queue["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(queue_path, &queue).await?;
    Ok(Json(queue))
}

async fn get_presence(
    State(state): State<AppState>,
    AxumPath(session_id): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let session_root = existing_session_root(&state, &session_id).await?;
    let presence_path = session_root.join("presence.json");
    let mut presence = read_json(presence_path.clone())
        .await
        .unwrap_or_else(|_| empty_presence(&session_id));
    prune_presence(&mut presence);
    write_json(presence_path, &presence).await?;
    Ok(Json(presence))
}

async fn post_presence(
    State(state): State<AppState>,
    AxumPath(session_id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let session_root = existing_session_root(&state, &session_id).await?;
    let presence_path = session_root.join("presence.json");
    let mut presence = read_json(presence_path.clone())
        .await
        .unwrap_or_else(|_| empty_presence(&session_id));
    prune_presence(&mut presence);

    let client_id = clean_client_id(
        payload
            .get("clientId")
            .and_then(Value::as_str)
            .unwrap_or(""),
    )
    .unwrap_or_else(random_session_id);
    let display_name = clean_short_text(
        payload
            .get("displayName")
            .and_then(Value::as_str)
            .unwrap_or(""),
        32,
    )
    .unwrap_or_else(|| "Listener".to_string());
    let now = Utc::now().to_rfc3339();

    let participants = presence
        .get_mut("participants")
        .and_then(Value::as_array_mut)
        .ok_or_else(|| AppError::bad_request("Presence payload was invalid."))?;
    if let Some(existing) = participants.iter_mut().find(|participant| {
        participant
            .get("clientId")
            .and_then(Value::as_str)
            .is_some_and(|value| value == client_id)
    }) {
        existing["displayName"] = json!(&display_name);
        existing["lastSeenUtc"] = json!(&now);
    } else {
        participants.push(json!({
            "clientId": client_id,
            "displayName": display_name,
            "lastSeenUtc": &now
        }));
    }

    update_presence_counts(&mut presence);
    write_json(presence_path, &presence).await?;
    Ok(Json(presence))
}

async fn get_reactions(
    State(state): State<AppState>,
    AxumPath(session_id): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let session_root = existing_session_root(&state, &session_id).await?;
    let reactions_path = session_root.join("reactions.json");
    let mut reactions = read_json(reactions_path.clone())
        .await
        .unwrap_or_else(|_| empty_reactions(&session_id));
    prune_reactions(&mut reactions);
    write_json(reactions_path, &reactions).await?;
    Ok(Json(reactions))
}

async fn post_reaction(
    State(state): State<AppState>,
    AxumPath(session_id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let session_root = existing_session_root(&state, &session_id).await?;
    let reactions_path = session_root.join("reactions.json");
    let mut reactions = read_json(reactions_path.clone())
        .await
        .unwrap_or_else(|_| empty_reactions(&session_id));
    prune_reactions(&mut reactions);

    let reaction_type = normalize_reaction_type(
        payload
            .get("type")
            .and_then(Value::as_str)
            .unwrap_or("spark"),
    );
    let label = clean_short_text(
        payload
            .get("label")
            .and_then(Value::as_str)
            .unwrap_or(""),
        18,
    )
    .unwrap_or_else(|| reaction_label(&reaction_type).to_string());
    let client_id = clean_client_id(
        payload
            .get("clientId")
            .and_then(Value::as_str)
            .unwrap_or(""),
    )
    .unwrap_or_else(random_session_id);
    let now = Utc::now().to_rfc3339();
    let reaction = json!({
        "id": random_session_id(),
        "type": reaction_type,
        "label": label,
        "clientId": client_id,
        "createdAtUtc": &now
    });

    let items = reactions
        .get_mut("items")
        .and_then(Value::as_array_mut)
        .ok_or_else(|| AppError::bad_request("Reaction payload was invalid."))?;
    items.push(reaction);
    if items.len() > MAX_REACTIONS {
        let drain_count = items.len() - MAX_REACTIONS;
        items.drain(0..drain_count);
    }

    reactions["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(reactions_path, &reactions).await?;
    Ok(Json(reactions))
}

async fn get_channel(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(channel_id): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let channel_id = clean_channel_id(&channel_id)?;
    Ok(Json(read_channel_payload(&state, &headers, &channel_id).await?))
}

async fn get_channel_stats(
    State(state): State<AppState>,
    AxumPath(channel_id): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let channel_id = clean_channel_id(&channel_id)?;
    let channel_path = channel_path(&state, &channel_id)?;
    let channel = read_json(channel_path).await?;
    Ok(Json(channel.get("stats").cloned().unwrap_or_else(|| empty_channel_stats())))
}

async fn put_channel(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(channel_id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let channel_id = clean_channel_id(&channel_id)?;
    let owner_token = clean_owner_token(
        payload
            .get("ownerToken")
            .and_then(Value::as_str)
            .unwrap_or(""),
    )?;
    let path = channel_path(&state, &channel_id)?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).await?;
    }

    let mut channel = read_json(path.clone()).await.unwrap_or_else(|_| {
        json!({
            "protocolVersion": PROTOCOL_VERSION,
            "channelId": &channel_id,
            "ownerToken": &owner_token,
            "createdAtUtc": Utc::now().to_rfc3339(),
            "stats": empty_channel_stats()
        })
    });

    let existing_token = channel.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !existing_token.is_empty() && existing_token != owner_token {
        return Err(AppError::forbidden("Live channel owner token was invalid."));
    }

    let was_live = channel.get("isLive").and_then(Value::as_bool).unwrap_or(false);
    let was_playing = channel
        .get("playback")
        .and_then(|playback| playback.get("isPlaying"))
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let previous_updated = channel
        .get("updatedAtUtc")
        .and_then(Value::as_str)
        .and_then(parse_utc);
    let now = Utc::now();
    let is_live = payload.get("isLive").and_then(Value::as_bool).unwrap_or(false);
    let session_id = payload.get("sessionId").and_then(Value::as_str).map(str::trim).filter(|value| !value.is_empty());
    let listener_count = if is_live {
        match session_id {
            Some(value) => read_session_listener_count(&state, value).await.unwrap_or(0),
            None => 0,
        }
    } else {
        0
    };

    if was_live && was_playing {
        if let Some(previous_updated) = previous_updated {
            let delta_seconds = (now - previous_updated).num_seconds().clamp(0, 10);
            if delta_seconds > 0 {
                let previous_track = channel.get("track").cloned();
                let previous_track_id = channel
                    .get("trackId")
                    .and_then(Value::as_str)
                    .map(ToOwned::to_owned);
                let previous_reactions_seen = channel
                    .get("reactionsSeen")
                    .and_then(Value::as_u64)
                    .unwrap_or(0);
                update_channel_stats(
                    &mut channel,
                    delta_seconds,
                    listener_count,
                    previous_track,
                    previous_track_id,
                    previous_reactions_seen,
                );
            }
        }
    }

    let display_name = clean_short_text(
        payload
            .get("displayName")
            .and_then(Value::as_str)
            .unwrap_or(""),
        32,
    )
    .unwrap_or_else(|| "Spectralis Listener".to_string());
    let join_url = payload.get("joinUrl").and_then(Value::as_str).map(str::trim).filter(|value| !value.is_empty()).unwrap_or("");

    channel["ownerToken"] = json!(owner_token);
    channel["displayName"] = json!(display_name);
    channel["isLive"] = json!(is_live);
    channel["sessionId"] = payload.get("sessionId").cloned().unwrap_or(Value::Null);
    channel["joinUrl"] = json!(join_url);
    channel["trackId"] = payload.get("trackId").cloned().unwrap_or(Value::Null);
    channel["track"] = payload.get("track").cloned().unwrap_or(Value::Null);
    channel["playback"] = payload.get("playback").cloned().unwrap_or(Value::Null);
    channel["listenerCount"] = json!(listener_count);
    channel["updatedAtUtc"] = json!(now.to_rfc3339());
    channel["channelUrl"] = json!(format!(
        "{}/spectralis/web-share/index.html?channel={}",
        base_url(&state, &headers),
        &channel_id
    ));

    write_json(path, &channel).await?;
    Ok(Json(public_channel_payload(channel)))
}

async fn get_package(
    State(state): State<AppState>,
    AxumPath(session_id): AxumPath<String>,
) -> Result<Response, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let package_path = existing_session_root(&state, &session_id)
        .await?
        .join("spectralis-rich.zip");
    send_file(
        package_path,
        Some("application/vnd.spectralis.shared-play+zip"),
    )
    .await
}

async fn get_track_package(
    State(state): State<AppState>,
    AxumPath((session_id, track_key)): AxumPath<(String, String)>,
) -> Result<Response, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let track_key = clean_asset_key(&track_key)?;
    let package_path = existing_session_root(&state, &session_id)
        .await?
        .join("tracks")
        .join(track_key)
        .join("spectralis-rich.zip");
    send_file(
        package_path,
        Some("application/vnd.spectralis.shared-play+zip"),
    )
    .await
}

async fn read_session_payload(
    state: &AppState,
    headers: &HeaderMap,
    session_id: &str,
) -> Result<Value, AppError> {
    let session_id = clean_session_id(session_id)?;
    let session_root = existing_session_root(state, &session_id).await?;
    let manifest = read_json(session_root.join("manifest.json")).await?;
    let playback_state = read_json(session_root.join("state.json")).await.ok();
    let base_url = base_url(state, headers);
    let package_url = format!("{base_url}/shared-play/v1/packages/{session_id}/spectralis-rich.zip");
    let state_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/state");
    let queue_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/queue");
    let presence_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/presence");
    let reactions_url = format!("{base_url}/shared-play/v1/sessions/{session_id}/reactions");
    let join_url = format!("{base_url}/spectralis/web-share/index.html?session={session_id}");
    let active_track_id = active_track_id(&manifest);
    let active_track = active_track_entry(&manifest, &active_track_id);
    let track = active_track
        .as_ref()
        .and_then(|entry| entry.get("track").cloned())
        .or_else(|| manifest.get("track").cloned())
        .unwrap_or_else(|| json!({}));
    let package = active_track
        .as_ref()
        .and_then(|entry| entry.get("package").cloned())
        .or_else(|| manifest.get("package").cloned())
        .unwrap_or_else(|| json!({}));
    let active_package_url = active_track_id
        .as_deref()
        .map(|track_id| {
            format!(
                "{base_url}/shared-play/v1/packages/{session_id}/tracks/{}/spectralis-rich.zip",
                track_asset_key(track_id)
            )
        })
        .unwrap_or_else(|| package_url.clone());

    let mut session = json!({
        "protocolVersion": PROTOCOL_VERSION,
        "sessionId": &session_id,
        "joinUrl": &join_url,
        "stateUrl": &state_url,
        "queueUrl": &queue_url,
        "presenceUrl": &presence_url,
        "reactionsUrl": &reactions_url,
        "activeTrackId": &active_track_id,
        "currentTrackId": &active_track_id,
        "trackId": &active_track_id,
        "packageUrl": &active_package_url,
        "spectralisPackageUrl": &active_package_url,
        "assetUrl": &active_package_url,
        "legacyPackageUrl": &package_url,
        "expiresAtUtc": manifest.get("expiresAtUtc").cloned().unwrap_or(Value::Null),
        "track": track,
        "package": merge_object(package, json!({
            "assetUrl": &active_package_url,
            "url": &active_package_url,
            "legacyAssetUrl": &package_url
        })),
        "assets": {
            "spectralisPackage": { "url": &active_package_url },
            "package": { "url": &active_package_url },
            "legacyPackage": { "url": &package_url }
        },
        "links": {
            "state": &state_url,
            "queue": &queue_url,
            "presence": &presence_url,
            "reactions": &reactions_url
        },
        "uploads": [{
            "name": "spectralis-package",
            "assetUrl": &package_url
        }]
    });

    if let Some(playback_state) = playback_state {
        let playback = playback_state
            .get("playback")
            .or_else(|| playback_state.get("state"))
            .cloned()
            .unwrap_or(playback_state);
        session["playback"] = playback;
    }

    Ok(json!({
        "protocolVersion": PROTOCOL_VERSION,
        "session": session,
        "sessionId": &session_id,
        "joinUrl": &join_url,
        "stateUrl": &state_url,
        "queueUrl": &queue_url,
        "presenceUrl": &presence_url,
        "reactionsUrl": &reactions_url,
        "packageUrl": &active_package_url,
        "legacyPackageUrl": &package_url,
        "expiresAtUtc": manifest.get("expiresAtUtc").cloned().unwrap_or(Value::Null)
    }))
}

async fn activate_track(
    State(state): State<AppState>,
    AxumPath((session_id, track_key)): AxumPath<(String, String)>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let session_id = clean_session_id(&session_id)?;
    let track_key = clean_asset_key(&track_key)?;
    let body = activate_track_package(&state, &session_id, &track_key, Some(payload)).await?;
    Ok(Json(body))
}

async fn read_state_payload(
    state: &AppState,
    headers: &HeaderMap,
    session_id: &str,
) -> Result<Value, AppError> {
    let session_id = clean_session_id(session_id)?;
    let session_root = existing_session_root(state, &session_id).await?;
    let mut payload = read_json(session_root.join("state.json")).await?;
    let manifest = read_json(session_root.join("manifest.json")).await?;
    let base_url = base_url(state, headers);
    enrich_with_active_track(&mut payload, &manifest, &base_url, &session_id);
    Ok(payload)
}

fn empty_queue(session_id: &str) -> Value {
    json!({
        "protocolVersion": PROTOCOL_VERSION,
        "sessionId": session_id,
        "version": 1,
        "currentIndex": -1,
        "updatedAtUtc": Utc::now().to_rfc3339(),
        "items": []
    })
}

fn empty_presence(session_id: &str) -> Value {
    json!({
        "protocolVersion": PROTOCOL_VERSION,
        "sessionId": session_id,
        "listenerCount": 0,
        "updatedAtUtc": Utc::now().to_rfc3339(),
        "participants": []
    })
}

fn prune_presence(presence: &mut Value) {
    if !presence.get("participants").is_some_and(Value::is_array) {
        presence["participants"] = json!([]);
    }

    if let Some(participants) = presence.get_mut("participants").and_then(Value::as_array_mut) {
        participants.retain(|participant| {
            participant
                .get("lastSeenUtc")
                .and_then(Value::as_str)
                .is_some_and(|value| is_recent_utc(value, PRESENCE_TTL_SECONDS))
        });
    }

    update_presence_counts(presence);
}

fn update_presence_counts(presence: &mut Value) {
    let listener_count = presence
        .get("participants")
        .and_then(Value::as_array)
        .map_or(0, Vec::len);
    presence["listenerCount"] = json!(listener_count);
    presence["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
}

fn empty_reactions(session_id: &str) -> Value {
    json!({
        "protocolVersion": PROTOCOL_VERSION,
        "sessionId": session_id,
        "updatedAtUtc": Utc::now().to_rfc3339(),
        "items": []
    })
}

fn prune_reactions(reactions: &mut Value) {
    if !reactions.get("items").is_some_and(Value::is_array) {
        reactions["items"] = json!([]);
    }

    if let Some(items) = reactions.get_mut("items").and_then(Value::as_array_mut) {
        items.retain(|reaction| {
            reaction
                .get("createdAtUtc")
                .and_then(Value::as_str)
                .is_some_and(|value| is_recent_utc(value, REACTION_TTL_SECONDS))
        });

        if items.len() > MAX_REACTIONS {
            let drain_count = items.len() - MAX_REACTIONS;
            items.drain(0..drain_count);
        }
    }

    reactions["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
}

fn is_recent_utc(value: &str, ttl_seconds: i64) -> bool {
    DateTime::parse_from_rfc3339(value)
        .map(|timestamp| Utc::now() - timestamp.with_timezone(&Utc) <= Duration::seconds(ttl_seconds))
        .unwrap_or(false)
}

fn clean_client_id(value: &str) -> Option<String> {
    let cleaned: String = value
        .trim()
        .chars()
        .filter(|ch| ch.is_ascii_alphanumeric() || matches!(ch, '.' | '_' | ':' | '-'))
        .take(80)
        .collect();
    if cleaned.is_empty() {
        None
    } else {
        Some(cleaned)
    }
}

fn clean_short_text(value: &str, max_chars: usize) -> Option<String> {
    let cleaned: String = value
        .trim()
        .chars()
        .filter(|ch| !ch.is_control())
        .take(max_chars)
        .collect::<String>()
        .trim()
        .to_string();
    if cleaned.is_empty() {
        None
    } else {
        Some(cleaned)
    }
}

fn normalize_reaction_type(value: &str) -> String {
    let normalized = value.trim().to_ascii_lowercase();
    match normalized.as_str() {
        "love" | "fire" | "wow" | "boost" | "spark" => normalized,
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

async fn read_channel_payload(
    state: &AppState,
    headers: &HeaderMap,
    channel_id: &str,
) -> Result<Value, AppError> {
    let path = channel_path(state, channel_id)?;
    let mut channel = read_json(path).await?;
    if channel.get("isLive").and_then(Value::as_bool).unwrap_or(false) {
        let updated_at = channel
            .get("updatedAtUtc")
            .and_then(Value::as_str)
            .and_then(parse_utc);
        if updated_at
            .map(|value| Utc::now() - value > Duration::seconds(CHANNEL_HEARTBEAT_TTL_SECONDS))
            .unwrap_or(true)
        {
            channel["isLive"] = json!(false);
            channel["sessionId"] = Value::Null;
            channel["joinUrl"] = json!("");
            channel["listenerCount"] = json!(0);
        }
    }

    channel["channelUrl"] = json!(format!(
        "{}/spectralis/web-share/index.html?channel={}",
        base_url(state, headers),
        channel_id
    ));
    Ok(public_channel_payload(channel))
}

fn public_channel_payload(mut channel: Value) -> Value {
    if let Some(object) = channel.as_object_mut() {
        object.remove("ownerToken");
    }
    channel
}

fn empty_channel_stats() -> Value {
    json!({
        "totalSharedSeconds": 0,
        "totalListenerSeconds": 0,
        "peakConcurrentListeners": 0,
        "trackStats": []
    })
}

fn update_channel_stats(
    channel: &mut Value,
    delta_seconds: i64,
    listener_count: usize,
    track: Option<Value>,
    track_id: Option<String>,
    reactions_seen: u64,
) {
    if !channel.get("stats").is_some_and(Value::is_object) {
        channel["stats"] = empty_channel_stats();
    }

    let stats = channel.get_mut("stats").expect("stats object was just initialized");
    let total_shared = stats
        .get("totalSharedSeconds")
        .and_then(Value::as_i64)
        .unwrap_or(0)
        + delta_seconds;
    let total_listener = stats
        .get("totalListenerSeconds")
        .and_then(Value::as_i64)
        .unwrap_or(0)
        + delta_seconds * listener_count as i64;
    let peak = stats
        .get("peakConcurrentListeners")
        .and_then(Value::as_u64)
        .unwrap_or(0)
        .max(listener_count as u64);

    stats["totalSharedSeconds"] = json!(total_shared);
    stats["totalListenerSeconds"] = json!(total_listener);
    stats["peakConcurrentListeners"] = json!(peak);

    let Some(track_id) = track_id.filter(|value| !value.trim().is_empty()) else {
        return;
    };

    if !stats.get("trackStats").is_some_and(Value::is_array) {
        stats["trackStats"] = json!([]);
    }

    let title = track
        .as_ref()
        .and_then(|value| value.get("displayName").or_else(|| value.get("title")))
        .and_then(Value::as_str)
        .unwrap_or("Shared Play Track")
        .to_string();
    let artist = track
        .as_ref()
        .and_then(|value| value.get("artist"))
        .and_then(Value::as_str)
        .unwrap_or("")
        .to_string();

    if let Some(items) = stats.get_mut("trackStats").and_then(Value::as_array_mut) {
        if let Some(existing) = items.iter_mut().find(|item| {
            item.get("trackId")
                .and_then(Value::as_str)
                .is_some_and(|value| value == track_id)
        }) {
            existing["title"] = json!(title);
            existing["artist"] = json!(artist);
            existing["playSeconds"] = json!(
                existing
                    .get("playSeconds")
                    .and_then(Value::as_i64)
                    .unwrap_or(0)
                    + delta_seconds
            );
            existing["listenerSeconds"] = json!(
                existing
                    .get("listenerSeconds")
                    .and_then(Value::as_i64)
                    .unwrap_or(0)
                    + delta_seconds * listener_count as i64
            );
            existing["reactions"] = json!(
                existing
                    .get("reactions")
                    .and_then(Value::as_u64)
                    .unwrap_or(0)
                    .max(reactions_seen)
            );
        } else {
            items.push(json!({
                "trackId": track_id,
                "title": title,
                "artist": artist,
                "playSeconds": delta_seconds,
                "listenerSeconds": delta_seconds * listener_count as i64,
                "reactions": reactions_seen
            }));
        }

        items.sort_by_key(|item| {
            std::cmp::Reverse(item.get("listenerSeconds").and_then(Value::as_i64).unwrap_or(0))
        });
        if items.len() > 50 {
            items.truncate(50);
        }
    }
}

async fn read_session_listener_count(state: &AppState, session_id: &str) -> Result<usize, AppError> {
    let session_id = clean_session_id(session_id)?;
    let presence_path = existing_session_root(state, &session_id).await?.join("presence.json");
    let mut presence = read_json(presence_path).await.unwrap_or_else(|_| empty_presence(&session_id));
    prune_presence(&mut presence);
    Ok(presence
        .get("listenerCount")
        .and_then(Value::as_u64)
        .unwrap_or(0) as usize)
}

fn parse_utc(value: &str) -> Option<DateTime<Utc>> {
    DateTime::parse_from_rfc3339(value)
        .map(|timestamp| timestamp.with_timezone(&Utc))
        .ok()
}

fn channel_path(state: &AppState, channel_id: &str) -> Result<PathBuf, AppError> {
    Ok(state
        .data_dir
        .join("channels")
        .join(format!("{}.json", clean_channel_id(channel_id)?)))
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
            tracks.values().find(|entry| {
                entry
                    .get("assetKey")
                    .and_then(Value::as_str)
                    .is_some_and(|value| value == asset_key)
            })
        })
        .cloned();

    let Some(mut entry) = entry else {
        return false;
    };

    let Some(track_id) = entry
        .get("trackId")
        .and_then(Value::as_str)
        .map(ToOwned::to_owned)
    else {
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

fn enrich_with_active_track(payload: &mut Value, manifest: &Value, base_url: &str, session_id: &str) {
    let active_track_id = active_track_id(manifest).or_else(|| payload_track_id(payload));
    let active_track = active_track_id
        .as_deref()
        .and_then(|track_id| active_track_entry(manifest, &Some(track_id.to_string())))
        .or_else(|| active_track_entry(manifest, &active_track_id));
    let package_url = active_track_id
        .as_deref()
        .map(|track_id| {
            format!(
                "{base_url}/shared-play/v1/packages/{session_id}/tracks/{}/spectralis-rich.zip",
                track_asset_key(track_id)
            )
        })
        .unwrap_or_else(|| {
            format!("{base_url}/shared-play/v1/packages/{session_id}/spectralis-rich.zip")
        });

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
            json!({
                "assetUrl": &package_url,
                "url": &package_url
            }),
        );
    } else {
        payload["track"] = manifest.get("track").cloned().unwrap_or_else(|| json!({}));
        payload["package"] = merge_object(
            manifest.get("package").cloned().unwrap_or_else(|| json!({})),
            json!({
                "assetUrl": &package_url,
                "url": &package_url
            }),
        );
    }
}

fn active_track_id(manifest: &Value) -> Option<String> {
    first_string(manifest, &["activeTrackId", "currentTrackId", "trackId"])
}

fn active_track_entry(manifest: &Value, active_track_id: &Option<String>) -> Option<Value> {
    let track_id = active_track_id.as_deref()?;
    manifest
        .get("tracks")
        .and_then(|tracks| tracks.get(track_id))
        .cloned()
}

fn payload_track_id(payload: &Value) -> Option<String> {
    first_string(payload, &["activeTrackId", "currentTrackId", "trackId"])
}

fn first_string(value: &Value, names: &[&str]) -> Option<String> {
    names.iter().find_map(|name| {
        value
            .get(name)
            .and_then(Value::as_str)
            .map(str::trim)
            .filter(|text| !text.is_empty())
            .map(ToOwned::to_owned)
    })
}

fn track_id_to_string(value: &Value) -> String {
    value
        .as_str()
        .map(str::trim)
        .filter(|text| !text.is_empty())
        .map(ToOwned::to_owned)
        .unwrap_or_else(random_session_id)
}

fn track_asset_key(track_id: &str) -> String {
    let cleaned: String = track_id
        .trim()
        .chars()
        .map(|ch| {
            if ch.is_ascii_alphanumeric() || ch == '-' || ch == '_' {
                ch
            } else {
                '-'
            }
        })
        .collect();

    let trimmed = cleaned.trim_matches('-');
    if trimmed.is_empty() {
        random_session_id()
    } else {
        trimmed.chars().take(96).collect()
    }
}

fn normalize_queue_payload(session_id: &str, payload: Value) -> Value {
    let source = if let Some(queue) = payload.get("queue") {
        queue.clone()
    } else {
        payload
    };
    let items = source
        .get("items")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();

    json!({
        "protocolVersion": PROTOCOL_VERSION,
        "sessionId": session_id,
        "version": source.get("version").cloned().unwrap_or(json!(1)),
        "currentIndex": source.get("currentIndex").cloned().unwrap_or(json!(-1)),
        "updatedAtUtc": Utc::now().to_rfc3339(),
        "items": items
    })
}

fn normalize_queue_item(payload: Value) -> Result<Value, AppError> {
    let item = if let Some(item) = payload.get("item") {
        item.clone()
    } else {
        payload
    };
    let url = item
        .get("url")
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .ok_or_else(|| AppError::bad_request("Queue item URL was empty."))?;

    if !(url.starts_with("https://") || url.starts_with("http://") || url.starts_with("spotify:")) {
        return Err(AppError::bad_request("Queue item URL must be http(s) or spotify:."));
    }

    let title = item
        .get("title")
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .unwrap_or(url);
    let source_kind = item
        .get("sourceKind")
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .unwrap_or("url");
    let id = item
        .get("id")
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .map(|value| value.to_string())
        .unwrap_or_else(random_session_id);

    let added_at_utc = item
        .get("addedAtUtc")
        .and_then(Value::as_str)
        .map(|value| value.to_string())
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

async fn write_playback_state(
    state: &AppState,
    session_id: &str,
    payload: Value,
) -> Result<Value, AppError> {
    let session_id = clean_session_id(session_id)?;
    let session_root = existing_session_root(state, &session_id).await?;
    let top_track_id = payload_track_id(&payload);
    let chosen = payload
        .get("playback")
        .or_else(|| payload.get("state"))
        .cloned()
        .unwrap_or_else(|| payload.clone());
    let manifest = read_json(session_root.join("manifest.json")).await.ok();
    let track_id = payload_track_id(&chosen)
        .or(top_track_id)
        .or_else(|| manifest.as_ref().and_then(active_track_id));
    let body = json!({
        "protocolVersion": PROTOCOL_VERSION,
        "sessionId": &session_id,
        "trackId": &track_id,
        "activeTrackId": &track_id,
        "currentTrackId": &track_id,
        "updatedAtUtc": Utc::now().to_rfc3339(),
        "state": chosen.clone(),
        "playback": chosen
    });
    write_json(session_root.join("state.json"), &body).await?;
    Ok(body)
}

async fn send_file(path: PathBuf, content_type: Option<&str>) -> Result<Response, AppError> {
    let metadata = fs::metadata(&path).await.map_err(|_| AppError::not_found("File not found."))?;
    if !metadata.is_file() {
        return Err(AppError::not_found("File not found."));
    }

    let bytes = fs::read(&path).await?;
    let mime = content_type
        .map(|value| value.to_string())
        .or_else(|| mime_guess::from_path(&path).first().map(|mime| mime.to_string()))
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
        response
            .headers_mut()
            .insert(header::PRAGMA, HeaderValue::from_static("no-cache"));
        response
            .headers_mut()
            .insert(header::EXPIRES, HeaderValue::from_static("0"));
    }
    response.headers_mut().insert(
        "cross-origin-resource-policy",
        HeaderValue::from_static("cross-origin"),
    );
    Ok(response)
}

fn cache_control_for_path(path: &Path) -> &'static str {
    match path.extension().and_then(|value| value.to_str()) {
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
        path.extension()
            .and_then(|value| value.to_str())
            .unwrap_or("json")
    ));
    let bytes = serde_json::to_vec_pretty(value)?;
    fs::write(&temp_path, bytes).await?;
    fs::rename(temp_path, path).await?;
    Ok(())
}

async fn existing_session_root(state: &AppState, session_id: &str) -> Result<PathBuf, AppError> {
    let root = session_root(state, session_id)?;
    if fs::metadata(&root).await.is_err() {
        return Err(AppError::not_found("Shared Play session was not found."));
    }
    if is_session_expired(&root).await {
        let _ = fs::remove_dir_all(&root).await;
        return Err(AppError::not_found("Shared Play session has expired."));
    }
    Ok(root)
}

fn session_root(state: &AppState, session_id: &str) -> Result<PathBuf, AppError> {
    Ok(state
        .data_dir
        .join("sessions")
        .join(clean_session_id(session_id)?))
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
    let Ok(metadata) = fs::metadata(session_root).await else {
        return false;
    };
    if !metadata.is_dir() {
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
        .map(|value| value.with_timezone(&Utc) <= Utc::now())
        .unwrap_or(false)
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
        .and_then(|value| value.to_str().ok())
        .map(|value| value.to_string())
        .filter(|value| !value.trim().is_empty())
}

fn clean_session_id(value: &str) -> Result<String, AppError> {
    let cleaned: String = value
        .trim()
        .chars()
        .filter(|ch| ch.is_ascii_alphanumeric() || matches!(ch, '.' | '_' | ':' | '-'))
        .collect();
    if cleaned.is_empty() {
        Err(AppError::bad_request("Session ID was empty."))
    } else {
        Ok(cleaned)
    }
}

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

fn clean_relative_path(path: &str) -> Result<PathBuf, AppError> {
    let candidate = Path::new(path);
    if candidate
        .components()
        .any(|component| matches!(component, std::path::Component::ParentDir))
    {
        return Err(AppError::forbidden("Forbidden."));
    }
    Ok(candidate.to_path_buf())
}

fn random_session_id() -> String {
    rand::thread_rng()
        .sample_iter(&Alphanumeric)
        .take(24)
        .map(char::from)
        .collect()
}

fn merge_object(mut base: Value, additions: Value) -> Value {
    if let (Some(base), Some(additions)) = (base.as_object_mut(), additions.as_object()) {
        for (key, value) in additions {
            base.insert(key.clone(), value.clone());
        }
    }
    base
}

#[derive(Debug)]
struct AppError {
    status: StatusCode,
    message: String,
}

impl AppError {
    fn bad_request(message: &str) -> Self {
        Self {
            status: StatusCode::BAD_REQUEST,
            message: message.to_string(),
        }
    }

    fn forbidden(message: &str) -> Self {
        Self {
            status: StatusCode::FORBIDDEN,
            message: message.to_string(),
        }
    }

    fn not_found(message: &str) -> Self {
        Self {
            status: StatusCode::NOT_FOUND,
            message: message.to_string(),
        }
    }

    fn payload_too_large(message: &str) -> Self {
        Self {
            status: StatusCode::PAYLOAD_TOO_LARGE,
            message: message.to_string(),
        }
    }
}

impl IntoResponse for AppError {
    fn into_response(self) -> Response {
        let body = Json(json!({
            "error": self.message,
            "status": self.status.as_u16()
        }));
        (self.status, body).into_response()
    }
}

impl From<anyhow::Error> for AppError {
    fn from(error: anyhow::Error) -> Self {
        Self {
            status: StatusCode::INTERNAL_SERVER_ERROR,
            message: error.to_string(),
        }
    }
}

impl From<std::io::Error> for AppError {
    fn from(error: std::io::Error) -> Self {
        Self {
            status: StatusCode::INTERNAL_SERVER_ERROR,
            message: error.to_string(),
        }
    }
}

impl From<serde_json::Error> for AppError {
    fn from(error: serde_json::Error) -> Self {
        Self {
            status: StatusCode::BAD_REQUEST,
            message: error.to_string(),
        }
    }
}
