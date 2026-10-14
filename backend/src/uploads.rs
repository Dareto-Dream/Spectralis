//! Listener files are a bounded handoff to the host, never an account library.
use super::*;

pub const MAX_BYTES: usize = 64 * 1024 * 1024;
const ROOM_BYTES: u64 = 256 * 1024 * 1024;
const MAX_PENDING: usize = 5;
const UPLOAD_MINUTES: i64 = 60;

fn record_key(code: &str, id: &str) -> String {
    format!("sp:upload:{code}:{id}")
}
fn gc_key(code: &str) -> String {
    format!("sp:gc:session:{code}")
}

pub async fn register_session(state: &AppState, code: &str) -> Result<(), AppError> {
    // This record deliberately outlives Redis session TTLs so S3 objects can be collected.
    state
        .store
        .put_json(&gc_key(code), &json!({"code":code}), None)
        .await
        .map_err(AppError::internal)
}

pub async fn authorize(
    state: &AppState,
    code: &str,
    headers: &HeaderMap,
) -> Result<Value, AppError> {
    player::session_access(state, code, headers).await?;
    let user = player::identity(state, headers).await?;
    if !collab::load_caps(state, code).await.audio_uploads {
        return Err(AppError::forbidden(
            "The host has turned off audio uploads.",
        ));
    }
    Ok(user)
}

fn host(headers: &HeaderMap) -> &str {
    headers
        .get("x-session-key")
        .and_then(|h| h.to_str().ok())
        .unwrap_or("")
}

pub async fn post_upload(
    State(state): State<AppState>,
    AxumPath(code): AxumPath<String>,
    headers: HeaderMap,
    mut multipart: Multipart,
) -> Result<Json<Value>, AppError> {
    let code = clean_room_code(&code)?;
    authorize(&state, &code, &headers).await?;
    let mut file = None;
    let mut title = None;
    while let Some(mut field) = multipart
        .next_field()
        .await
        .map_err(|_| AppError::bad_request("Invalid file upload."))?
    {
        match field.name().unwrap_or("") {
            "file" if file.is_none() => {
                let name = field.file_name().unwrap_or("").to_string();
                let mut data = Vec::new();
                while let Some(chunk) = field.chunk().await.map_err(|_| {
                    AppError::payload_too_large("Audio files must be 64 MB or smaller.")
                })? {
                    if data.len() + chunk.len() > MAX_BYTES {
                        return Err(AppError::payload_too_large(
                            "Audio files must be 64 MB or smaller.",
                        ));
                    }
                    data.extend_from_slice(&chunk);
                }
                file = Some((name, Bytes::from(data)));
            }
            "title" => {
                let value = field
                    .text()
                    .await
                    .map_err(|_| AppError::bad_request("Invalid title."))?;
                title = Some(
                    value
                        .chars()
                        .filter(|c| !c.is_control())
                        .take(180)
                        .collect::<String>(),
                );
            }
            _ => return Err(AppError::bad_request("Send one audio file at a time.")),
        }
    }
    let (name, bytes) = file.ok_or_else(|| AppError::bad_request("Choose an audio file."))?;
    // Bound work inside the lease; a slow store must not outlive the 120-second lease.
    let _lease = state
        .store
        .room_lease(&code)
        .await
        .map_err(AppError::internal)?;
    tokio::time::timeout(
        std::time::Duration::from_secs(60),
        accept(&state, &code, &headers, &name, title.as_deref(), bytes),
    )
    .await
    .map_err(|_| AppError::bad_request("Upload timed out. Refresh the queue before trying again."))?
    .map(Json)
}

