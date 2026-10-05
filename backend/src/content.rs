//! Self-hosted content: the warnings, changelog, community list and verified creators the app and
//! website used to read as files off the legacy CDN.
//!
//! Reads are public and cacheable. Writes need the admin token (`SPECTRALIS_ADMIN_TOKEN`), which is
//! what telescreen sends. With no token configured the admin routes answer 503, so a missing secret
//! can never leave them open.

use axum::extract::{Path as AxumPath, State};
use axum::http::{header, HeaderMap, HeaderValue, StatusCode};
use axum::response::{IntoResponse, Response};
use axum::Json;
use bytes::Bytes;
use chrono::Utc;
use serde_json::{json, Map, Value};
use sha2::{Digest, Sha256};

use crate::{AppError, AppState};

const KEY_WARNINGS: &str = "content:warnings";
const KEY_CHANGELOG: &str = "content:changelog";
const KEY_COMMUNITY: &str = "content:community";
const CREATOR_PREFIX: &str = "content:creator:";

pub const MAX_AVATAR_BYTES: usize = 2 * 1024 * 1024;
const FEED_CACHE: &str = "public, max-age=60";
/// The contract asks for five minutes so a revocation lands within a session.
const CREATOR_CACHE: &str = "public, max-age=300";

fn creator_key(fingerprint: &str) -> String {
    format!("{CREATOR_PREFIX}{fingerprint}")
}
fn creator_avatar_blob(fingerprint: &str) -> String {
    format!("content/creators/{fingerprint}")
}
fn community_avatar_blob(slug: &str) -> String {
    format!("content/community/{slug}")
}

// ── admin auth ───────────────────────────────────────────────────────────────────────────────

fn tokens_match(provided: &str, expected: &str) -> bool {
    // Compare digests so the comparison time does not depend on where the strings differ.
    let a = Sha256::digest(provided.as_bytes());
    let b = Sha256::digest(expected.as_bytes());
    a.iter().zip(b.iter()).fold(0u8, |acc, (x, y)| acc | (x ^ y)) == 0
}

fn check_admin(expected: &str, headers: &HeaderMap) -> Result<(), AppError> {
    let expected = expected.trim();
    if expected.len() < 24 {
        return Err(AppError {
            status: StatusCode::SERVICE_UNAVAILABLE,
            message: "The admin API is not configured.".to_string(),
        });
    }
    let provided = headers
        .get(header::AUTHORIZATION)
        .and_then(|v| v.to_str().ok())
        .and_then(|v| v.strip_prefix("Bearer "))
        .map(str::trim)
        .unwrap_or("");
    if provided.is_empty() || !tokens_match(provided, expected) {
        return Err(AppError::unauthorized("Admin token required."));
    }
    Ok(())
}

fn require_admin(headers: &HeaderMap) -> Result<(), AppError> {
    check_admin(&std::env::var("SPECTRALIS_ADMIN_TOKEN").unwrap_or_default(), headers)
}

// ── validation ───────────────────────────────────────────────────────────────────────────────

fn text(obj: &Map<String, Value>, key: &str, max: usize, required: bool) -> Result<Option<String>, String> {
    match obj.get(key) {
        None | Some(Value::Null) => {
            if required {
                Err(format!("\"{key}\" is required."))
            } else {
                Ok(None)
            }
        }
        Some(Value::String(s)) => {
            let s = s.trim();
            if s.is_empty() {
                return if required { Err(format!("\"{key}\" can't be empty.")) } else { Ok(None) };
            }
            if s.chars().count() > max {
                return Err(format!("\"{key}\" is longer than {max} characters."));
            }
            if s.chars().any(|c| c == '\u{0}') {
                return Err(format!("\"{key}\" has an invalid character."));
            }
            Ok(Some(s.to_string()))
        }
        Some(_) => Err(format!("\"{key}\" must be text.")),
    }
}

fn flag(obj: &Map<String, Value>, key: &str, default: bool) -> Result<bool, String> {
    match obj.get(key) {
        None | Some(Value::Null) => Ok(default),
        Some(Value::Bool(b)) => Ok(*b),
        Some(_) => Err(format!("\"{key}\" must be true or false.")),
    }
}

fn valid_id(s: &str) -> bool {
    !s.is_empty() && s.len() <= 64 && s.chars().all(|c| c.is_ascii_alphanumeric() || matches!(c, '.' | '_' | '-'))
}

fn valid_version(s: &str) -> bool {
    !s.is_empty() && s.len() <= 32 && s.chars().all(|c| c.is_ascii_alphanumeric() || matches!(c, '.' | '-' | '+'))
}

fn valid_https_url(s: &str) -> bool {
    s.len() <= 300 && s.starts_with("https://") && s.len() > 8 && !s.chars().any(|c| c.is_whitespace() || c.is_control())
}

/// Plain http is only accepted for this machine, so a local test server can hand out avatar links.
fn valid_loopback_http_url(s: &str) -> bool {
    s.len() <= 300
        && (s.starts_with("http://127.0.0.1") || s.starts_with("http://localhost"))
        && !s.chars().any(|c| c.is_whitespace() || c.is_control())
}

fn valid_fingerprint(s: &str) -> bool {
    s.len() == 64 && s.chars().all(|c| matches!(c, '0'..='9' | 'a'..='f'))
}

fn valid_slug(s: &str) -> bool {
    !s.is_empty() && s.len() <= 40 && s.chars().all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '-')
}

fn valid_capability(s: &str) -> bool {
    // dotted identifiers like "visualizer.multiLayer"; unknown ones are harmless because the
    // player only ever intersects them with what a capsule asks for
    s.len() <= 64
        && s.contains('.')
        && !s.starts_with('.')
        && !s.ends_with('.')
        && s.chars().all(|c| c.is_ascii_alphanumeric() || c == '.')
}

