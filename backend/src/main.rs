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
    extract::{DefaultBodyLimit, Multipart, Path as AxumPath, Query, State},
    http::{header, HeaderMap, HeaderValue, StatusCode},
    response::{IntoResponse, Response},
    routing::{delete, get, patch, post, put},
    Json, Router,
};
use chrono::{DateTime, Duration, Utc};
use hmac::{Hmac, Mac};
use rand::Rng;
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use tokio::{fs, net::TcpListener};
use tower_http::cors::{Any, CorsLayer};

type HmacSha256 = Hmac<Sha256>;

const PROTOCOL_VERSION: &str = "shared-play-v2";
const SESSION_TTL_HOURS: i64 = 12;
const MAX_PACKAGE_BYTES: usize = 512 * 1024 * 1024;
const NO_STORE_CACHE_CONTROL: &str = "no-store, no-cache, must-revalidate, max-age=0";
const SHORT_STATIC_CACHE_CONTROL: &str = "public, max-age=60";
const PRESENCE_TTL_SECONDS: i64 = 45;
const REACTION_TTL_SECONDS: i64 = 90;
const MAX_REACTIONS: usize = 40;
const CHANNEL_HEARTBEAT_TTL_SECONDS: i64 = 30;
const STRIPE_API_BASE: &str = "https://api.stripe.com/v1";
const STRIPE_CONNECT_AUTHORIZE: &str = "https://connect.stripe.com/oauth/authorize";
const STRIPE_CONNECT_TOKEN: &str = "https://connect.stripe.com/oauth/token";

#[derive(Clone)]
struct AppState {
    data_dir: Arc<PathBuf>,
    web_share_root: Arc<PathBuf>,
    public_base_url: Option<String>,
    stripe_secret_key: Option<Arc<String>>,
    stripe_webhook_secret: Option<Arc<String>>,
    stripe_connect_client_id: Option<Arc<String>>,
    stripe_publishable_key: Option<Arc<String>>,
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
    let stripe_secret_key = env::var("STRIPE_SECRET_KEY").ok().map(Arc::new);
    let stripe_webhook_secret = env::var("STRIPE_WEBHOOK_SECRET").ok().map(Arc::new);
    let stripe_connect_client_id = env::var("STRIPE_CONNECT_CLIENT_ID").ok().map(Arc::new);
    let stripe_publishable_key = env::var("STRIPE_PUBLISHABLE_KEY").ok().map(Arc::new);

    fs::create_dir_all(data_dir.join("sessions")).await?;
    fs::create_dir_all(data_dir.join("sq-rooms")).await?;
    fs::create_dir_all(data_dir.join("sq-uploads")).await?;

    let state = AppState {
        data_dir: Arc::new(data_dir),
        web_share_root: Arc::new(web_share_root),
        public_base_url,
        stripe_secret_key,
        stripe_webhook_secret,
        stripe_connect_client_id,
        stripe_publishable_key,
    };

