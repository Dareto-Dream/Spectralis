//! Chat-platform-agnostic webhook API for the Streamer Queue (docs/streamer-queue-webhooks.md).
//!
//! A room owner mints one webhook key. Anything holding it (the bundled chat bridge for Twitch and
//! YouTube, Streamer.bot, a Mixitup macro, curl) can submit songs on behalf of a chat user and read
//! queue status. It can't change settings, approve, reject or skip: those stay behind the owner token.
//!
//! Submissions go through the room's normal submit path, so queue length, duplicate checks, approval
//! and per-person limits behave exactly as they do for the web page and the Discord bot.

use axum::extract::{Path as AxumPath, State};
use axum::http::HeaderMap;
use axum::response::{IntoResponse, Response};
use axum::Json;
use chrono::Utc;
use rand::Rng;
use serde_json::{json, Value};
use sha2::{Digest, Sha256};

use crate::{AppError, AppState};

const KEY_HEADER: &str = "x-spectralis-webhook-key";
const MAX_USER_ID_CHARS: usize = 64;

/// Keys are stored hashed: the database never holds a usable credential.
pub fn hash_key(key: &str) -> String {
    let digest = Sha256::digest(key.as_bytes());
    crate::bytes_to_hex(&digest)
}

fn generate_key() -> String {
    let bytes: [u8; 32] = rand::thread_rng().gen();
    format!("wk_{}", crate::bytes_to_hex(&bytes))
}

fn authorize(room: &Value, headers: &HeaderMap) -> Result<(), AppError> {
    let stored = room.get("webhookKeyHash").and_then(Value::as_str).unwrap_or("");
    let presented = headers.get(KEY_HEADER).and_then(|v| v.to_str().ok()).unwrap_or("").trim();
    if stored.is_empty() || presented.is_empty() || hash_key(presented) != stored {
        return Err(AppError::forbidden("Webhook key is invalid."));
    }
    Ok(())
}

/// `[a-z0-9_-]{1,16}`: a platform slug, never free text.
pub fn clean_source(value: &str) -> Result<String, AppError> {
    let v = value.trim().to_lowercase();
    let ok = !v.is_empty()
        && v.len() <= 16
        && v.chars().all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '_' || c == '-');
    if ok {
        Ok(v)
    } else {
        Err(AppError::bad_request("source must be 1-16 characters of a-z, 0-9, _ or -."))
    }
}

pub fn clean_user_id(value: &str) -> Result<String, AppError> {
    let v = value.trim();
    if v.is_empty() || v.chars().count() > MAX_USER_ID_CHARS || v.chars().any(|c| c.is_control()) {
        return Err(AppError::bad_request("userId is required (max 64 characters)."));
    }
    Ok(v.to_string())
}

/// The authenticated identity the submit path counts per-person limits against.
fn person_id(source: &str, user_id: &str) -> String {
    format!("{source}:{user_id}")
}

/// Translates a webhook submission into the payload `post_sq_submit` understands.
///
/// Who the person is comes from the platform (`source:userId`), vouched for by the webhook key, so it
/// is sent as `fpId` and decides identity on its own (see `sq_fingerprint_score`). The fuzzy browser
/// fingerprint can't be trusted here: every webhook call arrives from the bridge's own IP, so IP and
/// user-agent signals say nothing about the viewer.
pub fn submit_payload(payload: &Value) -> Result<(Value, String, String), AppError> {
    let source = clean_source(payload.get("source").and_then(Value::as_str).unwrap_or(""))?;
    let user_id = clean_user_id(payload.get("userId").and_then(Value::as_str).unwrap_or(""))?;
    let id = person_id(&source, &user_id);

    let mut out = json!({
        "url": payload.get("url"),
        "displayName": payload.get("displayName"),
        "tier": "normal",
        "fpId": id,
        "fpUa": "webhook",
    });
    for key in ["title", "artist"] {
        if let Some(v) = payload.get(key) {
            out[key] = v.clone();
        }
    }
    Ok((out, source, user_id))
}