fn items<'a>(value: &'a Value, what: &str, max: usize) -> Result<&'a Vec<Value>, String> {
    let arr = value.as_array().ok_or_else(|| format!("{what} must be a list."))?;
    if arr.len() > max {
        return Err(format!("{what} can have at most {max} entries."));
    }
    Ok(arr)
}

fn object<'a>(value: &'a Value, what: &str) -> Result<&'a Map<String, Value>, String> {
    value.as_object().ok_or_else(|| format!("Each {what} must be an object."))
}

fn string_list(value: Option<&Value>, key: &str, max_items: usize, max_len: usize) -> Result<Vec<String>, String> {
    match value {
        None | Some(Value::Null) => Ok(Vec::new()),
        Some(Value::Array(arr)) => {
            if arr.len() > max_items {
                return Err(format!("\"{key}\" can have at most {max_items} entries."));
            }
            let mut out = Vec::new();
            for v in arr {
                let s = v.as_str().ok_or_else(|| format!("\"{key}\" must be a list of text."))?.trim();
                if s.is_empty() {
                    continue;
                }
                if s.chars().count() > max_len {
                    return Err(format!("An entry in \"{key}\" is longer than {max_len} characters."));
                }
                out.push(s.to_string());
            }
            Ok(out)
        }
        Some(_) => Err(format!("\"{key}\" must be a list.")),
    }
}

pub fn validate_warnings(value: &Value) -> Result<Value, String> {
    let mut out = Vec::new();
    let mut seen = std::collections::HashSet::new();
    for item in items(value, "Warnings", 50)? {
        let o = object(item, "warning")?;
        let id = text(o, "id", 64, true)?.unwrap();
        if !valid_id(&id) {
            return Err(format!("Warning id \"{id}\" can only use letters, numbers, dots, dashes and underscores."));
        }
        if !seen.insert(id.clone()) {
            return Err(format!("Warning id \"{id}\" is used twice."));
        }
        let severity = text(o, "severity", 16, false)?.unwrap_or_else(|| "warning".to_string()).to_lowercase();
        if !matches!(severity.as_str(), "info" | "warning" | "critical") {
            return Err("\"severity\" must be info, warning or critical.".to_string());
        }
        let versions = string_list(o.get("versions"), "versions", 50, 32)?;
        if let Some(bad) = versions.iter().find(|v| !valid_version(v)) {
            return Err(format!("\"{bad}\" is not a version number."));
        }
        let link_label = text(o, "linkLabel", 60, false)?;
        let link_url = text(o, "linkUrl", 300, false)?;
        match (&link_label, &link_url) {
            (Some(_), None) | (None, Some(_)) => return Err("A link needs both linkLabel and linkUrl.".to_string()),
            (_, Some(url)) if !valid_https_url(url) => return Err("linkUrl must be an https link.".to_string()),
            _ => {}
        }
        let mut w = Map::new();
        w.insert("id".into(), json!(id));
        w.insert("active".into(), json!(flag(o, "active", true)?));
        w.insert("title".into(), json!(text(o, "title", 120, true)?.unwrap()));
        w.insert("message".into(), json!(text(o, "message", 1000, true)?.unwrap()));
        w.insert("severity".into(), json!(severity));
        if !versions.is_empty() {
            w.insert("versions".into(), json!(versions));
        }
        w.insert("dismissible".into(), json!(flag(o, "dismissible", true)?));
        if let (Some(label), Some(url)) = (link_label, link_url) {
            w.insert("linkLabel".into(), json!(label));
            w.insert("linkUrl".into(), json!(url));
        }
        out.push(Value::Object(w));
    }
    Ok(Value::Array(out))
}

pub fn validate_changelog(value: &Value) -> Result<Value, String> {
    let mut out = Vec::new();
    let mut seen = std::collections::HashSet::new();
    for item in items(value, "The changelog", 100)? {
        let o = object(item, "release")?;
        // a display label: usually a version, sometimes a range like "4.0.x - 4.1.x"
        let version = text(o, "version", 40, true)?.unwrap();
        if version.chars().any(|c| c.is_control() || matches!(c, '<' | '>')) {
            return Err(format!("\"{version}\" has characters a version label can't use."));
        }
        if !seen.insert(version.clone()) {
            return Err(format!("Version {version} is listed twice."));
        }
        let metrics = string_list(o.get("metrics"), "metrics", 8, 60)?;
        let mut groups = Vec::new();
        match o.get("groups") {
            None | Some(Value::Null) => {}
            Some(Value::Array(arr)) => {
                if arr.len() > 12 {
                    return Err("A release can have at most 12 groups.".to_string());
                }
                for g in arr {
                    let go = object(g, "group")?;
                    let icon = text(go, "icon", 40, false)?.unwrap_or_default();
                    if !icon.chars().all(|c| c.is_ascii_alphanumeric()) {
                        return Err(format!("Icon \"{icon}\" can only use letters and numbers."));
                    }
                    groups.push(json!({
                        "icon": icon,
                        "title": text(go, "title", 120, true)?.unwrap(),
                        "bullets": string_list(go.get("bullets"), "bullets", 20, 800)?,
                    }));
                }
            }
            Some(_) => return Err("\"groups\" must be a list.".to_string()),
        }
        out.push(json!({
            "version": version,
            "label": text(o, "label", 120, true)?.unwrap(),
            "date": text(o, "date", 40, false)?.unwrap_or_default(),
            "summary": text(o, "summary", 4000, false)?.unwrap_or_default(),
            "metrics": metrics,
            "groups": groups,
        }));
    }
    Ok(Value::Array(out))
}