async fn accept(
    state: &AppState,
    code: &str,
    headers: &HeaderMap,
    filename: &str,
    title: Option<&str>,
    bytes: Bytes,
) -> Result<Value, AppError> {
    let user = authorize(state, code, headers).await?;
    let subject = user["sub"]
        .as_str()
        .filter(|s| !s.is_empty())
        .ok_or_else(|| AppError::unauthorized("Sign in to upload audio."))?;
    let (name, mime) = validate_audio(filename, &bytes)?;
    let records = records(state, code).await?;
    let pending = records
        .iter()
        .filter(|(_, v)| v["received"].as_bool() != Some(true));
    if pending
        .clone()
        .filter(|(_, v)| v["owner"].as_str() == Some(subject))
        .count()
        >= MAX_PENDING
    {
        return Err(AppError::bad_request(
            "You already have five files waiting for the host.",
        ));
    }
    if pending
        .map(|(_, v)| v["size"].as_u64().unwrap_or(0))
        .sum::<u64>()
        + bytes.len() as u64
        > ROOM_BYTES
    {
        return Err(AppError::bad_request(
            "This room's upload space is full. Try again after the host receives some tracks.",
        ));
    }
    let mut queue = read_json_opt(state, &sess_key(code, "queue"))
        .await
        .unwrap_or_else(|| empty_queue(code));
    let items = queue["items"]
        .as_array_mut()
        .ok_or_else(|| AppError::bad_request("Invalid queue."))?;
    if items.len() >= 100 {
        return Err(AppError::bad_request("The room queue is full."));
    }
    let id = uuid::Uuid::new_v4().to_string();
    let key = record_key(code, &id);
    let blob = sess_blob_key(code, &format!("uploads/{id}/audio"));
    let mut url = reqwest::Url::parse(&format!(
        "{}/shared-play/v2/sessions/{code}/uploads/{id}/audio/",
        base_url(state, headers)
    ))
    .map_err(AppError::internal)?;
    url.path_segments_mut()
        .map_err(|_| AppError::bad_request("Invalid server URL."))?
        .pop_if_empty()
        .push(&name);
    let title = title
        .filter(|s| !s.trim().is_empty())
        .unwrap_or_else(|| name.rsplit_once('.').map(|(n, _)| n).unwrap_or(&name));
    let item = json!({"id":id,"sourceKind":"upload","sourceId":id,"url":url.as_str(),"title":title,
        "addedBy":user["name"].as_str().or_else(||user["preferred_username"].as_str()).unwrap_or("Listener"),"addedAtUtc":Utc::now().to_rfc3339()});
    // Register before writing the blob. Interrupted writes remain discoverable by GC.
    register_session(state, code).await?;
    state
        .store
        .put_json(
            &key,
            &json!({"id":id,"owner":subject,"blob":blob,"name":name,"size":bytes.len(),
        "expires":(Utc::now()+Duration::minutes(UPLOAD_MINUTES)).timestamp(),"received":false}),
            None,
        )
        .await
        .map_err(AppError::internal)?;
    state
        .store
        .put_blob(&blob, bytes, mime)
        .await
        .map_err(AppError::internal)?;
    items.push(item.clone());
    queue["updatedAtUtc"] = json!(Utc::now().to_rfc3339());
    write_json(state, &sess_key(code, "queue"), &queue).await?;
    let _ = collab::broadcast_queue(state, code, &queue).await;
    Ok(json!({"item":item,"queue":queue}))
}

fn validate_audio(filename: &str, bytes: &[u8]) -> Result<(String, &'static str), AppError> {
    if bytes.is_empty() {
        return Err(AppError::bad_request("This audio file is empty."));
    }
    if bytes.len() > MAX_BYTES {
        return Err(AppError::payload_too_large(
            "Audio files must be 64 MB or smaller.",
        ));
    }
    let name: String = filename
        .rsplit(['/', '\\'])
        .next()
        .unwrap_or("")
        .chars()
        .filter(|c| !c.is_control() && !"<>:\"|?*".contains(*c))
        .take(160)
        .collect();
    let ext = name.rsplit('.').next().unwrap_or("").to_ascii_lowercase();
    let mime = match ext.as_str() {
        "mp3"
            if bytes.starts_with(b"ID3")
                || bytes
                    .get(0..2)
                    .is_some_and(|b| b[0] == 0xff && b[1] & 0xe0 == 0xe0) =>
        {
            "audio/mpeg"
        }
        "m4a" if bytes.get(4..8) == Some(b"ftyp") && bytes.len() >= 16 => "audio/mp4",
        "wav" if bytes.starts_with(b"RIFF") && bytes.get(8..12) == Some(b"WAVE") => "audio/wav",
        "flac" if bytes.starts_with(b"fLaC") => "audio/flac",
        "ogg" | "opus" if bytes.starts_with(b"OggS") => "audio/ogg",
        "aac"
            if bytes
                .get(0..2)
                .is_some_and(|b| b[0] == 0xff && b[1] & 0xf6 == 0xf0) =>
        {
            "audio/aac"
        }
        "aif" | "aiff"
            if bytes.starts_with(b"FORM")
                && matches!(bytes.get(8..12), Some(b"AIFF" | b"AIFC")) =>
        {
            "audio/aiff"
        }
        _ => {
            return Err(AppError::bad_request(
                "Choose an MP3, M4A, WAV, FLAC, Ogg, Opus, AAC or AIFF audio file.",
            ))
        }
    };
    Ok((name, mime))
}