pub async fn post_webhook_key(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let mut room = crate::read_sq_room_file(&state, &id).await?;
    let token = payload.get("ownerToken").and_then(Value::as_str).unwrap_or("");
    if !crate::sq_owner_token_valid(&room, token) {
        return Err(AppError::forbidden("Owner token is invalid."));
    }

    let response = match payload.get("action").and_then(Value::as_str) {
        Some("rotate") => {
            let key = generate_key();
            room["webhookKeyHash"] = json!(hash_key(&key));
            room["webhookKeyCreatedAtUtc"] = json!(Utc::now().to_rfc3339());
            json!({ "webhookKey": key })
        }
        Some("revoke") => {
            if let Some(obj) = room.as_object_mut() {
                obj.remove("webhookKeyHash");
                obj.remove("webhookKeyCreatedAtUtc");
            }
            json!({ "revoked": true })
        }
        _ => return Err(AppError::bad_request("action must be rotate or revoke.")),
    };

    crate::write_sq_room_file(&state, &id, &room).await?;
    Ok(Json(response))
}

pub async fn post_webhook_submit(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    headers: HeaderMap,
    Json(payload): Json<Value>,
) -> Result<Response, AppError> {
    let room = crate::read_sq_room_file(&state, &id).await?;
    authorize(&room, &headers)?;
    let (submit, source, _user_id) = submit_payload(&payload)?;
    let display_name = submit
        .get("displayName")
        .and_then(Value::as_str)
        .unwrap_or("Listener")
        .to_string();

    let response = crate::post_sq_submit(State(state.clone()), AxumPath(id.clone()), headers, Json(submit))
        .await?
        .into_response();

    // Read the result back so the room can be told who asked for what, then hand it to the caller untouched.
    let (parts, body) = response.into_parts();
    let bytes = axum::body::to_bytes(body, 64 * 1024).await.map_err(AppError::internal)?;
    let result: Value = serde_json::from_slice(&bytes)?;

    if let Some(submission_id) = result.get("submissionId").and_then(Value::as_str) {
        let status = result.get("status").and_then(Value::as_str).unwrap_or("queued");
        crate::sq_realtime::notify_submission(&state, &id, &source, &display_name, submission_id, status).await;
    }

    Ok((parts.status, Json(result)).into_response())
}

/// 1-based positions in the live queue held by the person identified by `id`.
pub fn positions_for(room: &Value, id: &str) -> Vec<usize> {
    crate::sq_ordered_queue(room)
        .iter()
        .enumerate()
        .filter(|(_, s)| {
            s.get("_fp")
                .and_then(|fp| fp.get("id"))
                .and_then(Value::as_str)
                == Some(id)
        })
        .map(|(i, _)| i + 1)
        .collect()
}