pub fn validate_community(value: &Value) -> Result<Value, String> {
    let mut out = Vec::new();
    for item in items(value, "The community list", 100)? {
        let o = object(item, "person")?;
        let avatar = text(o, "avatar", 300, false)?;
        if let Some(url) = &avatar {
            if !valid_https_url(url) && !valid_loopback_http_url(url) {
                return Err("avatar must be an https link.".to_string());
            }
        }
        out.push(json!({
            "name": text(o, "name", 80, true)?.unwrap(),
            "subtitle": text(o, "subtitle", 120, false)?.unwrap_or_default(),
            "avatar": avatar.unwrap_or_default(),
        }));
    }
    Ok(Value::Array(out))
}

/// Builds the stored creator record from an admin request, keeping what the server owns
/// (creation time, whether an avatar is on file) and stamping what it controls (updated, revoked).
pub fn validate_creator(fingerprint: &str, body: &Value, existing: Option<&Value>, now: &str) -> Result<Value, String> {
    if !valid_fingerprint(fingerprint) {
        return Err("The fingerprint must be 64 lowercase hex characters.".to_string());
    }
    let o = object(body, "creator")?;
    let status = text(o, "status", 16, false)?.unwrap_or_else(|| "active".to_string()).to_lowercase();
    if !matches!(status.as_str(), "active" | "suspended" | "revoked") {
        return Err("\"status\" must be active, suspended or revoked.".to_string());
    }
    let profile_url = text(o, "profileUrl", 300, false)?;
    if let Some(url) = &profile_url {
        if !valid_https_url(url) {
            return Err("profileUrl must be an https link.".to_string());
        }
    }
    let mut capabilities = string_list(o.get("allowedCapabilities"), "allowedCapabilities", 64, 64)?;
    if let Some(bad) = capabilities.iter().find(|c| !valid_capability(c)) {
        return Err(format!("\"{bad}\" is not a capability name."));
    }
    capabilities.sort();
    capabilities.dedup();

    let previous = |key: &str| existing.and_then(|e| e.get(key)).and_then(Value::as_str).map(str::to_string);
    // A first-time registration may carry the key's real history (an import from the old CDN does);
    // after that the server owns these times.
    let supplied = |key: &str| -> Option<String> {
        let raw = o.get(key)?.as_str()?;
        chrono::DateTime::parse_from_rfc3339(raw)
            .ok()
            .map(|t| t.with_timezone(&Utc).to_rfc3339_opts(chrono::SecondsFormat::Secs, true))
    };
    let created = previous("createdAtUtc").or_else(|| supplied("createdAtUtc")).unwrap_or_else(|| now.to_string());
    let revoked = if status == "active" {
        None
    } else {
        Some(previous("revokedAtUtc").or_else(|| supplied("revokedAtUtc")).unwrap_or_else(|| now.to_string()))
    };
    let key_id = match text(o, "keyId", 80, false)? {
        Some(k) => k,
        None => previous("keyId").unwrap_or_else(|| format!("key-{}", &fingerprint[..12])),
    };

    Ok(json!({
        "keyId": key_id,
        "fingerprint": fingerprint,
        "displayName": text(o, "displayName", 80, true)?.unwrap(),
        "profileUrl": profile_url,
        "status": status,
        "allowedCapabilities": capabilities,
        "createdAtUtc": created,
        "updatedAtUtc": now,
        "revokedAtUtc": revoked,
        "hasAvatar": existing.and_then(|e| e.get("hasAvatar")).and_then(Value::as_bool).unwrap_or(false),
    }))
}

/// The image type from the bytes themselves; the Content-Type header is not trusted.
pub fn sniff_image(bytes: &[u8]) -> Option<&'static str> {
    if bytes.len() >= 8 && bytes[..8] == [0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A] {
        Some("image/png")
    } else if bytes.len() >= 3 && bytes[..3] == [0xFF, 0xD8, 0xFF] {
        Some("image/jpeg")
    } else if bytes.len() >= 12 && &bytes[..4] == b"RIFF" && &bytes[8..12] == b"WEBP" {
        Some("image/webp")
    } else {
        None
    }
}

// ── responses ────────────────────────────────────────────────────────────────────────────────

fn cached_json(body: Value, cache: &'static str) -> Response {
    ([(header::CACHE_CONTROL, cache)], Json(body)).into_response()
}

fn image_response(bytes: Bytes, content_type: &str) -> Response {
    let mut response = (StatusCode::OK, bytes).into_response();
    let headers = response.headers_mut();
    headers.insert(
        header::CONTENT_TYPE,
        HeaderValue::from_str(content_type).unwrap_or(HeaderValue::from_static("application/octet-stream")),
    );
    headers.insert(header::CACHE_CONTROL, HeaderValue::from_static(CREATOR_CACHE));
    response
}

async fn read_list(state: &AppState, key: &str) -> Result<Value, AppError> {
    Ok(state
        .store
        .get_json(key)
        .await
        .map_err(AppError::internal)?
        .filter(Value::is_array)
        .unwrap_or_else(|| json!([])))
}

async fn write_list(state: &AppState, key: &str, value: &Value) -> Result<Json<Value>, AppError> {
    state.store.put_json(key, value, None).await.map_err(AppError::internal)?;
    Ok(Json(json!({ "ok": true, "count": value.as_array().map(Vec::len).unwrap_or(0) })))
}