pub async fn get_audio(
    State(state): State<AppState>,
    AxumPath((code, id, name)): AxumPath<(String, String, String)>,
    headers: HeaderMap,
) -> Result<Response, AppError> {
    let code = clean_room_code(&code)?;
    validate_session_key(&state, &code, host(&headers)).await?;
    let record = read_json(&state, &record_key(&code, &id)).await?;
    if record["received"] == true
        || record["expires"].as_i64().unwrap_or(0) <= Utc::now().timestamp()
        || record["name"].as_str() != Some(&name)
    {
        return Err(AppError::not_found("This upload is no longer available."));
    }
    let mut response = send_blob(&state, record["blob"].as_str().unwrap_or(""), None).await?;
    response
        .headers_mut()
        .insert(header::CACHE_CONTROL, HeaderValue::from_static("no-store"));
    response.headers_mut().insert(
        header::CONTENT_DISPOSITION,
        HeaderValue::from_static("attachment"),
    );
    response.headers_mut().insert(
        "x-content-type-options",
        HeaderValue::from_static("nosniff"),
    );
    Ok(response)
}

pub async fn received(
    State(state): State<AppState>,
    AxumPath((code, id)): AxumPath<(String, String)>,
    headers: HeaderMap,
) -> Result<Json<Value>, AppError> {
    let code = clean_room_code(&code)?;
    validate_session_key(&state, &code, host(&headers)).await?;
    let _lease = state
        .store
        .room_lease(&code)
        .await
        .map_err(AppError::internal)?;
    let key = record_key(&code, &id);
    let mut record = read_json(&state, &key).await?;
    delete_audio(&state, &record).await?;
    record["received"] = json!(true);
    state
        .store
        .put_json(&key, &record, None)
        .await
        .map_err(AppError::internal)?;
    Ok(Json(json!({"ok":true})))
}

async fn records(state: &AppState, code: &str) -> Result<Vec<(String, Value)>, AppError> {
    let mut records = Vec::new();
    for key in state
        .store
        .scan_keys(&format!("sp:upload:{code}:*"))
        .await
        .map_err(AppError::internal)?
    {
        if let Some(record) = state
            .store
            .get_json(&key)
            .await
            .map_err(AppError::internal)?
        {
            records.push((key, record));
        }
    }
    Ok(records)
}
async fn delete_audio(state: &AppState, record: &Value) -> Result<(), AppError> {
    if let Some(blob) = record["blob"].as_str() {
        state
            .store
            .delete_blob(blob)
            .await
            .map_err(AppError::internal)?;
    }
    Ok(())
}

/// Caller holds the room lease. The host's queue is the authority for removal.
pub async fn prune(
    state: &AppState,
    code: &str,
    queue: &mut Value,
    ended: bool,
) -> Result<(), AppError> {
    let mut expired = HashSet::new();
    for (key, record) in records(state, code).await? {
        let id = record["id"].as_str().unwrap_or("");
        let present = queue["items"]
            .as_array()
            .is_some_and(|a| a.iter().any(|i| i["id"].as_str() == Some(id)));
        let stale = record["received"] != true
            && record["expires"].as_i64().unwrap_or(0) <= Utc::now().timestamp();
        if ended || !present || stale {
            delete_audio(state, &record).await?;
            state.store.del(&key).await.map_err(AppError::internal)?;
            if stale {
                expired.insert(id.to_string());
            }
        }
    }
    if let Some(items) = queue["items"].as_array_mut() {
        items.retain(|i| !expired.contains(i["id"].as_str().unwrap_or("")));
    }
    Ok(())
}

pub async fn collect_room(state: &AppState, code: &str) -> Result<(), AppError> {
    let _lease = state
        .store
        .room_lease(code)
        .await
        .map_err(AppError::internal)?;
    let manifest = state
        .store
        .get_json(&sess_key(code, "manifest"))
        .await
        .map_err(AppError::internal)?;
    let ended = manifest.as_ref().is_none_or(|m| m["ended"] == true);
    let mut queue = read_json_opt(state, &sess_key(code, "queue"))
        .await
        .unwrap_or_else(|| empty_queue(code));
    let before = queue.clone();
    prune(state, code, &mut queue, ended).await?;
    if ended {
        state
            .store
            .delete_blob_prefix(&format!("sessions/{code}/"))
            .await
            .map_err(AppError::internal)?;
        state
            .store
            .del(&gc_key(code))
            .await
            .map_err(AppError::internal)?;
    } else if before != queue {
        write_json(state, &sess_key(code, "queue"), &queue).await?;
        let _ = collab::broadcast_queue(state, code, &queue).await;
    }
    Ok(())
}