pub async fn post_webhook_status(
    State(state): State<AppState>,
    AxumPath(id): AxumPath<String>,
    headers: HeaderMap,
    Json(payload): Json<Value>,
) -> Result<Json<Value>, AppError> {
    let room = crate::read_sq_room_file(&state, &id).await?;
    authorize(&room, &headers)?;
    if !room.get("enabled").and_then(Value::as_bool).unwrap_or(false) {
        return Err(AppError::not_found("Streamer queue is not enabled."));
    }

    let source = clean_source(payload.get("source").and_then(Value::as_str).unwrap_or(""))?;
    let user_id = clean_user_id(payload.get("userId").and_then(Value::as_str).unwrap_or(""))?;
    let status = crate::sq_realtime::public_status(&room);

    Ok(Json(json!({
        "acceptingSubmissions": status["acceptingSubmissions"],
        "queueLength": status["queueLength"],
        "nowPlaying": status["nowPlaying"],
        "yourPositions": positions_for(&room, &person_id(&source, &user_id)),
    })))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::collab::Rooms;
    use axum::http::HeaderValue;
    use std::path::PathBuf;
    use std::sync::Arc;

    async fn test_state() -> AppState {
        std::env::remove_var("REDIS_URL");
        AppState {
            store: Arc::new(crate::store::Store::from_env().await.unwrap()),
            rooms: Rooms::default(),
            sq_rooms: Rooms::default(),
            web_share_root: Arc::new(PathBuf::new()),
            public_base_url: None,
            stripe_secret_key: None,
            stripe_webhook_secret: None,
            stripe_connect_client_id: None,
            stripe_publishable_key: None,
            ward_issuer: Arc::new("http://ward.test".to_string()),
        }
    }

    async fn body_json(response: Response) -> Value {
        let bytes = axum::body::to_bytes(response.into_body(), 1 << 20).await.unwrap();
        serde_json::from_slice(&bytes).unwrap()
    }

    /// An enabled room plus its owner token and a fresh webhook key.
    async fn room_with_key(state: &AppState) -> (String, String, String) {
        let created = crate::post_sq_create_room(State(state.clone())).await.unwrap().into_response();
        let created = body_json(created).await;
        let room_id = created["roomId"].as_str().unwrap().to_string();
        let owner = created["ownerToken"].as_str().unwrap().to_string();

        let _ = crate::put_sq_settings(
            State(state.clone()),
            AxumPath(room_id.clone()),
            Json(json!({ "ownerToken": owner, "enabled": true })),
        )
        .await
        .unwrap();

        let Json(key) = post_webhook_key(
            State(state.clone()),
            AxumPath(room_id.clone()),
            Json(json!({ "ownerToken": owner, "action": "rotate" })),
        )
        .await
        .unwrap();
        (room_id, owner, key["webhookKey"].as_str().unwrap().to_string())
    }

    fn keyed(key: &str) -> HeaderMap {
        let mut h = HeaderMap::new();
        h.insert(KEY_HEADER, HeaderValue::from_str(key).unwrap());
        h
    }

    async fn submit(state: &AppState, room: &str, key: &str, user: &str, name: &str, url: &str) -> Result<Value, AppError> {
        let response = post_webhook_submit(
            State(state.clone()),
            AxumPath(room.to_string()),
            keyed(key),
            Json(json!({ "source": "twitch", "userId": user, "displayName": name, "url": url })),
        )
        .await?;
        Ok(body_json(response).await)
    }

    #[tokio::test]
    async fn a_valid_key_submits_and_gets_a_position() {
        let state = test_state().await;
        let (room, _, key) = room_with_key(&state).await;

        let result = submit(&state, &room, &key, "u1", "Ann", "https://youtu.be/a").await.unwrap();

        assert_eq!(result["status"], "queued");
        assert_eq!(result["position"], 1);
    }

    #[tokio::test]
    async fn missing_wrong_and_revoked_keys_are_refused() {
        let state = test_state().await;
        let (room, owner, key) = room_with_key(&state).await;

        let no_key = post_webhook_submit(
            State(state.clone()),
            AxumPath(room.clone()),
            HeaderMap::new(),
            Json(json!({ "source": "twitch", "userId": "u", "url": "https://x.test/a" })),
        )
        .await;
        assert_eq!(no_key.unwrap_err().status.as_u16(), 403);
        assert_eq!(
            submit(&state, &room, "wk_wrong", "u", "A", "https://x.test/a").await.unwrap_err().status.as_u16(),
            403
        );

        let _ = post_webhook_key(
            State(state.clone()),
            AxumPath(room.clone()),
            Json(json!({ "ownerToken": owner, "action": "revoke" })),
        )
        .await
        .unwrap();
        assert_eq!(
            submit(&state, &room, &key, "u", "A", "https://x.test/a").await.unwrap_err().status.as_u16(),
            403
        );
    }

    #[tokio::test]
    async fn rotating_invalidates_the_old_key() {
        let state = test_state().await;
        let (room, owner, old_key) = room_with_key(&state).await;

        let Json(new) = post_webhook_key(
            State(state.clone()),
            AxumPath(room.clone()),
            Json(json!({ "ownerToken": owner, "action": "rotate" })),
        )
        .await
        .unwrap();
        let new_key = new["webhookKey"].as_str().unwrap();

        assert_ne!(old_key, new_key);
        assert!(submit(&state, &room, &old_key, "u", "A", "https://x.test/a").await.is_err());
        assert!(submit(&state, &room, new_key, "u", "A", "https://x.test/b").await.is_ok());
    }

    #[tokio::test]
    async fn only_the_owner_can_manage_keys() {
        let state = test_state().await;
        let (room, _, _) = room_with_key(&state).await;

        let denied = post_webhook_key(
            State(state.clone()),
            AxumPath(room.clone()),
            Json(json!({ "ownerToken": "nope", "action": "rotate" })),
        )
        .await;
        assert_eq!(denied.unwrap_err().status.as_u16(), 403);
    }

    #[tokio::test]
    async fn the_key_is_stored_hashed_and_never_shown_to_the_owner_view() {
        let state = test_state().await;
        let (room, _, key) = room_with_key(&state).await;

        let stored = crate::read_sq_room_file(&state, &room).await.unwrap();
        assert!(!stored.to_string().contains(&key), "the raw key must not be persisted");
        assert_eq!(stored["webhookKeyHash"], hash_key(&key));

        let owner_view = crate::sq_build_response(stored);
        assert!(owner_view.get("webhookKeyHash").is_none());
    }

    #[tokio::test]
    async fn the_per_person_limit_follows_the_viewer_not_the_bridge() {
        let state = test_state().await;
        let (room, _, key) = room_with_key(&state).await;

        // Default limit is 2 per person. Same viewer, same id: the third is refused...
        submit(&state, &room, &key, "u1", "Sam", "https://x.test/1").await.unwrap();
        submit(&state, &room, &key, "u1", "Sam", "https://x.test/2").await.unwrap();
        let third = submit(&state, &room, &key, "u1", "Sam", "https://x.test/3").await.unwrap_err();
        assert!(third.message.contains("maximum number"));

        // ...and a different viewer with the SAME display name is a different person.
        submit(&state, &room, &key, "u2", "Sam", "https://x.test/4").await.unwrap();
        // A viewer who renames still counts as the same person.
        let renamed = submit(&state, &room, &key, "u1", "Samuel", "https://x.test/5").await.unwrap_err();
        assert!(renamed.message.contains("maximum number"));
    }

    #[tokio::test]
    async fn status_lists_only_that_viewers_positions() {
        let state = test_state().await;
        let (room, _, key) = room_with_key(&state).await;
        submit(&state, &room, &key, "a", "A", "https://x.test/1").await.unwrap();
        submit(&state, &room, &key, "b", "B", "https://x.test/2").await.unwrap();
        submit(&state, &room, &key, "a", "A", "https://x.test/3").await.unwrap();

        let Json(status) = post_webhook_status(
            State(state.clone()),
            AxumPath(room.clone()),
            keyed(&key),
            Json(json!({ "source": "twitch", "userId": "a" })),
        )
        .await
        .unwrap();

        assert_eq!(status["queueLength"], 3);
        assert_eq!(status["yourPositions"], json!([1, 3]));
        assert_eq!(status["acceptingSubmissions"], true);
        assert!(status["nowPlaying"].is_null());
    }

    #[tokio::test]
    async fn submissions_and_room_writes_are_announced_on_the_realtime_channel() {
        let state = test_state().await;
        let (room, _, key) = room_with_key(&state).await;
        let mut sub = state.store.subscribe(vec!["sq:ev:*".to_string()]).await.unwrap();

        submit(&state, &room, &key, "u1", "Ann", "https://x.test/a").await.unwrap();

        let mut kinds = Vec::new();
        while let Ok(Some((channel, payload))) =
            tokio::time::timeout(std::time::Duration::from_millis(300), sub.recv()).await
        {
            assert_eq!(channel, format!("sq:ev:{room}"));
            let frame: Value = serde_json::from_str(&payload).unwrap();
            kinds.push(frame["t"].as_str().unwrap().to_string());
            if frame["t"] == "sq.submission" {
                assert_eq!(frame["source"], "twitch");
                assert_eq!(frame["displayName"], "Ann");
            }
        }
        assert!(kinds.contains(&"sq.changed".to_string()), "got {kinds:?}");
        assert!(kinds.contains(&"sq.submission".to_string()), "got {kinds:?}");
    }

    #[test]
    fn source_and_user_id_are_validated() {
        assert_eq!(clean_source(" Twitch ").unwrap(), "twitch");
        assert!(clean_source("").is_err());
        assert!(clean_source("has space").is_err());
        assert!(clean_source("waytoolongplatformname").is_err());
        assert!(clean_user_id("").is_err());
        assert!(clean_user_id("bad\nid").is_err());
        assert!(clean_user_id(&"x".repeat(65)).is_err());
        assert_eq!(clean_user_id(" 123 ").unwrap(), "123");
    }

    fn fp_for(user: &str, name: &str, forwarded_for: Option<&'static str>) -> Value {
        let (payload, _, _) = submit_payload(
            &json!({ "source": "twitch", "userId": user, "displayName": name, "url": "https://x.test" }),
        )
        .unwrap();
        let mut h = HeaderMap::new();
        if let Some(ip) = forwarded_for {
            h.insert("x-forwarded-for", HeaderValue::from_static(ip));
        }
        let mut p = payload.clone();
        p["displayName"] = json!(name);
        crate::build_fingerprint(&h, &p)
    }

    #[test]
    fn authenticated_ids_decide_identity_with_or_without_a_proxy_header() {
        for ip in [Some("203.0.113.9"), None] {
            // Different viewers, same display name, same bridge IP: never the same person.
            let different = crate::sq_fingerprint_score(&fp_for("u1", "Sam", ip), &fp_for("u2", "Sam", ip));
            // Same viewer after a rename: always the same person.
            let same = crate::sq_fingerprint_score(&fp_for("u1", "Sam", ip), &fp_for("u1", "Samuel", ip));

            assert_eq!(different, 0.0, "ip {ip:?}");
            assert_eq!(same, 1.0, "ip {ip:?}");
        }
    }

    #[test]
    fn fingerprints_without_an_id_still_use_the_fuzzy_score() {
        // A web-page submitter has no authenticated id, so the old rules apply untouched.
        let web = |cookie: &str, ip: &'static str| {
            let mut h = HeaderMap::new();
            h.insert("x-forwarded-for", HeaderValue::from_static(ip));
            crate::build_fingerprint(&h, &json!({ "fpCookie": cookie, "displayName": "Pat" }))
        };

        let same_browser = crate::sq_fingerprint_score(&web("c1", "198.51.100.7"), &web("c1", "198.51.100.7"));
        let other_browser = crate::sq_fingerprint_score(&web("c1", "198.51.100.7"), &web("c2", "192.0.2.5"));

        assert!(same_browser >= crate::SQ_NORMAL_FP_THRESHOLD);
        assert!(other_browser < crate::SQ_NORMAL_FP_THRESHOLD);
        // And a webhook person never merges with a web-page person just because one side has no id.
        assert!(
            crate::sq_fingerprint_score(&fp_for("u1", "Pat", Some("203.0.113.9")), &web("c1", "198.51.100.7"))
                < crate::SQ_NORMAL_FP_THRESHOLD
        );
    }
}