/// The public view of a creator: the stored record with the avatar turned into a link.
fn public_creator(doc: &Value, base: &str) -> Value {
    let fingerprint = doc.get("fingerprint").and_then(Value::as_str).unwrap_or_default();
    let has_avatar = doc.get("hasAvatar").and_then(Value::as_bool).unwrap_or(false);
    let mut out = doc.as_object().cloned().unwrap_or_default();
    out.remove("hasAvatar");
    out.insert(
        "avatarUrl".into(),
        if has_avatar { json!(format!("{base}/spectralis/v1/creators/{fingerprint}/avatar")) } else { Value::Null },
    );
    Value::Object(out)
}

// ── public routes ────────────────────────────────────────────────────────────────────────────

pub async fn get_warnings(State(state): State<AppState>) -> Result<Response, AppError> {
    Ok(cached_json(read_list(&state, KEY_WARNINGS).await?, FEED_CACHE))
}

pub async fn get_changelog(State(state): State<AppState>) -> Result<Response, AppError> {
    Ok(cached_json(read_list(&state, KEY_CHANGELOG).await?, FEED_CACHE))
}

pub async fn get_community(State(state): State<AppState>) -> Result<Response, AppError> {
    Ok(cached_json(read_list(&state, KEY_COMMUNITY).await?, FEED_CACHE))
}

pub async fn get_community_avatar(
    State(state): State<AppState>,
    AxumPath(slug): AxumPath<String>,
) -> Result<Response, AppError> {
    if !valid_slug(&slug) {
        return Err(AppError::not_found("No such avatar."));
    }
    let blob = state
        .store
        .get_blob(&community_avatar_blob(&slug))
        .await
        .map_err(AppError::internal)?
        .ok_or_else(|| AppError::not_found("No such avatar."))?;
    Ok(image_response(blob.bytes, &blob.content_type))
}

pub async fn get_creator(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(fingerprint): AxumPath<String>,
) -> Result<Response, AppError> {
    let fingerprint = fingerprint.trim_end_matches(".json").to_lowercase();
    if !valid_fingerprint(&fingerprint) {
        return Err(AppError::not_found("That key is not registered."));
    }
    let doc = state
        .store
        .get_json(&creator_key(&fingerprint))
        .await
        .map_err(AppError::internal)?
        .ok_or_else(|| AppError::not_found("That key is not registered."))?;
    Ok(cached_json(public_creator(&doc, &crate::base_url(&state, &headers)), CREATOR_CACHE))
}

pub async fn get_creator_avatar(
    State(state): State<AppState>,
    AxumPath(fingerprint): AxumPath<String>,
) -> Result<Response, AppError> {
    if !valid_fingerprint(&fingerprint) {
        return Err(AppError::not_found("No such avatar."));
    }
    let blob = state
        .store
        .get_blob(&creator_avatar_blob(&fingerprint))
        .await
        .map_err(AppError::internal)?
        .ok_or_else(|| AppError::not_found("No such avatar."))?;
    Ok(image_response(blob.bytes, &blob.content_type))
}

// ── admin routes ─────────────────────────────────────────────────────────────────────────────

pub async fn put_warnings(
    State(state): State<AppState>,
    headers: HeaderMap,
    Json(body): Json<Value>,
) -> Result<Json<Value>, AppError> {
    require_admin(&headers)?;
    let clean = validate_warnings(&body).map_err(|m| AppError::bad_request(&m))?;
    write_list(&state, KEY_WARNINGS, &clean).await
}

pub async fn put_changelog(
    State(state): State<AppState>,
    headers: HeaderMap,
    Json(body): Json<Value>,
) -> Result<Json<Value>, AppError> {
    require_admin(&headers)?;
    let clean = validate_changelog(&body).map_err(|m| AppError::bad_request(&m))?;
    write_list(&state, KEY_CHANGELOG, &clean).await
}

pub async fn put_community(
    State(state): State<AppState>,
    headers: HeaderMap,
    Json(body): Json<Value>,
) -> Result<Json<Value>, AppError> {
    require_admin(&headers)?;
    let clean = validate_community(&body).map_err(|m| AppError::bad_request(&m))?;
    write_list(&state, KEY_COMMUNITY, &clean).await
}

fn check_avatar(bytes: &Bytes) -> Result<&'static str, AppError> {
    if bytes.is_empty() {
        return Err(AppError::bad_request("No image was sent."));
    }
    if bytes.len() > MAX_AVATAR_BYTES {
        return Err(AppError::payload_too_large("Avatars can be at most 2 MB."));
    }
    sniff_image(bytes).ok_or_else(|| AppError::bad_request("The avatar must be a PNG, JPEG or WebP image."))
}

pub async fn put_community_avatar(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(slug): AxumPath<String>,
    body: Bytes,
) -> Result<Json<Value>, AppError> {
    require_admin(&headers)?;
    if !valid_slug(&slug) {
        return Err(AppError::bad_request("The slug can only use lowercase letters, numbers and dashes."));
    }
    let content_type = check_avatar(&body)?;
    state
        .store
        .put_blob(&community_avatar_blob(&slug), body, content_type)
        .await
        .map_err(AppError::internal)?;
    Ok(Json(json!({ "ok": true, "url": format!("{}/spectralis/v1/community/avatars/{slug}", crate::base_url(&state, &headers)) })))
}

pub async fn list_creators(State(state): State<AppState>, headers: HeaderMap) -> Result<Json<Value>, AppError> {
    require_admin(&headers)?;
    let base = crate::base_url(&state, &headers);
    let mut creators = Vec::new();
    for key in state.store.scan_keys(&format!("{CREATOR_PREFIX}*")).await.map_err(AppError::internal)? {
        if let Some(doc) = state.store.get_json(&key).await.map_err(AppError::internal)? {
            creators.push(public_creator(&doc, &base));
        }
    }
    creators.sort_by_key(|c| c.get("displayName").and_then(Value::as_str).unwrap_or_default().to_lowercase());
    Ok(Json(Value::Array(creators)))
}