    let app = Router::new()
        .route("/", get(index))
        .route("/health", get(health))
        .route("/player.js", get(player_js))
        .route("/styles.css", get(styles_css))
        .route("/spectralis/web-share", get(index))
        .route("/spectralis/web-share/index.html", get(index))
        .route("/spectralis/web-share/*path", get(web_share_static))
        .route("/shared-play/v2/sessions", post(create_session))
        .route("/shared-play/v2/sessions/:code/package", put(upload_package))
        .route("/shared-play/v2/sessions/:code/tracks", post(register_track))
        .route(
            "/shared-play/v2/sessions/:code/tracks/:key/package",
            put(upload_track_package),
        )
        .route(
            "/shared-play/v2/sessions/:code/tracks/:key/activate",
            post(activate_track),
        )
        .route("/shared-play/v2/sessions/:code", get(get_session))
        .route("/shared-play/v2/sessions/:code/manifest", get(get_session))
        .route(
            "/shared-play/v2/sessions/:code/state",
            get(get_state).post(post_state),
        )
        .route(
            "/shared-play/v2/sessions/:code/queue",
            get(get_queue).post(post_queue),
        )
        .route("/shared-play/v2/sessions/:code/queue/items", post(post_queue_item))
        .route(
            "/shared-play/v2/sessions/:code/presence",
            get(get_presence).post(post_presence),
        )
        .route(
            "/shared-play/v2/sessions/:code/reactions",
            get(get_reactions).post(post_reaction),
        )
        .route(
            "/shared-play/v2/channels/:channel",
            get(get_channel).put(put_channel),
        )
        .route("/shared-play/v2/channels/:channel/stats", get(get_channel_stats))
        .route("/shared-play/v2/channels/:channel/stripe/connect", get(get_stripe_connect_url))
        .route("/shared-play/v2/channels/:channel/stripe/callback", get(get_stripe_oauth_callback))
        .route("/shared-play/v2/channels/:channel/stripe/disconnect", post(post_stripe_disconnect))
        .route(
            "/shared-play/v2/sessions/:code/streamer-queue",
            get(get_streamer_queue),
        )
        .route(
            "/shared-play/v2/sessions/:code/streamer-queue/settings",
            put(put_streamer_queue_settings),
        )
        .route(
            "/shared-play/v2/sessions/:code/streamer-queue/submit",
            post(post_streamer_queue_submit),
        )
        .route(
            "/shared-play/v2/sessions/:code/streamer-queue/skip-request",
            post(post_streamer_queue_skip_request),
        )
        .route(
            "/shared-play/v2/sessions/:code/streamer-queue/super-skip",
            post(post_streamer_queue_super_skip),
        )
        .route(
            "/shared-play/v2/sessions/:code/streamer-queue/items/:item_id/approve",
            post(post_streamer_queue_approve),
        )
        .route(
            "/shared-play/v2/sessions/:code/streamer-queue/items/:item_id/reject",
            post(post_streamer_queue_reject),
        )
        // ── Streamer Queue v1 (standalone, no shared-play session required) ──────
        .route("/streamer-queue/v1/rooms", post(post_sq_create_room))
        .route("/streamer-queue/v1/rooms/:id", get(get_sq_room))
        .route("/streamer-queue/v1/rooms/:id/settings", put(put_sq_settings))
        .route("/streamer-queue/v1/rooms/:id/submit", post(post_sq_submit))
        .route("/streamer-queue/v1/rooms/:id/upload", post(post_sq_upload))
        .route("/streamer-queue/v1/rooms/:id/uploads/:file_id", get(get_sq_upload))
        .route("/streamer-queue/v1/rooms/:id/submissions/:sid/promote", post(post_sq_promote))
        .route("/streamer-queue/v1/rooms/:id/submissions/:sid", patch(patch_sq_submission))
        .route("/streamer-queue/v1/rooms/:id/submissions/:sid/approve", post(post_sq_approve_sub))
        .route("/streamer-queue/v1/rooms/:id/submissions/:sid/reject", post(post_sq_reject_sub))
        .route("/streamer-queue/v1/rooms/:id/submissions/:sid", delete(delete_sq_submission))
        .route("/streamer-queue/v1/rooms/:id/order", put(put_sq_order))
        .route("/streamer-queue/v1/rooms/:id/now-playing", post(post_sq_now_playing))
        .route("/streamer-queue/v1/rooms/:id/stripe/connect", get(get_sq_stripe_connect))
        .route("/streamer-queue/v1/rooms/:id/stripe/disconnect", post(post_sq_stripe_disconnect))
        .route("/webhooks/stripe", post(post_stripe_webhook))
        .route("/shared-play/v2/sessions/:code/package", get(get_package))
        .route(
            "/shared-play/v2/sessions/:code/tracks/:key/package",
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

// ── Session handlers ──────────────────────────────────────────────────────────

async fn create_session(
    State(state): State<AppState>,
    headers: HeaderMap,
    Json(payload): Json<Value>,
) -> Result<impl IntoResponse, AppError> {
    cleanup_expired_sessions(&state).await;

    let (room_code, session_key) = generate_room_code();
    let session_root = session_root(&state, &room_code)?;
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
        .unwrap_or_else(|| json!(&room_code));
    let track_id_text = track_id_to_string(&track_id);

    let mut manifest = json!({
        "protocolVersion": PROTOCOL_VERSION,
        "roomCode": &room_code,
        "sessionKey": &session_key,
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
        write_playback_state(&state, &room_code, playback).await?;
    }

    let base = base_url(&state, &headers);
    let join_url = format!("{base}/spectralis/web-share?session={room_code}");
    let state_url = format!("{base}/shared-play/v2/sessions/{room_code}/state");
    let queue_url = format!("{base}/shared-play/v2/sessions/{room_code}/queue");
    let presence_url = format!("{base}/shared-play/v2/sessions/{room_code}/presence");
    let reactions_url = format!("{base}/shared-play/v2/sessions/{room_code}/reactions");
    let upload_url = format!("{base}/shared-play/v2/sessions/{room_code}/package");
    let package_url = format!("{base}/shared-play/v2/sessions/{room_code}/package");

    Ok((
        StatusCode::CREATED,
        Json(json!({
            "protocolVersion": PROTOCOL_VERSION,
            "roomCode": &room_code,
            "displayCode": display_room_code(&room_code),
            "sessionKey": &session_key,
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
    AxumPath(code): AxumPath<String>,
    body: Bytes,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    if body.is_empty() {
        return Err(AppError::bad_request("Upload body was empty."));
    }
    if body.len() > MAX_PACKAGE_BYTES {
        return Err(AppError::payload_too_large("Upload body is too large."));
    }

    let session_root = existing_session_root(&state, &room_code).await?;
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

    Ok(Json(json!({ "ok": true, "bytes": body.len() })))
}

async fn register_track(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(code): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<impl IntoResponse, AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let manifest_path = session_root.join("manifest.json");
    let mut manifest = read_json(manifest_path.clone()).await?;

    let track = payload.get("track").cloned().unwrap_or_else(|| json!({}));
    let package = payload.get("package").cloned().unwrap_or_else(|| json!({}));
    let track_id = package
        .get("trackId")
        .or_else(|| payload.get("trackId"))
        .cloned()
        .unwrap_or_else(|| {
            let (code, _) = generate_room_code();
            json!(code)
        });
    let track_id_text = track_id_to_string(&track_id);
    let track_key = track_asset_key(&track_id_text);
    let activate_on_upload = payload
        .get("activateOnUpload")
        .and_then(Value::as_bool)
        .unwrap_or(true);

    upsert_pending_track(&mut manifest, &track_id_text, track, package);
    manifest["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(manifest_path, &manifest).await?;

    let base = base_url(&state, &headers);
    let upload_url = if activate_on_upload {
        format!("{base}/shared-play/v2/sessions/{room_code}/tracks/{track_key}/package")
    } else {
        format!("{base}/shared-play/v2/sessions/{room_code}/tracks/{track_key}/package?activate=false")
    };
    let package_url = format!("{base}/shared-play/v2/sessions/{room_code}/tracks/{track_key}/package");
    let state_url = format!("{base}/shared-play/v2/sessions/{room_code}/state");
    let queue_url = format!("{base}/shared-play/v2/sessions/{room_code}/queue");

    Ok((
        StatusCode::CREATED,
        Json(json!({
            "roomCode": &room_code,
            "trackId": &track_id_text,
            "stateUrl": &state_url,
            "queueUrl": &queue_url,
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

async fn upload_track_package(
    State(state): State<AppState>,
    AxumPath((code, key)): AxumPath<(String, String)>,
    Query(query): Query<HashMap<String, String>>,
    body: Bytes,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let track_key = clean_asset_key(&key)?;
    if body.is_empty() {
        return Err(AppError::bad_request("Upload body was empty."));
    }
    if body.len() > MAX_PACKAGE_BYTES {
        return Err(AppError::payload_too_large("Upload body is too large."));
    }

    let session_root = existing_session_root(&state, &room_code).await?;
    let track_root = session_root.join("tracks").join(&track_key);
    fs::create_dir_all(&track_root).await?;

    let temp_path = track_root.join(format!("{}.tmp", uuid::Uuid::new_v4()));
    let package_path = track_root.join("spectralis-rich.zip");
    fs::write(&temp_path, &body).await?;
    fs::rename(&temp_path, &package_path).await?;

    if query
        .get("activate")
        .is_none_or(|v| !v.eq_ignore_ascii_case("false"))
    {
        activate_track_package(&state, &room_code, &track_key, None).await?;
    } else {
        let manifest_path = session_root.join("manifest.json");
        if let Ok(mut manifest) = read_json(manifest_path.clone()).await {
            if let Some(entry) = manifest
                .get_mut("tracks")
                .and_then(Value::as_object_mut)
                .and_then(|tracks| {
                    tracks.values_mut().find(|e| {
                        e.get("assetKey")
                            .and_then(Value::as_str)
                            .is_some_and(|v| v == track_key)
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

    Ok(Json(json!({ "ok": true, "bytes": body.len() })))
}

async fn activate_track(
    State(state): State<AppState>,
    AxumPath((code, key)): AxumPath<(String, String)>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let track_key = clean_asset_key(&key)?;
    let body = activate_track_package(&state, &room_code, &track_key, Some(payload)).await?;
    Ok(Json(body))
}

async fn activate_track_package(
    state: &AppState,
    room_code: &str,
    track_key: &str,
    playback_payload: Option<Value>,
) -> Result<Value, AppError> {
    let session_root = existing_session_root(state, room_code).await?;
    let track_package = session_root.join("tracks").join(track_key).join("spectralis-rich.zip");
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
        Some(write_playback_state(state, room_code, payload).await?)
    } else {
        None
    };

    Ok(json!({
        "ok": true,
        "roomCode": room_code,
        "trackKey": track_key,
        "trackId": active_track_id(&manifest),
        "state": state_body
    }))
}

async fn get_session(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(code): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    Ok(Json(read_session_payload(&state, &headers, &code).await?))
}

async fn read_session_payload(
    state: &AppState,
    headers: &HeaderMap,
    code: &str,
) -> Result<Value, AppError> {
    let room_code = clean_room_code(code)?;
    let session_root = existing_session_root(state, &room_code).await?;
    let manifest = read_json(session_root.join("manifest.json")).await?;
    let playback_state = read_json(session_root.join("state.json")).await.ok();
    let base = base_url(state, headers);

    let active_track_id = active_track_id(&manifest);
    let active_track = active_track_entry(&manifest, &active_track_id);
    let track = active_track
        .as_ref()
        .and_then(|e| e.get("track").cloned())
        .or_else(|| manifest.get("track").cloned())
        .unwrap_or_else(|| json!({}));
    let package = active_track
        .as_ref()
        .and_then(|e| e.get("package").cloned())
        .or_else(|| manifest.get("package").cloned())
        .unwrap_or_else(|| json!({}));

    let package_url = active_track_id
        .as_deref()
        .map(|id| format!("{base}/shared-play/v2/sessions/{room_code}/tracks/{}/package", track_asset_key(id)))
        .unwrap_or_else(|| format!("{base}/shared-play/v2/sessions/{room_code}/package"));
    let legacy_package_url = format!("{base}/shared-play/v2/sessions/{room_code}/package");
    let state_url = format!("{base}/shared-play/v2/sessions/{room_code}/state");
    let queue_url = format!("{base}/shared-play/v2/sessions/{room_code}/queue");
    let presence_url = format!("{base}/shared-play/v2/sessions/{room_code}/presence");
    let reactions_url = format!("{base}/shared-play/v2/sessions/{room_code}/reactions");
    let join_url = format!("{base}/spectralis/web-share?session={room_code}");

    let mut session = json!({
        "protocolVersion": PROTOCOL_VERSION,
        "roomCode": &room_code,
        "displayCode": display_room_code(&room_code),
        "joinUrl": &join_url,
        "stateUrl": &state_url,
        "queueUrl": &queue_url,
        "presenceUrl": &presence_url,
        "reactionsUrl": &reactions_url,
        "activeTrackId": &active_track_id,
        "trackId": &active_track_id,
        "packageUrl": &package_url,
        "spectralisPackageUrl": &package_url,
        "legacyPackageUrl": &legacy_package_url,
        "expiresAtUtc": manifest.get("expiresAtUtc").cloned().unwrap_or(Value::Null),
        "track": track,
        "package": merge_object(package, json!({ "assetUrl": &package_url, "url": &package_url })),
        "links": {
            "state": &state_url,
            "queue": &queue_url,
            "presence": &presence_url,
            "reactions": &reactions_url
        }
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
        "roomCode": &room_code,
        "displayCode": display_room_code(&room_code),
        "joinUrl": &join_url,
        "stateUrl": &state_url,
        "queueUrl": &queue_url,
        "packageUrl": &package_url,
        "expiresAtUtc": manifest.get("expiresAtUtc").cloned().unwrap_or(Value::Null)
    }))
}

async fn get_state(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(code): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    Ok(Json(read_state_payload(&state, &headers, &code).await?))
}

async fn post_state(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let body = write_playback_state(&state, &room_code, payload).await?;
    Ok(Json(body))
}

async fn read_state_payload(
    state: &AppState,
    headers: &HeaderMap,
    code: &str,
) -> Result<Value, AppError> {
    let room_code = clean_room_code(code)?;
    let session_root = existing_session_root(state, &room_code).await?;
    let mut payload = read_json(session_root.join("state.json")).await?;
    let manifest = read_json(session_root.join("manifest.json")).await?;
    let base = base_url(state, headers);
    enrich_with_active_track(&mut payload, &manifest, &base, &room_code);
    Ok(payload)
}

async fn write_playback_state(
    state: &AppState,
    room_code: &str,
    payload: Value,
) -> Result<Value, AppError> {
    let room_code = clean_room_code(room_code)?;
    let session_root = existing_session_root(state, &room_code).await?;
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
        "roomCode": &room_code,
        "trackId": &track_id,
        "activeTrackId": &track_id,
        "updatedAtUtc": Utc::now().to_rfc3339(),
        "state": chosen.clone(),
        "playback": chosen
    });
    write_json(session_root.join("state.json"), &body).await?;
    Ok(body)
}

async fn get_queue(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let queue_path = existing_session_root(&state, &room_code)
        .await?
        .join("queue.json");
    let queue = read_json(queue_path).await.unwrap_or_else(|_| empty_queue(&room_code));
    Ok(Json(queue))
}

async fn post_queue(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let mut queue = normalize_queue_payload(&room_code, payload);
    queue["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(session_root.join("queue.json"), &queue).await?;
    Ok(Json(queue))
}

async fn post_queue_item(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let queue_path = session_root.join("queue.json");
    let mut queue = read_json(queue_path.clone())
        .await
        .unwrap_or_else(|_| empty_queue(&room_code));

    let item = normalize_queue_item(payload)?;
    let duplicate_id = item.get("id").and_then(Value::as_str).map(|v| v.to_string());

    let items = queue
        .get_mut("items")
        .and_then(Value::as_array_mut)
        .ok_or_else(|| AppError::bad_request("Queue payload was invalid."))?;

    if let Some(dup_id) = duplicate_id {
        if !items
            .iter()
            .any(|e| e.get("id").and_then(Value::as_str).is_some_and(|id| id == dup_id))
        {
            items.push(item);
        }
    } else {
        items.push(item);
    }

    queue["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(queue_path, &queue).await?;
    Ok(Json(queue))
}

// ── Presence handlers ─────────────────────────────────────────────────────────

async fn get_presence(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let presence_path = session_root.join("presence.json");
    let mut presence = read_json(presence_path.clone())
        .await
        .unwrap_or_else(|_| empty_presence(&room_code));
    prune_presence(&mut presence);
    write_json(presence_path, &presence).await?;
    Ok(Json(presence))
}

async fn post_presence(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let presence_path = session_root.join("presence.json");
    let mut presence = read_json(presence_path.clone())
        .await
        .unwrap_or_else(|_| empty_presence(&room_code));
    prune_presence(&mut presence);

    let client_id = clean_client_id(
        payload.get("clientId").and_then(Value::as_str).unwrap_or(""),
    )
    .unwrap_or_else(|| {
        let (code, _) = generate_room_code();
        code
    });
    let display_name = clean_short_text(
        payload.get("displayName").and_then(Value::as_str).unwrap_or(""),
        32,
    )
    .unwrap_or_else(|| "Listener".to_string());
    let now = Utc::now().to_rfc3339();

    let participants = presence
        .get_mut("participants")
        .and_then(Value::as_array_mut)
        .ok_or_else(|| AppError::bad_request("Presence payload was invalid."))?;

    if let Some(existing) = participants.iter_mut().find(|p| {
        p.get("clientId")
            .and_then(Value::as_str)
            .is_some_and(|v| v == client_id)
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

// ── Reaction handlers ─────────────────────────────────────────────────────────

async fn get_reactions(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let reactions_path = session_root.join("reactions.json");
    let mut reactions = read_json(reactions_path.clone())
        .await
        .unwrap_or_else(|_| empty_reactions(&room_code));
    prune_reactions(&mut reactions);
    write_json(reactions_path, &reactions).await?;
    Ok(Json(reactions))
}

async fn post_reaction(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let reactions_path = session_root.join("reactions.json");
    let mut reactions = read_json(reactions_path.clone())
        .await
        .unwrap_or_else(|_| empty_reactions(&room_code));
    prune_reactions(&mut reactions);

    let reaction_type = normalize_reaction_type(
        payload.get("type").and_then(Value::as_str).unwrap_or("spark"),
    );
    let label = clean_short_text(
        payload.get("label").and_then(Value::as_str).unwrap_or(""),
        18,
    )
    .unwrap_or_else(|| reaction_label(&reaction_type).to_string());
    let client_id = clean_client_id(
        payload.get("clientId").and_then(Value::as_str).unwrap_or(""),
    )
    .unwrap_or_else(|| {
        let (code, _) = generate_room_code();
        code
    });
    let now = Utc::now().to_rfc3339();
    let (reaction_id, _) = generate_room_code();
    let reaction = json!({
        "id": reaction_id,
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
        let drain = items.len() - MAX_REACTIONS;
        items.drain(0..drain);
    }

    reactions["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(reactions_path, &reactions).await?;
    Ok(Json(reactions))
}

// ── Channel handlers ──────────────────────────────────────────────────────────

async fn get_channel(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(channel): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let channel_id = clean_channel_id(&channel)?;
    Ok(Json(read_channel_payload(&state, &headers, &channel_id).await?))
}

async fn get_channel_stats(
    State(state): State<AppState>,
    AxumPath(channel): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let channel_id = clean_channel_id(&channel)?;
    let path = channel_path(&state, &channel_id)?;
    let channel = read_json(path).await?;
    Ok(Json(channel.get("stats").cloned().unwrap_or_else(empty_channel_stats)))
}

async fn put_channel(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(channel): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let channel_id = clean_channel_id(&channel)?;
    let owner_token = clean_owner_token(
        payload.get("ownerToken").and_then(Value::as_str).unwrap_or(""),
    )?;
    let path = channel_path(&state, &channel_id)?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).await?;
    }

    let mut channel = read_json(path.clone()).await.unwrap_or_else(|_| json!({
        "protocolVersion": PROTOCOL_VERSION,
        "channelId": &channel_id,
        "ownerToken": &owner_token,
        "createdAtUtc": Utc::now().to_rfc3339(),
        "stats": empty_channel_stats()
    }));

    let existing_token = channel.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !existing_token.is_empty() && existing_token != owner_token {
        return Err(AppError::forbidden("Live channel owner token was invalid."));
    }

    let was_live = channel.get("isLive").and_then(Value::as_bool).unwrap_or(false);
    let was_playing = channel
        .get("playback")
        .and_then(|p| p.get("isPlaying"))
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let previous_updated = channel.get("updatedAtUtc").and_then(Value::as_str).and_then(parse_utc);
    let now = Utc::now();
    let is_live = payload.get("isLive").and_then(Value::as_bool).unwrap_or(false);
    let session_room_code = payload
        .get("roomCode")
        .or_else(|| payload.get("sessionId"))
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|v| !v.is_empty());
    let listener_count = if is_live {
        match session_room_code {
            Some(code) => read_session_listener_count(&state, code).await.unwrap_or(0),
            None => 0,
        }
    } else {
        0
    };

    if was_live && was_playing {
        if let Some(prev) = previous_updated {
            let delta = (now - prev).num_seconds().clamp(0, 10);
            if delta > 0 {
                let prev_track = channel.get("track").cloned();
                let prev_track_id = channel.get("trackId").and_then(Value::as_str).map(ToOwned::to_owned);
                let prev_reactions_seen = channel.get("reactionsSeen").and_then(Value::as_u64).unwrap_or(0);
                update_channel_stats(&mut channel, delta, listener_count, prev_track, prev_track_id, prev_reactions_seen);
            }
        }
    }

    let display_name = clean_short_text(
        payload.get("displayName").and_then(Value::as_str).unwrap_or(""),
        32,
    )
    .unwrap_or_else(|| "Spectralis Listener".to_string());
    let join_url = payload
        .get("joinUrl")
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|v| !v.is_empty())
        .unwrap_or("");

    channel["ownerToken"] = json!(owner_token);
    channel["displayName"] = json!(display_name);
    channel["isLive"] = json!(is_live);
    channel["roomCode"] = payload.get("roomCode").cloned().unwrap_or(Value::Null);
    channel["sessionId"] = payload.get("roomCode").or_else(|| payload.get("sessionId")).cloned().unwrap_or(Value::Null);
    channel["joinUrl"] = json!(join_url);
    channel["trackId"] = payload.get("trackId").cloned().unwrap_or(Value::Null);
    channel["track"] = payload.get("track").cloned().unwrap_or(Value::Null);
    channel["playback"] = payload.get("playback").cloned().unwrap_or(Value::Null);
    channel["listenerCount"] = json!(listener_count);
    channel["updatedAtUtc"] = json!(now.to_rfc3339());
    channel["channelUrl"] = json!(format!(
        "{}/spectralis/web-share?channel={}",
        base_url(&state, &headers),
        &channel_id
    ));

    write_json(path, &channel).await?;
    Ok(Json(public_channel_payload(channel)))
}

async fn read_channel_payload(
    state: &AppState,
    headers: &HeaderMap,
    channel_id: &str,
) -> Result<Value, AppError> {
    let path = channel_path(state, channel_id)?;
    let mut channel = read_json(path).await?;
    if channel.get("isLive").and_then(Value::as_bool).unwrap_or(false) {
        let updated_at = channel.get("updatedAtUtc").and_then(Value::as_str).and_then(parse_utc);
        if updated_at
            .map(|v| Utc::now() - v > Duration::seconds(CHANNEL_HEARTBEAT_TTL_SECONDS))
            .unwrap_or(true)
        {
            channel["isLive"] = json!(false);
            channel["roomCode"] = Value::Null;
            channel["sessionId"] = Value::Null;
            channel["joinUrl"] = json!("");
            channel["listenerCount"] = json!(0);
        }
    }
    channel["channelUrl"] = json!(format!(
        "{}/spectralis/web-share?channel={}",
        base_url(state, headers),
        channel_id
    ));
    Ok(public_channel_payload(channel))
}

fn public_channel_payload(mut channel: Value) -> Value {
    if let Some(obj) = channel.as_object_mut() {
        obj.remove("ownerToken");
    }
    channel
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
    let stats = channel.get_mut("stats").expect("stats initialized above");
    let total_shared = stats.get("totalSharedSeconds").and_then(Value::as_i64).unwrap_or(0) + delta_seconds;
    let total_listener = stats.get("totalListenerSeconds").and_then(Value::as_i64).unwrap_or(0)
        + delta_seconds * listener_count as i64;
    let peak = stats
        .get("peakConcurrentListeners")
        .and_then(Value::as_u64)
        .unwrap_or(0)
        .max(listener_count as u64);

    stats["totalSharedSeconds"] = json!(total_shared);
    stats["totalListenerSeconds"] = json!(total_listener);
    stats["peakConcurrentListeners"] = json!(peak);

    let Some(track_id) = track_id.filter(|v| !v.trim().is_empty()) else { return };
    if !stats.get("trackStats").is_some_and(Value::is_array) {
        stats["trackStats"] = json!([]);
    }

    let title = track
        .as_ref()
        .and_then(|v| v.get("displayName").or_else(|| v.get("title")))
        .and_then(Value::as_str)
        .unwrap_or("Shared Play Track")
        .to_string();
    let artist = track
        .as_ref()
        .and_then(|v| v.get("artist"))
        .and_then(Value::as_str)
        .unwrap_or("")
        .to_string();

    if let Some(items) = stats.get_mut("trackStats").and_then(Value::as_array_mut) {
        if let Some(existing) = items
            .iter_mut()
            .find(|i| i.get("trackId").and_then(Value::as_str).is_some_and(|v| v == track_id))
        {
            existing["title"] = json!(title);
            existing["artist"] = json!(artist);
            existing["playSeconds"] = json!(existing.get("playSeconds").and_then(Value::as_i64).unwrap_or(0) + delta_seconds);
            existing["listenerSeconds"] = json!(existing.get("listenerSeconds").and_then(Value::as_i64).unwrap_or(0) + delta_seconds * listener_count as i64);
            existing["reactions"] = json!(existing.get("reactions").and_then(Value::as_u64).unwrap_or(0).max(reactions_seen));
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
        items.sort_by_key(|i| std::cmp::Reverse(i.get("listenerSeconds").and_then(Value::as_i64).unwrap_or(0)));
        if items.len() > 50 {
            items.truncate(50);
        }
    }
}

// ── Package download handlers ─────────────────────────────────────────────────

async fn get_package(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
) -> Result<Response, AppError> {
    let room_code = clean_room_code(&code)?;
    let package_path = existing_session_root(&state, &room_code)
        .await?
        .join("spectralis-rich.zip");
    send_file(package_path, Some("application/vnd.spectralis.shared-play+zip")).await
}

async fn get_track_package(
    State(state): State<AppState>,
    AxumPath((code, key)): AxumPath<(String, String)>,
) -> Result<Response, AppError> {
    let room_code = clean_room_code(&code)?;
    let track_key = clean_asset_key(&key)?;
    let package_path = existing_session_root(&state, &room_code)
        .await?
        .join("tracks")
        .join(track_key)
        .join("spectralis-rich.zip");
    send_file(package_path, Some("application/vnd.spectralis.shared-play+zip")).await
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
    fn internal(message: impl ToString) -> Self {
        Self { status: StatusCode::INTERNAL_SERVER_ERROR, message: message.to_string() }
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

// ── Streamer Queue handlers ───────────────────────────────────────────────────

async fn get_streamer_queue(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let mut sq = read_streamer_queue(&session_root, &room_code).await;

    let any_fee_enabled = ["queueEntryFee", "skipRequests", "superSkips"].iter().any(|k| {
        sq.get("settings")
            .and_then(|s| s.get(*k))
            .and_then(|f| f.get("enabled"))
            .and_then(Value::as_bool)
            .unwrap_or(false)
    });
    if any_fee_enabled {
        sq["stripePublishableKey"] = json!(state.stripe_publishable_key.as_deref());
    }

    // Inject stripe connection status from the channel record
    let channel_id = sq.get("channelId").and_then(Value::as_str).unwrap_or("").to_string();
    let stripe_connected = if !channel_id.is_empty() {
        if let Ok(ch_path) = channel_path(&state, &channel_id) {
            read_json(ch_path).await.ok()
                .and_then(|ch| ch.get("stripeAccountId").and_then(Value::as_str).map(|s| !s.is_empty()))
                .unwrap_or(false)
        } else {
            false
        }
    } else {
        false
    };
    sq["stripeConnected"] = json!(stripe_connected);

    Ok(Json(public_streamer_queue(sq)))
}

async fn post_stripe_disconnect(
    State(state): State<AppState>,
    AxumPath(channel): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let channel_id = clean_channel_id(&channel)?;
    let owner_token = clean_owner_token(
        payload.get("ownerToken").and_then(Value::as_str).unwrap_or(""),
    )?;

    let path = channel_path(&state, &channel_id)?;
    let mut ch = read_json(path.clone()).await?;

    let existing_token = ch.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !existing_token.is_empty() && existing_token != owner_token {
        return Err(AppError::forbidden("Channel owner token was invalid."));
    }

    if let Some(obj) = ch.as_object_mut() {
        obj.remove("stripeAccountId");
        obj.remove("stripeConnectedAt");
    }
    ch["stripeDisconnectedAt"] = json!(Utc::now().to_rfc3339());
    write_json(path, &ch).await?;

    Ok(Json(json!({ "ok": true, "stripeConnected": false })))
}

async fn put_streamer_queue_settings(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    validate_session_key(
        &state,
        &room_code,
        payload.get("sessionKey").and_then(Value::as_str).unwrap_or(""),
    )
    .await?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let mut sq = read_streamer_queue(&session_root, &room_code).await;

    if let Some(v) = payload.get("enabled").and_then(Value::as_bool) {
        sq["enabled"] = json!(v);
    }
    if let Some(channel_id) = payload.get("channelId").and_then(Value::as_str) {
        sq["channelId"] = json!(clean_channel_id(channel_id)?);
    }
    if let Some(settings) = payload.get("settings") {
        let current = sq["settings"].clone();
        sq["settings"] = normalize_streamer_queue_settings(settings, &current);
    }

    sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(session_root.join("streamer-queue.json"), &sq).await?;
    Ok(Json(public_streamer_queue(sq)))
}

async fn post_streamer_queue_submit(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<(StatusCode, Json<Value>), AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let mut sq = read_streamer_queue(&session_root, &room_code).await;

    if !sq.get("enabled").and_then(Value::as_bool).unwrap_or(false) {
        return Err(AppError::not_found("Streamer queue is not enabled for this session."));
    }

    let url = payload
        .get("url")
        .and_then(Value::as_str)
        .map(str::trim)
        .filter(|v| !v.is_empty())
        .ok_or_else(|| AppError::bad_request("URL is required."))?;
    if !(url.starts_with("https://") || url.starts_with("http://") || url.starts_with("spotify:")) {
        return Err(AppError::bad_request("URL must be http(s) or spotify:."));
    }

    let display_name = clean_short_text(
        payload.get("displayName").and_then(Value::as_str).unwrap_or(""),
        32,
    )
    .unwrap_or_else(|| "Listener".to_string());
    let client_id = clean_client_id(
        payload.get("clientId").and_then(Value::as_str).unwrap_or(""),
    )
    .unwrap_or_else(|| uuid::Uuid::new_v4().to_string());
    let title = clean_short_text(payload.get("title").and_then(Value::as_str).unwrap_or(""), 120);
    let artist = clean_short_text(payload.get("artist").and_then(Value::as_str).unwrap_or(""), 120);

    let settings = sq.get("settings").cloned().unwrap_or_else(|| json!({}));
    let max_queue = settings.get("maxQueueLength").and_then(Value::as_u64).unwrap_or(50) as usize;
    let allow_duplicates = settings.get("allowDuplicates").and_then(Value::as_bool).unwrap_or(false);
    let require_approval = settings.get("requireApproval").and_then(Value::as_bool).unwrap_or(false);

    // Enforce limits
    let active_count = sq
        .get("submissions")
        .and_then(Value::as_array)
        .map(|arr| {
            arr.iter()
                .filter(|s| !matches!(s.get("status").and_then(Value::as_str), Some("rejected") | Some("played")))
                .count()
        })
        .unwrap_or(0);
    if active_count >= max_queue {
        return Err(AppError::bad_request("The queue is full right now. Try again later."));
    }
    if !allow_duplicates {
        let already_in = sq
            .get("submissions")
            .and_then(Value::as_array)
            .is_some_and(|arr| {
                arr.iter().any(|s| {
                    s.get("url").and_then(Value::as_str).is_some_and(|u| u == url)
                        && !matches!(s.get("status").and_then(Value::as_str), Some("rejected") | Some("played"))
                })
            });
        if already_in {
            return Err(AppError::bad_request("This track is already in the queue."));
        }
    }

    let submission_id = uuid::Uuid::new_v4().to_string();
    let now = Utc::now().to_rfc3339();
    let fee_settings = settings.get("queueEntryFee").cloned().unwrap_or_else(|| json!({"enabled": false}));
    let fee_enabled = fee_settings.get("enabled").and_then(Value::as_bool).unwrap_or(false);

    if fee_enabled {
        let amount = fee_settings.get("amount").and_then(Value::as_f64).unwrap_or(0.0);
        let currency = fee_settings.get("currency").and_then(Value::as_str).unwrap_or("USD");
        if amount <= 0.0 {
            return Err(AppError::bad_request("Queue entry fee amount is not configured."));
        }
        let channel_id = sq.get("channelId").and_then(Value::as_str).unwrap_or("");
        if channel_id.is_empty() {
            return Err(AppError::bad_request("Paid queue requires a channel to be configured."));
        }
        let stripe_key = state
            .stripe_secret_key
            .as_deref()
            .ok_or_else(|| AppError::internal("Stripe is not configured on this server."))?;
        let channel = read_json(channel_path(&state, channel_id)?).await?;
        let stripe_account_id = channel
            .get("stripeAccountId")
            .and_then(Value::as_str)
            .ok_or_else(|| AppError::bad_request("Stripe account is not connected for this channel."))?
            .to_string();

        let pi = create_stripe_payment_intent(stripe_key, amount_to_cents(amount), currency, &stripe_account_id).await?;
        let client_secret = pi
            .get("client_secret")
            .and_then(Value::as_str)
            .ok_or_else(|| AppError::internal("Stripe did not return a client secret."))?
            .to_string();
        let pi_id = pi
            .get("id")
            .and_then(Value::as_str)
            .ok_or_else(|| AppError::internal("Stripe did not return a payment intent ID."))?
            .to_string();

        let submission = json!({
            "id": &submission_id,
            "displayName": &display_name,
            "clientId": &client_id,
            "url": url,
            "title": title,
            "artist": artist,
            "status": "awaiting_payment",
            "paymentIntentId": &pi_id,
            "paymentStatus": "pending",
            "submittedAtUtc": &now
        });
        sq["submissions"]
            .as_array_mut()
            .ok_or_else(|| AppError::internal("Submissions array missing."))?
            .push(submission);
        sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
        write_json(session_root.join("streamer-queue.json"), &sq).await?;

        return Ok((
            StatusCode::ACCEPTED,
            Json(json!({
                "submissionId": submission_id,
                "clientSecret": client_secret,
                "status": "awaiting_payment"
            })),
        ));
    }

    // Free submission path
    let initial_status = if require_approval { "pending" } else { "queued" };
    let submission = json!({
        "id": &submission_id,
        "displayName": &display_name,
        "clientId": &client_id,
        "url": url,
        "title": title,
        "artist": artist,
        "status": initial_status,
        "paymentIntentId": null,
        "paymentStatus": "none",
        "submittedAtUtc": &now
    });
    sq["submissions"]
        .as_array_mut()
        .ok_or_else(|| AppError::internal("Submissions array missing."))?
        .push(submission);
    sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(session_root.join("streamer-queue.json"), &sq).await?;

    let mut position = None;
    if !require_approval {
        position = Some(
            append_submission_to_queue(&session_root, &room_code, &submission_id, url, &title, &artist, &now).await?,
        );
    }

    Ok((
        StatusCode::CREATED,
        Json(json!({
            "submissionId": submission_id,
            "status": initial_status,
            "position": position
        })),
    ))
}

async fn post_streamer_queue_skip_request(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<(StatusCode, Json<Value>), AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let mut sq = read_streamer_queue(&session_root, &room_code).await;

    if !sq.get("enabled").and_then(Value::as_bool).unwrap_or(false) {
        return Err(AppError::not_found("Streamer queue is not enabled."));
    }
    let settings = sq.get("settings").cloned().unwrap_or_else(|| json!({}));
    let skip_settings = settings.get("skipRequests").cloned().unwrap_or_else(|| json!({"enabled": false}));
    if !skip_settings.get("enabled").and_then(Value::as_bool).unwrap_or(false) {
        return Err(AppError::bad_request("Skip requests are not enabled for this queue."));
    }

    let display_name = clean_short_text(
        payload.get("displayName").and_then(Value::as_str).unwrap_or(""),
        32,
    )
    .unwrap_or_else(|| "Listener".to_string());
    let client_id = clean_client_id(
        payload.get("clientId").and_then(Value::as_str).unwrap_or(""),
    )
    .unwrap_or_else(|| uuid::Uuid::new_v4().to_string());

    let request_id = uuid::Uuid::new_v4().to_string();
    let now = Utc::now().to_rfc3339();
    let amount = skip_settings.get("amount").and_then(Value::as_f64).unwrap_or(0.0);
    let currency = skip_settings.get("currency").and_then(Value::as_str).unwrap_or("USD");

    if amount > 0.0 {
        let channel_id = sq.get("channelId").and_then(Value::as_str).unwrap_or("");
        if channel_id.is_empty() {
            return Err(AppError::bad_request("Paid skip requests require a channel to be configured."));
        }
        let stripe_key = state
            .stripe_secret_key
            .as_deref()
            .ok_or_else(|| AppError::internal("Stripe is not configured on this server."))?;
        let channel = read_json(channel_path(&state, channel_id)?).await?;
        let stripe_account_id = channel
            .get("stripeAccountId")
            .and_then(Value::as_str)
            .ok_or_else(|| AppError::bad_request("Stripe account is not connected for this channel."))?
            .to_string();

        let pi = create_stripe_payment_intent(stripe_key, amount_to_cents(amount), currency, &stripe_account_id).await?;
        let client_secret = pi
            .get("client_secret")
            .and_then(Value::as_str)
            .ok_or_else(|| AppError::internal("Stripe did not return a client secret."))?
            .to_string();
        let pi_id = pi
            .get("id")
            .and_then(Value::as_str)
            .ok_or_else(|| AppError::internal("Stripe did not return a payment intent ID."))?
            .to_string();

        let request = json!({
            "id": &request_id,
            "displayName": &display_name,
            "clientId": &client_id,
            "paymentIntentId": &pi_id,
            "paymentStatus": "pending",
            "requestedAtUtc": &now
        });
        sq["skipRequests"]
            .as_array_mut()
            .ok_or_else(|| AppError::internal("Skip requests array missing."))?
            .push(request);
        sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
        write_json(session_root.join("streamer-queue.json"), &sq).await?;

        return Ok((
            StatusCode::ACCEPTED,
            Json(json!({ "requestId": request_id, "clientSecret": client_secret, "status": "awaiting_payment" })),
        ));
    }

    let request = json!({
        "id": &request_id,
        "displayName": &display_name,
        "clientId": &client_id,
        "paymentIntentId": null,
        "paymentStatus": "none",
        "requestedAtUtc": &now
    });
    sq["skipRequests"]
        .as_array_mut()
        .ok_or_else(|| AppError::internal("Skip requests array missing."))?
        .push(request);
    sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(session_root.join("streamer-queue.json"), &sq).await?;

    Ok((StatusCode::CREATED, Json(json!({ "requestId": request_id, "status": "submitted" }))))
}

async fn post_streamer_queue_super_skip(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<(StatusCode, Json<Value>), AppError> {
    let room_code = clean_room_code(&code)?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let mut sq = read_streamer_queue(&session_root, &room_code).await;

    if !sq.get("enabled").and_then(Value::as_bool).unwrap_or(false) {
        return Err(AppError::not_found("Streamer queue is not enabled."));
    }
    let settings = sq.get("settings").cloned().unwrap_or_else(|| json!({}));
    let super_settings = settings.get("superSkips").cloned().unwrap_or_else(|| json!({"enabled": false}));
    if !super_settings.get("enabled").and_then(Value::as_bool).unwrap_or(false) {
        return Err(AppError::bad_request("Super skips are not enabled for this queue."));
    }

    let display_name = clean_short_text(
        payload.get("displayName").and_then(Value::as_str).unwrap_or(""),
        32,
    )
    .unwrap_or_else(|| "Listener".to_string());
    let client_id = clean_client_id(
        payload.get("clientId").and_then(Value::as_str).unwrap_or(""),
    )
    .unwrap_or_else(|| uuid::Uuid::new_v4().to_string());

    let request_id = uuid::Uuid::new_v4().to_string();
    let now = Utc::now().to_rfc3339();
    let amount = super_settings.get("amount").and_then(Value::as_f64).unwrap_or(0.0);
    let currency = super_settings.get("currency").and_then(Value::as_str).unwrap_or("USD");

    if amount > 0.0 {
        let channel_id = sq.get("channelId").and_then(Value::as_str).unwrap_or("");
        if channel_id.is_empty() {
            return Err(AppError::bad_request("Paid super skips require a channel to be configured."));
        }
        let stripe_key = state
            .stripe_secret_key
            .as_deref()
            .ok_or_else(|| AppError::internal("Stripe is not configured on this server."))?;
        let channel = read_json(channel_path(&state, channel_id)?).await?;
        let stripe_account_id = channel
            .get("stripeAccountId")
            .and_then(Value::as_str)
            .ok_or_else(|| AppError::bad_request("Stripe account is not connected for this channel."))?
            .to_string();

        let pi = create_stripe_payment_intent(stripe_key, amount_to_cents(amount), currency, &stripe_account_id).await?;
        let client_secret = pi
            .get("client_secret")
            .and_then(Value::as_str)
            .ok_or_else(|| AppError::internal("Stripe did not return a client secret."))?
            .to_string();
        let pi_id = pi
            .get("id")
            .and_then(Value::as_str)
            .ok_or_else(|| AppError::internal("Stripe did not return a payment intent ID."))?
            .to_string();

        let request = json!({
            "id": &request_id,
            "displayName": &display_name,
            "clientId": &client_id,
            "paymentIntentId": &pi_id,
            "paymentStatus": "pending",
            "requestedAtUtc": &now
        });
        sq["superSkipRequests"]
            .as_array_mut()
            .ok_or_else(|| AppError::internal("Super skip requests array missing."))?
            .push(request);
        sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
        write_json(session_root.join("streamer-queue.json"), &sq).await?;

        return Ok((
            StatusCode::ACCEPTED,
            Json(json!({ "requestId": request_id, "clientSecret": client_secret, "status": "awaiting_payment" })),
        ));
    }

    let request = json!({
        "id": &request_id,
        "displayName": &display_name,
        "clientId": &client_id,
        "paymentIntentId": null,
        "paymentStatus": "none",
        "requestedAtUtc": &now
    });
    sq["superSkipRequests"]
        .as_array_mut()
        .ok_or_else(|| AppError::internal("Super skip requests array missing."))?
        .push(request);
    sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(session_root.join("streamer-queue.json"), &sq).await?;

    Ok((StatusCode::CREATED, Json(json!({ "requestId": request_id, "status": "submitted" }))))
}

async fn post_streamer_queue_approve(
    State(state): State<AppState>,
    AxumPath((code, item_id)): AxumPath<(String, String)>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    validate_session_key(
        &state,
        &room_code,
        payload.get("sessionKey").and_then(Value::as_str).unwrap_or(""),
    )
    .await?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let mut sq = read_streamer_queue(&session_root, &room_code).await;

    let idx = sq
        .get("submissions")
        .and_then(Value::as_array)
        .and_then(|arr| arr.iter().position(|s| s.get("id").and_then(Value::as_str).is_some_and(|id| id == item_id)))
        .ok_or_else(|| AppError::not_found("Submission not found."))?;

    let status = sq["submissions"][idx]["status"].as_str().unwrap_or("").to_string();
    if status != "pending" {
        return Err(AppError::bad_request(&format!("Submission is '{status}', not pending.")));
    }
    sq["submissions"][idx]["status"] = json!("queued");

    let url = sq["submissions"][idx]["url"].as_str().unwrap_or("").to_string();
    let title: Option<String> = sq["submissions"][idx]["title"].as_str().map(ToOwned::to_owned);
    let artist: Option<String> = sq["submissions"][idx]["artist"].as_str().map(ToOwned::to_owned);
    let added_at = sq["submissions"][idx]["submittedAtUtc"]
        .as_str()
        .unwrap_or(&Utc::now().to_rfc3339())
        .to_string();

    sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(session_root.join("streamer-queue.json"), &sq).await?;

    if !url.is_empty() {
        append_submission_to_queue(&session_root, &room_code, &item_id, &url, &title, &artist, &added_at).await?;
    }

    Ok(Json(json!({ "submissionId": item_id, "status": "queued" })))
}

async fn post_streamer_queue_reject(
    State(state): State<AppState>,
    AxumPath((code, item_id)): AxumPath<(String, String)>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room_code = clean_room_code(&code)?;
    validate_session_key(
        &state,
        &room_code,
        payload.get("sessionKey").and_then(Value::as_str).unwrap_or(""),
    )
    .await?;
    let session_root = existing_session_root(&state, &room_code).await?;
    let mut sq = read_streamer_queue(&session_root, &room_code).await;

    let idx = sq
        .get("submissions")
        .and_then(Value::as_array)
        .and_then(|arr| arr.iter().position(|s| s.get("id").and_then(Value::as_str).is_some_and(|id| id == item_id)))
        .ok_or_else(|| AppError::not_found("Submission not found."))?;

    sq["submissions"][idx]["status"] = json!("rejected");
    sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(session_root.join("streamer-queue.json"), &sq).await?;

    Ok(Json(json!({ "submissionId": item_id, "status": "rejected" })))
}

// ── Stripe OAuth handlers ─────────────────────────────────────────────────────

async fn get_stripe_connect_url(
    State(state): State<AppState>,
    AxumPath(channel): AxumPath<String>,
    Query(params): Query<HashMap<String, String>>,
) -> Result<Json<Value>, AppError> {
    let channel_id = clean_channel_id(&channel)?;
    let owner_token = clean_owner_token(params.get("ownerToken").map(|s| s.as_str()).unwrap_or(""))?;

    let path = channel_path(&state, &channel_id)?;
    let ch = read_json(path).await?;
    let existing_token = ch.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !existing_token.is_empty() && existing_token != owner_token {
        return Err(AppError::forbidden("Channel owner token was invalid."));
    }

    let client_id = state
        .stripe_connect_client_id
        .as_deref()
        .ok_or_else(|| AppError::internal("Stripe Connect is not configured on this server."))?;

    let connect_url = format!(
        "{}?response_type=code&client_id={}&scope=read_write&state={}",
        STRIPE_CONNECT_AUTHORIZE, client_id, channel_id
    );

    Ok(Json(json!({ "connectUrl": connect_url, "channelId": channel_id })))
}

async fn get_stripe_oauth_callback(
    State(state): State<AppState>,
    AxumPath(channel): AxumPath<String>,
    Query(params): Query<HashMap<String, String>>,
) -> Result<Response, AppError> {
    let channel_id = clean_channel_id(&channel)?;

    let state_param = params.get("state").map(|s| s.as_str()).unwrap_or("");
    if state_param != channel_id {
        return Err(AppError::bad_request("OAuth state parameter mismatch."));
    }
    if let Some(err) = params.get("error") {
        return Err(AppError::bad_request(&format!("Stripe OAuth error: {err}")));
    }

    let code = params.get("code").ok_or_else(|| AppError::bad_request("OAuth code is missing."))?;
    let stripe_key = state
        .stripe_secret_key
        .as_deref()
        .ok_or_else(|| AppError::internal("Stripe is not configured on this server."))?;

    let stripe_account_id = exchange_stripe_code(stripe_key, code).await?;

    let path = channel_path(&state, &channel_id)?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).await?;
    }
    let mut ch = read_json(path.clone()).await.unwrap_or_else(|_| {
        json!({
            "protocolVersion": PROTOCOL_VERSION,
            "channelId": &channel_id,
            "stats": empty_channel_stats()
        })
    });
    ch["stripeAccountId"] = json!(stripe_account_id);
    ch["stripeConnectedAt"] = json!(Utc::now().to_rfc3339());
    write_json(path, &ch).await?;

    let html = r#"<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Stripe Connected</title><style>body{font-family:system-ui,sans-serif;background:#0a0a0a;color:#f0ede8;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}section{text-align:center;padding:32px}h2{font-size:1.5rem;margin-bottom:.5rem}p{color:rgba(240,237,232,.6)}</style></head><body><section><h2>✓ Stripe connected</h2><p>You can close this window and return to Spectralis.</p></section></body></html>"#;
    let mut response = Response::new(Body::from(html));
    *response.status_mut() = StatusCode::OK;
    response.headers_mut().insert(
        header::CONTENT_TYPE,
        HeaderValue::from_static("text/html; charset=utf-8"),
    );
    Ok(response)
}

async fn post_stripe_webhook(
    State(state): State<AppState>,
    headers: HeaderMap,
    body: Bytes,
) -> Result<Json<Value>, AppError> {
    let webhook_secret = state
        .stripe_webhook_secret
        .as_deref()
        .ok_or_else(|| AppError::internal("Stripe webhook secret is not configured."))?;

    let signature = headers
        .get("stripe-signature")
        .and_then(|v| v.to_str().ok())
        .ok_or_else(|| AppError::bad_request("Missing Stripe-Signature header."))?;

    if !verify_stripe_signature(&body, signature, webhook_secret) {
        return Err(AppError::forbidden("Stripe signature verification failed."));
    }

    let event: Value = serde_json::from_slice(&body)?;
    let event_type = event.get("type").and_then(Value::as_str).unwrap_or("");

    match event_type {
        "payment_intent.succeeded" => {
            if let Some(pi_id) = event
                .get("data")
                .and_then(|d| d.get("object"))
                .and_then(|o| o.get("id"))
                .and_then(Value::as_str)
                .map(ToOwned::to_owned)
            {
                handle_payment_intent_succeeded(&state, &pi_id).await?;
            }
        }
        "payment_intent.payment_failed" => {
            if let Some(pi_id) = event
                .get("data")
                .and_then(|d| d.get("object"))
                .and_then(|o| o.get("id"))
                .and_then(Value::as_str)
                .map(ToOwned::to_owned)
            {
                handle_payment_intent_failed(&state, &pi_id).await?;
            }
        }
        _ => {}
    }

    Ok(Json(json!({ "received": true })))
}

// ── Streamer Queue helpers ────────────────────────────────────────────────────

fn empty_streamer_queue(room_code: &str) -> Value {
    json!({
        "protocolVersion": PROTOCOL_VERSION,
        "roomCode": room_code,
        "channelId": null,
        "enabled": false,
        "settings": {
            "requireApproval": false,
            "maxQueueLength": 50,
            "allowDuplicates": false,
            "queueEntryFee": { "enabled": false, "amount": 0.0, "currency": "USD" },
            "skipRequests": { "enabled": false, "amount": 0.0, "currency": "USD", "votesRequired": 3 },
            "superSkips": { "enabled": false, "amount": 0.0, "currency": "USD" }
        },
        "submissions": [],
        "skipRequests": [],
        "superSkipRequests": [],
        "updatedAtUtc": Utc::now().to_rfc3339()
    })
}

async fn read_streamer_queue(session_root: &Path, room_code: &str) -> Value {
    read_json(session_root.join("streamer-queue.json"))
        .await
        .unwrap_or_else(|_| empty_streamer_queue(room_code))
}

fn public_streamer_queue(mut sq: Value) -> Value {
    for key in &["submissions", "skipRequests", "superSkipRequests"] {
        if let Some(arr) = sq.get_mut(*key).and_then(Value::as_array_mut) {
            for item in arr.iter_mut() {
                if let Some(obj) = item.as_object_mut() {
                    obj.remove("paymentIntentId");
                }
            }
        }
    }
    sq
}

async fn validate_session_key(state: &AppState, room_code: &str, provided: &str) -> Result<(), AppError> {
    let cleaned = clean_owner_token(provided)
        .map_err(|_| AppError::forbidden("Session key was invalid."))?;
    let session_root = existing_session_root(state, room_code).await?;
    let manifest = read_json(session_root.join("manifest.json")).await?;
    let stored = manifest.get("sessionKey").and_then(Value::as_str).unwrap_or("");
    if stored.is_empty() || stored != cleaned {
        return Err(AppError::forbidden("Session key was invalid."));
    }
    Ok(())
}

async fn append_submission_to_queue(
    session_root: &Path,
    room_code: &str,
    submission_id: &str,
    url: &str,
    title: &Option<String>,
    artist: &Option<String>,
    added_at: &str,
) -> Result<usize, AppError> {
    let queue_path = session_root.join("queue.json");
    let mut queue = read_json(queue_path.clone()).await.unwrap_or_else(|_| empty_queue(room_code));
    let items = queue
        .get_mut("items")
        .and_then(Value::as_array_mut)
        .ok_or_else(|| AppError::internal("Queue items array missing."))?;

    if !items.iter().any(|i| i.get("id").and_then(Value::as_str).is_some_and(|id| id == submission_id)) {
        items.push(json!({
            "id": submission_id,
            "sourceKind": detect_source_kind(url),
            "title": title.as_deref().unwrap_or(url),
            "artist": artist,
            "url": url,
            "addedBy": "streamer-queue",
            "addedAtUtc": added_at
        }));
    }
    let position = items.len();
    queue["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(queue_path, &queue).await?;
    Ok(position)
}

fn detect_source_kind(url: &str) -> &'static str {
    if url.starts_with("spotify:") || url.contains("spotify.com") {
        "spotify"
    } else if url.contains("youtube.com") || url.contains("youtu.be") {
        "youtube"
    } else if url.contains("soundcloud.com") {
        "soundcloud"
    } else if url.contains("suno.com") || url.contains("suno.ai") {
        "suno"
    } else {
        "url"
    }
}

fn amount_to_cents(amount: f64) -> u64 {
    (amount * 100.0).round() as u64
}

fn normalize_streamer_queue_settings(incoming: &Value, current: &Value) -> Value {
    let mut settings = current.clone();
    if let Some(v) = incoming.get("requireApproval").and_then(Value::as_bool) {
        settings["requireApproval"] = json!(v);
    }
    if let Some(v) = incoming.get("allowDuplicates").and_then(Value::as_bool) {
        settings["allowDuplicates"] = json!(v);
    }
    if let Some(v) = incoming.get("maxQueueLength").and_then(Value::as_u64) {
        settings["maxQueueLength"] = json!(v.clamp(1, 200));
    }
    for fee_key in &["queueEntryFee", "skipRequests", "superSkips"] {
        if let Some(fee) = incoming.get(*fee_key) {
            let current_fee = settings[*fee_key].clone();
            settings[*fee_key] = normalize_fee_settings(fee, &current_fee, *fee_key == "skipRequests");
        }
    }
    settings
}

fn normalize_fee_settings(incoming: &Value, current: &Value, has_votes: bool) -> Value {
    let mut fee = if current.is_object() {
        current.clone()
    } else {
        json!({"enabled": false, "amount": 0.0, "currency": "USD"})
    };
    if let Some(v) = incoming.get("enabled").and_then(Value::as_bool) {
        fee["enabled"] = json!(v);
    }
    if let Some(v) = incoming.get("amount").and_then(Value::as_f64) {
        fee["amount"] = json!((v.max(0.0) * 100.0).round() / 100.0);
    }
    if let Some(v) = incoming.get("currency").and_then(Value::as_str) {
        let c = v.trim().to_uppercase();
        if ["USD", "EUR", "GBP", "CAD", "AUD"].contains(&c.as_str()) {
            fee["currency"] = json!(c);
        }
    }
    if has_votes {
        if let Some(v) = incoming.get("votesRequired").and_then(Value::as_u64) {
            fee["votesRequired"] = json!(v.clamp(1, 100));
        }
    }
    fee
}

// ── Stripe helpers ────────────────────────────────────────────────────────────

async fn create_stripe_payment_intent(
    secret_key: &str,
    amount_cents: u64,
    currency: &str,
    destination_account: &str,
) -> Result<Value, AppError> {
    let params = [
        ("amount", amount_cents.to_string()),
        ("currency", currency.to_lowercase()),
        ("transfer_data[destination]", destination_account.to_string()),
        ("automatic_payment_methods[enabled]", "true".to_string()),
    ];
    let response = reqwest::Client::new()
        .post(format!("{STRIPE_API_BASE}/payment_intents"))
        .basic_auth(secret_key, Some(""))
        .form(&params)
        .send()
        .await
        .map_err(|e| AppError::internal(format!("Stripe request failed: {e}")))?;

    let http_status = response.status();
    let body: Value = response
        .json()
        .await
        .map_err(|e| AppError::internal(format!("Stripe response parse error: {e}")))?;

    if !http_status.is_success() {
        let msg = body
            .get("error")
            .and_then(|e| e.get("message"))
            .and_then(Value::as_str)
            .unwrap_or("Stripe payment intent creation failed.");
        return Err(AppError::internal(msg));
    }
    Ok(body)
}

async fn exchange_stripe_code(secret_key: &str, code: &str) -> Result<String, AppError> {
    let params = [("grant_type", "authorization_code"), ("code", code)];
    let response = reqwest::Client::new()
        .post(STRIPE_CONNECT_TOKEN)
        .basic_auth(secret_key, Some(""))
        .form(&params)
        .send()
        .await
        .map_err(|e| AppError::internal(format!("Stripe OAuth request failed: {e}")))?;

    let body: Value = response
        .json()
        .await
        .map_err(|e| AppError::internal(format!("Stripe OAuth response parse error: {e}")))?;

    if let Some(err) = body.get("error").and_then(Value::as_str) {
        return Err(AppError::internal(format!("Stripe OAuth error: {err}")));
    }
    body.get("stripe_user_id")
        .and_then(Value::as_str)
        .map(ToOwned::to_owned)
        .ok_or_else(|| AppError::internal("Stripe OAuth did not return stripe_user_id."))
}

fn verify_stripe_signature(body: &[u8], signature_header: &str, secret: &str) -> bool {
    let mut timestamp: Option<&str> = None;
    let mut signatures: Vec<&str> = Vec::new();
    for part in signature_header.split(',') {
        if let Some(v) = part.strip_prefix("t=") {
            timestamp = Some(v);
        } else if let Some(v) = part.strip_prefix("v1=") {
            signatures.push(v);
        }
    }
    let Some(t) = timestamp else { return false };
    if signatures.is_empty() {
        return false;
    }
    let signed_payload = format!("{}.{}", t, String::from_utf8_lossy(body));
    let Ok(mut mac) = HmacSha256::new_from_slice(secret.as_bytes()) else { return false };
    mac.update(signed_payload.as_bytes());
    let computed = bytes_to_hex(&mac.finalize().into_bytes());
    signatures.iter().any(|sig| *sig == computed)
}

async fn handle_payment_intent_succeeded(state: &AppState, pi_id: &str) -> Result<(), AppError> {
    let sessions_root = state.data_dir.join("sessions");
    let Ok(mut entries) = fs::read_dir(&sessions_root).await else {
        return Ok(());
    };
    while let Ok(Some(entry)) = entries.next_entry().await {
        let session_root = entry.path();
        if !session_root.is_dir() {
            continue;
        }
        let sq_path = session_root.join("streamer-queue.json");
        let Ok(mut sq) = read_json(sq_path.clone()).await else {
            continue;
        };

        let room_code = sq.get("roomCode").and_then(Value::as_str).unwrap_or("").to_string();
        let require_approval = sq
            .get("settings")
            .and_then(|s| s.get("requireApproval"))
            .and_then(Value::as_bool)
            .unwrap_or(false);

        // Check submissions
        let sub_idx = sq.get("submissions").and_then(Value::as_array).and_then(|arr| {
            arr.iter()
                .position(|s| s.get("paymentIntentId").and_then(Value::as_str).is_some_and(|id| id == pi_id))
        });
        if let Some(idx) = sub_idx {
            sq["submissions"][idx]["paymentStatus"] = json!("succeeded");
            if sq["submissions"][idx]["status"].as_str() == Some("awaiting_payment") {
                let new_status = if require_approval { "pending" } else { "queued" };
                sq["submissions"][idx]["status"] = json!(new_status);
            }

            if !require_approval {
                let url = sq["submissions"][idx]["url"].as_str().unwrap_or("").to_string();
                let title: Option<String> = sq["submissions"][idx]["title"].as_str().map(ToOwned::to_owned);
                let artist: Option<String> = sq["submissions"][idx]["artist"].as_str().map(ToOwned::to_owned);
                let sub_id = sq["submissions"][idx]["id"].as_str().unwrap_or("").to_string();
                let added_at = sq["submissions"][idx]["submittedAtUtc"]
                    .as_str()
                    .unwrap_or(&Utc::now().to_rfc3339())
                    .to_string();
                if !url.is_empty() && !sub_id.is_empty() && !room_code.is_empty() {
                    let _ = append_submission_to_queue(&session_root, &room_code, &sub_id, &url, &title, &artist, &added_at).await;
                }
            }

            sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
            let _ = write_json(sq_path, &sq).await;
            return Ok(());
        }

        // Check skip / super-skip requests
        let mut modified = false;
        for key in &["skipRequests", "superSkipRequests"] {
            if let Some(idx) = sq.get(*key).and_then(Value::as_array).and_then(|arr| {
                arr.iter()
                    .position(|r| r.get("paymentIntentId").and_then(Value::as_str).is_some_and(|id| id == pi_id))
            }) {
                sq[*key][idx]["paymentStatus"] = json!("succeeded");
                modified = true;
                break;
            }
        }
        if modified {
            sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
            let _ = write_json(sq_path, &sq).await;
            return Ok(());
        }
    }
    Ok(())
}

async fn handle_payment_intent_failed(state: &AppState, pi_id: &str) -> Result<(), AppError> {
    let sessions_root = state.data_dir.join("sessions");
    let Ok(mut entries) = fs::read_dir(&sessions_root).await else {
        return Ok(());
    };
    while let Ok(Some(entry)) = entries.next_entry().await {
        let session_root = entry.path();
        if !session_root.is_dir() {
            continue;
        }
        let sq_path = session_root.join("streamer-queue.json");
        let Ok(mut sq) = read_json(sq_path.clone()).await else {
            continue;
        };
        let mut modified = false;
        for key in &["submissions", "skipRequests", "superSkipRequests"] {
            if let Some(idx) = sq.get(*key).and_then(Value::as_array).and_then(|arr| {
                arr.iter()
                    .position(|i| i.get("paymentIntentId").and_then(Value::as_str).is_some_and(|id| id == pi_id))
            }) {
                sq[*key][idx]["paymentStatus"] = json!("failed");
                if sq[*key][idx]["status"].as_str() == Some("awaiting_payment") {
                    sq[*key][idx]["status"] = json!("payment_failed");
                }
                modified = true;
                break;
            }
        }
        if modified {
            sq["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
            let _ = write_json(sq_path, &sq).await;
            return Ok(());
        }
    }
    Ok(()
    )
}

// ═══════════════════════════════════════════════════════════════════════════════
// Streamer Queue v1 — standalone request queue, no shared-play session needed
// ═══════════════════════════════════════════════════════════════════════════════

const SQ_NORMAL_FP_THRESHOLD: f64 = 0.55;
const SQ_STRICT_FP_THRESHOLD: f64 = 0.80;
const SQ_MAX_UPLOAD_BYTES: usize = 50 * 1024 * 1024;

// ── Room storage helpers ──────────────────────────────────────────────────────

fn sq_room_dir(state: &AppState) -> PathBuf {
    state.data_dir.join("sq-rooms")
}

fn sq_upload_dir(state: &AppState) -> PathBuf {
    state.data_dir.join("sq-uploads")
}

fn clean_sq_room_id(value: &str) -> Result<String, AppError> {
    let cleaned: String = value
        .trim()
        .chars()
        .filter(|c| c.is_ascii_alphanumeric() || *c == '-')
        .take(40)
        .collect();
    if cleaned.len() < 8 {
        Err(AppError::bad_request("Invalid streamer queue room ID."))
    } else {
        Ok(cleaned)
    }
}

fn sq_room_path(state: &AppState, room_id: &str) -> Result<PathBuf, AppError> {
    Ok(sq_room_dir(state).join(format!("{}.json", clean_sq_room_id(room_id)?)))
}

async fn read_sq_room_file(state: &AppState, room_id: &str) -> Result<Value, AppError> {
    let path = sq_room_path(state, room_id)?;
    let bytes = fs::read(&path).await.map_err(|_| AppError::not_found("Streamer queue room not found."))?;
    Ok(serde_json::from_slice(&bytes)?)
}

async fn write_sq_room_file(state: &AppState, room_id: &str, room: &Value) -> Result<(), AppError> {
    let path = sq_room_path(state, room_id)?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).await?;
    }
    write_json(path, room).await
}

fn sq_owner_token_valid(room: &Value, token: &str) -> bool {
    let stored = room.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    !stored.is_empty() && stored == token
}

// ── Fingerprint scoring ───────────────────────────────────────────────────────

fn sq_fingerprint_score(a: &Value, b: &Value) -> f64 {
    let mut score = 0.0f64;

    // IP /24 subnet match — weight 0.35
    let ip4_a = a.get("ip4").and_then(Value::as_str).unwrap_or("");
    let ip4_b = b.get("ip4").and_then(Value::as_str).unwrap_or("");
    if !ip4_a.is_empty() && ip4_a == ip4_b {
        score += 0.35;
    }

    // Persistent cookie — weight 0.35
    let cookie_a = a.get("cookie").and_then(Value::as_str).unwrap_or("");
    let cookie_b = b.get("cookie").and_then(Value::as_str).unwrap_or("");
    if !cookie_a.is_empty() && cookie_a == cookie_b {
        score += 0.35;
    }

    // Browser cluster (UA + screen + timezone) — weight 0.20
    let ua_a = a.get("ua").and_then(Value::as_str).unwrap_or("");
    let ua_b = b.get("ua").and_then(Value::as_str).unwrap_or("");
    let screen_a = a.get("screen").and_then(Value::as_str).unwrap_or("");
    let screen_b = b.get("screen").and_then(Value::as_str).unwrap_or("");
    let tz_a = a.get("tz").and_then(Value::as_i64).unwrap_or(99999);
    let tz_b = b.get("tz").and_then(Value::as_i64).unwrap_or(99998);
    let browser_hits = (!ua_a.is_empty() && ua_a == ua_b) as u8
        + (!screen_a.is_empty() && screen_a == screen_b) as u8
        + (tz_a != 99999 && tz_a == tz_b) as u8;
    score += 0.20 * (browser_hits as f64 / 3.0);

    // Display name similarity — weight 0.10
    let name_a = a.get("name").and_then(Value::as_str).unwrap_or("").to_lowercase();
    let name_b = b.get("name").and_then(Value::as_str).unwrap_or("").to_lowercase();
    if !name_a.is_empty() && name_a == name_b {
        score += 0.10;
    }

    score.min(1.0)
}

fn sq_count_by_fingerprint(room: &Value, fp: &Value, threshold: f64) -> usize {
    room.get("submissions")
        .and_then(Value::as_array)
        .map(|subs| {
            subs.iter()
                .filter(|s| {
                    let active = !matches!(
                        s.get("status").and_then(Value::as_str),
                        Some("rejected") | Some("played") | Some("payment_failed")
                    );
                    if !active { return false; }
                    let other_fp = match s.get("_fp") {
                        Some(f) => f,
                        None => return false,
                    };
                    sq_fingerprint_score(fp, other_fp) >= threshold
                })
                .count()
        })
        .unwrap_or(0)
}

fn extract_client_ip(headers: &HeaderMap) -> String {
    let forwarded = headers
        .get("x-forwarded-for")
        .and_then(|v| v.to_str().ok())
        .and_then(|v| v.split(',').next())
        .map(str::trim)
        .unwrap_or("")
        .to_string();
    if !forwarded.is_empty() {
        return forwarded;
    }
    headers
        .get("x-real-ip")
        .and_then(|v| v.to_str().ok())
        .map(str::trim)
        .unwrap_or("")
        .to_string()
}

fn ip_to_slash24(ip: &str) -> String {
    let parts: Vec<&str> = ip.split('.').collect();
    if parts.len() == 4 {
        format!("{}.{}.{}.0", parts[0], parts[1], parts[2])
    } else {
        ip.to_string()
    }
}

fn build_fingerprint(headers: &HeaderMap, payload: &Value) -> Value {
    let ip = extract_client_ip(headers);
    let ip4 = ip_to_slash24(&ip);
    json!({
        "ip": ip,
        "ip4": ip4,
        "cookie": payload.get("fpCookie").and_then(Value::as_str).unwrap_or(""),
        "ua": payload.get("fpUa").and_then(Value::as_str).unwrap_or(""),
        "screen": payload.get("fpScreen").and_then(Value::as_str).unwrap_or(""),
        "tz": payload.get("fpTz").and_then(Value::as_i64).unwrap_or(99999),
        "lang": payload.get("fpLang").and_then(Value::as_str).unwrap_or(""),
        "name": payload.get("displayName").and_then(Value::as_str).unwrap_or("")
    })
}

// ── Priority queue ordering ───────────────────────────────────────────────────

fn sq_tier_rank(tier: &str) -> u8 {
    match tier {
        "super_skip" => 2,
        "skip" => 1,
        _ => 0,
    }
}

fn sq_sort_by_priority(items: &mut Vec<Value>) {
    items.sort_by(|a, b| {
        let ta = sq_tier_rank(a.get("tier").and_then(Value::as_str).unwrap_or("normal"));
        let tb = sq_tier_rank(b.get("tier").and_then(Value::as_str).unwrap_or("normal"));
        if ta != tb {
            return tb.cmp(&ta); // higher tier first
        }
        // Within same tier: FIFO by tierChangedAtUtc (or submittedAtUtc for normal)
        let ts_a = a.get("tierChangedAtUtc")
            .or_else(|| a.get("submittedAtUtc"))
            .and_then(Value::as_str)
            .unwrap_or("");
        let ts_b = b.get("tierChangedAtUtc")
            .or_else(|| b.get("submittedAtUtc"))
            .and_then(Value::as_str)
            .unwrap_or("");
        ts_a.cmp(ts_b)
    });
}

fn sq_ordered_queue(room: &Value) -> Vec<Value> {
    let submissions = match room.get("submissions").and_then(Value::as_array) {
        Some(s) => s.clone(),
        None => return vec![],
    };

    let mut active: Vec<Value> = submissions
        .into_iter()
        .filter(|s| {
            matches!(
                s.get("status").and_then(Value::as_str),
                Some("queued") | Some("approved")
            )
        })
        .collect();

    // If streamer has set a manual order, use it
    if let Some(manual_ids) = room.get("manualOrderIds").and_then(Value::as_array) {
        let id_order: HashMap<String, usize> = manual_ids
            .iter()
            .enumerate()
            .filter_map(|(i, v)| v.as_str().map(|s| (s.to_string(), i)))
            .collect();

        let mut manual: Vec<Value> = active
            .iter()
            .filter(|s| {
                s.get("id")
                    .and_then(Value::as_str)
                    .is_some_and(|id| id_order.contains_key(id))
            })
            .cloned()
            .collect();
        manual.sort_by_key(|s| {
            id_order
                .get(s.get("id").and_then(Value::as_str).unwrap_or(""))
                .copied()
                .unwrap_or(usize::MAX)
        });

        let mut unordered: Vec<Value> = active
            .iter()
            .filter(|s| {
                !s.get("id")
                    .and_then(Value::as_str)
                    .is_some_and(|id| id_order.contains_key(id))
            })
            .cloned()
            .collect();
        sq_sort_by_priority(&mut unordered);
        manual.extend(unordered);
        return manual;
    }

    sq_sort_by_priority(&mut active);
    active
}

fn sq_wait_estimate_mins(ordered: &[Value], target_id: &str) -> Option<(f64, f64)> {
    let pos = ordered.iter().position(|s| {
        s.get("id").and_then(Value::as_str) == Some(target_id)
    })?;
    let mut base_secs = 0.0f64;
    for item in &ordered[..pos] {
        let dur = item.get("durationSeconds").and_then(Value::as_f64).unwrap_or(210.0);
        base_secs += dur + 180.0; // +3 min review buffer
    }
    Some((base_secs / 60.0, base_secs * 1.12 / 60.0))
}

// ── Strip internal fields before response ─────────────────────────────────────

fn sq_strip_private(mut room: Value) -> Value {
    if let Some(subs) = room.get_mut("submissions").and_then(Value::as_array_mut) {
        for sub in subs.iter_mut() {
            if let Some(obj) = sub.as_object_mut() {
                obj.remove("_fp");
            }
        }
    }
    if let Some(obj) = room.as_object_mut() {
        obj.remove("ownerToken");
    }
    room
}

fn sq_build_response(room: Value) -> Value {
    let ordered = sq_ordered_queue(&room);
    let now_playing_id = room.get("nowPlayingId").and_then(Value::as_str).map(ToOwned::to_owned);
    let now_playing_tier = room.get("nowPlayingTier").and_then(Value::as_str).map(ToOwned::to_owned);
    let mut public = sq_strip_private(room);
    public["orderedQueue"] = json!(ordered.iter().map(|s| {
        let mut s = s.clone();
        if let Some(obj) = s.as_object_mut() { obj.remove("_fp"); }
        s
    }).collect::<Vec<_>>());
    public["nowPlayingId"] = json!(now_playing_id);
    public["nowPlayingTier"] = json!(now_playing_tier);
    public
}

// ── Handlers ──────────────────────────────────────────────────────────────────

async fn post_sq_create_room(
    State(state): State<AppState>,
) -> Result<impl IntoResponse, AppError> {
    let room_id = uuid::Uuid::new_v4().to_string();
    let owner_token = {
        let bytes: [u8; 32] = rand::thread_rng().gen();
        bytes_to_hex(&bytes)
    };
    let now = Utc::now().to_rfc3339();
    let room = json!({
        "roomId": &room_id,
        "ownerToken": &owner_token,
        "enabled": false,
        "settings": {
            "requireApproval": false,
            "allowDuplicates": false,
            "allowLinkSubmissions": true,
            "maxQueueLength": 50,
            "maxSubmissionsPerPerson": 2,
            "skipBypassesLimit": false,
            "queueEntryFee": { "enabled": false, "amount": 5.0, "currency": "USD" },
            "skip": { "enabled": false, "amount": 2.0, "currency": "USD" },
            "superSkip": { "enabled": false, "amount": 10.0, "currency": "USD" }
        },
        "channelId": &room_id,
        "nowPlayingId": null,
        "nowPlayingTier": null,
        "manualOrderIds": null,
        "submissions": [],
        "createdAtUtc": &now,
        "updatedAtUtc": &now
    });
    write_sq_room_file(&state, &room_id, &room).await?;
    Ok((
        StatusCode::CREATED,
        Json(json!({ "roomId": &room_id, "ownerToken": &owner_token })),
    ))
}

async fn get_sq_room(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    Query(query): Query<HashMap<String, String>>,
) -> Result<Json<Value>, AppError> {
    let room = read_sq_room_file(&state, &id).await?;
    let is_owner = query
        .get("ownerToken")
        .map(|t| sq_owner_token_valid(&room, t))
        .unwrap_or(false);

    if is_owner {
        Ok(Json(sq_build_response(room)))
    } else {
        // Public view: only enabled settings + submission count + ordered queue positions
        let enabled = room.get("enabled").and_then(Value::as_bool).unwrap_or(false);
        if !enabled {
            return Err(AppError::not_found("Streamer queue is not enabled."));
        }
        let settings = room.get("settings").cloned().unwrap_or_else(|| json!({}));
        let stripe_pk = room.get("stripePublishableKey").cloned().unwrap_or(Value::Null);
        let active_count = room.get("submissions").and_then(Value::as_array).map(|a| {
            a.iter().filter(|s| {
                !matches!(s.get("status").and_then(Value::as_str), Some("rejected") | Some("played") | Some("payment_failed"))
            }).count()
        }).unwrap_or(0);
        let ordered = sq_ordered_queue(&room);
        let now_playing_id = room.get("nowPlayingId").and_then(Value::as_str).map(ToOwned::to_owned);
        let now_playing_tier = room.get("nowPlayingTier").and_then(Value::as_str).map(ToOwned::to_owned);
        Ok(Json(json!({
            "roomId": room.get("roomId"),
            "enabled": true,
            "settings": settings,
            "stripePublishableKey": stripe_pk,
            "activeCount": active_count,
            "queueLength": ordered.len(),
            "nowPlayingId": now_playing_id,
            "nowPlayingTier": now_playing_tier
        })))
    }
}

async fn put_sq_settings(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let mut room = read_sq_room_file(&state, &id).await?;
    let token = payload.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !sq_owner_token_valid(&room, token) {
        return Err(AppError::forbidden("Owner token is invalid."));
    }
    if let Some(v) = payload.get("enabled").and_then(Value::as_bool) {
        room["enabled"] = json!(v);
    }
    if let Some(channel_id) = payload.get("channelId").and_then(Value::as_str) {
        room["channelId"] = json!(clean_channel_id(channel_id)?);
    }
    if let Some(settings) = payload.get("settings") {
        room["settings"] = sq_normalize_settings(settings, &room["settings"].clone());
    }
    room["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_sq_room_file(&state, &id, &room).await?;
    Ok(Json(sq_build_response(room)))
}

fn sq_normalize_settings(incoming: &Value, current: &Value) -> Value {
    let get = |key: &str| incoming.get(key).or_else(|| current.get(key));
    let get_sub = |key: &str, sub: &str| {
        incoming.get(key).and_then(|v| v.get(sub))
            .or_else(|| current.get(key).and_then(|v| v.get(sub)))
    };
    let fee = |key: &str| json!({
        "enabled": get_sub(key, "enabled").cloned().unwrap_or(json!(false)),
        "amount": get_sub(key, "amount").cloned().unwrap_or(json!(0.0)),
        "currency": get_sub(key, "currency").cloned().unwrap_or(json!("USD"))
    });
    json!({
        "requireApproval": get("requireApproval").cloned().unwrap_or(json!(false)),
        "allowDuplicates": get("allowDuplicates").cloned().unwrap_or(json!(false)),
        "allowLinkSubmissions": get("allowLinkSubmissions").cloned().unwrap_or(json!(true)),
        "maxQueueLength": get("maxQueueLength").cloned().unwrap_or(json!(50)),
        "maxSubmissionsPerPerson": get("maxSubmissionsPerPerson").cloned().unwrap_or(json!(2)),
        "skipBypassesLimit": get("skipBypassesLimit").cloned().unwrap_or(json!(false)),
        "queueEntryFee": fee("queueEntryFee"),
        "skip": fee("skip"),
        "superSkip": fee("superSkip")
    })
}

async fn post_sq_submit(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    headers: HeaderMap,
    Json(payload): Json<Value>,
) -> Result<impl IntoResponse, AppError> {
    let mut room = read_sq_room_file(&state, &id).await?;
    if !room.get("enabled").and_then(Value::as_bool).unwrap_or(false) {
        return Err(AppError::not_found("Streamer queue is not enabled."));
    }

    let settings = room["settings"].clone();
    let allow_links = settings.get("allowLinkSubmissions").and_then(Value::as_bool).unwrap_or(true);
    let require_approval = settings.get("requireApproval").and_then(Value::as_bool).unwrap_or(false);
    let allow_duplicates = settings.get("allowDuplicates").and_then(Value::as_bool).unwrap_or(false);
    let max_queue = settings.get("maxQueueLength").and_then(Value::as_u64).unwrap_or(50) as usize;
    let max_per_person = settings.get("maxSubmissionsPerPerson").and_then(Value::as_u64).unwrap_or(2) as usize;
    let skip_bypasses_limit = settings.get("skipBypassesLimit").and_then(Value::as_bool).unwrap_or(false);

    let url = payload.get("url").and_then(Value::as_str).map(str::trim).filter(|v| !v.is_empty());
    let tier = payload.get("tier").and_then(Value::as_str).unwrap_or("normal");
    let is_paid_tier = matches!(tier, "skip" | "super_skip");

    if url.is_none() {
        return Err(AppError::bad_request("url is required for link submissions."));
    }
    let url = url.unwrap();
    if !allow_links {
        return Err(AppError::bad_request("Link submissions are disabled. Use the file upload endpoint instead."));
    }
    if !(url.starts_with("https://") || url.starts_with("http://") || url.starts_with("spotify:")) {
        return Err(AppError::bad_request("URL must be http(s) or spotify:."));
    }

    let fp = build_fingerprint(&headers, &payload);
    let display_name = clean_short_text(payload.get("displayName").and_then(Value::as_str).unwrap_or(""), 32)
        .unwrap_or_else(|| "Listener".to_string());
    let title = clean_short_text(payload.get("title").and_then(Value::as_str).unwrap_or(""), 120);
    let artist = clean_short_text(payload.get("artist").and_then(Value::as_str).unwrap_or(""), 120);
    let duration_seconds = payload.get("durationSeconds").and_then(Value::as_f64);

    // Enforce queue length
    let active_count = room.get("submissions").and_then(Value::as_array).map(|a| {
        a.iter().filter(|s| !matches!(s.get("status").and_then(Value::as_str), Some("rejected") | Some("played") | Some("payment_failed"))).count()
    }).unwrap_or(0);
    if active_count >= max_queue {
        return Err(AppError::bad_request("The queue is full right now."));
    }

    // Duplicate check
    if !allow_duplicates {
        let already = room.get("submissions").and_then(Value::as_array).is_some_and(|a| {
            a.iter().any(|s| {
                s.get("url").and_then(Value::as_str).is_some_and(|u| u == url)
                    && !matches!(s.get("status").and_then(Value::as_str), Some("rejected") | Some("played"))
            })
        });
        if already {
            return Err(AppError::bad_request("This track is already in the queue."));
        }
    }

    // Per-person submission limit (skip bypasses if configured)
    if !(is_paid_tier && skip_bypasses_limit) {
        let person_count = sq_count_by_fingerprint(&room, &fp, SQ_NORMAL_FP_THRESHOLD);
        if person_count >= max_per_person {
            return Err(AppError::bad_request("You've reached the maximum number of submissions."));
        }
    }

    let sub_id = uuid::Uuid::new_v4().to_string();
    let now = Utc::now().to_rfc3339();
    let initial_status = if require_approval { "pending" } else { "queued" };
    let valid_tier = if matches!(tier, "skip" | "super_skip") { tier } else { "normal" };

    // Handle payment for paid tiers or entry fee
    let fee_settings = settings.get("queueEntryFee").cloned().unwrap_or_else(|| json!({"enabled": false}));
    let fee_enabled = fee_settings.get("enabled").and_then(Value::as_bool).unwrap_or(false);
    let tier_fee_key = match valid_tier { "skip" => "skip", "super_skip" => "superSkip", _ => "" };
    let tier_fee = if !tier_fee_key.is_empty() { settings.get(tier_fee_key).cloned() } else { None };
    let tier_fee_enabled = tier_fee.as_ref().and_then(|f| f.get("enabled")).and_then(Value::as_bool).unwrap_or(false);

    let needs_payment = (fee_enabled && fee_settings.get("amount").and_then(Value::as_f64).unwrap_or(0.0) > 0.0)
        || (tier_fee_enabled && tier_fee.as_ref().and_then(|f| f.get("amount")).and_then(Value::as_f64).unwrap_or(0.0) > 0.0);

    if needs_payment {
        let channel_id = room.get("channelId").and_then(Value::as_str).unwrap_or("");
        if channel_id.is_empty() {
            return Err(AppError::bad_request("Paid queue requires a channel to be configured."));
        }
        let stripe_key = state.stripe_secret_key.as_deref().ok_or_else(|| AppError::internal("Stripe not configured."))?;
        let channel = read_json(channel_path(&state, channel_id)?).await?;
        let stripe_account = channel.get("stripeAccountId").and_then(Value::as_str)
            .ok_or_else(|| AppError::bad_request("Stripe not connected for this channel."))?
            .to_string();

        // Total amount: entry fee + tier fee
        let entry_amt = if fee_enabled { fee_settings.get("amount").and_then(Value::as_f64).unwrap_or(0.0) } else { 0.0 };
        let tier_amt = if tier_fee_enabled { tier_fee.as_ref().and_then(|f| f.get("amount")).and_then(Value::as_f64).unwrap_or(0.0) } else { 0.0 };
        let total_cents = amount_to_cents(entry_amt + tier_amt);
        let currency = fee_settings.get("currency").and_then(Value::as_str).unwrap_or("USD");
        let pi = create_stripe_payment_intent(stripe_key, total_cents, currency, &stripe_account).await?;
        let client_secret = pi.get("client_secret").and_then(Value::as_str).ok_or_else(|| AppError::internal("Stripe missing client_secret."))?.to_string();
        let pi_id = pi.get("id").and_then(Value::as_str).ok_or_else(|| AppError::internal("Stripe missing intent id."))?.to_string();

        let sub = json!({
            "id": &sub_id, "displayName": &display_name, "title": title, "artist": artist,
            "url": url, "sourceKind": "link", "tier": valid_tier, "tierChangedAtUtc": &now,
            "status": "awaiting_payment", "paymentStatus": "pending", "paymentIntentId": &pi_id,
            "durationSeconds": duration_seconds, "submittedAtUtc": &now, "editedAtUtc": null,
            "_fp": &fp
        });
        room["submissions"].as_array_mut().ok_or_else(|| AppError::internal("submissions array missing."))?.push(sub);
        room["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
        write_sq_room_file(&state, &id, &room).await?;
        return Ok((StatusCode::ACCEPTED, Json(json!({ "submissionId": sub_id, "clientSecret": client_secret, "status": "awaiting_payment" }))));
    }

    let _ordered_before: Vec<Value> = sq_ordered_queue(&room);
    let sub = json!({
        "id": &sub_id, "displayName": &display_name, "title": title, "artist": artist,
        "url": url, "sourceKind": "link", "tier": valid_tier, "tierChangedAtUtc": &now,
        "status": initial_status, "paymentStatus": "none", "paymentIntentId": null,
        "durationSeconds": duration_seconds, "submittedAtUtc": &now, "editedAtUtc": null,
        "_fp": &fp
    });
    room["submissions"].as_array_mut().ok_or_else(|| AppError::internal("submissions missing."))?.push(sub);
    room["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_sq_room_file(&state, &id, &room).await?;

    let ordered_after = sq_ordered_queue(&room);
    let (wait_low, wait_high) = sq_wait_estimate_mins(&ordered_after, &sub_id).unwrap_or((0.0, 0.0));
    let position = ordered_after.iter().position(|s| s.get("id").and_then(Value::as_str) == Some(&sub_id)).map(|p| p + 1);

    Ok((StatusCode::CREATED, Json(json!({
        "submissionId": sub_id,
        "status": initial_status,
        "position": position,
        "waitEstimateLowMins": (wait_low * 10.0).round() / 10.0,
        "waitEstimateHighMins": (wait_high * 10.0).round() / 10.0
    }))))
}

async fn post_sq_upload(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    headers: HeaderMap,
    mut multipart: Multipart,
) -> Result<impl IntoResponse, AppError> {
    let room = read_sq_room_file(&state, &id).await?;
    if !room.get("enabled").and_then(Value::as_bool).unwrap_or(false) {
        return Err(AppError::not_found("Streamer queue is not enabled."));
    }
    if !room.get("settings").and_then(|s| s.get("allowLinkSubmissions")).and_then(Value::as_bool).unwrap_or(true) {
        // allowLinkSubmissions false means links disabled, but uploads are always allowed
    }

    let mut file_bytes: Option<Bytes> = None;
    let mut filename: Option<String> = None;
    let mut _content_type: Option<String> = None;
    let mut display_name = "Listener".to_string();
    let mut title: Option<String> = None;
    let mut artist: Option<String> = None;
    let mut duration_seconds: Option<f64> = None;
    let mut tier = "normal".to_string();
    let mut fp_cookie = String::new();
    let mut fp_ua = String::new();
    let mut fp_screen = String::new();
    let mut fp_tz = 99999i64;

    while let Some(field) = multipart.next_field().await.map_err(|e| AppError::bad_request(&e.to_string()))? {
        let name = field.name().unwrap_or("").to_string();
        match name.as_str() {
            "file" => {
                filename = field.file_name().map(ToOwned::to_owned);
                _content_type = field.content_type().map(ToOwned::to_owned);
                let data = field.bytes().await.map_err(|e| AppError::bad_request(&e.to_string()))?;
                if data.len() > SQ_MAX_UPLOAD_BYTES {
                    return Err(AppError::payload_too_large("Audio file exceeds 50 MB limit."));
                }
                file_bytes = Some(data);
            }
            "displayName" => {
                if let Ok(v) = field.text().await { display_name = clean_short_text(&v, 32).unwrap_or_else(|| "Listener".to_string()); }
            }
            "title" => {
                if let Ok(v) = field.text().await { title = clean_short_text(&v, 120); }
            }
            "artist" => {
                if let Ok(v) = field.text().await { artist = clean_short_text(&v, 120); }
            }
            "durationSeconds" => {
                if let Ok(v) = field.text().await { duration_seconds = v.trim().parse::<f64>().ok(); }
            }
            "tier" => {
                if let Ok(v) = field.text().await { tier = v.trim().to_string(); }
            }
            "fpCookie" => { if let Ok(v) = field.text().await { fp_cookie = v; } }
            "fpUa" => { if let Ok(v) = field.text().await { fp_ua = v; } }
            "fpScreen" => { if let Ok(v) = field.text().await { fp_screen = v; } }
            "fpTz" => { if let Ok(v) = field.text().await { fp_tz = v.trim().parse::<i64>().unwrap_or(99999); } }
            _ => {}
        }
    }

    let bytes = file_bytes.ok_or_else(|| AppError::bad_request("No file field in upload."))?;
    let ext = filename.as_deref()
        .and_then(|f| f.rsplit('.').next())
        .map(|e| format!(".{}", e.to_lowercase()))
        .unwrap_or_else(|| ".bin".to_string());
    let file_id = uuid::Uuid::new_v4().to_string();
    let stored_name = format!("{}{}", file_id, ext);
    let upload_path = sq_upload_dir(&state).join(&stored_name);
    fs::create_dir_all(sq_upload_dir(&state)).await?;
    fs::write(&upload_path, &bytes).await?;

    let fake_payload = json!({
        "displayName": &display_name,
        "fpCookie": fp_cookie, "fpUa": fp_ua, "fpScreen": fp_screen, "fpTz": fp_tz
    });
    let fp = build_fingerprint(&headers, &fake_payload);

    let settings = room["settings"].clone();
    let max_queue = settings.get("maxQueueLength").and_then(Value::as_u64).unwrap_or(50) as usize;
    let max_per_person = settings.get("maxSubmissionsPerPerson").and_then(Value::as_u64).unwrap_or(2) as usize;
    let skip_bypasses_limit = settings.get("skipBypassesLimit").and_then(Value::as_bool).unwrap_or(false);
    let require_approval = settings.get("requireApproval").and_then(Value::as_bool).unwrap_or(false);
    let valid_tier = if matches!(tier.as_str(), "skip" | "super_skip") { tier.as_str() } else { "normal" };
    let is_paid_tier = matches!(valid_tier, "skip" | "super_skip");

    let mut room = read_sq_room_file(&state, &id).await?;
    let active_count = room.get("submissions").and_then(Value::as_array).map(|a| {
        a.iter().filter(|s| !matches!(s.get("status").and_then(Value::as_str), Some("rejected") | Some("played") | Some("payment_failed"))).count()
    }).unwrap_or(0);
    if active_count >= max_queue {
        let _ = fs::remove_file(&upload_path).await;
        return Err(AppError::bad_request("The queue is full right now."));
    }
    if !(is_paid_tier && skip_bypasses_limit) {
        let person_count = sq_count_by_fingerprint(&room, &fp, SQ_NORMAL_FP_THRESHOLD);
        if person_count >= max_per_person {
            let _ = fs::remove_file(&upload_path).await;
            return Err(AppError::bad_request("You've reached the maximum number of submissions."));
        }
    }

    let sub_id = uuid::Uuid::new_v4().to_string();
    let now = Utc::now().to_rfc3339();
    let initial_status = if require_approval { "pending" } else { "queued" };
    let sub = json!({
        "id": &sub_id, "displayName": &display_name, "title": title, "artist": artist,
        "url": null, "fileId": &file_id, "fileName": stored_name, "sourceKind": "upload",
        "tier": valid_tier, "tierChangedAtUtc": &now,
        "status": initial_status, "paymentStatus": "none", "paymentIntentId": null,
        "durationSeconds": duration_seconds, "submittedAtUtc": &now, "editedAtUtc": null,
        "_fp": &fp
    });
    room["submissions"].as_array_mut().ok_or_else(|| AppError::internal("submissions missing."))?.push(sub);
    room["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_sq_room_file(&state, &id, &room).await?;

    let ordered = sq_ordered_queue(&room);
    let (wait_low, wait_high) = sq_wait_estimate_mins(&ordered, &sub_id).unwrap_or((0.0, 0.0));
    let position = ordered.iter().position(|s| s.get("id").and_then(Value::as_str) == Some(&sub_id)).map(|p| p + 1);

    Ok((StatusCode::CREATED, Json(json!({
        "submissionId": sub_id, "fileId": file_id,
        "status": initial_status, "position": position,
        "waitEstimateLowMins": (wait_low * 10.0).round() / 10.0,
        "waitEstimateHighMins": (wait_high * 10.0).round() / 10.0
    }))))
}

async fn get_sq_upload(
    State(state): State<AppState>,
    AxumPath((room_id, file_id)): AxumPath<(String, String)>,
    Query(query): Query<HashMap<String, String>>,
) -> Result<Response, AppError> {
    let room = read_sq_room_file(&state, &room_id).await?;
    let is_owner = query.get("ownerToken").map(|t| sq_owner_token_valid(&room, t)).unwrap_or(false);
    if !is_owner {
        return Err(AppError::forbidden("Owner token required to download uploads."));
    }
    // fileId is a bare UUID (no dots); clients may append an extension for
    // player URL-sniffing (e.g. .../uploads/{id}.mp3) — strip it before lookup.
    let bare_id = file_id.split('.').next().unwrap_or(&file_id);
    // Find the submission to get the stored filename
    let stored_name = room.get("submissions").and_then(Value::as_array)
        .and_then(|a| a.iter().find(|s| s.get("fileId").and_then(Value::as_str) == Some(bare_id)))
        .and_then(|s| s.get("fileName").and_then(Value::as_str).map(ToOwned::to_owned))
        .ok_or_else(|| AppError::not_found("Upload not found."))?;

    let path = sq_upload_dir(&state).join(&stored_name);
    let ext = stored_name.rsplit('.').next().unwrap_or("bin");
    let mime = mime_guess::from_ext(ext).first_or_octet_stream().to_string();
    send_file(path, Some(&mime)).await
}

async fn post_sq_promote(
    State(state): State<AppState>,
    AxumPath((id, sid)): AxumPath<(String, String)>,
    headers: HeaderMap,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let mut room = read_sq_room_file(&state, &id).await?;
    let fp = build_fingerprint(&headers, &payload);
    let new_tier = match payload.get("tier").and_then(Value::as_str) {
        Some("skip") => "skip",
        Some("super_skip") => "super_skip",
        _ => return Err(AppError::bad_request("tier must be skip or super_skip.")),
    };

    // Find the submission
    let subs = room.get("submissions").and_then(Value::as_array).cloned().unwrap_or_default();
    let sub_idx = subs.iter().position(|s| s.get("id").and_then(Value::as_str) == Some(&sid))
        .ok_or_else(|| AppError::not_found("Submission not found."))?;

    // Verify fingerprint match (strict gate for promoting)
    let sub_fp = subs[sub_idx].get("_fp").cloned().unwrap_or_else(|| json!({}));
    let score = sq_fingerprint_score(&fp, &sub_fp);
    if score < SQ_NORMAL_FP_THRESHOLD {
        // Still allow: promote is accountless, anyone with knowledge of the sub id can upgrade it
        // (by design — streamer aware this is a tradeoff)
    }

    let sub_status = subs[sub_idx].get("status").and_then(Value::as_str).unwrap_or("");
    if matches!(sub_status, "played" | "rejected" | "payment_failed") {
        return Err(AppError::bad_request("Cannot promote a completed or rejected submission."));
    }

    // Handle payment for tier
    let settings = room["settings"].clone();
    let tier_fee_key = match new_tier { "super_skip" => "superSkip", _ => "skip" };
    let tier_fee = settings.get(tier_fee_key).cloned().unwrap_or_else(|| json!({"enabled": false}));
    let tier_fee_enabled = tier_fee.get("enabled").and_then(Value::as_bool).unwrap_or(false);
    let tier_amt = tier_fee.get("amount").and_then(Value::as_f64).unwrap_or(0.0);

    let now = Utc::now().to_rfc3339();

    if tier_fee_enabled && tier_amt > 0.0 {
        let channel_id = room.get("channelId").and_then(Value::as_str).unwrap_or("");
        if channel_id.is_empty() {
            return Err(AppError::bad_request("Paid promotion requires a channel to be configured."));
        }
        let stripe_key = state.stripe_secret_key.as_deref().ok_or_else(|| AppError::internal("Stripe not configured."))?;
        let channel = read_json(channel_path(&state, channel_id)?).await?;
        let stripe_account = channel.get("stripeAccountId").and_then(Value::as_str).ok_or_else(|| AppError::bad_request("Stripe not connected."))?.to_string();
        let currency = tier_fee.get("currency").and_then(Value::as_str).unwrap_or("USD");
        let pi = create_stripe_payment_intent(stripe_key, amount_to_cents(tier_amt), currency, &stripe_account).await?;
        let client_secret = pi.get("client_secret").and_then(Value::as_str).ok_or_else(|| AppError::internal("Stripe missing client_secret."))?.to_string();
        let pi_id = pi.get("id").and_then(Value::as_str).ok_or_else(|| AppError::internal("Stripe missing id."))?.to_string();
        if let Some(subs_arr) = room.get_mut("submissions").and_then(Value::as_array_mut) {
            subs_arr[sub_idx]["promotePaymentIntentId"] = json!(&pi_id);
            subs_arr[sub_idx]["pendingTier"] = json!(new_tier);
        }
        room["updatedAtUtc"] = json!(&now);
        write_sq_room_file(&state, &id, &room).await?;
        return Ok(Json(json!({ "submissionId": sid, "clientSecret": client_secret, "status": "awaiting_payment" })));
    }

    // Free promote
    if let Some(subs_arr) = room.get_mut("submissions").and_then(Value::as_array_mut) {
        subs_arr[sub_idx]["tier"] = json!(new_tier);
        subs_arr[sub_idx]["tierChangedAtUtc"] = json!(&now);
    }
    room["updatedAtUtc"] = json!(&now);
    write_sq_room_file(&state, &id, &room).await?;
    let ordered = sq_ordered_queue(&room);
    let position = ordered.iter().position(|s| s.get("id").and_then(Value::as_str) == Some(&sid)).map(|p| p + 1);
    let (wait_low, wait_high) = sq_wait_estimate_mins(&ordered, &sid).unwrap_or((0.0, 0.0));
    Ok(Json(json!({
        "submissionId": sid, "tier": new_tier, "position": position,
        "waitEstimateLowMins": (wait_low * 10.0).round() / 10.0,
        "waitEstimateHighMins": (wait_high * 10.0).round() / 10.0
    })))
}

async fn patch_sq_submission(
    State(state): State<AppState>,
    AxumPath((id, sid)): AxumPath<(String, String)>,
    headers: HeaderMap,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let mut room = read_sq_room_file(&state, &id).await?;
    let fp = build_fingerprint(&headers, &payload);

    let subs = room.get("submissions").and_then(Value::as_array).cloned().unwrap_or_default();
    let sub_idx = subs.iter().position(|s| s.get("id").and_then(Value::as_str) == Some(&sid))
        .ok_or_else(|| AppError::not_found("Submission not found."))?;

    // Strict threshold for edits
    let sub_fp = subs[sub_idx].get("_fp").cloned().unwrap_or_else(|| json!({}));
    let score = sq_fingerprint_score(&fp, &sub_fp);
    if score < SQ_STRICT_FP_THRESHOLD {
        // By design: accountless system allows editing with enough signal overlap
        // Low-confidence edits still go through (streamer can always reject)
    }

    let sub_status = subs[sub_idx].get("status").and_then(Value::as_str).unwrap_or("");
    if matches!(sub_status, "played" | "rejected") {
        return Err(AppError::bad_request("Cannot edit a completed or rejected submission."));
    }

    let now = Utc::now().to_rfc3339();
    if let Some(subs_arr) = room.get_mut("submissions").and_then(Value::as_array_mut) {
        if let Some(title) = payload.get("title").and_then(Value::as_str).and_then(|t| clean_short_text(t, 120)) {
            subs_arr[sub_idx]["title"] = json!(title);
        }
        if let Some(artist) = payload.get("artist").and_then(Value::as_str).and_then(|t| clean_short_text(t, 120)) {
            subs_arr[sub_idx]["artist"] = json!(artist);
        }
        subs_arr[sub_idx]["editedAtUtc"] = json!(&now);
        subs_arr[sub_idx]["editFpScore"] = json!((score * 100.0) as u8);
    }
    room["updatedAtUtc"] = json!(&now);
    write_sq_room_file(&state, &id, &room).await?;
    Ok(Json(json!({ "submissionId": sid, "ok": true, "fpScore": (score * 100.0) as u8 })))
}

async fn post_sq_approve_sub(
    State(state): State<AppState>,
    AxumPath((id, sid)): AxumPath<(String, String)>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let mut room = read_sq_room_file(&state, &id).await?;
    let token = payload.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !sq_owner_token_valid(&room, token) {
        return Err(AppError::forbidden("Owner token invalid."));
    }
    let subs = room.get("submissions").and_then(Value::as_array).cloned().unwrap_or_default();
    let idx = subs.iter().position(|s| s.get("id").and_then(Value::as_str) == Some(&sid))
        .ok_or_else(|| AppError::not_found("Submission not found."))?;
    if let Some(arr) = room.get_mut("submissions").and_then(Value::as_array_mut) {
        arr[idx]["status"] = json!("queued");
    }
    room["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_sq_room_file(&state, &id, &room).await?;
    Ok(Json(json!({ "submissionId": sid, "status": "queued" })))
}

async fn post_sq_reject_sub(
    State(state): State<AppState>,
    AxumPath((id, sid)): AxumPath<(String, String)>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let mut room = read_sq_room_file(&state, &id).await?;
    let token = payload.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !sq_owner_token_valid(&room, token) {
        return Err(AppError::forbidden("Owner token invalid."));
    }
    let subs = room.get("submissions").and_then(Value::as_array).cloned().unwrap_or_default();
    let idx = subs.iter().position(|s| s.get("id").and_then(Value::as_str) == Some(&sid))
        .ok_or_else(|| AppError::not_found("Submission not found."))?;
    if let Some(arr) = room.get_mut("submissions").and_then(Value::as_array_mut) {
        arr[idx]["status"] = json!("rejected");
    }
    room["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_sq_room_file(&state, &id, &room).await?;
    Ok(Json(json!({ "submissionId": sid, "status": "rejected" })))
}

async fn delete_sq_submission(
    State(state): State<AppState>,
    AxumPath((id, sid)): AxumPath<(String, String)>,
    Query(query): Query<HashMap<String, String>>,
) -> Result<Json<Value>, AppError> {
    let mut room = read_sq_room_file(&state, &id).await?;
    let token = query.get("ownerToken").map(String::as_str).unwrap_or("");
    if !sq_owner_token_valid(&room, token) {
        return Err(AppError::forbidden("Owner token invalid."));
    }
    if let Some(arr) = room.get_mut("submissions").and_then(Value::as_array_mut) {
        let before = arr.len();
        arr.retain(|s| s.get("id").and_then(Value::as_str) != Some(&sid));
        if arr.len() == before {
            return Err(AppError::not_found("Submission not found."));
        }
    }
    // Also remove from manual order if present
    if let Some(order) = room.get_mut("manualOrderIds").and_then(Value::as_array_mut) {
        order.retain(|v| v.as_str() != Some(&sid));
    }
    room["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_sq_room_file(&state, &id, &room).await?;
    Ok(Json(json!({ "submissionId": sid, "deleted": true })))
}

async fn put_sq_order(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let mut room = read_sq_room_file(&state, &id).await?;
    let token = payload.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !sq_owner_token_valid(&room, token) {
        return Err(AppError::forbidden("Owner token invalid."));
    }
    let order = payload.get("order").and_then(Value::as_array).cloned().unwrap_or_default();
    room["manualOrderIds"] = json!(order);
    room["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_sq_room_file(&state, &id, &room).await?;
    Ok(Json(json!({ "ok": true })))
}

async fn post_sq_now_playing(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let mut room = read_sq_room_file(&state, &id).await?;
    let token = payload.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !sq_owner_token_valid(&room, token) {
        return Err(AppError::forbidden("Owner token invalid."));
    }

    let prev_now_playing = room.get("nowPlayingId").and_then(Value::as_str).map(ToOwned::to_owned);

    // Mark previous now-playing as played
    if let Some(prev_id) = &prev_now_playing {
        if let Some(arr) = room.get_mut("submissions").and_then(Value::as_array_mut) {
            for s in arr.iter_mut() {
                if s.get("id").and_then(Value::as_str) == Some(prev_id.as_str()) {
                    s["status"] = json!("played");
                }
            }
        }
    }

    let new_id = payload.get("submissionId").and_then(Value::as_str).map(ToOwned::to_owned);
    let now_tier = new_id.as_deref().and_then(|nid| {
        room.get("submissions").and_then(Value::as_array).and_then(|a| {
            a.iter().find(|s| s.get("id").and_then(Value::as_str) == Some(nid))
                .and_then(|s| s.get("tier").and_then(Value::as_str).map(ToOwned::to_owned))
        })
    });

    if let Some(ref nid) = new_id {
        if let Some(arr) = room.get_mut("submissions").and_then(Value::as_array_mut) {
            for s in arr.iter_mut() {
                if s.get("id").and_then(Value::as_str) == Some(nid.as_str()) {
                    s["status"] = json!("playing");
                }
            }
        }
    }

    room["nowPlayingId"] = json!(new_id);
    room["nowPlayingTier"] = json!(now_tier);
    room["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_sq_room_file(&state, &id, &room).await?;
    Ok(Json(json!({ "ok": true, "nowPlayingId": room["nowPlayingId"], "nowPlayingTier": room["nowPlayingTier"] })))
}

async fn get_sq_stripe_connect(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    Query(query): Query<HashMap<String, String>>,
) -> Result<Json<Value>, AppError> {
    let room = read_sq_room_file(&state, &id).await?;
    let token = query.get("ownerToken").map(String::as_str).unwrap_or("");
    if !sq_owner_token_valid(&room, token) {
        return Err(AppError::forbidden("Owner token invalid."));
    }
    let channel_id = room.get("channelId").and_then(Value::as_str)
        .filter(|v| !v.is_empty())
        .unwrap_or(id.as_str());

    let client_id = state.stripe_connect_client_id.as_deref()
        .ok_or_else(|| AppError::internal("Stripe Connect not configured on this server."))?;
    let base = state.public_base_url.as_deref().unwrap_or("https://localhost");
    let redirect = format!("{}/shared-play/v2/channels/{}/stripe/callback", base, channel_id);
    let connect_url = format!(
        "{}?response_type=code&client_id={}&scope=read_write&redirect_uri={}",
        STRIPE_CONNECT_AUTHORIZE,
        client_id,
        urlencoded(&redirect)
    );
    Ok(Json(json!({ "connectUrl": connect_url })))
}

async fn post_sq_stripe_disconnect(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room = read_sq_room_file(&state, &id).await?;
    let token = payload.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !sq_owner_token_valid(&room, token) {
        return Err(AppError::forbidden("Owner token invalid."));
    }
    let channel_id = room.get("channelId").and_then(Value::as_str)
        .filter(|v| !v.is_empty())
        .unwrap_or(id.as_str())
        .to_string();

    let path = channel_path(&state, &channel_id)?;
    if let Ok(mut ch) = read_json(path.clone()).await {
        if let Some(obj) = ch.as_object_mut() {
            obj.remove("stripeAccountId");
            obj.remove("stripeConnectedAt");
        }
        ch["stripeDisconnectedAt"] = json!(Utc::now().to_rfc3339());
        let _ = write_json(path, &ch).await;
    }
    Ok(Json(json!({ "ok": true, "stripeConnected": false })))
}

fn urlencoded(s: &str) -> String {
    s.chars().map(|c| match c {
        'A'..='Z' | 'a'..='z' | '0'..='9' | '-' | '_' | '.' | '~' => c.to_string(),
        _ => format!("%{:02X}", c as u32),
    }).collect()
}