pub fn spawn_gc(state: AppState) {
    tokio::spawn(async move {
        let mut interval = tokio::time::interval(std::time::Duration::from_secs(60));
        loop {
            interval.tick().await;
            if let Ok(keys) = state.store.scan_keys("sp:gc:session:*").await {
                for key in keys {
                    if let Some(code) = key.strip_prefix("sp:gc:session:") {
                        if let Err(err) = collect_room(&state, code).await {
                            eprintln!("Shared Play cleanup: {}", err.message);
                        }
                    }
                }
            }
        }
    });
}

#[cfg(test)]
mod tests {
    use super::*;
    async fn fixture() -> (AppState, HeaderMap, HeaderMap) {
        std::env::remove_var("REDIS_URL");
        let state = AppState {
            store: Arc::new(store::Store::from_env().await.unwrap()),
            rooms: collab::Rooms::default(),
            sq_rooms: collab::Rooms::default(),
            web_share_root: Arc::new(PathBuf::from("web-share")),
            public_base_url: Some("https://api.test".into()),
            stripe_secret_key: None,
            stripe_webhook_secret: None,
            stripe_connect_client_id: None,
            stripe_publishable_key: None,
            ward_issuer: Arc::new("http://127.0.0.1:1".into()),
            ward_enabled: true,
        };
        write_json(
            &state,
            &sess_key("ABC123", "manifest"),
            &json!({"sessionKey":"abcdefgh12345678"}),
        )
        .await
        .unwrap();
        register_session(&state, "ABC123").await.unwrap();
        let token = "sp_app_test-listener";
        state
            .store
            .put_json(
                &format!(
                    "player:device:{}",
                    bytes_to_hex(&Sha256::digest(token.as_bytes()))
                ),
                &json!({"sub":"listener","name":"Alex"}),
                None,
            )
            .await
            .unwrap();
        let mut user = HeaderMap::new();
        user.insert(
            header::AUTHORIZATION,
            HeaderValue::from_static("Bearer sp_app_test-listener"),
        );
        let mut host = HeaderMap::new();
        host.insert(
            "x-session-key",
            HeaderValue::from_static("abcdefgh12345678"),
        );
        (state, user, host)
    }
    fn audio() -> Bytes {
        Bytes::from_static(b"RIFFxxxxWAVEfmt data")
    }
    async fn upload(state: &AppState, user: &HeaderMap) -> Value {
        accept(state, "ABC123", user, "My song.wav", None, audio())
            .await
            .unwrap()
    }

    #[tokio::test]
    async fn uploads_require_an_account_even_with_host_proof_and_development_never_bypasses_it() {
        let (mut state, user, host) = fixture().await;
        assert_eq!(
            authorize(&state, "ABC123", &HeaderMap::new())
                .await
                .unwrap_err()
                .status,
            StatusCode::UNAUTHORIZED
        );
        assert!(authorize(&state, "ABC123", &host).await.is_err());
        assert!(authorize(&state, "ABC123", &user).await.is_ok());
        write_json(
            &state,
            &sess_key("ABC123", "room:caps"),
            &json!({"audioUploads":false,"queueAdd":"everyone"}),
        )
        .await
        .unwrap();
        assert_eq!(
            authorize(&state, "ABC123", &user).await.unwrap_err().status,
            StatusCode::FORBIDDEN
        );
        write_json(
            &state,
            &sess_key("ABC123", "room:caps"),
            &json!({"audioUploads":true,"queueAdd":"host"}),
        )
        .await
        .unwrap();
        assert!(authorize(&state, "ABC123", &user).await.is_ok());
        state.ward_enabled = false;
        assert!(authorize(&state, "ABC123", &user).await.is_err());
    }