pub async fn put_creator(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(fingerprint): AxumPath<String>,
    Json(body): Json<Value>,
) -> Result<Json<Value>, AppError> {
    require_admin(&headers)?;
    let existing = state.store.get_json(&creator_key(&fingerprint)).await.map_err(AppError::internal)?;
    let doc = validate_creator(&fingerprint, &body, existing.as_ref(), &Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true))
        .map_err(|m| AppError::bad_request(&m))?;
    state.store.put_json(&creator_key(&fingerprint), &doc, None).await.map_err(AppError::internal)?;
    Ok(Json(public_creator(&doc, &crate::base_url(&state, &headers))))
}

pub async fn delete_creator(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(fingerprint): AxumPath<String>,
) -> Result<Json<Value>, AppError> {
    require_admin(&headers)?;
    if !valid_fingerprint(&fingerprint) {
        return Err(AppError::bad_request("The fingerprint must be 64 lowercase hex characters."));
    }
    state.store.del(&creator_key(&fingerprint)).await.map_err(AppError::internal)?;
    state.store.delete_blob(&creator_avatar_blob(&fingerprint)).await.map_err(AppError::internal)?;
    Ok(Json(json!({ "ok": true })))
}

pub async fn put_creator_avatar(
    State(state): State<AppState>,
    headers: HeaderMap,
    AxumPath(fingerprint): AxumPath<String>,
    body: Bytes,
) -> Result<Json<Value>, AppError> {
    require_admin(&headers)?;
    if !valid_fingerprint(&fingerprint) {
        return Err(AppError::bad_request("The fingerprint must be 64 lowercase hex characters."));
    }
    let mut doc = state
        .store
        .get_json(&creator_key(&fingerprint))
        .await
        .map_err(AppError::internal)?
        .ok_or_else(|| AppError::not_found("Register the creator before uploading an avatar."))?;
    let content_type = check_avatar(&body)?;
    state
        .store
        .put_blob(&creator_avatar_blob(&fingerprint), body, content_type)
        .await
        .map_err(AppError::internal)?;
    if let Some(obj) = doc.as_object_mut() {
        obj.insert("hasAvatar".into(), json!(true));
        obj.insert(
            "updatedAtUtc".into(),
            json!(Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true)),
        );
    }
    state.store.put_json(&creator_key(&fingerprint), &doc, None).await.map_err(AppError::internal)?;
    Ok(Json(public_creator(&doc, &crate::base_url(&state, &headers))))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::collab::Rooms;
    use std::path::PathBuf;
    use std::sync::Arc;

    const TOKEN: &str = "test-admin-token-0123456789abcdef";
    const FP: &str = "6961405f930dad8a49271cfa49936d161e2c8943645be3d355598f0f76c48d60";
    const NOW: &str = "2026-10-04T12:00:00Z";
    const PNG: [u8; 12] = [0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

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
        }
    }

    fn admin() -> HeaderMap {
        let mut h = HeaderMap::new();
        h.insert(header::AUTHORIZATION, HeaderValue::from_str(&format!("Bearer {TOKEN}")).unwrap());
        h
    }

    async fn body_json(response: Response) -> Value {
        let bytes = axum::body::to_bytes(response.into_body(), 1 << 20).await.unwrap();
        serde_json::from_slice(&bytes).unwrap()
    }

    #[test]
    fn the_admin_api_stays_off_without_a_real_token() {
        assert_eq!(check_admin("", &admin()).unwrap_err().status, StatusCode::SERVICE_UNAVAILABLE);
        assert_eq!(check_admin("short", &admin()).unwrap_err().status, StatusCode::SERVICE_UNAVAILABLE);
    }

    #[test]
    fn the_admin_token_must_match_exactly() {
        assert!(check_admin(TOKEN, &admin()).is_ok());
        assert_eq!(check_admin(TOKEN, &HeaderMap::new()).unwrap_err().status, StatusCode::UNAUTHORIZED);
        let mut wrong = HeaderMap::new();
        wrong.insert(header::AUTHORIZATION, HeaderValue::from_static("Bearer test-admin-token-0123456789abcdeX"));
        assert_eq!(check_admin(TOKEN, &wrong).unwrap_err().status, StatusCode::UNAUTHORIZED);
        let mut not_bearer = HeaderMap::new();
        not_bearer.insert(header::AUTHORIZATION, HeaderValue::from_str(TOKEN).unwrap());
        assert_eq!(check_admin(TOKEN, &not_bearer).unwrap_err().status, StatusCode::UNAUTHORIZED);
    }

    #[test]
    fn warnings_are_normalized_and_keep_the_legacy_shape() {
        let input = json!([{
            "id": "6.1.0-backend-7-0-0", "active": true, "title": " Update to 7.0.0 ", "message": "Update.",
            "severity": "CRITICAL", "versions": ["6.1.0"], "dismissible": true,
            "linkLabel": "Download 7.0.0", "linkUrl": "https://spectralis.deltavdevs.com", "junk": 1
        }]);
        let out = validate_warnings(&input).unwrap();
        let w = &out[0];
        assert_eq!(w["title"], "Update to 7.0.0");
        assert_eq!(w["severity"], "critical");
        assert_eq!(w["versions"], json!(["6.1.0"]));
        assert!(w.get("junk").is_none());
    }

    #[test]
    fn warnings_without_versions_apply_to_everyone_and_omit_the_field() {
        let out = validate_warnings(&json!([{ "id": "notice", "title": "t", "message": "m" }])).unwrap();
        assert!(out[0].get("versions").is_none());
        assert_eq!(out[0]["active"], true);
        assert_eq!(out[0]["dismissible"], true);
        assert_eq!(out[0]["severity"], "warning");
    }

    #[test]
    fn bad_warnings_are_refused_with_a_reason() {
        let base = |extra: Value| {
            let mut o = json!({ "id": "a", "title": "t", "message": "m" });
            for (k, v) in extra.as_object().unwrap() {
                o[k] = v.clone();
            }
            json!([o])
        };
        assert!(validate_warnings(&base(json!({ "severity": "loud" }))).is_err());
        assert!(validate_warnings(&base(json!({ "linkLabel": "x" }))).is_err(), "label without url");
        assert!(validate_warnings(&base(json!({ "linkLabel": "x", "linkUrl": "http://insecure.example" }))).is_err());
        assert!(validate_warnings(&base(json!({ "linkLabel": "x", "linkUrl": "javascript:alert(1)" }))).is_err());
        assert!(validate_warnings(&base(json!({ "versions": ["6.1.0; drop"] }))).is_err());
        assert!(validate_warnings(&base(json!({ "id": "has space" }))).is_err());
        assert!(validate_warnings(&json!([{ "id": "a", "title": "t", "message": "m" }, { "id": "a", "title": "t", "message": "m" }])).is_err());
        assert!(validate_warnings(&json!({ "not": "a list" })).is_err());
        assert!(validate_warnings(&json!([{ "id": "a", "title": "t" }])).is_err(), "message is required");
    }

    #[test]
    fn the_changelog_round_trips_the_legacy_entry_shape() {
        let entry = json!({
            "version": "7.0.0", "label": "Protocol v2", "date": "Latest", "summary": "s",
            "metrics": ["Protocol v2", "Gapless"],
            "groups": [{ "icon": "Users", "title": "Shared Play", "bullets": ["one", "two"] }]
        });
        let out = validate_changelog(&json!([entry.clone()])).unwrap();
        assert_eq!(out[0], entry);
    }

    #[test]
    fn a_range_label_from_the_legacy_changelog_is_accepted() {
        let out = validate_changelog(&json!([{ "version": "4.0.x - 4.1.x", "label": "Early builds" }])).unwrap();
        assert_eq!(out[0]["version"], "4.0.x - 4.1.x");
    }

    #[test]
    fn bad_changelogs_are_refused() {
        assert!(validate_changelog(&json!([{ "version": "7.0.0", "label": "a" }, { "version": "7.0.0", "label": "b" }])).is_err());
        assert!(validate_changelog(&json!([{ "version": "7<script>", "label": "a" }])).is_err());
        assert!(validate_changelog(&json!([{ "version": "7.0.0" }])).is_err(), "label is required");
        assert!(validate_changelog(&json!([{ "version": "7.0.0", "label": "a", "groups": [{ "icon": "<b>", "title": "t" }] }])).is_err());
    }

    #[test]
    fn community_avatars_must_be_https_links() {
        let ok = validate_community(&json!([{ "name": "A", "subtitle": "s", "avatar": "https://x.test/a.png" }])).unwrap();
        assert_eq!(ok[0]["avatar"], "https://x.test/a.png");
        assert!(validate_community(&json!([{ "name": "A", "avatar": "http://x.test/a.png" }])).is_err());
        assert!(validate_community(&json!([{ "name": "A", "avatar": "http://127.0.0.1:8094/a.png" }])).is_ok(), "this machine only");
        assert!(validate_community(&json!([{ "subtitle": "no name" }])).is_err());
    }

    #[test]
    fn a_new_creator_is_stamped_and_an_update_keeps_its_creation_time() {
        let body = json!({ "displayName": "DeltaWave", "profileUrl": "https://www.deltavdevs.com", "status": "active",
            "allowedCapabilities": ["webview.localContent", "album.world", "album.world"] });
        let first = validate_creator(FP, &body, None, NOW).unwrap();
        assert_eq!(first["createdAtUtc"], NOW);
        assert_eq!(first["allowedCapabilities"], json!(["album.world", "webview.localContent"]), "sorted and de-duplicated");
        assert_eq!(first["revokedAtUtc"], Value::Null);
        assert!(first["keyId"].as_str().unwrap().starts_with("key-6961405f930d"));

        let later = validate_creator(FP, &body, Some(&first), "2026-12-01T00:00:00Z").unwrap();
        assert_eq!(later["createdAtUtc"], NOW);
        assert_eq!(later["updatedAtUtc"], "2026-12-01T00:00:00Z");
    }

    #[test]
    fn revoking_a_creator_stamps_when_and_keeps_the_first_time() {
        let active = validate_creator(FP, &json!({ "displayName": "X", "status": "active" }), None, NOW).unwrap();
        let revoked = validate_creator(FP, &json!({ "displayName": "X", "status": "revoked" }), Some(&active), "2026-11-01T00:00:00Z").unwrap();
        assert_eq!(revoked["revokedAtUtc"], "2026-11-01T00:00:00Z");
        let again = validate_creator(FP, &json!({ "displayName": "X", "status": "suspended" }), Some(&revoked), "2026-11-09T00:00:00Z").unwrap();
        assert_eq!(again["revokedAtUtc"], "2026-11-01T00:00:00Z");
        let restored = validate_creator(FP, &json!({ "displayName": "X", "status": "active" }), Some(&again), "2026-11-10T00:00:00Z").unwrap();
        assert_eq!(restored["revokedAtUtc"], Value::Null);
    }

    #[test]
    fn a_first_registration_can_carry_the_keys_real_history_but_an_update_cannot_rewrite_it() {
        let body = json!({ "displayName": "DeltaWave", "status": "revoked",
            "createdAtUtc": "2026-05-15T10:30:43Z", "revokedAtUtc": "2026-06-01T08:00:00Z" });
        let imported = validate_creator(FP, &body, None, NOW).unwrap();
        assert_eq!(imported["createdAtUtc"], "2026-05-15T10:30:43Z");
        assert_eq!(imported["revokedAtUtc"], "2026-06-01T08:00:00Z");

        let forged = json!({ "displayName": "DeltaWave", "status": "revoked",
            "createdAtUtc": "2020-01-01T00:00:00Z", "revokedAtUtc": "2020-01-02T00:00:00Z" });
        let again = validate_creator(FP, &forged, Some(&imported), "2026-12-01T00:00:00Z").unwrap();
        assert_eq!(again["createdAtUtc"], "2026-05-15T10:30:43Z", "an update must not rewrite when the key was created");
        assert_eq!(again["revokedAtUtc"], "2026-06-01T08:00:00Z", "or when it was revoked");

        let junk = validate_creator(FP, &json!({ "displayName": "X", "createdAtUtc": "yesterday" }), None, NOW).unwrap();
        assert_eq!(junk["createdAtUtc"], NOW, "an unreadable time falls back to now");
    }

    #[test]
    fn bad_creators_are_refused() {
        let ok = json!({ "displayName": "X" });
        assert!(validate_creator("ABC", &ok, None, NOW).is_err(), "short fingerprint");
        assert!(validate_creator(&FP.to_uppercase(), &ok, None, NOW).is_err(), "must be lowercase");
        assert!(validate_creator(FP, &json!({ "displayName": "X", "status": "trusted" }), None, NOW).is_err());
        assert!(validate_creator(FP, &json!({ "displayName": "X", "profileUrl": "ftp://x" }), None, NOW).is_err());
        assert!(validate_creator(FP, &json!({ "displayName": "X", "allowedCapabilities": ["not a capability"] }), None, NOW).is_err());
        assert!(validate_creator(FP, &json!({ "displayName": "X", "allowedCapabilities": ["nodot"] }), None, NOW).is_err());
        assert!(validate_creator(FP, &json!({ "status": "active" }), None, NOW).is_err(), "name is required");
    }

    #[test]
    fn images_are_recognised_by_their_bytes_not_their_label() {
        assert_eq!(sniff_image(&PNG), Some("image/png"));
        assert_eq!(sniff_image(&[0xFF, 0xD8, 0xFF, 0xE0, 0, 0]), Some("image/jpeg"));
        assert_eq!(sniff_image(b"RIFF\0\0\0\0WEBPVP8 "), Some("image/webp"));
        assert_eq!(sniff_image(b"<svg onload=alert(1)>"), None);
        assert_eq!(sniff_image(b"GIF89a"), None);
        assert_eq!(sniff_image(&[]), None);
    }

    #[tokio::test]
    async fn empty_feeds_answer_with_empty_lists() {
        let state = test_state().await;
        for response in [
            get_warnings(State(state.clone())).await.unwrap(),
            get_changelog(State(state.clone())).await.unwrap(),
            get_community(State(state.clone())).await.unwrap(),
        ] {
            assert_eq!(response.headers()[header::CACHE_CONTROL], FEED_CACHE);
            assert_eq!(body_json(response).await, json!([]));
        }
    }

    #[tokio::test]
    async fn admin_writes_need_the_token() {
        let state = test_state().await;
        let denied = put_warnings(State(state.clone()), HeaderMap::new(), Json(json!([]))).await.unwrap_err();
        assert_eq!(denied.status, StatusCode::UNAUTHORIZED);
        assert!(put_warnings(State(state.clone()), admin(), Json(json!([]))).await.is_ok());
    }

    #[tokio::test]
    async fn warnings_written_by_admin_are_served_publicly() {
        let state = test_state().await;
        let input = json!([{ "id": "w1", "title": "Update", "message": "Please update.", "versions": ["6.1.0"] }]);
        let written = put_warnings(State(state.clone()), admin(), Json(input)).await.unwrap();
        assert_eq!(written.0["count"], 1);
        let served = body_json(get_warnings(State(state.clone())).await.unwrap()).await;
        assert_eq!(served[0]["id"], "w1");
        assert_eq!(served[0]["versions"], json!(["6.1.0"]));
    }

    #[tokio::test]
    async fn a_rejected_write_leaves_the_published_list_alone() {
        let state = test_state().await;
        put_warnings(State(state.clone()), admin(), Json(json!([{ "id": "keep", "title": "t", "message": "m" }]))).await.unwrap();
        let err = put_warnings(State(state.clone()), admin(), Json(json!([{ "id": "bad id", "title": "t", "message": "m" }]))).await.unwrap_err();
        assert_eq!(err.status, StatusCode::BAD_REQUEST);
        let served = body_json(get_warnings(State(state.clone())).await.unwrap()).await;
        assert_eq!(served[0]["id"], "keep");
    }

    #[tokio::test]
    async fn an_unregistered_key_is_a_404_and_a_registered_one_carries_its_avatar_link() {
        let state = test_state().await;
        let missing = get_creator(State(state.clone()), HeaderMap::new(), AxumPath(FP.to_string())).await.unwrap_err();
        assert_eq!(missing.status, StatusCode::NOT_FOUND);

        put_creator(State(state.clone()), admin(), AxumPath(FP.to_string()),
            Json(json!({ "displayName": "DeltaWave", "allowedCapabilities": ["album.world"] }))).await.unwrap();
        let before = body_json(get_creator(State(state.clone()), HeaderMap::new(), AxumPath(FP.to_string())).await.unwrap()).await;
        assert_eq!(before["avatarUrl"], Value::Null);
        assert!(before.get("hasAvatar").is_none(), "internal flag must not leak");

        put_creator_avatar(State(state.clone()), admin(), AxumPath(FP.to_string()), Bytes::from(PNG.to_vec())).await.unwrap();
        let response = get_creator(State(state.clone()), HeaderMap::new(), AxumPath(format!("{FP}.json"))).await.unwrap();
        assert_eq!(response.headers()[header::CACHE_CONTROL], CREATOR_CACHE);
        let after = body_json(response).await;
        assert_eq!(after["avatarUrl"], format!("https://api.test/spectralis/v1/creators/{FP}/avatar"));

        let image = get_creator_avatar(State(state.clone()), AxumPath(FP.to_string())).await.unwrap();
        assert_eq!(image.headers()[header::CONTENT_TYPE], "image/png");
    }

    #[tokio::test]
    async fn revoked_keys_are_still_served_so_the_player_can_reject_them() {
        let state = test_state().await;
        put_creator(State(state.clone()), admin(), AxumPath(FP.to_string()), Json(json!({ "displayName": "X", "status": "revoked" }))).await.unwrap();
        let doc = body_json(get_creator(State(state.clone()), HeaderMap::new(), AxumPath(FP.to_string())).await.unwrap()).await;
        assert_eq!(doc["status"], "revoked");
        assert!(doc["revokedAtUtc"].is_string());
    }

    #[tokio::test]
    async fn avatars_that_are_not_images_or_are_too_big_are_refused() {
        let state = test_state().await;
        put_creator(State(state.clone()), admin(), AxumPath(FP.to_string()), Json(json!({ "displayName": "X" }))).await.unwrap();
        let svg = put_creator_avatar(State(state.clone()), admin(), AxumPath(FP.to_string()), Bytes::from_static(b"<svg onload=alert(1)>")).await.unwrap_err();
        assert_eq!(svg.status, StatusCode::BAD_REQUEST);
        let mut huge = PNG.to_vec();
        huge.resize(MAX_AVATAR_BYTES + 1, 0);
        let big = put_creator_avatar(State(state.clone()), admin(), AxumPath(FP.to_string()), Bytes::from(huge)).await.unwrap_err();
        assert_eq!(big.status, StatusCode::PAYLOAD_TOO_LARGE);
        let orphan = put_creator_avatar(State(state.clone()), admin(), AxumPath("0".repeat(64)), Bytes::from(PNG.to_vec())).await.unwrap_err();
        assert_eq!(orphan.status, StatusCode::NOT_FOUND, "no avatar for a creator that isn't registered");
    }

    #[tokio::test]
    async fn deleting_a_creator_removes_the_record_and_the_avatar() {
        let state = test_state().await;
        put_creator(State(state.clone()), admin(), AxumPath(FP.to_string()), Json(json!({ "displayName": "X" }))).await.unwrap();
        put_creator_avatar(State(state.clone()), admin(), AxumPath(FP.to_string()), Bytes::from(PNG.to_vec())).await.unwrap();
        delete_creator(State(state.clone()), admin(), AxumPath(FP.to_string())).await.unwrap();
        assert_eq!(get_creator(State(state.clone()), HeaderMap::new(), AxumPath(FP.to_string())).await.unwrap_err().status, StatusCode::NOT_FOUND);
        assert_eq!(get_creator_avatar(State(state.clone()), AxumPath(FP.to_string())).await.unwrap_err().status, StatusCode::NOT_FOUND);
    }

    #[tokio::test]
    async fn the_creator_list_is_admin_only_and_sorted_by_name() {
        let state = test_state().await;
        let other = "a".repeat(64);
        put_creator(State(state.clone()), admin(), AxumPath(FP.to_string()), Json(json!({ "displayName": "zed" }))).await.unwrap();
        put_creator(State(state.clone()), admin(), AxumPath(other), Json(json!({ "displayName": "Alpha" }))).await.unwrap();
        assert_eq!(list_creators(State(state.clone()), HeaderMap::new()).await.unwrap_err().status, StatusCode::UNAUTHORIZED);
        let list = list_creators(State(state.clone()), admin()).await.unwrap().0;
        let names: Vec<_> = list.as_array().unwrap().iter().map(|c| c["displayName"].as_str().unwrap().to_string()).collect();
        assert_eq!(names, vec!["Alpha", "zed"]);
    }

    #[tokio::test]
    async fn community_avatars_are_stored_and_served_by_slug() {
        let state = test_state().await;
        let bad = put_community_avatar(State(state.clone()), admin(), AxumPath("Bad Slug".to_string()), Bytes::from(PNG.to_vec())).await.unwrap_err();
        assert_eq!(bad.status, StatusCode::BAD_REQUEST);
        let ok = put_community_avatar(State(state.clone()), admin(), AxumPath("marczero".to_string()), Bytes::from(PNG.to_vec())).await.unwrap();
        assert_eq!(ok.0["url"], "https://api.test/spectralis/v1/community/avatars/marczero");
        let image = get_community_avatar(State(state.clone()), AxumPath("marczero".to_string())).await.unwrap();
        assert_eq!(image.headers()[header::CONTENT_TYPE], "image/png");
        assert_eq!(get_community_avatar(State(state.clone()), AxumPath("nobody".to_string())).await.unwrap_err().status, StatusCode::NOT_FOUND);
    }
}