    #[tokio::test]
    async fn received_audio_is_deleted_only_after_host_ack_and_request_stays_in_queue() {
        let (state, user, host) = fixture().await;
        let result = upload(&state, &user).await;
        let item = &result["item"];
        let id = item["id"].as_str().unwrap().to_string();
        assert_eq!(item["addedBy"], "Alex");
        assert_eq!(item["title"], "My song");
        let key = record_key("ABC123", &id);
        let record = read_json(&state, &key).await.unwrap();
        let path = AxumPath(("ABC123".into(), id.clone(), "My song.wav".into()));
        assert!(
            get_audio(State(state.clone()), AxumPath(path.0.clone()), user.clone())
                .await
                .is_err()
        );
        assert!(get_audio(State(state.clone()), path, host.clone())
            .await
            .is_ok());
        assert!(received(
            State(state.clone()),
            AxumPath(("ABC123".into(), id.clone())),
            user
        )
        .await
        .is_err());
        received(
            State(state.clone()),
            AxumPath(("ABC123".into(), id.clone())),
            host.clone(),
        )
        .await
        .unwrap();
        assert!(state
            .store
            .get_blob(record["blob"].as_str().unwrap())
            .await
            .unwrap()
            .is_none());
        assert_eq!(
            read_json(&state, &sess_key("ABC123", "queue"))
                .await
                .unwrap()["items"]
                .as_array()
                .unwrap()
                .len(),
            1
        );
        received(State(state.clone()), AxumPath(("ABC123".into(), id)), host)
            .await
            .unwrap(); // idempotent retry
    }

    #[tokio::test]
    async fn removal_expiry_and_room_end_collect_files_including_playback_packages() {
        let (state, user, _) = fixture().await;
        let first = upload(&state, &user).await;
        let id = first["item"]["id"].as_str().unwrap();
        let mut empty = empty_queue("ABC123");
        prune(&state, "ABC123", &mut empty, false).await.unwrap();
        assert!(records(&state, "ABC123").await.unwrap().is_empty());
        assert!(state
            .store
            .get_blob(&sess_blob_key("ABC123", &format!("uploads/{id}/audio")))
            .await
            .unwrap()
            .is_none());
        let second = upload(&state, &user).await;
        let id = second["item"]["id"].as_str().unwrap();
        let key = record_key("ABC123", id);
        let mut record = read_json(&state, &key).await.unwrap();
        record["expires"] = json!(0);
        state.store.put_json(&key, &record, None).await.unwrap();
        collect_room(&state, "ABC123").await.unwrap();
        assert!(!read_json(&state, &sess_key("ABC123", "queue"))
            .await
            .unwrap()["items"]
            .as_array()
            .unwrap()
            .iter()
            .any(|v| v["id"] == id));
        upload(&state, &user).await;
        state
            .store
            .put_blob("sessions/ABC123/package.zip", audio(), "application/zip")
            .await
            .unwrap();
        state
            .store
            .del(&sess_key("ABC123", "manifest"))
            .await
            .unwrap();
        collect_room(&state, "ABC123").await.unwrap();
        assert!(state
            .store
            .get_blob("sessions/ABC123/package.zip")
            .await
            .unwrap()
            .is_none());
        assert!(records(&state, "ABC123").await.unwrap().is_empty());
        assert!(state
            .store
            .get_json(&gc_key("ABC123"))
            .await
            .unwrap()
            .is_none());
    }

    #[tokio::test]
    async fn room_lease_preserves_concurrent_uploads_and_enforces_account_budget() {
        let (state, user, _) = fixture().await;
        let mut tasks = Vec::new();
        for _ in 0..8 {
            let (state, user) = (state.clone(), user.clone());
            tasks.push(tokio::spawn(async move {
                let _lease = state.store.room_lease("ABC123").await.unwrap();
                accept(&state, "ABC123", &user, "track.wav", None, audio())
                    .await
                    .is_ok()
            }));
        }
        let mut accepted = 0;
        for task in tasks {
            if task.await.unwrap() {
                accepted += 1;
            }
        }
        assert_eq!(accepted, MAX_PENDING);
        assert_eq!(
            read_json(&state, &sess_key("ABC123", "queue"))
                .await
                .unwrap()["items"]
                .as_array()
                .unwrap()
                .len(),
            MAX_PENDING
        );
    }

    #[test]
    fn file_validation_and_queue_paths_cannot_spoof_uploads() {
        assert!(validate_audio("fake.m4a", b"<html>not audio</html>").is_err());
        assert!(validate_audio("fake.zip", &audio()).is_err());
        assert!(validate_audio("empty.wav", b"").is_err());
        assert!(validate_audio("real.m4a", b"\0\0\0\x18ftypM4A \0\0\0\0").is_ok());
        assert!(normalize_queue_item(
            json!({"url":"https://api.test/audio.wav","sourceKind":"UPLOAD"})
        )
        .is_err());
        assert!(normalize_queue_item(
            json!({"url":"https://api.test/shared-play/v2/sessions/ABC123/uploads/id/audio/a.wav"})
        )
        .is_err());
        assert!(normalize_queue_item(json!({"url":"https://music.test/song"})).is_ok());
        assert!(normalize_queue_item(json!({"url":"https://music.test/uploads/album/song.mp3"})).is_ok());
    }
}
